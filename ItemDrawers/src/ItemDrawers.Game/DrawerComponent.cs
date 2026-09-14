using System;
using System.Collections.Generic;
using HarmonyLib;
using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Derives from Container so that a GetComponent&lt;Container&gt;() from
    /// another mod finds this type, and re-declares Interactable and
    /// Hoverable so player interaction reaches this class rather than
    /// Container's -- re-implementing an interface in a derived type
    /// replaces the interface mapping.
    ///
    /// Container.Awake's own body never runs on this type -- ContainerBridge
    /// patches Container.Awake with a prefix that returns false only for a
    /// DrawerComponent, so that body (a second, unused Inventory; vanilla's
    /// container RPCs; an OnDestroyed subscription) is always skipped -- but
    /// Awake below DOES call base.Awake(), specifically so that Harmony
    /// postfixes other mods place on Container.Awake (this is exactly how
    /// OttoFuel and NoVikingLeftBehind discover containers, confirmed by
    /// decompiling both) still fire. Before that call this class sets
    /// Container's own m_nview field to the ZNetView it caches as _view,
    /// and m_inventory to the drawer's view Inventory (see DrawerView) --
    /// mods such as QuickStackStore read that field directly rather than
    /// calling GetInventory. ContainerBridge also patches
    /// Container.GetInventory to refresh and return the same view, and
    /// Container.Save/Load to persist it in the drawer's own ZDO fields
    /// instead of vanilla's items field.
    /// </summary>
    public class DrawerComponent : Container, Interactable, Hoverable
    {
        public static readonly List<DrawerComponent> All = new List<DrawerComponent>();

        private const string KeyPrefab = "Prefab";
        private const string KeyAmount = "Amount";

        // ZDO Get/Set have string overloads that re-hash the key name on
        // every single call (StringExtensionMethods.GetStableHashCode,
        // confirmed by decompiling ZDO -- e.g. `GetInt(string name, ...) =>
        // GetInt(GetStableHashCode(name), ...)`). Hashing once and using the
        // int overloads avoids repeating that on every Snapshot/Commit call,
        // which is on the hot path for a wall of drawers.
        private static readonly int KeyPrefabHash = KeyPrefab.GetStableHashCode();
        private static readonly int KeyAmountHash = KeyAmount.GetStableHashCode();

        // The exact same hash OttoFuel and NoVikingLeftBehind both compute
        // for "creator" (StringExtensionMethods.GetStableHashCode("creator")
        // -- confirmed identical to ZDOVars.s_creator by decompile), and the
        // same hash Piece.SetCreator itself writes to. Used only by
        // EnsureCreator below.
        private static readonly int CreatorHash = "creator".GetStableHashCode();

        // Shown on every player-facing path when a mutation refuses to
        // apply (see WriteOwned's docstring) so a rejected interaction is
        // visible rather than a dead keypress with no feedback. Internal,
        // not private: DrawerManager's give-up handling for a timed-out
        // request shows this same message.
        internal const string CommitFailedMessage = "Try again";

        // ---------- RPC-to-owner mutation protocol ----------
        // A player-driven mutation is a REQUEST to the ZDO's owner, naming
        // only a delta (how much to withdraw, or how much was already
        // removed from the player and needs a home) -- never an absolute
        // target. The owner -- possibly this client, possibly not --
        // clamps the request against its own live Snapshot, applies it,
        // and replies with what it actually granted or accepted. Only that
        // reply gives anything to (or refunds anything to) the player;
        // see RequestWithdraw/RequestDeposit and their RPC_* handlers
        // below. When this client already owns the ZDO -- always true in
        // single-player, and the common case in multiplayer since
        // ownership tracks physical proximity (confirmed by decompiling
        // ZDOMan.ReleaseNearbyZDOS) -- both methods short-circuit to a
        // direct local call: no RPC, no delay, nothing to await.
        //
        // Every mutation call in this class -- the two short-circuits
        // above included -- goes through WriteOwned, which requires
        // IsOwner() and re-reads/compares before writing (see its
        // docstring for why the CAS is not redundant even for a client
        // that just checked IsOwner()==true).
        //
        // The player's own interactions never claim ownership; they use
        // this request protocol. ZNetView.ClaimOwnership() is a purely
        // local field set that PROPAGATES (ZDO.SetOwner ->
        // IncreaseOwnerRevision -> ZDOMan.ClientChanged, confirmed by
        // decompile) and is accepted by every other peer with no server
        // arbitration (ZDOMan.RPC_ZDOData adopts a higher OwnerRevision
        // unconditionally), so it takes the drawer from whoever held it.
        // It is used deliberately in three places, all automation acting
        // the way vanilla and the chest mods act on a chest:
        // TryWithdrawExternally (ItemDrawersAPI), ResolveDepositRoute
        // (auto-pickup, unowned drawers only) and FlushView (a change
        // another mod made through the container view). The first and last
        // write nothing until ownership has settled (OwnershipSettleSeconds).
        // TryDepositExternally refuses rather than claims.
        //
        // Verified by decompiling assembly_valheim.dll rather than
        // assumed (see this task's report for the full trail):
        // ZNetView.InvokeRPC(method, args) always targets
        // m_zdo.GetOwner() -- there is no reply channel on a routed RPC,
        // so a reply is a second, independent RPC the owner sends back at
        // ZRoutedRpc.RoutedRPCData.m_senderPeerID via
        // ZNetView.InvokeRPC(long targetID, ...). A request whose target
        // peer has disconnected is silently dropped by
        // ZRoutedRpc.RouteRPC on the relaying server (GetPeer returns
        // null) -- there is no error, no exception, nothing: the reply
        // simply never comes.
        //
        // A ZDO with NO owner (Owner uid 0) routes as ZRoutedRpc.Everybody,
        // which is handled locally AND rebroadcast to every peer --
        // RPC_RequestWithdraw/RPC_RequestDeposit/RPC_RequestClear all
        // therefore re-check IsOwner() themselves before touching the ZDO,
        // rather than trusting that only the "real" owner receives the
        // message.
        //
        // Every retry of a request targets the SAME peer id the original
        // send resolved to (captured once, in DrawerManager's pending
        // record, at first-send time), never whatever ZNetView.InvokeRPC's
        // owner-resolving overload would return at retry time. Ownership
        // can legitimately migrate to a DIFFERENT, still-connected client
        // mid-request -- not just on disconnect, but any time the current
        // owner physically walks out of range (ZDOMan.ReleaseNearbyZDOS
        // runs on an ordinary ~2-second cadence, well inside this
        // protocol's retry window, for exactly that reason) -- and a retry
        // that re-resolved the owner fresh would then land on a SECOND,
        // independent owner with no memory of the first attempt, which can
        // durably double-apply the mutation (see this task's report on the
        // Critical this closes). Pinning every RETRY to the original
        // target closes that specific path: once a request is in flight
        // against a captured non-zero peer id, no second owner ever sees
        // it. If the pinned target has genuinely lost ownership by the
        // time a retry reaches it, RPC_RequestWithdraw/RPC_RequestDeposit's
        // own IsOwner() check refuses to apply anything and no reply is
        // sent -- the request simply keeps retrying against that same
        // (no-longer-owning) peer until this client gives up, rather than
        // silently succeeding via a second owner.
        //
        // Pinning a retry only works because the FIRST send already
        // resolved to a real, single peer. If the ZDO is UNOWNED at
        // first-send time (GetOwner() == 0 -- routine: ReleaseNearbyZDOS
        // sets owner to 0 whenever the previous owner leaves the active
        // area, before anyone else claims it), there is no single peer to
        // pin to at all: ZRoutedRpc treats target 0 as Everybody, which
        // broadcasts to every connected client and would defeat pinning
        // just as badly as a fresh per-retry resolve would -- every
        // connected peer would independently process the request and
        // reply, since target 0 is delivered locally AND rebroadcast to
        // every peer (see above). RequestWithdraw/RequestDeposit therefore
        // refuse to send at all when GetOwner() == 0, going straight to a
        // give-up (message, or spill for a deposit whose removal has
        // already happened) rather than ever broadcasting.
        //
        // See DrawerManager for where pending state and retries actually
        // live -- not on this component, precisely because this component
        // can be destroyed and recreated (a drawer leaving and re-entering
        // range) many times within one still-outstanding request's
        // lifetime, and a pending deposit dying with it would silently
        // destroy the player's already-removed items.
        internal const string RpcReqWithdraw = "RID_ReqWithdraw";
        internal const string RpcGrantWithdraw = "RID_GrantWithdraw";
        internal const string RpcReqDeposit = "RID_ReqDeposit";
        internal const string RpcGrantDeposit = "RID_GrantDeposit";
        private const string RpcReqClear = "RID_ReqClear";

        // How long this client waits for a reply before retrying (pinned
        // to the same target -- see above), and how many attempts before
        // giving up. Three attempts at five seconds each is generous
        // relative to a live peer's actual round-trip time, while still
        // resolving within a timeframe a player will tolerate.
        internal const float RequestTimeoutSeconds = 5f;
        internal const int MaxRequestAttempts = 3;

        // Owner-side idempotency: a request retried under the same
        // (sender, id) -- because this client's own reply was lost, or
        // simply because the requester's timeout fired before a live
        // reply arrived, not because the original was never processed --
        // must resend the SAME answer rather than debit the drawer a
        // second time. This is per-instance (not persisted across an
        // ownership hand-off) and does not need to be: since every retry
        // is now pinned to a single target peer (see above), only THIS
        // instance -- for as long as it remains the owner -- can ever see
        // retries of a given request; once ownership genuinely moves on,
        // pinned retries stop reaching an owner that can act on them at
        // all, so there is nothing left for a cache tied to the NEW
        // owner to deduplicate. Pruned by age so this cannot grow without
        // bound over a drawer's lifetime.
        private const float HandledRequestLifetimeSeconds = 60f;
        private readonly HandledRequestCache<(string ItemName, int Amount)> _handledWithdrawals =
            new HandledRequestCache<(string, int)>(HandledRequestLifetimeSeconds);
        private readonly HandledRequestCache<int> _handledDeposits =
            new HandledRequestCache<int>(HandledRequestLifetimeSeconds);

        private ZNetView _view;

        // Captured once in Awake and never re-read from _view afterward:
        // OnDestroy needs this drawer's ZDOID to tell DrawerManager which
        // pending requests to resolve immediately, and by the time
        // OnDestroy runs _view.GetZDO() may already be unusable.
        private ZDOID _zdoId = ZDOID.None;
        internal ZDOID ZdoId => _zdoId;

        internal DrawerTier Tier { get; private set; }

        /// <summary>
        /// The Inventory other mods see through Container.GetInventory.
        /// Null until Awake succeeds, or when Inventory internals could not
        /// be resolved (InventoryAccess.Available false).
        /// </summary>
        internal DrawerView View { get; private set; }

        // Set once ReconcileView has logged that it is holding back an
        // unreconciled view change it cannot apply (this client cannot
        // resolve the drawer's item); cleared when a reconcile runs normally.
        private bool _loggedUnresolvedPending;

        /// <summary>The drawer's face renderer, attached in DrawerPieces.BuildPrefab. Null until Awake runs.</summary>
        public DrawerRenderer Face { get; private set; }

        public DrawerSnapshot Snapshot => _view != null && _view.IsValid()
            ? new DrawerSnapshot(_view.GetZDO().GetString(KeyPrefabHash, ""), _view.GetZDO().GetInt(KeyAmountHash, 0))
            : new DrawerSnapshot("", 0);

        public int Capacity => DrawerConfig.CapacityFor(Tier);

        private new void Awake()
        {
            _view = GetComponent<ZNetView>();
            Face = GetComponentInChildren<DrawerRenderer>();

            if (_view == null || !_view.IsValid()) return;

            _zdoId = _view.GetZDO().m_uid;
            Tier = TierFromPrefabName(gameObject.name);

            // Conservative start: ownership observed at load counts as fresh.
            ResetOwnershipClock();

            // Restores exactly one fact Container.Awake would otherwise
            // establish: its own private m_nview field, which is simply a
            // cached GetComponent<ZNetView>() -- the same reference this
            // class already holds in _view. This is not faking state; it is
            // assigning the one true value that field is defined to hold.
            //
            // Why this matters: OttoFuel and NoVikingLeftBehind both
            // discover containers via a Harmony postfix on Container.Awake
            // (confirmed by decompiling both), and both ultimately depend on
            // Container.m_nview being non-null somewhere in their call chain
            // -- OttoFuel reads it via Harmony field-injection at
            // registration time, NVLB reads it one step later when turning a
            // registered container into a usable "box". Container.Awake's
            // body is never allowed to run for a drawer (see
            // ContainerBridge.AwakePatch, a Harmony prefix that returns
            // false only for DrawerComponent) -- that body would construct
            // a second, unused Inventory, register vanilla's own container
            // RPCs, and subscribe Container.OnDestroyed to WearNTear/
            // Destructible (risking a double-spill alongside
            // OnDrawerDestroyed below) -- so nothing else sets this field
            // for us. base.Awake() is still called, immediately after, so
            // that Harmony's postfixes on Container.Awake -- which is the
            // only thing that makes them run at all -- still fire; they
            // just find AwakePatch's prefix has already skipped past
            // everything else that method would have done.
            //
            // Every other Container member that reads m_nview or
            // m_inventory was audited against the decompiled Container
            // (1.0.12): each is either patched for a drawer (Awake's body,
            // GetInventory, Save, Load, CanBeRemoved), shadowed through the
            // re-declared interfaces (Interact, UseItem, GetHoverText,
            // GetHoverName), reachable only from Container.Awake's skipped
            // body or from RPCs that body registers (AddDefaultItems,
            // DropAllItems, OnDestroyed, CheckForChanges,
            // OnContainerChanged, UpdateRows, every RPC_* handler), or reads
            // only m_nview/plain fields (CheckAccess, IsOwner, IsInUse,
            // SetInUse, UpdateUseVisual, StackAll, TakeAll). m_inventory is
            // set below only when the GetInventory/Save/Load patches are in
            // place, so vanilla Save/Load can never serialize or overwrite
            // the view.
            // Guarded because this is the one name-based lookup in the mod
            // that is both load-bearing and on a hot path. FieldRefAccess
            // throws if "m_nview" is ever renamed, and unguarded that throw
            // lands inside Awake -- once per drawer, per load, as a raw
            // stack trace with nothing pointing at a game update as the
            // cause. ValheimCompat.Verify already reports the same problem
            // clearly at startup; this catch keeps the per-drawer noise
            // down to one line and, crucially, stops before base.Awake().
            //
            // Returning early rather than continuing is deliberate. With
            // m_nview unset, base.Awake() runs against a Container whose
            // view is null, and the skip-prefix that normally protects it
            // is itself registered by name -- so on the update that breaks
            // one, the other is likely broken too. An inert drawer is
            // recoverable; a half-initialised Container that vanilla code
            // then operates on is how items get eaten.
            try
            {
                AccessTools.FieldRefAccess<Container, ZNetView>(this, "m_nview") = _view;
            }
            catch (System.Exception ex)
            {
                DrawerPlugin.Log.LogError(
                    "Could not set Container.m_nview; this drawer stays inert. "
                    + $"See the compatibility check at startup. {ex.GetType().Name}: {ex.Message}");
                return;
            }

            // Before base.Awake(): other mods' Container.Awake postfixes may
            // call GetInventory straight away, and must get the view rather
            // than null. The constructor only stores references and builds an
            // empty Inventory; the first GetInventory call loads it.
            if (InventoryAccess.Available) View = new DrawerView(this, _view);

            // Container.m_inventory is the view's Inventory -- one instance
            // for the drawer's lifetime; DrawerView rebuilds it in place and
            // never replaces it. QuickStackStore (and likely other
            // container mods) read the field directly, and a null here threw
            // inside their loops, breaking quick-stack for every container.
            // Set only when ContainerBridge's GetInventory/Save/Load patches
            // are active: otherwise vanilla Save would write the view as
            // vanilla item bytes and vanilla Load would overwrite it with
            // stacks capped at max stack size (read as a huge withdrawal).
            if (View != null && ContainerBridge.ViewPatchesApplied)
            {
                try
                {
                    AccessTools.FieldRefAccess<Container, Inventory>(this, "m_inventory") = View.Inventory;
                }
                catch (System.Exception ex)
                {
                    DrawerPlugin.Log.LogError(
                        "Could not set Container.m_inventory; mods that read it directly will not see this drawer. "
                        + $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            // Before base.Awake(): OttoFuel registers containers in a
            // Container.Awake postfix and skips any whose ZDO has no creator
            // (unless the prefab name starts with piece_/Container). A freshly
            // placed piece gets its creator from Player.PlacePiece ->
            // Piece.SetCreator only after Instantiate returns, i.e. after
            // Awake -- so a drawer built this session was never registered
            // and OttoFuel ignored it until a reload. See
            // PieceSetCreatorPatch for how vanilla's own SetCreator still runs.
            EnsureCreator();
            base.Awake();

            // Container.Awake never runs its own body on this type (see
            // AwakePatch), so nothing else it would normally set up happens
            // for free: vanilla's own Inventory is never constructed (the
            // view is assigned above instead),
            // and there is no subscription to WearNTear.m_onDestroyed via
            // Container.OnDestroyed either -- confirmed explicitly, not
            // assumed: that subscription line lives inside the exact block
            // AwakePatch's prefix skips, so it still never executes even
            // though base.Awake() is now genuinely called. The latter
            // matters here: this class hooks that event itself, below, using
            // its own snapshot instead of Container.OnDestroyed's
            // null-checked m_inventory.
            var wearNTear = GetComponent<WearNTear>();
            if (wearNTear != null)
                wearNTear.m_onDestroyed = (Action)Delegate.Combine(wearNTear.m_onDestroyed, new Action(OnDrawerDestroyed));

            // RPC-to-owner mutation protocol -- see the big comment block
            // above RequestWithdraw for the design. Registered on every
            // client that has this drawer instantiated, since any of them
            // may end up owning the ZDO at some point (ownership migrates
            // by proximity -- confirmed by decompiling ZDOMan.ReleaseZDOS).
            _view.Register<long, int>(RpcReqWithdraw, RPC_RequestWithdraw);
            _view.Register<long, string, int>(RpcGrantWithdraw, RPC_GrantWithdraw);
            _view.Register<long, string, int>(RpcReqDeposit, RPC_RequestDeposit);
            _view.Register<long, int>(RpcGrantDeposit, RPC_GrantDeposit);
            _view.Register(RpcReqClear, RPC_RequestClear);

            All.Add(this);

            // Not RefreshFace() directly here: Unity does not guarantee
            // Awake order between sibling components on the same freshly
            // instantiated object, so Face's own DrawerRenderer.Awake may
            // not have configured its font/material yet when this runs.
            // Register() marks this drawer dirty instead, which defers the
            // actual refresh to DrawerManager's next Update() -- by which
            // point every Awake for this frame is guaranteed complete.
            DrawerManager.Instance?.Register(this);
        }

        /// <summary>
        /// Self-heal for a missing ZDO "creator" field. OttoFuel's
        /// registration filter and NoVikingLeftBehind's query-time filter
        /// both require this to be nonzero (confirmed by decompiling both);
        /// vanilla sets it via
        /// Player.PlacePiece -&gt; Piece.SetCreator for a normally-placed
        /// piece, which this class never touches or interferes with. This
        /// exists only to cover the gap for a drawer that reached this
        /// point WITHOUT going through that path -- confirmed to be exactly
        /// what DebugCommands.rid_wall did (Object.Instantiate directly,
        /// no SetCreator call at all) -- rather than leaving such a drawer
        /// permanently invisible to either integration.
        ///
        /// Mirrors Piece.SetCreator's own guard exactly: only touches the
        /// ZDO when it is currently zero AND this client owns it (never
        /// overwrite a creator another client already legitimately set, and
        /// never claim a ZDO we do not own to write to it). Reads the
        /// player id the same way Player.PlacePiece does
        /// (Game.instance.GetPlayerProfile().GetPlayerID()) so a
        /// self-healed drawer is attributed the same way a normally-placed
        /// one would be, not a synthetic value.
        /// </summary>
        private void EnsureCreator()
        {
            if (_view == null || !_view.IsValid()) return;

            var zdo = _view.GetZDO();
            if (!_view.IsOwner() || zdo.GetLong(CreatorHash, 0L) != 0L) return;

            // global:: required -- unqualified Game resolves to this file's
            // own ItemDrawers.Game namespace, not the Valheim Game class
            // (same trap as global::Game.m_worldLevel elsewhere in this
            // file).
            var profile = global::Game.instance != null ? global::Game.instance.GetPlayerProfile() : null;
            if (profile == null) return;

            zdo.Set(CreatorHash, profile.GetPlayerID());
        }

        private void OnDestroy()
        {
            // Fallback only. On a normal unload ZNetScene calls
            // ZNetView.ResetZDO before Object.Destroy, so by now the ZDO is
            // gone and this does nothing; ZNetViewResetZdoPatch flushes the
            // view before that happens. This still covers a destroy that
            // bypasses ZNetScene with the ZDO intact.
            FlushView(teardown: true);

            All.Remove(this);
            DrawerManager.Instance?.Unregister(this);

            // A pending deposit's payload lives on DrawerManager (which
            // survives this component's destruction), not on this
            // component -- but nothing will ever retry or time it out if
            // this drawer never becomes locally instantiated again (it
            // left the active area, the world unloaded, the player
            // quit...). Resolve it NOW rather than leaving it to wait out
            // a deadline that may never usefully arrive: a deposit spills
            // its already-removed items immediately; a withdrawal (which
            // gave nothing to anyone yet) is simply dropped.
            if (!_zdoId.IsNone())
                DrawerManager.Instance?.ResolvePendingForDrawer(_zdoId);
        }

        /// <summary>
        /// Fired by WearNTear.Destroy via the m_onDestroyed subscription set
        /// up in Awake. Spills the drawer's contents at its own position
        /// rather than letting them vanish with the destroyed ZDO -- a
        /// destroyed drawer full of items is exactly the kind of loss this
        /// mod exists to prevent. Guarded by IsOwner() the same way
        /// Container.OnDestroyed is, so only one client in a multiplayer
        /// session performs the spill.
        /// </summary>
        private void OnDrawerDestroyed()
        {
            if (_view == null || !_view.IsOwner()) return;

            // Fold any not-yet-reconciled view change into the stock first,
            // so it is spilled too rather than lost with the ZDO. Forced:
            // there is no later moment to wait for ownership to settle.
            ReconcileView(force: true);

            var s = Snapshot;
            if (s.IsAssigned && s.Amount > 0)
                ItemFacts.SpillAtPosition(transform.position, s.ItemName, s.Amount);
        }

        private static DrawerTier TierFromPrefabName(string name)
        {
            foreach (var tier in DrawerTiers.All)
                if (name.StartsWith(DrawerTiers.PrefabName(tier)))
                    return tier;
            return DrawerTier.Wood;
        }

        /// <summary>
        /// The one place drawer state is written. Requires this client to
        /// already own the ZDO (every caller checks IsOwner() itself
        /// before reaching here, but this re-checks rather than trusting
        /// that) and performs a compare-and-swap against
        /// <paramref name="expected"/>, the Snapshot the caller's decision
        /// was actually computed from.
        ///
        /// BE PRECISE ABOUT WHAT THIS CAS DOES AND DOES NOT PROTECT
        /// AGAINST -- this project has already shipped one CAS comment
        /// (the original Commit, now deleted) whose claimed protection
        /// turned out not to hold, and review correctly refused to let a
        /// second one repeat that. The re-read here is of THIS CLIENT'S
        /// OWN LOCAL COPY of the ZDO's fields. It guards LOCAL
        /// RE-ENTRANCY: two mutations reaching this method on this
        /// client's own single thread between when a caller's `expected`
        /// snapshot was read and when this call runs (Valheim's game
        /// logic is single-threaded per client, so this can only happen
        /// via nested/re-entrant calls, not true concurrency).
        ///
        /// It does NOT, and cannot, guard against two DIFFERENT clients
        /// both genuinely owning this ZDO during a split-ownership window
        /// and both writing. ZDO.SetOwner -> IncreaseOwnerRevision ->
        /// ZDOMan.ClientChanged propagates an ownership change to every
        /// other client with NO server arbitration (ZDOMan.RPC_ZDOData
        /// accepts whichever OwnerRevision it receives is higher,
        /// unconditionally -- confirmed by decompile), so two clients can
        /// briefly both observe IsOwner()==true for the same ZDO. If both
        /// call WriteOwned during that window, EACH ONE'S CAS only ever
        /// re-reads its OWN local ZDO copy -- which may not yet reflect
        /// the other client's write at all -- so both CAS checks can pass
        /// and both writes can go out. Whichever write's DataRevision
        /// (not this CAS) is later accepted as higher by
        /// ZDOMan.RPC_ZDOData on every other peer is the one that
        /// survives; the other is silently discarded by that same
        /// highest-revision-wins rule, not by anything this method does.
        /// This CAS therefore contributes nothing to closing that window;
        /// it exists solely for the local re-entrancy case above, which is
        /// real but narrower than "concurrent clients."
        /// </summary>
        private bool WriteOwned(DrawerSnapshot expected, DrawerSnapshot next)
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return false;

            var zdo = _view.GetZDO();
            var actual = new DrawerSnapshot(zdo.GetString(KeyPrefabHash, ""), zdo.GetInt(KeyAmountHash, 0));
            if (!actual.Equals(expected)) return false;

            WriteState(next);

            // Every change the drawer's own code makes to Amount republishes
            // the container view and its baseline. Reconciling (rather than
            // only publishing) first credits or debits anything another mod
            // changed through the view that has not been reconciled yet --
            // publishing alone would overwrite that change and lose or
            // duplicate it. The pre-write item name is passed on: after a
            // Clear the drawer has no item, and a pending deposit must still
            // be spilled as the item it was.
            ReconcileView(actual.ItemName);
            return true;
        }

        /// <summary>
        /// Writes drawer state with no compare-and-swap. Callers must own the
        /// ZDO: WriteOwned (after its CAS) and ReconcileView.
        /// </summary>
        private void WriteState(DrawerSnapshot next)
        {
            var zdo = _view.GetZDO();
            zdo.Set(KeyPrefabHash, next.ItemName);
            zdo.Set(KeyAmountHash, next.Amount);

            DrawerManager.Instance?.MarkDirty(this);
            RefreshFace();
        }

        /// <summary>
        /// Repaints the face from the current ZDO state. Cheap to call often:
        /// DrawerRenderer.Show no-ops when nothing changed. Called directly
        /// after a local WriteOwned, and periodically by DrawerManager for
        /// drawers changed by other clients.
        /// </summary>
        public void RefreshFace()
        {
            if (Face == null) return;
            var s = Snapshot;
            Face.Show(s.ItemName, s.Amount);
        }

        // ---------- player interaction ----------

        /// <summary>
        /// Take-one is bound to Ctrl, not Alt, because Valheim 1.0 has no
        /// Alt binding at all. `alt` here is sourced from the "AltPlace"
        /// input action (decompiled from Player.Update:
        /// <c>alt = ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyAltPlace")</c>
        /// on keyboard/mouse) -- "alternative placement", not the Alt key --
        /// and the user's own registry confirms both
        /// <c>kbmBinding_AltPlace</c> and <c>kbmBinding_Run</c> are bound to
        /// <c>&lt;Keyboard&gt;/LeftShift</c>, the same physical key. There is
        /// also no "Sneak" button in Valheim 1.0 at all (an earlier version
        /// of this method checked that nonexistent name, which was always
        /// false). Under default bindings `alt` and Shift are therefore the
        /// same signal, and makail's original "Alt+Interact" was never
        /// actually reading a distinct Alt key even in the original mod.
        ///
        /// Take-one is read directly via <c>ZInput.GetButton("Crouch")</c>
        /// instead, which the same registry confirms is bound to
        /// <c>&lt;Keyboard&gt;/LeftCtrl</c> by default -- a key genuinely
        /// distinct from both Run and AltPlace. Reading the named button
        /// rather than a raw KeyCode means a player who rebinds Crouch gets
        /// the rebound key, the same way Shift here follows a rebound Run.
        /// `alt` itself is now unused for this purpose.
        ///
        /// Ctrl and Shift are physically distinct keys, but a player can
        /// still hold both at once, so the check order below is a
        /// deliberate, defined choice rather than an accident of which
        /// branch happens to run first: Ctrl (take one) wins over Shift
        /// (deposit all) if both are held.
        /// </summary>
        public new bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            var player = user as Player;
            if (player == null) return false;

            var current = Snapshot;

            bool ctrlHeld = ZInput.GetButton("Crouch");
            bool shiftHeld = ZInput.GetButton("Run");

            // Ctrl checked first: see this method's docstring for the
            // defined precedence when both modifiers are held.
            if (ctrlHeld)
            {
                // An unassigned drawer has nothing to clear and nothing to
                // take -- without this guard, requesting Clear on an
                // already-unassigned snapshot would round-trip to the
                // owner for no reason.
                if (!current.IsAssigned) return true;

                if (current.IsEmpty)
                {
                    RequestClear(current);
                    return true;
                }

                BeginWithdraw(player, current, DrawerState.WithdrawOne(current));
                return true;
            }

            if (shiftHeld)
                return DepositEverythingMatching(player, current);

            int maxStack = ItemFacts.MaxStackSize(current.ItemName);
            BeginWithdraw(player, current, DrawerState.WithdrawStack(current, maxStack));
            return true;
        }

        // UseItem is part of Interactable (like Interact below) but Container's
        // implementation is sealed (virtual + final in the compiled assembly),
        // so `override` is rejected by the compiler. `new` plus re-declaring
        // Interactable on this class is what makes the interface dispatch
        // reach this method instead of Container's.
        public new bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            var player = user as Player;
            if (player == null || item == null) return false;

            // Checked before anything is removed from the player, not just
            // inside Commit: if another player or a troll destroys this
            // drawer between the interaction starting and RemoveItem
            // running, _view is non-null but ZNetView.IsValid() goes false
            // the moment ZDOMan invalidates the ZDO, Snapshot silently
            // degrades to an empty ("", 0) snapshot, DrawerState.Deposit
            // accepts against that empty snapshot, RemoveItem succeeds, and
            // only then does Commit refuse -- taking the player's items
            // with no drawer ever crediting them. This does not make the
            // whole operation atomic (the ZDO can still be invalidated
            // between this check and Commit), but it shrinks a window that
            // was open for as long as a drawer stayed rendered down to a
            // single frame.
            if (_view == null || !_view.IsValid())
            {
                player.Message(MessageHud.MessageType.Center, CommitFailedMessage);
                return true;
            }

            // Checked before anything leaves the player, so a refusal keeps
            // the items in their inventory. See RefuseWhileUnsettled.
            if (RefuseWhileUnsettled(player)) return true;

            if (item.m_shared.m_maxStackSize <= 1)
            {
                player.Message(MessageHud.MessageType.Center,
                    "Drawers only hold stackable items");
                return true;
            }

            // Only the prefab name is ever written to a drawer's ZDO.
            // m_shared.m_name is a localization token; if
            // that were persisted instead, every later ObjectDB lookup for
            // this drawer would fail and withdrawal would destroy its
            // contents. Refuse rather than guess when it can't be determined.
            string itemName = ItemFacts.PrefabNameOf(item);
            if (itemName == null)
            {
                player.Message(MessageHud.MessageType.Center, "Cannot identify this item");
                return true;
            }

            var current = Snapshot;

            // Rejected early, with the specific item named, rather than
            // falling through to DrawerState.Deposit's generic "This drawer
            // holds a different item" message. Same rule DrawerState.Deposit
            // itself enforces (a mismatched item is refused either way);
            // this only improves what the player is told and stops short of
            // running the rest of the deposit path for a request that was
            // never going to succeed.
            if (current.IsAssigned && current.ItemName != itemName)
            {
                player.Message(MessageHud.MessageType.Center,
                    $"This drawer holds {ItemFacts.LocalizedName(current.ItemName)}");
                return true;
            }

            var outcome = DrawerState.Deposit(current, Capacity, itemName, item.m_stack);
            if (!outcome.Accepted)
            {
                player.Message(MessageHud.MessageType.Center, outcome.Rejection);
                return true;
            }

            // Removed before the drawer is credited -- same reasoning as
            // DepositEverythingMatching (see its docstring): crediting
            // first and removing after would let a failed RemoveItem
            // duplicate the offered stack. RemoveItem(ItemData, int) is
            // atomic (confirmed against the decompiled body: it either
            // removes exactly the clamped amount and returns true, or -- if
            // the item is no longer in this inventory -- changes nothing
            // and returns false), so unlike the deposit-all path there is
            // no partial-removal case to reconcile here.
            if (!player.GetInventory().RemoveItem(item, outcome.MovedToDrawer))
            {
                player.Message(MessageHud.MessageType.Center, "Could not remove item");
                return true;
            }

            // outcome.Result is never written anywhere from here on: it
            // was only ever a local, possibly-stale guess used to decide
            // how much room to expect and how much to remove above. The
            // credit itself goes through RequestDeposit, which asks
            // whoever actually owns the ZDO to apply
            // DrawerState.Deposit again against ITS live state and refunds
            // any shortfall -- see RequestDeposit's docstring.
            RequestDeposit(player, itemName, outcome.MovedToDrawer, announce: false);
            return true;
        }

        /// <summary>
        /// Shift+Interact: deposit every matching item in the player's
        /// inventory. Removal happens BEFORE the drawer is credited, and
        /// the drawer is credited with exactly what was actually removed --
        /// never with what we merely hoped to remove. An earlier version
        /// committed the hoped-for amount first and removed afterward,
        /// logging (but not correcting) any shortfall: the drawer had
        /// already been credited by the time a failed removal was detected,
        /// which is duplication, not just a bookkeeping error.
        ///
        /// The RPC-to-owner rework this docstring used to defer to has
        /// landed: the credit below goes through RequestDeposit, which
        /// clamps against the owner's own live state and refunds any
        /// shortfall (including the previously-accepted loss window --
        /// this method's own RemoveItem calls fire Inventory's
        /// m_onChanged, so a third-party mod's re-entrant mutation between
        /// the snapshot read above and the credit below is no longer a
        /// tautology-defeated race: the owner sees its own current state
        /// regardless of what this client believed).
        /// </summary>
        private bool DepositEverythingMatching(Player player, DrawerSnapshot current)
        {
            // Same pre-flight as UseItem, and for the same reason: this
            // must be checked before any RemoveItem runs below. See
            // UseItem's comment on this check for the full failure
            // sequence it closes.
            if (_view == null || !_view.IsValid())
            {
                player.Message(MessageHud.MessageType.Center, CommitFailedMessage);
                return true;
            }

            // Before any RemoveItem, so a refusal keeps the items with the player.
            if (RefuseWhileUnsettled(player)) return true;

            if (!current.IsAssigned)
            {
                player.Message(MessageHud.MessageType.Center, DrawerState.NotAssigned);
                return true;
            }

            var inventory = player.GetInventory();
            var matching = new List<ItemDrop.ItemData>();
            int total = 0;
            foreach (var item in inventory.GetAllItems())
            {
                if (ItemFacts.PrefabNameOf(item) != current.ItemName) continue;
                matching.Add(item);
                total += item.m_stack;
            }

            if (total <= 0)
            {
                player.Message(MessageHud.MessageType.Center, "You have none of those");
                return true;
            }

            // How much the drawer could accept, capped by capacity. This is
            // only ever used as an upper bound on how much to try to remove
            // below -- nothing is taken from the player and nothing is
            // committed to the drawer based on this number alone.
            var wanted = DrawerState.Deposit(current, Capacity, current.ItemName, total);
            if (!wanted.Accepted)
            {
                player.Message(MessageHud.MessageType.Center, wanted.Rejection);
                return true;
            }

            // Removal by ItemData reference (the same overload UseItem
            // uses), never Inventory.RemoveItem(string, ...): that string
            // overload compares against m_shared.m_name (a localization
            // token), while current.ItemName is a prefab name -- they never
            // match, nothing is removed, and the call returns void so the
            // failure is silent. That mismatch is exactly what let
            // Shift+Interact create items in an earlier version.
            int target = wanted.MovedToDrawer;
            int removed = 0;
            foreach (var item in matching)
            {
                if (removed >= target) break;
                int take = item.m_stack < (target - removed) ? item.m_stack : (target - removed);
                // RemoveItem returns false, unchanged, if the item is no
                // longer in this inventory (e.g. another mod's m_onChanged
                // hook moved or consumed it between the counting pass above
                // and here). Only count an actual removal -- removed is what
                // gets committed to the drawer below, so this loop can never
                // credit the drawer for more than genuinely left the player.
                if (!inventory.RemoveItem(item, take)) continue;
                removed += take;
            }

            if (removed <= 0)
            {
                player.Message(MessageHud.MessageType.Center, "You have none of those");
                return true;
            }

            // The removal is done and cannot be undone from here; what
            // remains is getting it credited somewhere. No second local
            // Deposit precheck is needed before that: RequestDeposit (and
            // the owner-side handler behind it) applies DrawerState.Deposit
            // itself, against whatever `removed` genuinely left the player
            // and against the owner's own live Snapshot -- exactly what
            // the old precheck here existed to double-confirm locally, now
            // authoritative rather than advisory. A refusal there (e.g. the
            // drawer filled or was reassigned in the interim) refunds the
            // shortfall to the player rather than logging an "unreachable"
            // error and leaving items gone.
            RequestDeposit(player, current.ItemName, removed, announce: true);
            return true;
        }

        /// <summary>
        /// Entry point for every player-facing withdrawal (take-one,
        /// take-stack). `outcome` here is only used to learn HOW MUCH to
        /// ask for and to surface a same-client refusal (not assigned /
        /// empty) without a network round trip -- outcome.Result is never
        /// written anywhere. The ZDO owner (possibly this client, possibly
        /// not) makes the real decision against its own live state; see
        /// RequestWithdraw.
        /// </summary>
        private void BeginWithdraw(Player player, DrawerSnapshot current, DrawerOutcome outcome)
        {
            if (!outcome.Accepted)
            {
                player.Message(MessageHud.MessageType.Center, outcome.Rejection);
                return;
            }

            if (outcome.MovedToPlayer <= 0) return;

            // Resolved before the request is sent, not after a grant comes
            // back: GiveToPlayer returns silently if it can't find an
            // ItemDrop to give, which would otherwise mean the owner's ZDO
            // already dropped with nothing handed to the player -- silent
            // item loss. This must test the exact same predicate
            // GiveToPlayer's own guard uses (ItemFacts.Drop, not just
            // ItemFacts.Prefab) -- an item prefab that resolves but has no
            // ItemDrop component would otherwise pass this check and then
            // be silently swallowed by the guard inside GiveToPlayer after
            // the withdrawal already granted.
            if (ItemFacts.Drop(current.ItemName) == null)
            {
                player.Message(MessageHud.MessageType.Center, "Cannot identify this item");
                return;
            }

            RequestWithdraw(player, current.ItemName, outcome.MovedToPlayer);
        }

        /// <summary>
        /// The heart of the RPC-to-owner withdrawal protocol. When this
        /// client owns the ZDO -- the common case, and the only case in
        /// single-player -- this is a direct local call: no RPC, no delay,
        /// no reply to wait for. Otherwise a request naming only the
        /// AMOUNT WANTED (never an absolute target) is sent to the owner,
        /// who clamps it against its own live Snapshot, debits, and
        /// replies with what it actually granted. Only that reply -- never
        /// this call -- gives the player anything; see RPC_RequestWithdraw
        /// / RPC_GrantWithdraw below.
        ///
        /// Pending state (who to give items to, how much was requested,
        /// which peer this request is pinned to) lives on DrawerManager,
        /// not on this component -- see the class-level RPC comment for
        /// why: this component's own lifetime is tied to the drawer being
        /// locally instantiated, which a still-outstanding request must
        /// not depend on.
        /// </summary>
        private void RequestWithdraw(Player player, string itemName, int requested)
        {
            if (_view == null || !_view.IsValid() || requested <= 0) return;

            if (_view.IsOwner())
            {
                // Nothing has been given yet, so a refusal costs nothing.
                if (RefuseWhileUnsettled(player)) return;

                var current = Snapshot;
                var outcome = DrawerState.WithdrawExact(current, requested);
                if (outcome.MovedToPlayer <= 0) return;
                if (!WriteOwned(current, outcome.Result)) return;
                ItemFacts.GiveToPlayer(player, current.ItemName, outcome.MovedToPlayer);
                return;
            }

            var manager = DrawerManager.Instance;
            if (manager == null) return;

            long targetOwner = _view.GetZDO().GetOwner();
            if (targetOwner == 0L)
            {
                // See the class-level RPC comment on why 0 (no owner) is
                // refused outright rather than sent: nothing was removed
                // from the player yet for a withdrawal, so failing
                // immediately costs nothing.
                player.Message(MessageHud.MessageType.Center, CommitFailedMessage);
                return;
            }

            long id = manager.NextRequestId();
            manager.AddPendingWithdrawal(id, new PendingWithdrawal(
                drawerId: _zdoId, targetOwner: targetOwner, player: player,
                requested: requested, playerPosition: player.transform.position));
            SendWithdrawRequestRpc(id, targetOwner, requested);
        }

        /// <summary>
        /// Sends (or resends) a withdraw request to an EXPLICIT peer,
        /// never re-resolving the ZDO's current owner -- used for both the
        /// very first send above and every pinned retry DrawerManager
        /// issues later. See the class-level comment on the RPC protocol
        /// for why a retry must never target a freshly-resolved owner.
        /// </summary>
        internal void SendWithdrawRequestRpc(long id, long targetOwner, int requested)
        {
            if (_view != null && _view.IsValid())
                _view.InvokeRPC(targetOwner, RpcReqWithdraw, id, requested);
        }

        /// <summary>
        /// Runs on whichever client currently owns this drawer's ZDO --
        /// re-checked here, not assumed, because a ZDO with NO owner routes
        /// this call to every connected client (see the class-level
        /// comment on the RPC protocol). Clamps the request against the
        /// CURRENT Snapshot -- never anything the requester believed --
        /// so a stale or racing request can only ever be granted less,
        /// never more, than truly exists: this is what makes two players
        /// racing the same drawer conserve items instead of duplicating
        /// them. Idempotent per (sender, id): a retried request (see
        /// DrawerManager's retry loop) resends the same answer instead of
        /// debiting the drawer twice -- safe to rely on here because every
        /// retry is pinned to this exact peer (see the class-level comment
        /// on the RPC protocol for why a NEW owner never sees a retry of a
        /// request an OLD owner already started answering).
        ///
        /// Refuses to grant anything for an item this client cannot
        /// resolve to a real prefab (ItemFacts.Drop == null): granting
        /// against an unresolvable item would debit the drawer for
        /// something RPC_GrantWithdraw could never actually hand to
        /// anyone, since ItemFacts.GiveToPlayer silently no-ops on the
        /// exact same check.
        /// </summary>
        private void RPC_RequestWithdraw(long sender, long id, int requested)
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return;

            _handledWithdrawals.Prune(Time.time);
            if (_handledWithdrawals.TryGet(sender, id, out var cached))
            {
                _view.InvokeRPC(sender, RpcGrantWithdraw, id, cached.ItemName, cached.Amount);
                return;
            }

            // No reply while ownership is unsettled: nothing is recorded, so
            // the requester's pinned retry (or its give-up, which gives
            // nothing for a withdrawal) is handled normally later.
            if (!OwnershipSettled()) return;

            var current = Snapshot;
            int granted = 0;
            if (current.IsAssigned && ItemFacts.Drop(current.ItemName) != null)
            {
                var outcome = DrawerState.WithdrawExact(current, requested);
                if (outcome.MovedToPlayer > 0 && WriteOwned(current, outcome.Result))
                    granted = outcome.MovedToPlayer;
            }

            _handledWithdrawals.Record(sender, id, (current.ItemName, granted), Time.time);
            _view.InvokeRPC(sender, RpcGrantWithdraw, id, current.ItemName, granted);
        }

        /// <summary>
        /// Runs on the requester. The only place a withdrawal ever reaches
        /// the player's inventory -- see RequestWithdraw's docstring for
        /// why that ordering is load-bearing. Ignores a reply for an id
        /// that is not (or is no longer) pending: either a duplicate/late
        /// reply after this client already gave up (see
        /// DrawerManager.RetryWithdrawal/GiveUpWithdrawal), or a reply to a
        /// request this client never made (defensive only). Clamps the
        /// granted amount to what was actually requested (defence in depth
        /// against a stale replayed answer -- see HandledRequestCache's
        /// docstring).
        ///
        /// Also logs if the reply did not come from the peer this request
        /// was pinned to. This is no longer merely a diagnostic hedge: now
        /// that RequestWithdraw refuses to send at all when the ZDO is
        /// unowned (see the class-level RPC comment on why -- an unowned
        /// ZDO's target-0 broadcast used to be the one LEGITIMATE way a
        /// reply could arrive from someone other than the pinned peer),
        /// every request that reaches this handler was sent to exactly one
        /// peer and stays pinned to it across every retry. A mismatch here
        /// therefore has no innocent explanation left -- it is a genuine
        /// anomaly (a stale id, a routing bug, or a regression in the
        /// pinning itself), worth investigating rather than shrugging off.
        /// </summary>
        private void RPC_GrantWithdraw(long sender, long id, string itemName, int granted)
        {
            var manager = DrawerManager.Instance;
            if (manager == null || !manager.TryCompleteWithdrawal(id, out var pending)) return;

            if (sender != pending.TargetOwner)
                DrawerPlugin.Log.LogWarning(
                    $"RPC_GrantWithdraw: reply for request {id} came from peer {sender}, not the pinned target {pending.TargetOwner}.");

            int clamped = granted < 0 ? 0 : Math.Min(granted, pending.Requested);
            if (granted != clamped)
                DrawerPlugin.Log.LogWarning(
                    $"RPC_GrantWithdraw: request {id} asked for {pending.Requested} but was granted {granted}; clamped to {clamped}.");

            if (clamped <= 0) return;

            if (pending.Player != null)
                ItemFacts.GiveToPlayer(pending.Player, itemName, clamped);
            else
                ItemFacts.SpillAtPosition(pending.PlayerPosition, itemName, clamped);
        }

        /// <summary>
        /// Mirror image of RequestWithdraw. The caller has ALREADY removed
        /// <paramref name="removed"/> of <paramref name="itemName"/> from
        /// the player's inventory -- this method never removes anything
        /// itself, only tries to get it credited somewhere. When this
        /// client owns the ZDO, that is an immediate local credit.
        /// Otherwise it is a request to the owner, who clamps to its own
        /// remaining capacity and replies with what it actually accepted;
        /// any shortfall between <paramref name="removed"/> and the
        /// accepted amount is refunded to the player (see RPC_GrantDeposit
        /// / DrawerManager.GiveUpDeposit for the two ways that refund can
        /// happen).
        /// <paramref name="announce"/> controls whether a successful
        /// credit shows a "Stored N" toast (DepositEverythingMatching does;
        /// UseItem never did and still doesn't) -- shown against the REAL
        /// accepted amount, once it is known, never the requested one.
        /// </summary>
        private void RequestDeposit(Player player, string itemName, int removed, bool announce)
        {
            if (removed <= 0) return;

            if (_view == null || !_view.IsValid())
            {
                // The drawer is gone; there is nowhere to credit this and
                // no owner to ask. Spill immediately rather than losing it
                // -- same reasoning as DrawerManager.GiveUpDeposit, just
                // with zero retries to wait out first.
                ItemFacts.SpillAtPosition(player.transform.position, itemName, removed);
                player.Message(MessageHud.MessageType.Center, CommitFailedMessage);
                return;
            }

            if (_view.IsOwner())
            {
                var current = Snapshot;
                var outcome = DrawerState.Deposit(current, Capacity, itemName, removed);

                // UseItem/DepositEverythingMatching already refuse before
                // removing anything; this covers ownership becoming unsettled
                // between that check and here. Accepting 0 refunds the whole
                // removal through the shortfall path below.
                bool settled = OwnershipSettled();
                if (!settled) player.Message(MessageHud.MessageType.Center, CommitFailedMessage);

                int accepted = (settled && outcome.Accepted && WriteOwned(current, outcome.Result)) ? outcome.MovedToDrawer : 0;

                int shortfall = DrawerState.RefundShortfall(removed, accepted);
                if (shortfall > 0) ItemFacts.GiveToPlayer(player, itemName, shortfall);
                if (announce) AnnounceDeposit(player, itemName, accepted);
                return;
            }

            var manager = DrawerManager.Instance;
            if (manager == null)
            {
                ItemFacts.SpillAtPosition(player.transform.position, itemName, removed);
                player.Message(MessageHud.MessageType.Center, CommitFailedMessage);
                return;
            }

            long targetOwner = _view.GetZDO().GetOwner();
            if (targetOwner == 0L)
            {
                // See the class-level RPC comment on why 0 (no owner) is
                // refused outright rather than sent -- ZRoutedRpc routes
                // target 0 as Everybody, broadcasting to every connected
                // peer on the first send AND every retry, which is
                // precisely the multi-owner double-apply pinning exists to
                // prevent. Unlike the withdraw case, the player's items
                // were already removed for this deposit, so this must
                // spill rather than merely fail.
                ItemFacts.SpillAtPosition(player.transform.position, itemName, removed);
                player.Message(MessageHud.MessageType.Center, CommitFailedMessage);
                return;
            }

            long id = manager.NextRequestId();
            manager.AddPendingDeposit(id, new PendingDeposit(
                drawerId: _zdoId, targetOwner: targetOwner, player: player, itemName: itemName,
                removed: removed, playerPosition: player.transform.position, announce: announce));
            SendDepositRequestRpc(id, targetOwner, itemName, removed);
        }

        /// <summary>
        /// Sends (or resends) a deposit request to an EXPLICIT peer -- the
        /// deposit-side counterpart of SendWithdrawRequestRpc; see its
        /// docstring.
        /// </summary>
        internal void SendDepositRequestRpc(long id, long targetOwner, string itemName, int removed)
        {
            if (_view != null && _view.IsValid())
                _view.InvokeRPC(targetOwner, RpcReqDeposit, id, itemName, removed);
        }

        /// <summary>
        /// Owner side of RequestDeposit. See RPC_RequestWithdraw for the
        /// idempotency/ownership-recheck/pinned-retry reasoning; it applies
        /// identically here.
        /// </summary>
        private void RPC_RequestDeposit(long sender, long id, string itemName, int amount)
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return;

            _handledDeposits.Prune(Time.time);
            if (_handledDeposits.TryGet(sender, id, out var cachedAccepted))
            {
                _view.InvokeRPC(sender, RpcGrantDeposit, id, cachedAccepted);
                return;
            }

            // No reply while ownership is unsettled: nothing is recorded, so
            // a pinned retry after the settle is processed normally, and if
            // every retry is used up the requester's GiveUpDeposit spills
            // the already-removed items, exactly as for any unanswered request.
            if (!OwnershipSettled()) return;

            var current = Snapshot;
            var outcome = DrawerState.Deposit(current, Capacity, itemName, amount);
            int accepted = (outcome.Accepted && WriteOwned(current, outcome.Result)) ? outcome.MovedToDrawer : 0;

            _handledDeposits.Record(sender, id, accepted, Time.time);
            _view.InvokeRPC(sender, RpcGrantDeposit, id, accepted);
        }

        /// <summary>
        /// Runs on the requester. Refunds any shortfall between what was
        /// removed from the player and what the owner actually accepted --
        /// this is the ordinary, expected way a deposit reply arrives. See
        /// DrawerManager.GiveUpDeposit for what happens when no reply ever
        /// comes. Clamps and cross-checks the sender the same way
        /// RPC_GrantWithdraw does; see that method's docstring.
        /// </summary>
        private void RPC_GrantDeposit(long sender, long id, int accepted)
        {
            var manager = DrawerManager.Instance;
            if (manager == null || !manager.TryCompleteDeposit(id, out var pending)) return;

            if (sender != pending.TargetOwner)
                DrawerPlugin.Log.LogWarning(
                    $"RPC_GrantDeposit: reply for request {id} came from peer {sender}, not the pinned target {pending.TargetOwner}.");

            int clamped = accepted < 0 ? 0 : Math.Min(accepted, pending.Removed);
            if (accepted != clamped)
                DrawerPlugin.Log.LogWarning(
                    $"RPC_GrantDeposit: request {id} offered {pending.Removed} but {accepted} was reported accepted; clamped to {clamped}.");

            int shortfall = DrawerState.RefundShortfall(pending.Removed, clamped);
            if (shortfall > 0)
            {
                if (pending.Player != null) ItemFacts.GiveToPlayer(pending.Player, pending.ItemName, shortfall);
                else ItemFacts.SpillAtPosition(pending.PlayerPosition, pending.ItemName, shortfall);
            }

            if (pending.Announce && pending.Player != null)
                AnnounceDeposit(pending.Player, pending.ItemName, clamped);
        }

        private static void AnnounceDeposit(Player player, string itemName, int accepted)
        {
            if (accepted <= 0) return;
            player.Message(MessageHud.MessageType.TopLeft, $"Stored {accepted} {ItemFacts.LocalizedName(itemName)}");
        }

        /// <summary>
        /// Ctrl+Interact at zero. Fire-and-forget, unlike the withdraw/
        /// deposit protocol above: no items ever move for a Clear, so
        /// there is nothing to reconcile if this never arrives (owner
        /// unreachable) or is refused (something was deposited into the
        /// drawer microseconds before the owner processed this). Either
        /// way this client's own view of the drawer self-corrects from the
        /// next ordinary ZDO sync.
        /// </summary>
        private void RequestClear(DrawerSnapshot current)
        {
            if (_view == null || !_view.IsValid()) return;

            if (_view.IsOwner())
            {
                if (!OwnershipSettled())
                {
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, CommitFailedMessage);
                    return;
                }

                var outcome = DrawerState.Clear(current);
                if (outcome.Accepted) WriteOwned(current, outcome.Result);
                return;
            }

            _view.InvokeRPC(RpcReqClear);
        }

        private void RPC_RequestClear(long sender)
        {
            // Clear is fire-and-forget; an unsettled owner simply ignores it.
            if (_view == null || !_view.IsValid() || !_view.IsOwner() || !OwnershipSettled()) return;

            var current = Snapshot;
            var outcome = DrawerState.Clear(current);
            if (outcome.Accepted) WriteOwned(current, outcome.Result);
        }

        // ---------- external access, used by the Container bridge ----------
        // ItemDrawersAPI and auto-pickup reach the drawer through these
        // methods, ContainerBridge through the container view below -- never
        // by poking the ZDO directly.

        /// <summary>
        /// Withdraws up to <paramref name="requested"/> on behalf of
        /// ItemDrawersAPI.Withdraw. Clamps rather than refusing (see
        /// DrawerState.WithdrawExact). When this client does not own the
        /// drawer it claims it and returns false (nothing withdrawn); it
        /// also returns false until ownership has settled
        /// (OwnershipSettleSeconds). The caller retries later -- the only
        /// caller, ItemDrawersAPI.Withdraw, simply reports less taken and
        /// hands nothing out for a false. Claiming and debiting in the same
        /// frame would race the previous owner's own absolute write of
        /// Amount, and on a tie the debit is discarded after the items were
        /// handed out. WriteOwned's compare-and-swap refuses a write
        /// computed from a stale snapshot.
        /// </summary>
        public bool TryWithdrawExternally(int requested, out int taken)
        {
            taken = 0;
            if (_view == null || !_view.IsValid()) return false;

            if (!_view.IsOwner())
            {
                _view.ClaimOwnership();
                return false;
            }
            if (!OwnershipSettled()) return false;

            var current = Snapshot;
            var outcome = DrawerState.WithdrawExact(current, requested);
            if (outcome.MovedToPlayer <= 0) return false;
            if (!WriteOwned(current, outcome.Result)) return false;

            taken = outcome.MovedToPlayer;
            return true;
        }

        /// <summary>
        /// How an automated deposit into THIS drawer has to be routed right
        /// now, which depends entirely on who owns its ZDO.
        /// </summary>
        internal enum DepositRoute
        {
            /// <summary>We own it: write directly.</summary>
            Owned,

            /// <summary>
            /// Nobody owns it, and we have just asked to. Ownership transfer
            /// is a network round trip, so the caller should do nothing this
            /// tick and try again on the next one.
            /// </summary>
            Claiming,

            /// <summary>Another client owns it: go through the RPC protocol.</summary>
            Foreign,

            /// <summary>No usable view or no reachable owner; skip entirely.</summary>
            Unavailable,
        }

        /// <summary>
        /// Decides how an automated deposit must reach this drawer, and
        /// starts an ownership claim if that is what is needed.
        ///
        /// This exists because auto-pickup was silently doing nothing for
        /// most drawers in multiplayer. The only deposit path automation had
        /// was TryDepositExternally, which refuses unless this client already
        /// owns the drawer -- so absorbing a dropped item required one client
        /// to happen to own BOTH the item and the drawer, and any drawer
        /// owned by another player, or by nobody because nobody had touched
        /// it since the zone loaded, was skipped with no indication.
        ///
        /// The three outcomes are deliberately different, because the two
        /// non-owned cases are not the same situation:
        ///
        /// - No owner at all is not somebody else's drawer, so claiming it
        ///   takes nothing from anyone. This is the common case for a drawer
        ///   in a freshly loaded zone and was the bulk of the reported
        ///   inconsistency.
        /// - A live foreign owner must NOT be claimed away. Stealing a ZDO
        ///   that someone legitimately holds is what the RPC protocol exists
        ///   to avoid (see the class-level comment), so that case routes a
        ///   request to the owner instead.
        ///
        /// Claiming returns rather than proceeding, on purpose. ClaimOwnership
        /// is a local field write that then replicates; acting immediately on
        /// an ownership we asked for microseconds ago is exactly how two
        /// clients both believe they own a drawer and both credit it. Waiting
        /// a tick costs half a second and lets the claim settle.
        /// </summary>
        internal DepositRoute ResolveDepositRoute()
        {
            if (_view == null || !_view.IsValid()) return DepositRoute.Unavailable;

            // An owner whose ownership has not settled must not write Amount
            // (see OwnershipSettleSeconds); treat it like a claim in progress
            // and skip this tick.
            if (_view.IsOwner()) return OwnershipSettled() ? DepositRoute.Owned : DepositRoute.Claiming;

            var zdo = _view.GetZDO();
            if (zdo == null) return DepositRoute.Unavailable;

            if (zdo.GetOwner() == 0L)
            {
                _view.ClaimOwnership();
                return DepositRoute.Claiming;
            }

            return DepositRoute.Foreign;
        }

        /// <summary>
        /// Submits a deposit to a drawer owned by ANOTHER client, through the
        /// same request/grant protocol the player's own deposit uses.
        ///
        /// Returns the count the caller must now remove from whatever it is
        /// depositing from. Those items become this mod's responsibility at
        /// that moment: the owner either credits them to the drawer or the
        /// pending record refunds them by spilling at
        /// <paramref name="refundPosition"/>, exactly as the player path
        /// spills at the player's feet. The caller must not remove more than
        /// the returned count, and must not skip removing it.
        ///
        /// The amount is computed against this client's REPLICATED view of
        /// the drawer, so it is a best guess at how much will fit. The owner
        /// re-validates and reports what it actually took; any shortfall
        /// comes back through RPC_GrantDeposit and is spilled. Being
        /// optimistic here is safe for that reason, and being pessimistic
        /// would mean absorbing nothing whenever a drawer's replicated count
        /// was slightly stale.
        /// </summary>
        internal bool TrySubmitForeignDeposit(
            string itemName, int amount, Vector3 refundPosition, out int submitted)
        {
            submitted = 0;

            if (!ItemFacts.IsStorable(itemName)) return false;
            if (_view == null || !_view.IsValid()) return false;

            var zdo = _view.GetZDO();
            if (zdo == null) return false;

            // See the class-level RPC comment: ZRoutedRpc routes target 0 as
            // Everybody, which broadcasts the request -- and every retry --
            // to every peer, the multi-owner double-apply that pinning exists
            // to prevent. ResolveDepositRoute claims unowned drawers rather
            // than sending to 0, so reaching here with 0 means ownership was
            // lost between the two calls; skip and pick it up next tick.
            long targetOwner = zdo.GetOwner();
            if (targetOwner == 0L) return false;

            var outcome = DrawerState.Deposit(Snapshot, Capacity, itemName, amount);
            if (!outcome.Accepted || outcome.MovedToDrawer <= 0) return false;

            var manager = DrawerManager.Instance;
            if (manager == null) return false;

            long id = manager.NextRequestId();
            manager.AddPendingDeposit(id, new PendingDeposit(
                drawerId: _zdoId, targetOwner: targetOwner, player: null, itemName: itemName,
                removed: outcome.MovedToDrawer, playerPosition: refundPosition, announce: false));
            SendDepositRequestRpc(id, targetOwner, itemName, outcome.MovedToDrawer);

            submitted = outcome.MovedToDrawer;
            return true;
        }

        /// <summary>
        /// Credits the drawer with <paramref name="amount"/> of
        /// <paramref name="itemName"/>, clamped to remaining capacity, without
        /// removing anything from anywhere. The caller must already have
        /// removed (or must remove exactly <c>accepted</c> of) these items
        /// from their source: DrawerManager.RunAutoPickup removes the
        /// accepted count from the ground stack afterwards; DebugCommands
        /// rid_wall is a cheat-only tool that conjures test stock. Refuses
        /// unless this client already owns the ZDO.
        /// </summary>
        public bool TryDepositExternally(string itemName, int amount, out int accepted)
        {
            accepted = 0;

            // A drawer holds one integer; equipment carries quality and
            // durability that an integer can't represent. Enforced here too,
            // not just on the hotbar-use path, since this is the one call an
            // external automation mod can reach without going through
            // Interact/UseItem at all.
            if (!ItemFacts.IsStorable(itemName)) return false;

            // Refuse rather than claim when this client is not already the
            // owner; auto-pickup decides routing via ResolveDepositRoute.
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return false;

            var current = Snapshot;
            var outcome = DrawerState.Deposit(current, Capacity, itemName, amount);
            if (!outcome.Accepted || outcome.MovedToDrawer <= 0) return false;
            if (!WriteOwned(current, outcome.Result)) return false;

            accepted = outcome.MovedToDrawer;
            return true;
        }

        // ---------- Container view ----------
        // See DrawerView for the design. ContainerBridge reaches the view
        // only through GetViewInventory/SaveView/LoadView; DrawerManager
        // drives FlushView (end of frame) and ReconcileView (owner tick).

        /// <summary>
        /// A per-instance identifier for logs and rid_diag: the GameObject
        /// name is identical across every drawer of a tier, so the ZDOID is
        /// appended.
        /// </summary>
        internal string DiagId =>
            _view != null && _view.IsValid()
                ? $"{gameObject.name}#{_view.GetZDO().m_uid}"
                : $"{gameObject.name}#(no ZDO)";

        internal Inventory GetViewInventory() => View?.GetForCaller();

        /// <summary>
        /// Container.Save on a drawer. Some mods change m_stack directly and
        /// call Save without Changed(), so the live view is compared against
        /// its last sync and a change marks it dirty. Nothing is written
        /// here: it goes through FlushView like any view change (claim,
        /// wait for ownership to settle, reconcile and republish), or the
        /// ResetZDO teardown flush if the drawer unloads first. Save is often called in the middle of a
        /// mod's loop over the inventory, and republishing rebuilds the
        /// item list, which would break that loop or orphan ItemData
        /// references the mod is about to change.
        /// </summary>
        internal void SaveView()
        {
            if (View == null) return;
            if (View.IsDirty || View.HasUnflushedChange()) View.MarkDirty();
        }

        /// <summary>Container.Load on a drawer: true when the view was rebuilt from newer ZDO data.</summary>
        internal bool LoadView() => View != null && View.RefreshFromZdo();

        /// <summary>
        /// How long this peer must have held ownership, with no ownership
        /// change at all, before it writes drawer state computed from a
        /// view change (and before ItemDrawersAPI withdraws). Longer than a
        /// round trip, so writes the previous owner sent before it saw the
        /// claim have arrived and are in the live ZDO that reconcile reads,
        /// and the previous owner has stopped writing.
        ///
        /// Why not claim and write in the same frame: ClaimOwnership only
        /// moves the owner (ZDO.SetOwner -> IncreaseOwnerRevision). The old
        /// owner may be sending its own absolute Amount at the same moment
        /// (a hand deposit, a grant to another peer). ZDOMan.RPC_ZDOData
        /// keeps whichever data revision is higher, so one of the two
        /// absolute writes is silently discarded -- items lost or
        /// duplicated either way. Waiting removes the overlap instead of
        /// picking a winner.
        /// </summary>
        internal const float OwnershipSettleSeconds = 1f;

        // Ownership continuity is tracked through the ZDO's OwnerRevision,
        // which increases on every owner change (local SetOwner or an
        // adopted remote one), so losing and regaining ownership between
        // two checks still restarts the clock. _ownerRevisionSince is when
        // this peer first observed the current revision -- never earlier
        // than the real change, so the wait can only be longer, not shorter.
        private ushort _ownerRevisionSeen;
        private float _ownerRevisionSince;

        // This peer claimed the drawer to apply its current view change and
        // has not applied it yet. After such a claim is lost to someone
        // else, the claim is repeated only once the other owner has itself
        // been stable for the settle delay, so two peers with pending view
        // changes take turns instead of stealing it back every frame.
        private bool _viewClaimMade;

        // A reconcile that WriteOwned or the tick wanted while ownership had
        // not settled; the tick retries it. _deferredPriorItemName keeps
        // WriteOwned's pre-write item name for that retry (see ReconcileView).
        private bool _reconcileDeferred;
        private string _deferredPriorItemName;

        private void ResetOwnershipClock()
        {
            _ownerRevisionSeen = _view != null && _view.IsValid() ? _view.GetZDO().OwnerRevision : (ushort)0;
            _ownerRevisionSince = Time.time;
        }

        /// <summary>Seconds since this peer first observed the ZDO's current OwnerRevision.</summary>
        private float OwnerRevisionAge()
        {
            ushort revision = _view.GetZDO().OwnerRevision;
            if (revision != _ownerRevisionSeen)
            {
                _ownerRevisionSeen = revision;
                _ownerRevisionSince = Time.time;
            }
            return Time.time - _ownerRevisionSince;
        }

        /// <summary>
        /// Player hand paths on an owner whose ownership has not settled:
        /// show "Try again" and return true (refused). Every write of Amount
        /// is absolute, and the previous owner may still be sending its own
        /// within the round trip; if both arrive with the same data revision
        /// the receivers adopt this peer's owner revision but drop its data,
        /// and nothing resends it -- this peer's next write would then erase
        /// the other's change. Callers check this before removing anything
        /// from the player or giving anything to them. Non-owners are not
        /// refused here; they use the request protocol.
        /// </summary>
        private bool RefuseWhileUnsettled(Player player)
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner() || OwnershipSettled()) return false;
            player?.Message(MessageHud.MessageType.Center, CommitFailedMessage);
            return true;
        }

        /// <summary>True when this peer owns the drawer and ownership has not changed for OwnershipSettleSeconds.</summary>
        internal bool OwnershipSettled() =>
            _view != null && _view.IsValid() && _view.IsOwner() && OwnerRevisionAge() >= OwnershipSettleSeconds;

        /// <summary>
        /// Applies a change made through the view. Called from
        /// DrawerManager's end-of-frame flush, from the ZNetView.ResetZDO
        /// prefix and from OnDestroy (both with <paramref name="teardown"/>
        /// true) -- never from inside an Inventory callback.
        ///
        /// Non-owners never write ViewSlots. A peer that does not own the
        /// drawer claims it (the way the chest mods and vanilla take-all
        /// claim a container) and writes nothing; the view stays dirty, so
        /// refreshes cannot overwrite what mods see, and this is retried
        /// every frame. Once ownership has held for OwnershipSettleSeconds
        /// it reconciles as the owner. Reconcile reads the live ZDO, so it
        /// includes everything the previous owner wrote before it saw the
        /// claim, and counts this peer's change once through the dirty
        /// branch of TryReadForReconcile (stored − baseline, plus live −
        /// last sync). If the claim is lost while waiting, it is made again
        /// once the other owner has been stable for the same delay; the
        /// change is never dropped.
        ///
        /// Teardown (ZDO unloading or scene shutdown) cannot wait: the
        /// delta is written onto ViewSlots immediately (WriteDeltaToZdo) for
        /// the next owner to reconcile. A non-owner's teardown write keeps
        /// the old exposure -- the owner's reaction to an earlier write can
        /// overwrite it within one round trip -- documented as a known
        /// limitation.
        /// </summary>
        internal void FlushView(bool teardown = false)
        {
            if (View == null || !View.IsDirty) return;
            if (_view == null || !_view.IsValid()) return;

            if (!View.HasUnflushedChange())
            {
                View.ClearDirty();
                _viewClaimMade = false;
                return;
            }

            if (teardown)
            {
                View.WriteDeltaToZdo();
                _viewClaimMade = false;
                return;
            }

            var manager = DrawerManager.Instance;

            if (!_view.IsOwner())
            {
                if (!_viewClaimMade || OwnerRevisionAge() >= OwnershipSettleSeconds)
                {
                    _view.ClaimOwnership();
                    _viewClaimMade = true;
                }
                manager?.MarkViewDirty(this);
                return;
            }

            if (!OwnershipSettled())
            {
                manager?.MarkViewDirty(this);
                return;
            }

            ReconcileView();
        }

        /// <summary>Owner tick: a reconcile is due because the view's ZDO data needs one, or an earlier one was deferred until ownership settled.</summary>
        internal bool ViewNeedsReconcile() =>
            View != null && _view != null && _view.IsValid() && _view.IsOwner()
            && (_reconcileDeferred || View.NeedsReconcile());

        internal string ViewDiag => View != null ? View.Describe() : "no view";

        /// <summary>
        /// Owner only. Applies what changed through the view since it was
        /// last published: a deposit is credited up to capacity and the rest
        /// spilled at the drawer; a withdrawal is debited; items that are not
        /// the drawer's are spilled. Then republishes the view from the
        /// resulting state. Never called from inside an Inventory callback.
        ///
        /// Publish ALWAYS follows the read -- even when nothing changed and
        /// nothing is foreign -- and nothing may write the view to the ZDO
        /// (WriteDeltaToZdo) in between. TryReadForReconcile folds this peer's
        /// unflushed delta into its result; only Publish resets the view's
        /// sync baseline, so skipping it (or flushing first) would send that
        /// same delta again and count it twice. For that reason Publish runs
        /// straight after the state write, before any spill or log call.
        ///
        /// <paramref name="priorItemName"/>: the item the drawer held before
        /// the write that triggered this reconcile (WriteOwned). When that
        /// write unassigned the drawer (Clear), the pending view change is
        /// still read and spilled as that item -- under the new empty name
        /// ItemFacts.SpillAtPosition finds no prefab and the items vanish.
        ///
        /// Unless <paramref name="force"/>, nothing happens until ownership
        /// has settled (OwnershipSettled): two peers that both briefly
        /// believe they own the drawer would otherwise both reconcile the
        /// same stored delta and both spill it. The reconcile is marked
        /// deferred and the tick retries it; the delta stays stored. Only
        /// OnDrawerDestroyed forces, because the ZDO is about to be gone.
        /// </summary>
        internal void ReconcileView(string priorItemName = null, bool force = false)
        {
            if (View == null || _view == null || !_view.IsValid() || !_view.IsOwner()) return;

            if (!force && !OwnershipSettled())
            {
                _reconcileDeferred = true;
                if (!string.IsNullOrEmpty(priorItemName) && _deferredPriorItemName == null)
                    _deferredPriorItemName = priorItemName;
                return;
            }

            if (priorItemName == null) priorItemName = _deferredPriorItemName;
            _reconcileDeferred = false;
            _deferredPriorItemName = null;
            _viewClaimMade = false;

            var current = Snapshot;
            if (View.CannotResolve(current))
            {
                ReconcileUnresolved(current);
                return;
            }
            _loggedUnresolvedPending = false;

            string viewItemName = !current.IsAssigned && !string.IsNullOrEmpty(priorItemName)
                ? priorItemName
                : current.ItemName;

            if (!View.TryReadForReconcile(viewItemName, out int itemTotal, out int baseline, out var foreign))
            {
                View.Publish(current);
                return;
            }

            // Simultaneous takes from several peers can drive the combined
            // total below zero. ViewReconciliation clamps a negative total to
            // 0, which would shrink the withdrawal; shift both sides up
            // instead so total - baseline (the real change) is preserved and
            // WithdrawExact's clamp decides what the stock can cover.
            if (itemTotal < 0)
            {
                baseline -= itemTotal;
                itemTotal = 0;
            }

            int foreignTotal = 0;
            foreach (var f in foreign) foreignTotal += f.Value;

            var outcome = ViewReconciliation.Apply(current, Capacity, baseline, itemTotal, foreignTotal);
            if (!outcome.Result.Equals(current)) WriteState(outcome.Result);

            View.Publish(outcome.Result);

            var spillAt = transform.position;
            if (outcome.SpillDrawerItem > 0)
                ItemFacts.SpillAtPosition(spillAt, viewItemName, outcome.SpillDrawerItem);

            foreach (var f in foreign)
            {
                if (ItemFacts.Prefab(f.Key) != null)
                    ItemFacts.SpillAtPosition(spillAt, f.Key, f.Value);
                else
                    DrawerPlugin.Log.LogWarning(
                        $"{DiagId}: {f.Value} of unidentifiable item '{f.Key}' left in the drawer's view could not be returned.");
            }

            if (outcome.Unbacked > 0)
                DrawerPlugin.Log.LogWarning(
                    $"{DiagId}: {outcome.Unbacked} {current.ItemName} were taken through the view beyond what the drawer held "
                    + "(another player changed it at the same moment).");
        }

        /// <summary>
        /// ReconcileView for an owner that cannot resolve the drawer's item
        /// (it shows nothing, so it cannot trust or apply a view total).
        ///
        /// If ViewSlots holds a change other peers made (total differs from
        /// the stored baseline, or foreign items), it is left untouched:
        /// publishing would overwrite it with an empty layout and baseline 0,
        /// losing those deltas. It stays for an owner that can resolve the
        /// item, and is logged once per drawer.
        ///
        /// Otherwise nothing is written unless the stored layout is stale
        /// against Amount (this owner changed Amount, e.g. through the API).
        /// Replacing a correct layout would race a peer's in-flight
        /// withdrawal: its negative delta has no slots to apply to once the
        /// empty layout lands, is dropped, and the items are duplicated.
        /// </summary>
        private void ReconcileUnresolved(DrawerSnapshot current)
        {
            switch (View.InspectForUnresolvedOwner(current.Amount))
            {
                case DrawerView.UnresolvedViewState.PendingChanges:
                    if (_loggedUnresolvedPending) return;
                    _loggedUnresolvedPending = true;
                    DrawerPlugin.Log.LogWarning(
                        $"{DiagId}: this client cannot resolve item '{current.ItemName}', so a change made through "
                        + "the drawer's container view is left unreconciled for an owner that can.");
                    return;

                case DrawerView.UnresolvedViewState.AlreadyPublished:
                    return;

                default:
                    View.Publish(current);
                    return;
            }
        }

        // ---------- hover ----------

        public new string GetHoverText()
        {
            var s = Snapshot;
            if (!s.IsAssigned)
                return Localization.instance.Localize(
                    $"{DrawerTiers.DisplayName(Tier)}\n" +
                    "[<color=yellow><b>1-8</b></color>] Store an item");

            string label = ItemFacts.LocalizedName(s.ItemName);

            // Ctrl+E does two different things depending on the count: it
            // takes one item, or -- once the drawer reads zero -- it releases
            // the item type so the drawer can be assigned something else
            // (see DrawerState.Clear, which refuses unless the drawer is
            // empty). Saying "Take one" on a drawer that has none left named
            // the wrong action AND hid the only way to reassign a drawer,
            // which is not discoverable anywhere else in the UI.
            //
            // The take lines are dropped at zero rather than shown as
            // no-ops: there is nothing to take, and offering the action is
            // what made this confusing in the first place.
            string actions = s.Amount > 0
                ? "[<color=yellow><b>E</b></color>] Take stack\n" +
                  "[<color=yellow><b>Ctrl+E</b></color>] Take one\n" +
                  "[<color=yellow><b>Shift+E</b></color>] Store all"
                : "[<color=yellow><b>Ctrl+E</b></color>] Unassign\n" +
                  "[<color=yellow><b>Shift+E</b></color>] Store all";

            return Localization.instance.Localize(
                $"{label}  <color=orange>{s.Amount}</color>/{Capacity}\n" + actions);
        }

        public new string GetHoverName() => DrawerTiers.DisplayName(Tier);
    }

    /// <summary>
    /// Container.CanBeRemoved() is non-virtual, and Piece.CanBeRemoved()
    /// calls it through a Container-typed reference obtained via
    /// GetComponentInChildren&lt;Container&gt;() -- a non-virtual call
    /// resolved at compile time in Valheim's own assembly, so no amount of
    /// `new` or `override` in DrawerComponent can intercept it. Its
    /// original body is also unsafe for this type: it reads m_privacy
    /// (defaulting to Private, field value 0) and, since Container.Awake
    /// never runs, calls GetInventory().NrOfItems() on a null Inventory --
    /// an NRE on every attempt to hammer-remove a drawer. A Harmony prefix
    /// is the only way to reach this call site at all; it substitutes the
    /// drawer's own ZDO snapshot for Container's inventory check (mirroring
    /// vanilla's own "don't let a private container with contents be
    /// hammered away" rule) and skips the original method entirely for
    /// drawers.
    /// </summary>
    [HarmonyPatch(typeof(Container), nameof(Container.CanBeRemoved))]
    internal static class DrawerCanBeRemovedPatch
    {
        // Harmony calls this before applying the patch and skips it (rather
        // than throwing out of PatchAll) when it returns false. If a future
        // Valheim update renames or removes Container.CanBeRemoved,
        // AccessTools.Method returns null here instead of PatchAll failing
        // on a missing target -- this patch is the only thing that degrades,
        // not prefab registration.
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Container), "CanBeRemoved");

        private static bool Prefix(Container __instance, ref bool __result)
        {
            if (!(__instance is DrawerComponent drawer)) return true;

            __result = drawer.Snapshot.IsEmpty;
            return false;
        }
    }

    /// <summary>
    /// DrawerComponent.EnsureCreator writes the ZDO creator during Awake,
    /// before Player.PlacePiece calls Piece.SetCreator. If Piece.Awake ran
    /// after that write, Piece.m_creator already holds the same id and
    /// SetCreator's "creator == 0" guard would skip it, so the creator's
    /// platform-user index (ZDOVars.s_creatorIndex) would never be written.
    /// For a drawer whose creator is exactly the id being set, clear the
    /// cached field so vanilla runs in full and writes both values -- the
    /// same id, so nothing about ownership or access changes.
    /// </summary>
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class PieceSetCreatorPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Piece), nameof(Piece.SetCreator));

        private static void Prefix(Piece __instance, long uid)
        {
            if (__instance.m_creator == uid && __instance.GetComponent<DrawerComponent>() != null)
                __instance.m_creator = 0L;
        }
    }
}
