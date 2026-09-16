using System;
using System.Collections.Generic;
using HarmonyLib;
using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Payload for one outstanding withdraw request -- see
    /// DrawerComponent.RequestWithdraw for how it is created and
    /// DrawerManager for why it lives here rather than on the
    /// DrawerComponent that created it. TargetOwner is the peer id this
    /// request (and every retry of it) is pinned to; PlayerPosition is
    /// captured at request time purely as a last-resort spill target if
    /// the player reference itself is gone by the time a grant arrives.
    /// </summary>
    internal sealed class PendingWithdrawal
    {
        public readonly ZDOID DrawerId;
        public readonly long TargetOwner;
        public readonly Player Player;
        public readonly int Requested;
        public readonly Vector3 PlayerPosition;

        public PendingWithdrawal(ZDOID drawerId, long targetOwner, Player player, int requested, Vector3 playerPosition)
        {
            DrawerId = drawerId;
            TargetOwner = targetOwner;
            Player = player;
            Requested = requested;
            PlayerPosition = playerPosition;
        }
    }

    /// <summary>Payload for one outstanding deposit request -- see PendingWithdrawal and DrawerComponent.RequestDeposit.</summary>
    internal sealed class PendingDeposit
    {
        public readonly ZDOID DrawerId;
        public readonly long TargetOwner;
        public readonly Player Player;
        public readonly string ItemName;
        public readonly int Removed;
        public readonly Vector3 PlayerPosition;
        public readonly bool Announce;

        public PendingDeposit(ZDOID drawerId, long targetOwner, Player player, string itemName, int removed, Vector3 playerPosition, bool announce)
        {
            DrawerId = drawerId;
            TargetOwner = targetOwner;
            Player = player;
            ItemName = itemName;
            Removed = removed;
            PlayerPosition = playerPosition;
            Announce = announce;
        }
    }

    /// <summary>
    /// The only ticking object in this mod. Individual drawers have no
    /// Update and no InvokeRepeating: a hundred of them in a wall must cost
    /// what one costs, which is why every periodic concern lives here.
    ///
    /// This is also, deliberately, where the RPC-to-owner protocol's
    /// requester-side pending state lives (see PendingRequestLedger in
    /// ItemDrawers.Core, and PendingWithdrawal/PendingDeposit above) rather
    /// than on the DrawerComponent that issued a request. This
    /// MonoBehaviour is DontDestroyOnLoad and created once per client
    /// session; a DrawerComponent is not -- it is destroyed and recreated
    /// every time its drawer leaves and re-enters the locally active area
    /// (a portal trip, a zone unload, simply walking far enough away).
    /// Keeping a still-outstanding request's state on the component would
    /// mean a deposit that had already removed real items from a player's
    /// inventory could be silently destroyed by an ordinary render-distance
    /// event with no spill, no message, and no trace -- exactly the defect
    /// this task's review found and named Critical.
    /// </summary>
    public class DrawerManager : MonoBehaviour
    {
        public static DrawerManager Instance { get; private set; }

        private readonly SpatialGrid<DrawerComponent> _grid = new SpatialGrid<DrawerComponent>(8f);
        private readonly List<DrawerComponent> _all = new List<DrawerComponent>();
        private readonly HashSet<DrawerComponent> _dirty = new HashSet<DrawerComponent>();

        // Player withdrawals waiting for ownership to settle. The wait is
        // real -- the peer we just took the drawer from may have an absolute
        // write in flight -- but failing the player for it is a choice, and
        // the wrong one: they pressed a key and the drawer is theirs to use.
        // Holding the request for the second it takes turns "Try again" into
        // a short delay. See DrawerComponent.RequestWithdraw.
        private readonly List<DeferredWithdraw> _deferredWithdraws = new List<DeferredWithdraw>();

        // Views another mod changed this frame; written to their ZDOs in
        // LateUpdate, once each, however many Inventory calls changed them.
        private readonly HashSet<DrawerComponent> _viewDirty = new HashSet<DrawerComponent>();
        private readonly List<DrawerComponent> _viewFlushScratch = new List<DrawerComponent>(16);

        private float _cullTimer;
        private int _syncCursor;

        private float _pickupTimer;
        private readonly List<DrawerComponent> _pickupScratch = new List<DrawerComponent>(16);

        // Seeded ONCE per client session from a wall-clock value, never
        // reset when an individual drawer's component is destroyed and
        // recreated -- see RequestIdGenerator's docstring for why a
        // per-component counter that restarts at a small number on every
        // rebuild is a Critical bug (a fresh, unrelated request can
        // collide with a still-cached answer to an old one under the same
        // small id).
        private readonly RequestIdGenerator _requestIds = new RequestIdGenerator(DateTimeOffset.UtcNow.Ticks);

        private readonly PendingRequestLedger<PendingWithdrawal> _pendingWithdrawals =
            new PendingRequestLedger<PendingWithdrawal>(DrawerComponent.RequestTimeoutSeconds, DrawerComponent.MaxRequestAttempts);
        private readonly PendingRequestLedger<PendingDeposit> _pendingDeposits =
            new PendingRequestLedger<PendingDeposit>(DrawerComponent.RequestTimeoutSeconds, DrawerComponent.MaxRequestAttempts);

        public static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("RidDrawerManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DrawerManager>();
        }

        public long NextRequestId() => _requestIds.Next();

        internal void AddPendingWithdrawal(long id, PendingWithdrawal payload) => _pendingWithdrawals.Add(id, payload, Time.time);
        internal void AddPendingDeposit(long id, PendingDeposit payload) => _pendingDeposits.Add(id, payload, Time.time);

        internal bool TryCompleteWithdrawal(long id, out PendingWithdrawal payload) => _pendingWithdrawals.TryComplete(id, out payload);
        internal bool TryCompleteDeposit(long id, out PendingDeposit payload) => _pendingDeposits.TryComplete(id, out payload);

        /// <summary>
        /// Called from DrawerComponent.OnDestroy. Resolves -- immediately,
        /// not on the next Tick -- every pending request this now-destroyed
        /// component originated for <paramref name="drawerId"/>, rather
        /// than leaving them to wait out a deadline that may never usefully
        /// arrive (the drawer may not come back into range before then, or
        /// ever). A withdrawal is simply dropped (nothing was ever given to
        /// anyone yet); a deposit spills its already-removed items -- see
        /// GiveUpDeposit.
        /// </summary>
        public void ResolvePendingForDrawer(ZDOID drawerId)
        {
            ResolveMatching(_pendingWithdrawals, drawerId, p => p.DrawerId, GiveUpWithdrawal);
            ResolveMatching(_pendingDeposits, drawerId, p => p.DrawerId, GiveUpDeposit);
        }

        private static void ResolveMatching<T>(
            PendingRequestLedger<T> ledger, ZDOID drawerId, Func<T, ZDOID> drawerOf, Action<long, T> onResolved)
        {
            List<long> matching = null;
            foreach (var kv in ledger.All())
                if (drawerOf(kv.Value).Equals(drawerId))
                    (matching ?? (matching = new List<long>())).Add(kv.Key);
            if (matching == null) return;

            foreach (var id in matching)
                if (ledger.Remove(id, out var payload))
                    onResolved(id, payload);
        }

        /// <summary>
        /// Last-resort drain, called from BOTH a Harmony prefix on
        /// Game.Shutdown (GameShutdownPatch below -- the primary,
        /// verified-correct hook, see its own docstring) and this
        /// component's own OnApplicationQuit (a redundant second net for
        /// any quit path this task's decompile audit missed). Safe to call
        /// twice, or a hundred times: each entry is REMOVED from the
        /// ledger as it is spilled, so a second call -- from whichever
        /// hook did not fire first -- finds nothing left to do.
        ///
        /// An earlier version of this method iterated
        /// <see cref="_pendingDeposits"/>.All() and called GiveUpDeposit
        /// WITHOUT removing each entry, on the (wrong) assumption that
        /// OnApplicationQuit was the only caller and the process would be
        /// gone before anything else could observe the ledger again. That
        /// was a genuine duplication bug review caught: DrawerComponent's
        /// own OnDestroy (firing as Unity tears down every GameObject
        /// during the same shutdown) calls ResolvePendingForDrawer for the
        /// same still-present entries moments later, spilling each one a
        /// SECOND time. Removing as we go closes that regardless of what
        /// order Unity happens to tear things down in.
        ///
        /// Anything still pending at this point already had real items
        /// removed from a player who is about to vanish along with
        /// everything else this session holds in memory -- spill now or
        /// those items cease to exist. Withdrawals need no equivalent:
        /// nothing was ever removed from anywhere for one until its grant
        /// reply actually arrives and is banked, so they are left to
        /// simply be discarded along with everything else in memory.
        /// </summary>
        internal void DrainPendingDepositsBeforeSave()
        {
            List<long> ids = null;
            foreach (var kv in _pendingDeposits.All())
                (ids ?? (ids = new List<long>())).Add(kv.Key);
            if (ids == null) return;

            foreach (var id in ids)
                if (_pendingDeposits.Remove(id, out var payload))
                    GiveUpDeposit(id, payload);
        }

        private void OnApplicationQuit() => DrainPendingDepositsBeforeSave();

        public void Register(DrawerComponent drawer)
        {
            if (drawer == null || _all.Contains(drawer)) return;
            _all.Add(drawer);
            var p = drawer.transform.position;
            _grid.Insert(drawer, p.x, p.y, p.z);

            // Not drawer.RefreshFace() directly: this runs from inside
            // DrawerComponent.Awake, and Unity does not guarantee Awake
            // order between sibling components on the same freshly
            // instantiated object -- the drawer's own DrawerRenderer.Awake
            // (which assigns the face's font and material) may not have run
            // yet. Marking dirty defers the actual refresh to this
            // manager's own next Update(), by which point every Awake for
            // this frame is guaranteed complete. This is also what makes a
            // drawer's face show correctly right after placement and after
            // a world loads -- previously nothing called RefreshFace() at
            // either of those points except an unreliable direct call from
            // Awake, so a drawer with contents could show an empty face
            // until this client happened to mutate it itself.
            MarkDirty(drawer);
        }

        public void Unregister(DrawerComponent drawer)
        {
            if (drawer == null) return;
            _all.Remove(drawer);
            _dirty.Remove(drawer);
            _viewDirty.Remove(drawer);
            _grid.Remove(drawer);
        }

        public void MarkDirty(DrawerComponent drawer)
        {
            if (drawer != null) _dirty.Add(drawer);
        }

        internal void MarkViewDirty(DrawerComponent drawer)
        {
            if (drawer != null) _viewDirty.Add(drawer);
        }

        /// <summary>True when some view changed since the last flush; lets ZNetViewResetZdoPatch skip every other object for free.</summary>
        internal bool HasDirtyViews => _viewDirty.Count > 0;

        /// <summary>
        /// Hands every dirty view to DrawerComponent.FlushView. In LateUpdate
        /// that claims, waits for ownership to settle (re-queuing the view
        /// each frame) and then reconciles. GameShutdownPatch passes
        /// teardown, which writes each delta immediately instead. Teardown
        /// never spawns anything, so it is also safe inside
        /// ZNetScene.Shutdown's enumeration. Iterates a copy: FlushView may
        /// re-queue a view.
        /// </summary>
        internal void FlushViewWrites(bool teardown = false)
        {
            if (_viewDirty.Count == 0) return;
            _viewFlushScratch.Clear();
            _viewFlushScratch.AddRange(_viewDirty);
            _viewDirty.Clear();

            for (int i = 0; i < _viewFlushScratch.Count; i++)
                if (_viewFlushScratch[i] != null) _viewFlushScratch[i].FlushView(teardown);
            _viewFlushScratch.Clear();
        }

        private void LateUpdate() => FlushViewWrites();

        /// <summary>
        /// Owner-side reconcile of view changes, from this client or any
        /// other. One revision comparison per owned drawer per frame unless
        /// its ZDO data actually changed.
        /// </summary>
        private void ReconcileViews()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var drawer = _all[i];
                if (drawer != null && drawer.ViewNeedsReconcile()) drawer.ReconcileView();
            }
        }

        /// <summary>
        /// Rebuilds every non-dirty view whose ZDO data changed, in place
        /// (the Inventory instance never changes). Mods that cache
        /// GetInventory() or Container.m_inventory -- NoVikingLeftBehind
        /// does -- otherwise read counts from whenever they last called
        /// GetInventory and can take more than the drawer holds. Idle cost
        /// is one revision comparison per drawer, no allocation. Runs after
        /// ReconcileViews, so an owner's fresh publish is already current.
        /// </summary>
        private void RefreshViews()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var drawer = _all[i];
                if (drawer == null) continue;
                var view = drawer.View;
                if (view != null && !view.IsDirty) view.RefreshFromZdo();
            }
        }

        public void QueryNear(Vector3 position, float radius, List<DrawerComponent> results)
        {
            _grid.Query(position.x, position.y, position.z, radius, results);
        }

        private void Update()
        {
            if (_deferredWithdraws.Count > 0) TickDeferredWithdraws();
            if (_dirty.Count > 0) FlushDirty();

            ReconcileViews();
            RefreshViews();

            SyncSomeFaces();

            if (_pendingWithdrawals.Count > 0)
                _pendingWithdrawals.Tick(Time.time, RetryWithdrawal, GiveUpWithdrawal);
            if (_pendingDeposits.Count > 0)
                _pendingDeposits.Tick(Time.time, RetryDeposit, GiveUpDeposit);

            _cullTimer -= Time.deltaTime;
            if (_cullTimer <= 0f)
            {
                _cullTimer = 0.5f;
                UpdateLabelVisibility();
            }

            _pickupTimer -= Time.deltaTime;
            if (_pickupTimer <= 0f)
            {
                _pickupTimer = DrawerConfig.PickupInterval.Value;
                if (DrawerConfig.AutoPickupEnabled.Value) RunAutoPickup();
            }
        }

        /// <summary>
        /// Iterates dropped items and asks which drawers are near them --
        /// never the reverse. Cost scales with items on the ground, which is
        /// normally zero, rather than with the number of drawers.
        ///
        /// Enumerates <see cref="ItemDrop.s_instances"/> directly (confirmed
        /// present in Valheim 1.0 by decompiling assembly_valheim.dll) rather
        /// than a Physics.OverlapSphere fallback: it is exact -- no layer
        /// mask or collider-radius guessing -- and it is already the list
        /// Valheim itself maintains, so this adds no bookkeeping of its own.
        /// </summary>
        private void RunAutoPickup()
        {
            var player = Player.m_localPlayer;
            if (player == null || _all.Count == 0) return;

            var drops = ItemDrop.s_instances;
            if (drops == null || drops.Count == 0) return;

            float radius = DrawerConfig.PickupRadius.Value;
            float scanRange = DrawerConfig.PickupScanRange.Value;
            float scanRangeSq = scanRange * scanRange;
            var playerPos = player.transform.position;

            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                if (drop == null) continue;
                if ((drop.transform.position - playerPos).sqrMagnitude > scanRangeSq) continue;

                if (drop.m_nview == null || !drop.m_nview.IsValid()) continue;
                if (drop.m_itemData == null || drop.m_itemData.m_shared.m_maxStackSize <= 1) continue;

                string itemName = drop.m_itemData.m_dropPrefab != null
                    ? drop.m_itemData.m_dropPrefab.name
                    : drop.name.Replace("(Clone)", "");

                _pickupScratch.Clear();
                QueryNear(drop.transform.position, radius, _pickupScratch);

                // Find the drawer that wants this drop BEFORE worrying about
                // who owns it. Ownership is only needed to actually take the
                // item, and asking for it has a cost (see below), so it is
                // not worth paying for a drop no drawer wants.
                DrawerComponent target = null;
                for (int d = 0; d < _pickupScratch.Count; d++)
                {
                    var candidate = _pickupScratch[d];
                    if (candidate == null) continue;

                    var snapshot = candidate.Snapshot;
                    if (!snapshot.IsAssigned || snapshot.ItemName != itemName) continue;

                    target = candidate;
                    break;
                }
                if (target == null) continue;

                // A drop must be OWNED by this client before its stack can be
                // changed, and this is where multiplayer pickup was failing.
                // The previous code skipped any drop it did not already own
                // and never asked to own one, so whether a drop was ever
                // absorbed came down to who happened to hold its ZDO: items
                // you dropped yourself worked, because dropping makes you the
                // owner, while anything from a mob kill, another player, or a
                // zone that had just loaded could sit next to a matching
                // drawer forever. That is the inconsistency.
                //
                // RequestOwn is what vanilla does in the same spot --
                // Player's own auto-pickup calls it when CanPickup fails and
                // retries on a later frame -- because ownership transfer is a
                // network round trip and cannot complete inside this call.
                // So: ask, skip this drop for now, and absorb it on a
                // subsequent tick once the transfer lands.
                //
                // CanPickup rather than a bare IsOwner check, so this also
                // inherits vanilla's settle delay on freshly dropped items
                // instead of snatching one out of the air the instant it
                // leaves a player's hands.
                if (!drop.CanPickup())
                {
                    drop.RequestOwn();
                    continue;
                }

                // Both deposit paths below report how many items the drawer
                // took responsibility for, and removing exactly that -- no
                // more, no less -- from the drop's stack is what keeps this
                // conservative. Anything else creates or destroys items.
                //
                // The foreign path's count is not "accepted" so much as
                // "handed over": the owning client credits what fits and the
                // pending record spills any remainder back on the ground at
                // the drop's position. Either way the items exist exactly
                // once, which is the only property that matters here.
                int accepted;
                switch (target.ResolveDepositRoute())
                {
                    case DrawerComponent.DepositRoute.Owned:
                        if (!target.TryDepositExternally(itemName, drop.m_itemData.m_stack, out accepted))
                            continue;
                        break;

                    case DrawerComponent.DepositRoute.Foreign:
                        if (!target.TrySubmitForeignDeposit(
                                itemName, drop.m_itemData.m_stack, drop.transform.position, out accepted))
                            continue;
                        break;

                    // Claiming: ownership was just requested and has not
                    // settled. Unavailable: no usable view. Either way, leave
                    // the drop alone and look at it again next tick.
                    default:
                        continue;
                }

                if (accepted <= 0) continue;

                drop.m_itemData.m_stack -= accepted;
                if (drop.m_itemData.m_stack <= 0)
                    ZNetScene.instance.Destroy(drop.gameObject);
                else
                    drop.Save();
            }
        }

        /// <summary>
        /// Finds the currently-instantiated DrawerComponent for a given
        /// ZDOID, if any -- needed because a retry must be sent through
        /// SOME live ZNetView representing this drawer, and the component
        /// that originally issued the request may have since been
        /// destroyed and (perhaps) recreated as the drawer left and
        /// re-entered range. A linear scan of DrawerComponent.All is fine
        /// here: retries are rare (the overwhelmingly common request never
        /// needs one at all) and All is typically a few dozen to a few
        /// hundred entries, not a per-frame cost.
        /// </summary>
        private static DrawerComponent FindLive(ZDOID drawerId)
        {
            var all = DrawerComponent.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ZdoId.Equals(drawerId))
                    return all[i];
            return null;
        }

        private void RetryWithdrawal(long id, PendingWithdrawal payload)
        {
            var drawer = FindLive(payload.DrawerId);
            if (drawer == null) { GiveUpWithdrawal(id, payload); _pendingWithdrawals.Remove(id, out _); return; }
            drawer.SendWithdrawRequestRpc(id, payload.TargetOwner, payload.Requested);
        }

        private void RetryDeposit(long id, PendingDeposit payload)
        {
            var drawer = FindLive(payload.DrawerId);
            if (drawer == null) { GiveUpDeposit(id, payload); _pendingDeposits.Remove(id, out _); return; }
            drawer.SendDepositRequestRpc(id, payload.TargetOwner, payload.ItemName, payload.Removed);
        }

        /// <summary>
        /// Every retry across the whole timeout window failed to produce a
        /// reply (or, mid-window, this drawer stopped being locally
        /// instantiated at all -- see RetryWithdrawal). Nothing was ever
        /// given to the player for a withdrawal that never got a grant, so
        /// there is nothing to unwind: just tell them.
        /// </summary>
        private static void GiveUpWithdrawal(long id, PendingWithdrawal payload)
        {
            // A request that never came back: counted, and its timing entry
            // dropped so an unanswered send cannot leak one.
            DrawerDiagnostics.GrantsGivenUp++;
            DrawerDiagnostics.RequestClosed(id);

            payload.Player?.Message(MessageHud.MessageType.Center, DrawerComponent.CommitFailedMessage);
        }

        /// <summary>
        /// The owner never replied across every retry -- either it
        /// disconnected before this request could reach it (a request
        /// pinned to a peer that then disconnects is silently dropped by
        /// ZRoutedRpc.RouteRPC -- confirmed by decompile), or it simply
        /// stopped being this drawer's owner and correctly refused to act
        /// on a pinned retry that still reached it (see the class-level RPC
        /// comment on DrawerComponent for why retries are pinned rather
        /// than re-resolving the current owner). The player's items were
        /// already removed from their inventory before this deposit was
        /// ever requested (see DrawerComponent.RequestDeposit's
        /// docstring), so there is nowhere left to refund them TO in their
        /// inventory -- it may be full, or they may be far away by now, or
        /// (see ResolvePendingForDrawer) their own drawer component may no
        /// longer even exist locally. Spilling them at the position they
        /// were standing at when the deposit began is the same recovery
        /// this codebase already uses when a drawer is destroyed out from
        /// under a deposit (DrawerComponent.OnDrawerDestroyed) -- visible
        /// and recoverable, never silently deleted.
        ///
        /// Spills at the player's CURRENT position when the player still
        /// exists, not the position captured when the request was first
        /// sent -- this can wait out up to three retries (up to
        /// MaxRequestAttempts * RequestTimeoutSeconds, ~15s by default),
        /// which is easily enough time for a player to have walked well
        /// away from where they started; spilling at a stale position
        /// would drop the items somewhere the player no longer is. The
        /// captured PlayerPosition is used only as a last resort, for a
        /// player reference that is itself gone (application quit,
        /// disconnect) by the time this runs.
        ///
        /// See this task's report for the narrow residual window this does
        /// not close (a reply that was merely very slow, not lost,
        /// arriving after this give-up).
        /// </summary>
        private static void GiveUpDeposit(long id, PendingDeposit payload)
        {
            if (payload.Removed > 0)
            {
                Vector3 spillAt = payload.Player != null ? payload.Player.transform.position : payload.PlayerPosition;
                ItemFacts.SpillAtPosition(spillAt, payload.ItemName, payload.Removed);
            }
            payload.Player?.Message(MessageHud.MessageType.Center, DrawerComponent.CommitFailedMessage);
        }

        /// <summary>Drawers this client changed itself: refresh immediately.</summary>
        private void FlushDirty()
        {
            foreach (var drawer in _dirty)
                if (drawer != null) drawer.RefreshFace();
            _dirty.Clear();
        }

        /// <summary>
        /// Drawers another client changed: their ZDO values arrive by
        /// replication with no local event, so a slice of the list is
        /// re-read each frame. Show() returns immediately when nothing
        /// changed, so this is a cheap comparison, not a rebuild.
        ///
        /// Also retries a renderer's count-text font here if it's still
        /// unresolved (DrawerRenderer.NeedsFont): Awake only gets one
        /// attempt at DrawerFont.Shared, and that first attempt can
        /// legitimately fail (Valheim's own UI may not have created any
        /// TMP_Text yet that early) without the failure being permanent.
        /// This reuses the same rotating slice rather than a second scan,
        /// so a fontless drawer gets retried about as often as its face
        /// already gets re-synced.
        /// </summary>
        private void SyncSomeFaces()
        {
            if (_all.Count == 0) return;

            int slice = Mathf.Max(1, _all.Count / 30);   // whole list about twice a second
            for (int i = 0; i < slice; i++)
            {
                if (_syncCursor >= _all.Count) _syncCursor = 0;
                var drawer = _all[_syncCursor++];
                if (drawer == null) continue;

                if (drawer.Face != null && drawer.Face.NeedsFont)
                    drawer.Face.TryConfigureCountText();

                drawer.RefreshFace();
            }
        }

        /// <summary>
        /// A player withdrawal held until the drawer's ownership settles.
        /// </summary>
        private struct DeferredWithdraw
        {
            public DrawerComponent Drawer;
            public Player Player;
            public int Requested;
            public float Deadline;
        }

        /// <summary>
        /// Queues a withdrawal to run as soon as ownership settles. Called
        /// instead of refusing the player outright.
        /// </summary>
        internal void DeferWithdraw(DrawerComponent drawer, Player player, int requested, float timeoutSeconds)
        {
            if (drawer == null || player == null || requested <= 0) return;

            // One deferral per drawer. A player holding the key down would
            // otherwise queue a dozen withdrawals that all fire the instant
            // ownership settles, emptying the drawer in one frame.
            for (int i = 0; i < _deferredWithdraws.Count; i++)
                if (_deferredWithdraws[i].Drawer == drawer) return;

            _deferredWithdraws.Add(new DeferredWithdraw
            {
                Drawer = drawer,
                Player = player,
                Requested = requested,
                Deadline = Time.time + timeoutSeconds,
            });
        }

        private void TickDeferredWithdraws()
        {
            for (int i = _deferredWithdraws.Count - 1; i >= 0; i--)
            {
                var pending = _deferredWithdraws[i];

                if (pending.Drawer == null || pending.Player == null)
                {
                    _deferredWithdraws.RemoveAt(i);
                    continue;
                }

                if (pending.Drawer.TryCompleteDeferredWithdraw(pending.Player, pending.Requested))
                {
                    _deferredWithdraws.RemoveAt(i);
                    continue;
                }

                // Only now does the player hear about it. Ownership that has
                // not settled in this long is not a momentary handover, and
                // silently dropping the request would be worse than saying so.
                if (Time.time >= pending.Deadline)
                {
                    pending.Player.Message(MessageHud.MessageType.Center, DrawerComponent.CommitFailedMessage);
                    _deferredWithdraws.RemoveAt(i);
                }
            }
        }

        private void UpdateLabelVisibility()
        {
            var camera = Camera.main;
            if (camera == null) return;

            var eye = camera.transform.position;
            float radius = DrawerConfig.LabelDistance.Value;
            float radiusSq = radius * radius;

            for (int i = 0; i < _all.Count; i++)
            {
                var drawer = _all[i];
                if (drawer == null) continue;
                drawer.Face?.SetLabelVisible(
                    (drawer.transform.position - eye).sqrMagnitude <= radiusSq);
            }
        }
    }

    /// <summary>
    /// Runs DrawerManager's pending-deposit drain BEFORE the world save
    /// that a quit performs, on both quit paths this task's decompile
    /// found -- neither of which is reliably covered by Unity's own
    /// OnApplicationQuit message alone (see DrawerManager's own comment on
    /// this).
    ///
    /// Verified by decompiling assembly_valheim.dll, not assumed:
    /// <c>Game.Shutdown(bool saveWorld)</c> is the one method both quit
    /// paths funnel through --
    /// <c>Game.ContinueLogout</c> (the pause-menu "Save and Exit" flow)
    /// calls it directly and then <c>SystemResourceManager.FastLoadScene</c>,
    /// NEVER <c>Application.Quit()</c> -- confirming Unity's
    /// <c>OnApplicationQuit</c> message is never broadcast to ANY
    /// MonoBehaviour for that path, ours included -- while
    /// <c>Game.OnApplicationQuit</c> (Unity's own engine-level handler,
    /// fired only on an actual process exit) also calls
    /// <c>Shutdown(saveWorld)</c>. Inside <c>Game.Shutdown</c>, in order:
    /// <c>SavePlayerProfile</c>, then <c>ZNetScene.instance.Shutdown()</c>,
    /// then <c>ZNet.instance.Shutdown(saveWorld)</c> -- and
    /// <c>ZNet.Shutdown(save: true)</c> calls <c>Save(sync: true)</c>,
    /// which performs the actual world save SYNCHRONOUSLY
    /// (<c>SaveWorld(sync: true)</c> starts <c>SaveWorldThread</c> and
    /// immediately <c>Join()</c>s it before <c>Shutdown</c> returns). A
    /// Harmony PREFIX on <c>Game.Shutdown</c> therefore runs before any of
    /// that -- before the synchronous world save -- on both paths
    /// uniformly, which is exactly the guarantee neither Unity's
    /// OnApplicationQuit message (unreliable ordering across components,
    /// and simply absent on the "Save and Exit" path) nor a prefix on
    /// <c>ZNet.Save</c>/<c>SaveWorld</c> alone would give (those don't
    /// fire on the "Save and Exit" path's specific call chain any
    /// differently than Shutdown itself does, and patching the outermost
    /// method is simpler and covers both callers with one patch).
    /// </summary>
    [HarmonyPatch(typeof(global::Game), nameof(global::Game.Shutdown))]
    internal static class GameShutdownPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(global::Game), nameof(global::Game.Shutdown));

        private static void Prefix()
        {
            // Teardown: there is no time left to wait for a claim to settle.
            DrawerManager.Instance?.FlushViewWrites(teardown: true);
            DrawerManager.Instance?.DrainPendingDepositsBeforeSave();
        }
    }

    /// <summary>
    /// Flushes a drawer's dirty container view before its ZNetView loses its
    /// ZDO. Verified by decompiling assembly_valheim.dll: every ZNetScene
    /// removal path (RemoveObjects when a zone unloads, Destroy,
    /// OnZDODestroyed, Shutdown) calls <c>ZNetView.ResetZDO()</c> -- which
    /// sets m_zdo to null -- before <c>Object.Destroy</c>, so by the time
    /// DrawerComponent.OnDestroy runs its FlushView finds no ZDO and a change
    /// another mod made this frame would be lost -- including a view still
    /// waiting for its ownership claim to settle. This teardown flush writes
    /// the delta immediately (see DrawerComponent.FlushView). A ZDO that is
    /// no longer valid (the OnZDODestroyed path) is skipped; there is
    /// nothing to write to.
    /// </summary>
    [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.ResetZDO))]
    internal static class ZNetViewResetZdoPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ZNetView), nameof(ZNetView.ResetZDO));

        private static void Prefix(ZNetView __instance)
        {
            var manager = DrawerManager.Instance;
            if (manager == null || !manager.HasDirtyViews) return;
            if (__instance == null || !__instance.IsValid()) return;

            var drawer = __instance.GetComponent<DrawerComponent>();
            if (drawer != null) drawer.FlushView(teardown: true);
        }
    }
}
