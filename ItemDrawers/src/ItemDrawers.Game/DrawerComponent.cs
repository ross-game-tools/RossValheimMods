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
    /// Awake's own docstring below DOES call base.Awake(), specifically so
    /// that Harmony postfixes other mods place on Container.Awake (this is
    /// exactly how OttoFuel and NoVikingLeftBehind discover containers, per
    /// task-12-report.md's decompile) still fire. This class sets
    /// Container's own m_nview field to the same ZNetView it already caches
    /// as _view before that call, since both of those mods' discovery paths
    /// depend on that field being non-null. ContainerBridge separately
    /// patches Container.GetInventory to hand back a lazily-refreshed
    /// one-stack mirror (see MirrorInventory/RefreshMirror below) whenever
    /// the instance is a DrawerComponent, since m_inventory itself is never
    /// constructed, and patches Container.Save/Load to no-op for the same
    /// instances so that mirror never gets serialised into the ZDO's own
    /// items field.
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
        // above included -- now goes through WriteOwned, which requires
        // IsOwner() and re-reads/compares before writing (see its
        // docstring for why the CAS is not redundant even for a client
        // that just checked IsOwner()==true). Nothing in this class calls
        // ZNetView.ClaimOwnership() any more, anywhere, for any reason:
        // it is a purely local field set that PROPAGATES (ZDO.SetOwner ->
        // IncreaseOwnerRevision -> ZDOMan.ClientChanged, confirmed by
        // decompile) and is accepted by every other client with no server
        // arbitration (ZDOMan.RPC_ZDOData applies whichever OwnerRevision
        // it receives is higher, unconditionally) -- calling it from
        // automation code (the pre-fix state of
        // TryWithdrawExternally/TryDepositExternally) durably STEALS
        // ownership out from under whoever legitimately held it,
        // permanently breaking this protocol's short-circuit for them.
        // Both of those methods now refuse outright when this client does
        // not already own the ZDO, rather than claiming it.
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

        // ---------- Container bridge mirror ----------
        // See MirrorInventory/RefreshMirror below and ContainerBridge.cs.
        // _mirrored is a bookkeeping baseline only -- what the mirror's
        // live Inventory was last known to hold -- not necessarily equal to
        // Snapshot at every instant (that mismatch is itself the signal
        // RefreshMirror uses to know a rebuild is due). Starts at ("", 0),
        // the same value an unassigned drawer's Snapshot naturally produces,
        // so a never-yet-built mirror on an empty drawer correctly needs no
        // rebuild rather than performing a pointless first one.
        private Inventory _mirror;
        private DrawerSnapshot _mirrored = new DrawerSnapshot("", 0);
        private bool _applyingMirror;

        // Set when OnMirrorChanged sees a write-back refused (either
        // because this client isn't the owner, or a WriteOwned
        // compare-and-swap lost a split-ownership race while nominally
        // owning). Forces the NEXT RefreshMirror to rebuild even though
        // _mirrored may coincidentally still equal the current snapshot --
        // the mirror's own live Inventory list already diverged from that
        // snapshot the instant the foreign mod's RemoveItem ran, and only
        // an actual rebuild (safely off this call stack, never inside
        // OnMirrorChanged itself -- see its docstring) corrects it.
        private bool _mirrorNeedsRebuild;

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
            // m_inventory was individually audited against this change
            // (task-12-report.md, "m_nview reader audit"): each is either
            // already patched for a drawer (GetInventory, Save, Load,
            // CanBeRemoved), shadowed by this class's own `new` overrides
            // (Interact, UseItem, GetHoverText, GetHoverName), unreachable
            // because its only call sites are inside Container.Awake's own
            // skipped body or behind vanilla RPC registrations that now
            // never happen (AddDefaultItems, DropAllItems, OnDestroyed,
            // CheckForChanges, OnContainerChanged, UpdateRows, every
            // RPC_* handler), or reads only m_nview/plain fields that stay
            // safe once m_nview is set (CheckAccess, IsOwner, IsInUse,
            // SetInUse, UpdateUseVisual, StackAll, TakeAll) -- none of
            // which are reachable through m_inventory, which remains null.
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

            base.Awake();
            EnsureCreator();

            // Container.Awake never runs its own body on this type (see
            // AwakePatch), so nothing else it would normally set up happens
            // for free: m_inventory is never constructed here
            // (ContainerBridge's GetInventory patch substitutes the mirror
            // instead, built on demand -- see MirrorInventory/RefreshMirror),
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
        /// both require this to be nonzero (confirmed by decompiling both --
        /// see task-12-report.md); vanilla sets it via
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

            zdo.Set(KeyPrefabHash, next.ItemName);
            zdo.Set(KeyAmountHash, next.Amount);

            DrawerManager.Instance?.MarkDirty(this);
            RefreshFace();
            return true;
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
                int accepted = (outcome.Accepted && WriteOwned(current, outcome.Result)) ? outcome.MovedToDrawer : 0;

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
                var outcome = DrawerState.Clear(current);
                if (outcome.Accepted) WriteOwned(current, outcome.Result);
                return;
            }

            _view.InvokeRPC(RpcReqClear);
        }

        private void RPC_RequestClear(long sender)
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return;

            var current = Snapshot;
            var outcome = DrawerState.Clear(current);
            if (outcome.Accepted) WriteOwned(current, outcome.Result);
        }

        // ---------- external access, used by the Container bridge ----------
        // ContainerBridge/ItemDrawersAPI reach the drawer only through these
        // two methods and the mirror below -- never by poking the ZDO
        // directly.

        /// <summary>
        /// Withdraws up to <paramref name="requested"/> on behalf of an
        /// external caller (ItemDrawersAPI.Withdraw, or the mirror's own
        /// write-back for a removal another mod performed against it).
        /// Clamps rather than refusing -- see DrawerState.WithdrawExact --
        /// but REFUSES OUTRIGHT (returns false, taken = 0) if this client
        /// does not already own the ZDO, rather than claiming ownership to
        /// proceed.
        ///
        /// This used to call ClaimOwnership() unconditionally, the same as
        /// every other caller pre-dating the RPC-to-owner rework. That was
        /// a genuine bug, not a narrow theoretical one: ZDO.SetOwner
        /// PROPAGATES (SetOwner -> IncreaseOwnerRevision ->
        /// ZDOMan.ClientChanged, confirmed by decompile) and is accepted by
        /// every other client with NO server arbitration
        /// (ZDOMan.RPC_ZDOData applies whichever OwnerRevision it receives
        /// is higher, unconditionally). DrawerManager.RunAutoPickup calls
        /// this on every client, on a 0.5s timer by default, for every
        /// drawer within 40m of a matching dropped item -- against a
        /// drawer that client did not already own, the old code would
        /// durably STEAL ownership out from under whoever legitimately
        /// held it, and that new owner would then take the IsOwner()
        /// short-circuit in RequestWithdraw/RequestDeposit for every
        /// subsequent player interaction while the ORIGINAL owner kept
        /// routing through the RPC protocol against a ZDO it no longer
        /// owned -- exactly the kind of split-brain state this whole
        /// protocol exists to prevent.
        ///
        /// Refusing here instead of stealing is an acceptable cost:
        /// auto-pickup, and the mirror write-back that shares this method,
        /// are both proximity-triggered (a drop this client already owns,
        /// or a drawer this client's own scan is touching), so the ZDO in
        /// question is, in the ordinary case, already owned by this exact
        /// client -- see this task's report for the residual limitation
        /// this leaves when it is not.
        /// </summary>
        public bool TryWithdrawExternally(int requested, out int taken)
        {
            taken = 0;
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return false;

            var current = Snapshot;
            var outcome = DrawerState.WithdrawExact(current, requested);
            if (outcome.MovedToPlayer <= 0) return false;
            if (!WriteOwned(current, outcome.Result)) return false;

            taken = outcome.MovedToPlayer;
            return true;
        }

        /// <summary>
        /// Credits the drawer with <paramref name="amount"/> of
        /// <paramref name="itemName"/>, clamped to the drawer's remaining
        /// capacity, without removing anything from anywhere.
        ///
        /// Contract: the caller must have already removed these items from
        /// wherever they came from -- via a real, verified removal (e.g. a
        /// successful Inventory.RemoveItem, or another mod's own AddItem
        /// having already merged them into this drawer's mirror inventory
        /// before this method is asked to make that authoritative) -- before
        /// calling this. This method does not verify a removal happened and
        /// cannot roll one back if the caller's removal never actually
        /// occurred; calling it without a prior removal duplicates items
        /// outright. Its two existing callers:
        /// DrawerManager.RunAutoPickup deposits first, then removes exactly
        /// <c>accepted</c> from the ground-item stack afterward -- never
        /// the other way around, which is what keeps auto-pickup
        /// conservative. DebugCommands.rid_wall is the one deliberate
        /// exception: an isCheat: true dev tool that conjures test stock
        /// with nothing removed from anywhere, the same category as
        /// vanilla's own cheat console commands -- not a template for a
        /// real caller.
        ///
        /// OnMirrorChanged (below) deliberately does NOT call this for a
        /// mirror-side deposit: vanilla's own move idiom is
        /// <c>if (AddItem(item)) fromInventory.RemoveItem(item);</c> --
        /// add first, remove from the source second. By the time
        /// m_onChanged fires here, AddItem has already returned true and
        /// the depositing mod is about to remove from its own source
        /// regardless of anything this method does; the removal has NOT
        /// happened yet, so crediting the drawer here would be exactly the
        /// item creation this contract exists to prevent. See
        /// OnMirrorChanged's own comment on why that path stays inert
        /// until a real, testable depositing caller exists.
        /// </summary>
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
            if (_view.IsOwner()) return DepositRoute.Owned;

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

        public bool TryDepositExternally(string itemName, int amount, out int accepted)
        {
            accepted = 0;

            // A drawer holds one integer; equipment carries quality and
            // durability that an integer can't represent. Enforced here too,
            // not just on the hotbar-use path, since this is the one call an
            // external automation mod can reach without going through
            // Interact/UseItem at all.
            if (!ItemFacts.IsStorable(itemName)) return false;

            // See TryWithdrawExternally's docstring: refuse rather than
            // steal ownership when this client is not already the owner.
            if (_view == null || !_view.IsValid() || !_view.IsOwner()) return false;

            var current = Snapshot;
            var outcome = DrawerState.Deposit(current, Capacity, itemName, amount);
            if (!outcome.Accepted || outcome.MovedToDrawer <= 0) return false;
            if (!WriteOwned(current, outcome.Result)) return false;

            accepted = outcome.MovedToDrawer;
            return true;
        }

        // ---------- Container bridge: lazily-refreshed mirror inventory ----------
        // A drawer's whole state is one item name and one int (Snapshot).
        // Container-aware mods want a real Inventory to read and remove
        // from. This section is the translation: exactly one ItemData whose
        // m_stack carries the drawer's entire count -- four thousand coal is
        // one stack of four thousand, not eighty stacks of fifty.
        // Materialising real stacks would mean up to Capacity ItemData
        // objects per drawer, rebuilt on every scan across however many
        // drawers a mod's radius touches; a mirror makes that a handful of
        // integer comparisons instead. ContainerBridge's GetInventory patch
        // calls RefreshMirror before every read, and mutates only when the
        // ZDO amount has actually changed since the mirror was last built.

        /// <summary>
        /// The mirror Inventory handed back by ContainerBridge's
        /// Container.GetInventory patch. Built once per drawer, on first
        /// access, then reused -- RefreshMirror mutates it in place.
        ///
        /// Deliberately 1x1, not larger. A larger mirror was tried
        /// specifically to give a foreign AddItem probe that can't merge
        /// into the occupied slot (a mismatched item, or the same item at a
        /// different m_worldLevel -- see FindFreeStackItem) a genuine empty
        /// slot to land in instead of failing, which avoids vanilla logging
        /// `ZLog.LogError("Trying to add item to occupied slot -1, -1")`
        /// (confirmed by decompiling Inventory.AddItem/FindEmptySlot). That
        /// trade was wrong: with a spare slot, a full drawer reports
        /// GetEmptySlots()==1, HaveEmptySlot()==true, CanAddItem() a whole
        /// maxStackSize of phantom room, and SlotsUsedPercentage() 50% on a
        /// drawer that is actually full. Since a deposit is deliberately
        /// inert here (see OnMirrorChanged) -- never credited to the ZDO --
        /// a foreign AddItem that used to correctly FAIL against a full 1x1
        /// mirror instead SUCCEEDS against the spare slot, the depositing
        /// mod removes from its own source per vanilla's own move idiom,
        /// and those items are destroyed outright, not merely mis-logged.
        /// A logged, correct refusal is strictly better than a silent
        /// success that destroys items, so the log line is accepted rather
        /// than engineered around. See the human verification steps in
        /// this task's report for how to tell this vanilla log line apart
        /// from an actual bug.
        /// </summary>
        /// <summary>
        /// The mirror's last-reconciled baseline (see _mirrored), exposed
        /// read-only for rid_diag -- diagnosing "OttoFuel/NVLB don't see
        /// drawers" needs to distinguish "the mirror was never built"
        /// (baseline still ("", 0)) from "the mirror is built but stale"
        /// from "the mirror and ZDO agree", none of which are visible from
        /// outside this class otherwise.
        /// </summary>
        internal DrawerSnapshot MirroredBaseline => _mirrored;

        /// <summary>
        /// A stable per-instance identifier for rid_diag, combining the
        /// GameObject name (which is identical -- "rid_drawer_wood(Clone)"
        /// -- across every drawer of a tier) with its ZDOID, so rid_diag's
        /// output can distinguish two different drawer instances of the
        /// same tier.
        /// </summary>
        internal string DiagId =>
            _view != null && _view.IsValid()
                ? $"{gameObject.name}#{_view.GetZDO().m_uid}"
                : $"{gameObject.name}#(no ZDO)";

        internal Inventory MirrorInventory
        {
            get
            {
                if (_mirror == null)
                {
                    _mirror = new Inventory("drawer", null, 1, 1);
                    _mirror.m_onChanged += OnMirrorChanged;
                }
                return _mirror;
            }
        }

        /// <summary>
        /// Brings the mirror in line with what this client is ALLOWED to
        /// show, but only when something actually changed since the last
        /// call -- one snapshot comparison, not a rebuild, for the common
        /// case of a scan touching a drawer nothing has altered. A hundred
        /// drawers in a wall should cost a hundred of these comparisons,
        /// not a hundred rebuilds.
        ///
        /// Deliberately NOT simply <c>Snapshot</c>: when this client does
        /// not own the ZDO, the mirror is built EMPTY (see
        /// EffectiveMirrorSnapshot) regardless of what the ZDO actually
        /// holds. This is the fix for a duplication bug review found: this
        /// class's Container-bridge write-back
        /// (TryWithdrawExternally/OnMirrorChanged) correctly refuses to
        /// debit a ZDO this client does not own, but a foreign mod
        /// (OttoFuel, NoVikingLeftBehind) that ALREADY saw real stock in
        /// the mirror has already removed it from ITS OWN accounting by
        /// the time that refusal happens -- the mod keeps the items and
        /// the drawer keeps them too. A mod that sees no stock in the
        /// first place never tries to remove any, so there is nothing left
        /// to reconcile and nothing to duplicate. The accepted tradeoff:
        /// automation reads only drawers this client currently owns.
        /// Ownership tracks proximity and both target mods operate near
        /// the player, so a drawer a player is standing next to is, in the
        /// overwhelming common case, one they own. That assumption held
        /// only in single-player; see ClaimForAutomationIfUnowned, which
        /// RefreshMirror now calls first so ownership actually follows the
        /// client doing the automating instead of being assumed to.
        ///
        /// Also rebuilds unconditionally when <see cref="_mirrorNeedsRebuild"/>
        /// is set -- see OnMirrorChanged's belt-and-braces handling of a
        /// refused write-back, which can still happen even while this
        /// client IS the owner (a WriteOwned compare-and-swap refusal
        /// during a split-ownership window -- see WriteOwned's docstring).
        /// </summary>
        internal void RefreshMirror()
        {
            ClaimForAutomationIfUnowned();

            var current = EffectiveMirrorSnapshot();
            if (!_mirrorNeedsRebuild && current.Equals(_mirrored)) return;
            RebuildMirror(current);
            _mirrorNeedsRebuild = false;
        }

        /// <summary>
        /// Takes ownership of an UNOWNED drawer when a foreign mod reads it.
        ///
        /// RefreshMirror's tradeoff -- automation only sees drawers this
        /// client owns -- rests on an assumption stated in its docstring:
        /// that ownership tracks proximity, so a drawer a player stands next
        /// to is one they own. That is true in single-player, where the one
        /// client owns everything, and false on a server, where a drawer is
        /// unowned until somebody touches it and stays owned by whoever
        /// touched it last. The visible result was OttoFuel and
        /// NoVikingLeftBehind seeing empty drawers for no apparent reason,
        /// and OttoFuel feeding a kiln forever because the coal it counts
        /// against its own cutoff was sitting in drawers it could not see.
        ///
        /// Claiming only when the owner is 0 is what keeps this honest.
        /// Nobody holds an unowned ZDO, so there is nothing to steal, and
        /// after the first claim the condition is false -- two clients
        /// reading the same drawer do not trade it back and forth. A drawer
        /// another client genuinely owns is left alone and continues to read
        /// as empty, which is the safe answer: we could not commit a
        /// withdrawal from it, and showing stock we cannot commit is the
        /// duplication RefreshMirror's gate exists to prevent.
        ///
        /// This tick still reads empty. Ownership is a replicated write, not
        /// an immediate one, so the mirror only fills once the claim has
        /// landed -- the same reason DepositRoute.Claiming makes auto-pickup
        /// wait a tick.
        /// </summary>
        private void ClaimForAutomationIfUnowned()
        {
            if (_view == null || !_view.IsValid() || _view.IsOwner()) return;

            var zdo = _view.GetZDO();
            if (zdo == null || zdo.GetOwner() != 0L) return;

            _view.ClaimOwnership();
        }

        /// <summary>
        /// What the mirror is allowed to show: the true Snapshot when this
        /// client owns the ZDO, otherwise an unassigned/empty snapshot --
        /// see RefreshMirror's docstring for why. Never mutates anything;
        /// purely a read.
        /// </summary>
        private DrawerSnapshot EffectiveMirrorSnapshot() =>
            (_view != null && _view.IsValid() && _view.IsOwner()) ? Snapshot : new DrawerSnapshot("", 0);

        /// <summary>
        /// Unconditional rebuild of the mirror from a given snapshot.
        /// Mutates the existing ItemData's m_stack in place where possible
        /// (RemoveAll+AddItem is still one allocation, not
        /// Capacity-many) and is guarded by _applyingMirror so the
        /// RemoveAll/AddItem calls below -- which themselves fire
        /// m_onChanged -- do not re-enter OnMirrorChanged.
        /// </summary>
        private void RebuildMirror(DrawerSnapshot current)
        {
            _applyingMirror = true;
            try
            {
                var inventory = MirrorInventory;
                inventory.RemoveAll();

                if (current.IsAssigned && !current.IsEmpty)
                {
                    var prefab = ItemFacts.Prefab(current.ItemName);
                    if (prefab != null)
                    {
                        var drop = prefab.GetComponent<ItemDrop>();
                        if (drop != null)
                        {
                            var data = drop.m_itemData.Clone();
                            data.m_stack = current.Amount;
                            data.m_dropPrefab = prefab;
                            data.m_gridPos = new Vector2i(0, 0);
                            // Without this, CountItems/HaveItem/RemoveItem(string, ...)
                            // -- all of which filter on
                            // item.m_worldLevel >= Game.m_worldLevel -- see every
                            // drawer as empty stock past the first world-level
                            // bump. Matches how vanilla's own AddItem(GameObject, int)
                            // stamps a freshly materialised ItemData. `global::` is
                            // required here: unqualified `Game` resolves to this
                            // file's own `ItemDrawers.Game` namespace, not the
                            // Valheim `Game` class (confirmed the hard way in an
                            // earlier task -- see task-8-10-report.md).
                            data.m_worldLevel = (byte)global::Game.m_worldLevel;
                            inventory.AddItem(data);
                        }
                    }
                }
                _mirrored = current;
            }
            finally
            {
                _applyingMirror = false;
            }
        }

        /// <summary>
        /// Fires when another mod's Inventory call removed from the mirror
        /// -- guarded against firing from this class's own RebuildMirror
        /// via _applyingMirror. A deposit into the mirror also fires this
        /// (Inventory.Changed doesn't distinguish), but is not acted on:
        /// see the no-deposit-case note on MirrorDelta for why crediting a
        /// foreign deposit here would trust a source removal that, per
        /// vanilla's own add-then-remove move idiom, has not happened yet.
        ///
        /// Reads only slot (0,0), not the whole mirror. The mirror is a
        /// 1x1 Inventory and slot (0,0) is the only slot this class ever
        /// deliberately populates, but reading by position rather than
        /// "whatever GetAllItems() happens to contain" is the more
        /// defensive choice regardless of current sizing: it cannot be
        /// fooled into deriving the wrong item identity from something a
        /// foreign mod left lying around elsewhere in the inventory.
        ///
        /// Three things this deliberately does NOT do, all load-bearing:
        ///
        /// It never diffs the mirror against the live ZDO amount
        /// (Snapshot) -- only against _mirrored, the baseline recorded the
        /// last time this mirror's content was accounted for. The ZDO can
        /// have moved for reasons that have nothing to do with this
        /// specific Inventory call (a normal player interaction, say,
        /// committed without ever touching the mirror); diffing against it
        /// directly inverts the sign whenever that happened, turning a
        /// withdrawal into an apparent deposit that creates items. See
        /// MirrorReconciliation.Compute's own docs and
        /// MirrorReconciliationTests for the worked example. _mirrored is
        /// advanced to the mirror's true content at the end of this method
        /// ONLY when the resulting debit was actually applied -- see below
        /// for why a refused debit must NOT advance it.
        ///
        /// It never mutates _mirror's own list here (no RemoveAll/AddItem).
        /// GetItemAt below reads that list live, and the mod whose call
        /// fired this event may still be iterating it on its own call stack
        /// (Inventory.Changed -- and therefore m_onChanged -- fires
        /// synchronously from inside RemoveItem/AddItem, before either
        /// returns to its caller). Mutating the same list here would throw
        /// an InvalidOperationException out of that mod's own enumerator.
        /// Any mismatch this leaves between the mirror and the ZDO is
        /// picked up by the next RefreshMirror -- driven by the next
        /// Container.GetInventory call, safely off this stack -- which
        /// performs the actual rebuild.
        ///
        /// It does NOT assume TryWithdrawExternally always succeeds here.
        /// An earlier version of this comment claimed the underlying
        /// compare-and-swap "cannot fail at this call site" and called it
        /// a tautology -- that was wrong, and review caught it: this
        /// client may not even own the ZDO (RefreshMirror now builds an
        /// EMPTY mirror in that case specifically to keep this path from
        /// ever running against real stock -- see RefreshMirror's
        /// docstring -- but a foreign mod holding a REFERENCE to a mirror
        /// built earlier, while this client still owned the ZDO, can still
        /// call RemoveItem on it after ownership has since moved on), or
        /// WriteOwned's own compare-and-swap can lose a genuine
        /// split-ownership race (see WriteOwned's docstring) even while
        /// this client nominally still owns the ZDO. Either way,
        /// TryWithdrawExternally returning false means the ZDO was NOT
        /// debited even though the mirror's live list already lost the
        /// item (the foreign mod's RemoveItem already ran, before this
        /// handler was ever invoked) -- advancing _mirrored to match that
        /// list would make the next RefreshMirror believe the mirror
        /// already reflects reality and skip correcting it, permanently
        /// under-reporting the drawer's true contents. So on a refusal,
        /// _mirrored is left untouched and _mirrorNeedsRebuild is set
        /// instead, forcing the next RefreshMirror to rebuild the mirror
        /// back to ZDO truth regardless of what _mirrored happens to
        /// already equal.
        /// </summary>
        private void OnMirrorChanged()
        {
            if (_applyingMirror) return;

            var slot = _mirror.GetItemAt(0, 0);
            string mirrorItem = slot != null ? (ItemFacts.PrefabNameOf(slot) ?? "") : "";
            int mirrorTotal = slot != null ? slot.m_stack : 0;

            var zdo = Snapshot;
            var delta = MirrorReconciliation.Compute(
                mirrorItem, mirrorTotal,
                _mirrored.ItemName, _mirrored.Amount,
                zdo.ItemName, zdo.Amount);

            if (delta.Withdraw > 0 && !TryWithdrawExternally(delta.Withdraw, out _))
            {
                _mirrorNeedsRebuild = true;
                return;
            }

            // _mirrored now records what slot (0,0) actually, physically
            // holds -- not what we wish it held. Only reached when there
            // was nothing to debit, or the debit above genuinely applied.
            _mirrored = new DrawerSnapshot(mirrorItem, mirrorTotal);
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
            return Localization.instance.Localize(
                $"{label}  <color=orange>{s.Amount}</color>/{Capacity}\n" +
                "[<color=yellow><b>E</b></color>] Take stack\n" +
                "[<color=yellow><b>Ctrl+E</b></color>] Take one\n" +
                "[<color=yellow><b>Shift+E</b></color>] Store all");
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
}
