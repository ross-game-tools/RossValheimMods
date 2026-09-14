using System;
using System.Collections.Generic;
using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// What Container.GetInventory returns for a drawer: a small real
    /// Inventory laid out by ViewLayout, so every container-aware mod can
    /// read, take from and store into a drawer with the ordinary API.
    ///
    /// The drawer's Prefab + Amount stay the source of truth. Changes made
    /// through this Inventory mark it dirty; at the end of the frame
    /// DrawerManager hands every dirty view to DrawerComponent.FlushView,
    /// which claims ownership when this peer does not hold it, waits with
    /// the view still dirty until ownership has settled, and then
    /// reconciles the change into Amount and republishes (ReconcileView,
    /// Publish) as the owner. Non-owners never write ViewSlots, except as
    /// a last resort when the drawer unloads or the game shuts down
    /// (WriteDeltaToZdo). The owner also reconciles ViewSlots written by
    /// such a last-resort write on its tick.
    ///
    /// The Inventory instance is created once and never replaced: every
    /// rebuild (RefreshFromZdo, Publish) rewrites its item list in place,
    /// so a mod holding the reference (or Container.m_inventory) always
    /// sees current contents.
    ///
    /// ViewSlots is ItemDrawers' own format, never vanilla item bytes:
    /// Inventory.Load caps each stack at its max stack size, which would
    /// truncate slot 1 and read as a huge withdrawal.
    ///
    /// The Inventory is never mutated from inside its own m_onChanged
    /// handler; rebuilds happen from GetInventory/Load calls or the owner's
    /// tick, never during another mod's AddItem/RemoveItem.
    /// </summary>
    internal sealed class DrawerView
    {
        internal const int SlotCount = ViewLayout.DefaultSlotCount;
        internal const int FormatVersion = 1;
        internal const int NoBaseline = -1;

        // Upper bound on foreign entries accepted when decoding, so a
        // corrupt package cannot allocate without limit. Encode enforces
        // the same cap so what is written can always round-trip through
        // TryDecode -- the two must never disagree.
        private const int MaxForeignEntries = 64;

        // Upper bound on any single decoded (or locally accumulated) slot
        // or foreign count. ViewSlots is a ZDO field: ANY peer can write
        // it, maliciously or via a bug, and ZDO replication applies
        // whatever bytes arrive with no range check of its own. Without
        // this cap a small number of entries with near-int.MaxValue counts
        // could overflow int arithmetic downstream (Sum, baseline
        // comparisons) and wrap to a small or negative total, which reads
        // as a huge spurious withdrawal or deposit. A million of one item
        // is already far beyond anything a drawer can hold.
        private const int MaxEntryValue = 1_000_000;

        internal static readonly int KeyViewSlotsHash = "ViewSlots".GetStableHashCode();
        internal static readonly int KeyViewBaselineHash = "ViewBaseline".GetStableHashCode();

        private readonly DrawerComponent _drawer;
        private readonly ZNetView _nview;
        private readonly Inventory _inventory;

        // Foreign items read from ViewSlots are not placed in the grid (they
        // would occupy a slot the layout needs); they are carried here so
        // TryReadForReconcile can report them to the owner for spilling.
        // WriteDeltaToZdo does NOT read this to build what it writes -- it
        // re-decodes the ZDO fresh at write time instead.
        private readonly List<KeyValuePair<string, int>> _carriedForeign = new List<KeyValuePair<string, int>>();

        private bool _applying;
        private bool _dirty;
        private bool _unresolved;

        // What the grid was last built from (RefreshFromZdo, Publish), so a
        // DataRevision bump that changed none of it skips the rebuild.
        private byte[] _builtBytes;
        private int _builtBaseline = NoBaseline;
        private int _builtAmount;
        private string _builtItem;

        // The inputs of the last NeedsReconcile decode that found nothing to
        // do; equal inputs skip the decode.
        private bool _checkedClean;
        private byte[] _checkedBytes;
        private int _checkedBaseline;
        private int _checkedAmount;

        // ReadLive runs on every flush and Save; warn about an
        // unidentifiable item once per drawer, not once per call.
        private bool _warnedNoDropPrefab;
        private int _localBaseline;
        private uint _loadedRevision = uint.MaxValue;
        private uint _checkedRevision = uint.MaxValue;

        // This view's own last known drawer-item total and foreign counts,
        // as of its last successful load (RefreshFromZdo), publish
        // (Publish), or write (WriteDeltaToZdo). The difference between
        // these and the live Inventory now is exactly what THIS peer has
        // changed since then -- HasUnflushedChange, TryReadForReconcile's
        // dirty branch and WriteDeltaToZdo all measure that delta.
        private int _syncTotal;
        private Dictionary<string, int> _syncForeign = new Dictionary<string, int>();

        internal DrawerView(DrawerComponent drawer, ZNetView nview)
        {
            _drawer = drawer;
            _nview = nview;
            _inventory = new Inventory("drawer", null, 0, 1);
            _inventory.m_onChanged = (Action)Delegate.Combine(_inventory.m_onChanged, new Action(OnInventoryChanged));
        }

        internal Inventory Inventory => _inventory;

        internal bool IsDirty => _dirty;

        internal Inventory GetForCaller()
        {
            RefreshFromZdo();
            return _inventory;
        }

        /// <summary>
        /// Rebuilds the Inventory from ViewSlots when the ZDO's data revision
        /// changed since the last load/write and nothing local is pending.
        /// A drawer with no ViewSlots yet (every pre-1.0 drawer) is laid out
        /// from its current Amount, without writing; the owner's tick
        /// publishes it.
        ///
        /// Dirty normally blocks a refresh (a local change in flight must
        /// not be clobbered by a concurrent ZDO read), except for a view
        /// that has never loaded at all: flushing refuses to report anything for
        /// such a view (nothing trustworthy to report yet -- see its own
        /// docstring), so blocking its first load here too would leave it
        /// permanently stuck showing nothing.
        /// </summary>
        internal bool RefreshFromZdo()
        {
            if (_dirty && _loadedRevision != uint.MaxValue) return false;

            // Like vanilla Container.Load: don't reload a container a player
            // (or a chest-UI mod) has open. Only while this peer owns it:
            // Container.SetInUse acts only for the owner (Container.cs:289),
            // so a UI closed after ownership moved away leaves m_inUse stuck
            // true on this peer, and gating on it alone would freeze the view
            // for good.
            if (_drawer.IsInUse() && _nview != null && _nview.IsOwner()) return false;

            var zdo = ValidZdo();
            if (zdo == null) return false;

            uint revision = zdo.DataRevision;
            if (revision == _loadedRevision) return false;

            // Any ZDO field bumps DataRevision -- WearNTear health and
            // support, and snow, which is Set every frame while it builds up.
            // Rebuild only when what the view is built from actually changed.
            // Non-allocating: ZDO.GetByteArray returns the stored array
            // itself (ZDOExtraData.GetByteArray -> GetValueOrDefault, no
            // copy), GetInt/GetString return stored values, DrawerSnapshot
            // is a struct.
            byte[] bytes = zdo.GetByteArray(KeyViewSlotsHash);
            int storedBaseline = zdo.GetInt(KeyViewBaselineHash, NoBaseline);
            var current = _drawer.Snapshot;
            if (_loadedRevision != uint.MaxValue
                && SameBytes(bytes, _builtBytes)
                && storedBaseline == _builtBaseline
                && current.Amount == _builtAmount
                && current.ItemName == _builtItem)
            {
                _loadedRevision = revision;
                return false;
            }

            _builtBytes = bytes;
            _builtBaseline = storedBaseline;
            _builtAmount = current.Amount;
            _builtItem = current.ItemName;

            _carriedForeign.Clear();
            if (TryDecode(bytes, out var slots, out var foreign))
            {
                ShowSlots(current.ItemName, slots);
                _carriedForeign.AddRange(foreign);
                int stored = storedBaseline;
                // Safe to narrow: TryDecode bounds every slot to
                // MaxEntryValue and there are at most SlotCount of them, so
                // the long sum always fits in int here.
                _localBaseline = stored == NoBaseline ? (int)Sum(slots) : stored;

                _syncTotal = (int)Sum(slots);
                _syncForeign = ToDictionary(foreign);
            }
            else
            {
                _localBaseline = ShowSlots(current.ItemName, LayoutFor(current));
                _syncTotal = _localBaseline;
                _syncForeign = new Dictionary<string, int>();
            }

            _loadedRevision = revision;
            return true;
        }

        /// <summary>
        /// True when the live Inventory differs from what this view last
        /// loaded, published or wrote: this peer changed something that has
        /// not reached the drawer yet. Also catches mods that change m_stack
        /// directly and call Container.Save without Changed(). False for a
        /// view that never loaded or cannot resolve its item -- there is
        /// nothing trustworthy to report (see WriteDeltaToZdo). Allocates;
        /// call it from flush/save paths only, never per frame per drawer.
        /// </summary>
        internal bool HasUnflushedChange()
        {
            if (_loadedRevision == uint.MaxValue || _unresolved) return false;

            ReadLive(_drawer.Snapshot.ItemName, out var liveSlots, out var liveForeign);
            if (Sum(liveSlots) != _syncTotal) return true;
            return Delta(ToDictionary(liveForeign), _syncForeign).Count > 0;
        }

        /// <summary>Marks the view as holding a change and queues it for DrawerManager's end-of-frame flush.</summary>
        internal void MarkDirty()
        {
            _dirty = true;
            DrawerManager.Instance?.MarkViewDirty(_drawer);
        }

        /// <summary>Drops the dirty flag for a view that turned out to have nothing to report.</summary>
        internal void ClearDirty() => _dirty = false;

        /// <summary>
        /// LAST RESORT ONLY -- DrawerComponent.FlushView calls this solely
        /// on teardown (ZNetView.ResetZDO, OnDestroy, Game.Shutdown), when
        /// there is no time to claim and wait for ownership to settle.
        /// Every normal path claims and reconciles instead: a non-owner's
        /// ViewSlots write can be overwritten by the owner's reaction to an
        /// earlier write before this peer sees it, losing this debit after
        /// its items were handed out.
        ///
        /// Writes only THIS PEER'S change since its last successful load or
        /// write -- never an absolute snapshot -- rebased on whatever the
        /// ZDO currently holds, so the next owner's total − baseline still
        /// sums every unreconciled change exactly once.
        ///
        /// The live Inventory is never mutated here -- a write only ever
        /// reports what already happened to it.
        ///
        /// A view that was never loaded (RefreshFromZdo/Publish never ran,
        /// so _syncTotal/_syncForeign are not real baselines) has nothing
        /// trustworthy to report, so it refuses outright.
        /// </summary>
        internal void WriteDeltaToZdo()
        {
            if (!_dirty) return;

            var zdo = ValidZdo();
            if (zdo == null) return;

            if (_loadedRevision == uint.MaxValue)
            {
                // Nothing was ever genuinely loaded -- clear the flag
                // rather than leave a view that can never write (see
                // above) permanently stuck "dirty" (RefreshFromZdo, unlike
                // WriteDeltaToZdo, does still allow a first load through while
                // dirty, precisely so this is recoverable).
                _dirty = false;
                return;
            }

            _dirty = false;

            // An item this client cannot resolve showed 0 slots; a live
            // total of 0 would read as this peer withdrawing everything it
            // never actually saw or touched.
            if (_unresolved) return;

            ReadLive(_drawer.Snapshot.ItemName, out var liveSlots, out var liveForeignList);
            long live = Sum(liveSlots);
            var liveForeign = ToDictionary(liveForeignList);

            long delta = live - _syncTotal;
            var foreignDelta = Delta(liveForeign, _syncForeign);
            if (delta == 0 && foreignDelta.Count == 0) return; // nothing this peer changed since last sync

            int[] storedSlots;
            Dictionary<string, int> storedForeign;
            int storedBaseline;
            if (TryDecode(zdo.GetByteArray(KeyViewSlotsHash), out var decodedSlots, out var decodedForeign))
            {
                storedSlots = decodedSlots;
                storedForeign = ToDictionary(decodedForeign);
                int stored = zdo.GetInt(KeyViewBaselineHash, NoBaseline);
                storedBaseline = stored == NoBaseline ? (int)Sum(decodedSlots) : stored;
            }
            else
            {
                // Nothing decodable on the ZDO right now (never published
                // yet, or corrupt) -- fall back to this view's own last
                // known state rather than guessing at what is stored.
                storedSlots = new[] { _syncTotal };
                storedForeign = new Dictionary<string, int>(_syncForeign);
                storedBaseline = _localBaseline;
            }

            storedSlots = ApplyDelta(storedSlots, delta);
            foreach (var kv in foreignDelta)
            {
                storedForeign.TryGetValue(kv.Key, out int existing);
                int updated = existing + kv.Value;
                if (updated <= 0) storedForeign.Remove(kv.Key);
                else storedForeign[kv.Key] = updated;
            }

            zdo.Set(KeyViewSlotsHash, Encode(storedSlots, FinalizeForeign(storedForeign)));
            zdo.Set(KeyViewBaselineHash, storedBaseline);

            _syncTotal = (int)live;
            _syncForeign = liveForeign;
            _loadedRevision = zdo.DataRevision;
        }

        /// <summary>
        /// Owner-tick check. Costs one revision comparison unless the ZDO's
        /// data changed. On a change, the ViewSlots bytes, baseline and
        /// Amount are compared (without allocating) against the last check
        /// that found nothing to do, and ViewSlots is decoded only when they
        /// differ -- a revision bump from snow or wear does not decode.
        /// </summary>
        internal bool NeedsReconcile()
        {
            if (_dirty) return false;
            var zdo = ValidZdo();
            if (zdo == null) return false;

            uint revision = zdo.DataRevision;
            if (revision == _checkedRevision) return false;
            _checkedRevision = revision;

            byte[] bytes = zdo.GetByteArray(KeyViewSlotsHash);
            int baseline = zdo.GetInt(KeyViewBaselineHash, NoBaseline);
            int amount = _drawer.Snapshot.Amount;
            if (_checkedClean && SameBytes(bytes, _checkedBytes) && baseline == _checkedBaseline && amount == _checkedAmount)
                return false;

            bool needs = !TryDecode(bytes, out var slots, out var foreign)
                         || baseline == NoBaseline
                         || foreign.Count > 0
                         || Sum(slots) != baseline
                         || baseline != amount;

            _checkedClean = !needs;
            _checkedBytes = bytes;
            _checkedBaseline = baseline;
            _checkedAmount = amount;
            return needs;
        }

        /// <summary>Content equality without allocating. ZDO byte arrays are replaced, never edited in place, so the reference check covers local Sets of other fields; content covers a received copy.</summary>
        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// The view's content for reconcile: the live Inventory when a
        /// local change is pending, otherwise ViewSlots. False when there
        /// is nothing trustworthy to reconcile (no ViewSlots yet, or an
        /// item this client cannot resolve).
        ///
        /// A dirty view has a pending write that has not reached the ZDO
        /// yet -- but other peers may already have their own unreconciled
        /// deltas sitting there (this peer simply hasn't flushed its own
        /// yet). Reporting only this peer's live-vs-baseline delta would
        /// silently drop those the moment the caller's following Publish
        /// overwrites ViewSlots with a fresh layout. So for a dirty view,
        /// itemTotal is whatever is currently STORED (other peers' deltas,
        /// already applied on top of the baseline) PLUS this peer's own
        /// still-unflushed delta (live drawer-item total − _syncTotal) on
        /// top of that: itemTotal − baseline = (storedTotal − baseline) +
        /// (live − _syncTotal), which sums every unreconciled peer's
        /// change exactly once, this peer's unflushed one included. The
        /// baseline itself is read straight from what is stored -- ordinary
        /// peer writes never change it (see WriteDeltaToZdo) -- falling back to
        /// this view's own last known baseline when nothing is decodable
        /// yet. Foreign is likewise stored foreign plus this peer's own
        /// unflushed foreign delta, merged by name, entries dropped once
        /// they reach zero.
        /// </summary>
        internal bool TryReadForReconcile(string itemName, out int itemTotal, out int baseline, out List<KeyValuePair<string, int>> foreign)
        {
            itemTotal = 0;
            baseline = 0;
            foreign = null;
            if (_unresolved) return false;

            if (_dirty)
            {
                int storedTotal;
                Dictionary<string, int> storedForeign;
                var storedZdo = ValidZdo();
                if (storedZdo != null && TryDecode(storedZdo.GetByteArray(KeyViewSlotsHash), out var decodedSlots, out var decodedForeign))
                {
                    storedTotal = (int)Sum(decodedSlots);
                    storedForeign = ToDictionary(decodedForeign);
                    int stored = storedZdo.GetInt(KeyViewBaselineHash, NoBaseline);
                    baseline = stored == NoBaseline ? storedTotal : stored;
                }
                else
                {
                    storedTotal = _syncTotal;
                    storedForeign = new Dictionary<string, int>(_syncForeign);
                    baseline = _localBaseline;
                }

                ReadLive(itemName, out var live, out var liveForeignList);
                long unflushed = Sum(live) - _syncTotal;
                itemTotal = (int)(storedTotal + unflushed);

                var liveForeign = ToDictionary(liveForeignList);
                foreach (var kv in Delta(liveForeign, _syncForeign))
                {
                    storedForeign.TryGetValue(kv.Key, out int existing);
                    int updated = existing + kv.Value;
                    if (updated <= 0) storedForeign.Remove(kv.Key);
                    else storedForeign[kv.Key] = updated;
                }
                foreign = FinalizeForeign(storedForeign);
                return true;
            }

            var zdo = ValidZdo();
            if (zdo == null) return false;
            if (!TryDecode(zdo.GetByteArray(KeyViewSlotsHash), out var slots, out foreign)) return false;

            itemTotal = (int)Sum(slots);
            int storedBaseline = zdo.GetInt(KeyViewBaselineHash, NoBaseline);
            baseline = storedBaseline == NoBaseline ? itemTotal : storedBaseline;
            return true;
        }

        /// <summary>Owner only: lays the view out from the drawer's state and writes ViewSlots and ViewBaseline.</summary>
        internal void Publish(DrawerSnapshot state)
        {
            var zdo = ValidZdo();
            if (zdo == null) return;

            int[] layout = LayoutFor(state);
            int shown = ShowSlots(state.ItemName, layout);
            _carriedForeign.Clear();

            byte[] encoded = Encode(_unresolved ? Array.Empty<int>() : layout, _carriedForeign);
            zdo.Set(KeyViewSlotsHash, encoded);
            zdo.Set(KeyViewBaselineHash, shown);

            _builtBytes = encoded;
            _builtBaseline = shown;
            _builtAmount = state.Amount;
            _builtItem = state.ItemName;

            _localBaseline = shown;
            _syncTotal = shown;
            _syncForeign = new Dictionary<string, int>();
            _dirty = false;
            _loadedRevision = zdo.DataRevision;
            _checkedRevision = zdo.DataRevision;
        }

        internal enum UnresolvedViewState
        {
            /// <summary>Nothing pending, and the stored layout is stale against the drawer's Amount.</summary>
            NeedsPublish,

            /// <summary>Nothing pending and nothing to correct (or nothing trustworthy to compare); write nothing.</summary>
            AlreadyPublished,

            /// <summary>ViewSlots (or this view, if dirty) carries a change not yet reconciled.</summary>
            PendingChanges,
        }

        /// <summary>
        /// True when this client cannot show <paramref name="state"/>'s item,
        /// the same test ShowSlots applies (it lays out no slots).
        /// </summary>
        internal bool CannotResolve(DrawerSnapshot state) =>
            state.IsAssigned && ItemFacts.Drop(state.ItemName) == null;

        /// <summary>
        /// For an owner that cannot resolve the drawer's item. Read-only.
        ///
        /// PendingChanges: this view is dirty, or stored ViewSlots has
        /// foreign items or a total that differs from the stored baseline.
        ///
        /// NeedsPublish only when stored data decodes, has no foreign items,
        /// total equals the stored baseline, and that baseline differs from
        /// <paramref name="amount"/>: the layout is stale because Amount
        /// changed. One exception: the zero-slot, baseline-0 layout this
        /// owner's own Publish writes while unresolved counts as current
        /// even though Amount is not 0, or it would republish on every
        /// revision change.
        ///
        /// Everything else is AlreadyPublished (write nothing), including
        /// absent or undecodable data: overwriting a layout that might be
        /// correct races a peer's in-flight withdrawal, whose negative delta
        /// ApplyDelta drops on empty slots, duplicating the items.
        /// </summary>
        internal UnresolvedViewState InspectForUnresolvedOwner(int amount)
        {
            if (_dirty) return UnresolvedViewState.PendingChanges;

            var zdo = ValidZdo();
            if (zdo == null) return UnresolvedViewState.AlreadyPublished;

            if (!TryDecode(zdo.GetByteArray(KeyViewSlotsHash), out var slots, out var foreign))
                return UnresolvedViewState.AlreadyPublished;

            long total = Sum(slots);
            int stored = zdo.GetInt(KeyViewBaselineHash, NoBaseline);
            if (foreign.Count > 0 || (stored != NoBaseline && total != stored))
                return UnresolvedViewState.PendingChanges;

            long baseline = stored == NoBaseline ? total : stored;
            bool ownUnresolvedLayout = slots.Length == 0 && stored == 0;
            return baseline != amount && !ownUnresolvedLayout
                ? UnresolvedViewState.NeedsPublish
                : UnresolvedViewState.AlreadyPublished;
        }

        internal string Describe()
        {
            int total = 0;
            foreach (var item in _inventory.GetAllItems()) total += item.m_stack;
            return $"slots={_inventory.GetWidth() * _inventory.GetHeight()} stacks={_inventory.NrOfItems()} "
                   + $"total={total} baseline={_localBaseline} dirty={_dirty} unresolved={_unresolved} "
                   + $"carriedForeign={_carriedForeign.Count}";
        }

        internal static byte[] Encode(IReadOnlyList<int> slots, IReadOnlyList<KeyValuePair<string, int>> foreign)
        {
            var pkg = new ZPackage();
            pkg.Write(FormatVersion);
            pkg.Write(slots.Count);
            for (int i = 0; i < slots.Count; i++) pkg.Write(slots[i]);

            // Never write more than TryDecode accepts: callers (ReadLive,
            // WriteDeltaToZdo's FinalizeForeign call) are expected to have
            // already merged and trimmed to this same cap, but this is the
            // one place both sides of the format meet, so it enforces the
            // cap itself rather than trusting every caller to.
            int foreignCount = Math.Min(foreign.Count, MaxForeignEntries);
            pkg.Write(foreignCount);
            for (int i = 0; i < foreignCount; i++)
            {
                pkg.Write(foreign[i].Key ?? "");
                pkg.Write(foreign[i].Value);
            }
            return pkg.GetArray();
        }

        internal static bool TryDecode(byte[] data, out int[] slots, out List<KeyValuePair<string, int>> foreign)
        {
            slots = null;
            foreign = null;
            if (data == null || data.Length == 0) return false;

            try
            {
                var pkg = new ZPackage(data);
                if (pkg.ReadInt() != FormatVersion) return false;

                int count = pkg.ReadInt();
                if (count < 0 || count > SlotCount) return false;
                var s = new int[count];
                for (int i = 0; i < count; i++)
                {
                    s[i] = pkg.ReadInt();
                    // ViewSlots is a ZDO field any peer can write; a value
                    // outside this range is either corrupt or hostile
                    // either way, treat it the same as unparsable bytes.
                    if (s[i] < 0 || s[i] > MaxEntryValue) return false;
                }

                int foreignCount = pkg.ReadInt();
                if (foreignCount < 0 || foreignCount > MaxForeignEntries) return false;
                var f = new List<KeyValuePair<string, int>>(foreignCount);
                for (int i = 0; i < foreignCount; i++)
                {
                    string name = pkg.ReadString();
                    int n = pkg.ReadInt();
                    if (n < 0 || n > MaxEntryValue) return false;
                    if (n > 0) f.Add(new KeyValuePair<string, int>(name, n));
                }

                slots = s;
                foreign = f;
                return true;
            }
            catch (Exception ex)
            {
                DrawerPlugin.Log.LogWarning($"Unreadable drawer ViewSlots ({data.Length} bytes); rebuilding it. {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private void OnInventoryChanged()
        {
            if (_applying) return;
            _dirty = true;
            DrawerManager.Instance?.MarkViewDirty(_drawer);
        }

        private int[] LayoutFor(DrawerSnapshot state) =>
            ViewLayout.Compute(state.IsAssigned, state.Amount, _drawer.Capacity, ItemFacts.MaxStackSize(state.ItemName), SlotCount);

        /// <summary>
        /// Replaces the Inventory's contents and dimensions with the given
        /// slot counts of one item, straight into the backing list -- never
        /// through AddItem, which the stack guard would split. Returns the
        /// total shown (0 when the item cannot be resolved on this client).
        /// </summary>
        private int ShowSlots(string itemName, int[] slots)
        {
            // Without InventoryAccess there is no safe way to write straight
            // into the Inventory's backing list -- treat this exactly like
            // an item this client cannot resolve: nothing shown, nothing
            // written back (HasUnflushedChange, WriteDeltaToZdo and Publish all refuse
            // while _unresolved).
            if (!InventoryAccess.Available)
            {
                _unresolved = slots.Length > 0;
                return 0;
            }

            var drop = slots.Length > 0 ? ItemFacts.Drop(itemName) : null;
            _unresolved = slots.Length > 0 && drop == null;

            int cells = _unresolved ? 0 : slots.Length;
            int width = cells == SlotCount ? 4 : cells;
            int height = cells == SlotCount ? 2 : 1;

            _applying = true;
            try
            {
                var items = InventoryAccess.Items(_inventory);
                items.Clear();
                InventoryAccess.Width(_inventory) = width;
                InventoryAccess.Height(_inventory) = height;

                int total = 0;
                for (int i = 0; i < cells; i++)
                {
                    if (slots[i] <= 0) continue;
                    var data = drop.m_itemData.Clone();
                    data.m_stack = slots[i];
                    data.m_dropPrefab = drop.gameObject;
                    // CountItems/HaveItem/RemoveItem(string, ...) filter on
                    // m_worldLevel >= Game.m_worldLevel.
                    data.m_worldLevel = global::Game.m_worldLevel;
                    data.m_cheated = false;
                    data.m_gridPos = new Vector2i(i % width, i / width);
                    items.Add(data);
                    total += slots[i];
                }

                InventoryAccess.Changed(_inventory, false, false);
                return total;
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>
        /// Reads the live Inventory back into slot counts for the drawer's
        /// own item plus a foreign list for everything else (including
        /// _carriedForeign, so TryReadForReconcile's dirty-branch caller
        /// sees the full current picture the owner needs, foreign items
        /// this peer never touched included). Foreign entries are merged
        /// by prefab name (carried-over entries and freshly read ones
        /// alike) rather than appended -- Encode/TryDecode round-trip one
        /// entry per name, so two entries for the same name would silently
        /// double-count on the next decode.
        ///
        /// Any single count -- a slot or a merged foreign total -- above
        /// MaxEntryValue is clamped and logged: a stack this large cannot
        /// round-trip through the persisted format (see MaxEntryValue's
        /// comment), and clamping here is the one place that is true for
        /// both live-inventory sources of a count (a single huge stack, or
        /// several stacks merging past the cap).
        /// </summary>
        private void ReadLive(string itemName, out int[] slots, out List<KeyValuePair<string, int>> foreign)
        {
            int width = _inventory.GetWidth();
            int height = _inventory.GetHeight();
            int cells = width * height;
            slots = new int[cells];

            var merged = new Dictionary<string, int>(_carriedForeign.Count);
            foreach (var kv in _carriedForeign) Merge(merged, kv.Key, kv.Value);

            // Without InventoryAccess, ShowSlots never wrote anything real
            // into the Inventory either -- there is nothing genuine to read
            // back here beyond whatever was already carried over.
            if (!InventoryAccess.Available)
            {
                foreign = FinalizeForeign(merged);
                return;
            }

            string drawerSharedName = SharedNameOf(itemName);

            foreach (var item in _inventory.GetAllItems())
            {
                if (item == null || item.m_stack <= 0) continue;
                string name = ItemFacts.PrefabNameOf(item);

                if (cells > 0 && !string.IsNullOrEmpty(itemName) && name == itemName)
                {
                    int x = item.m_gridPos.x, y = item.m_gridPos.y;
                    bool inGrid = x >= 0 && y >= 0 && x < width && y < height;
                    slots[inGrid ? y * width + x : 0] += item.m_stack;
                    continue;
                }

                if (string.IsNullOrEmpty(name))
                {
                    // No m_dropPrefab: this item cannot be identified for a
                    // foreign entry (which must be spillable by prefab name
                    // later) or persisted as one. If its shared display name
                    // matches the drawer's own item, it is almost certainly
                    // that same item missing only its prefab reference, so
                    // count it as the drawer's item rather than lose track
                    // of it entirely; otherwise drop it, logged, rather than
                    // record an unspillable empty-named foreign entry.
                    string sharedName = item.m_shared?.m_name;
                    if (cells > 0 && !string.IsNullOrEmpty(drawerSharedName) && sharedName == drawerSharedName)
                    {
                        slots[0] += item.m_stack;
                    }
                    else if (!_warnedNoDropPrefab)
                    {
                        _warnedNoDropPrefab = true;
                        DrawerPlugin.Log.LogWarning(
                            $"Drawer {_drawer.ZdoId}: dropping an item from its view with no m_dropPrefab "
                            + $"(m_shared.m_name=\"{sharedName}\"); it cannot be identified or spilled. "
                            + "Further such items in this drawer are dropped without a warning.");
                    }
                    continue;
                }

                Merge(merged, name, item.m_stack);
            }

            for (int i = 0; i < slots.Length; i++) slots[i] = ClampEntry(slots[i], itemName);

            foreign = FinalizeForeign(merged);
        }

        private static void Merge(Dictionary<string, int> merged, string name, int amount)
        {
            if (string.IsNullOrEmpty(name) || amount <= 0) return;
            merged.TryGetValue(name, out int existing);
            merged[name] = existing + amount;
        }

        /// <summary>
        /// Clamps a single decoded/read count to MaxEntryValue, logging
        /// (drawer id + name) when clamping actually changes the value --
        /// see MaxEntryValue's comment for why the cap exists.
        /// </summary>
        private int ClampEntry(int value, string name)
        {
            if (value <= MaxEntryValue) return value;
            DrawerPlugin.Log.LogWarning(
                $"Drawer {_drawer.ZdoId}: \"{name}\" count {value} exceeds the persisted cap ({MaxEntryValue}); clamped.");
            return MaxEntryValue;
        }

        /// <summary>
        /// Clamps every value in a merged foreign map to MaxEntryValue, then
        /// caps the map to MaxForeignEntries (the same limit TryDecode
        /// enforces), keeping the largest counts and logging (drawer id +
        /// names) whatever had to be dropped. The count cap is only reached
        /// when many distinct foreign item types pile up in a view without
        /// ever being reconciled/spilled by the owner.
        /// </summary>
        private List<KeyValuePair<string, int>> FinalizeForeign(Dictionary<string, int> merged)
        {
            if (merged.Count > 0)
            {
                var keys = new List<string>(merged.Keys);
                foreach (var key in keys) merged[key] = ClampEntry(merged[key], key);
            }

            if (merged.Count <= MaxForeignEntries)
                return new List<KeyValuePair<string, int>>(merged);

            var ordered = new List<KeyValuePair<string, int>>(merged);
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

            var kept = ordered.GetRange(0, MaxForeignEntries);
            var dropped = ordered.GetRange(MaxForeignEntries, ordered.Count - MaxForeignEntries);
            var droppedNames = string.Join(", ", dropped.ConvertAll(kv => kv.Key));
            DrawerPlugin.Log.LogWarning(
                $"Drawer {_drawer.ZdoId}: {dropped.Count} foreign item type(s) dropped from its view "
                + $"(more than {MaxForeignEntries} distinct types accumulated): {droppedNames}");

            return kept;
        }

        /// <summary>Converts a decoded/read foreign list to a name-keyed map, summing duplicate names defensively (ViewSlots is peer-writable and TryDecode does not itself dedupe).</summary>
        private static Dictionary<string, int> ToDictionary(List<KeyValuePair<string, int>> list)
        {
            var dict = new Dictionary<string, int>(list.Count);
            foreach (var kv in list) Merge(dict, kv.Key, kv.Value);
            return dict;
        }

        /// <summary>Per-name delta live − sync, both directions: a name missing from one side counts as 0 there. Only nonzero deltas are returned.</summary>
        private static Dictionary<string, int> Delta(Dictionary<string, int> live, Dictionary<string, int> sync)
        {
            var result = new Dictionary<string, int>();
            foreach (var kv in live)
            {
                sync.TryGetValue(kv.Key, out int before);
                int change = kv.Value - before;
                if (change != 0) result[kv.Key] = change;
            }
            foreach (var kv in sync)
            {
                if (live.ContainsKey(kv.Key)) continue;
                result[kv.Key] = -kv.Value;
            }
            return result;
        }

        /// <summary>
        /// Applies a total delta to a stored slot array, returning the
        /// array to write -- the same instance, mutated in place, except
        /// when it must grow from zero slots (see below).
        ///
        /// A positive delta is added to slot 0, clamped to MaxEntryValue
        /// with any excess spread into the remaining slots in turn (each
        /// up to MaxEntryValue); if every slot is already at the cap,
        /// whatever is left over is clamped away and logged (only
        /// reachable with a drawer capacity configured near MaxEntryValue).
        ///
        /// A negative delta is taken from slot 0 first, then -- if that is
        /// not enough -- from the remaining slots in order, never taking a
        /// slot below 0. If the array cannot absorb the whole negative
        /// delta (this peer's own view disagreed with the ZDO by more than
        /// what the ZDO actually has -- e.g. a concurrent write already
        /// removed it), whatever is left over is logged rather than
        /// driving any slot negative.
        ///
        /// An empty stored array (the drawer was just emptied and
        /// republished as zero slots) has nothing to remove a negative
        /// delta from, so that case is dropped and logged same as always
        /// -- but a POSITIVE delta must not be dropped just because
        /// nothing is currently stored: a deposit arriving between the
        /// owner's zero-slot publish and this peer's next refresh is real,
        /// and dropping it would silently lose it the moment this peer
        /// next reloads. That case grows a new single-slot array holding
        /// the (capped) delta, the same shape WriteDeltaToZdo's own
        /// undecodable-ZDO fallback already uses.
        /// </summary>
        private int[] ApplyDelta(int[] stored, long delta)
        {
            if (delta == 0) return stored;

            if (stored.Length == 0)
            {
                if (delta < 0)
                {
                    DrawerPlugin.Log.LogWarning(
                        $"Drawer {_drawer.ZdoId}: view write delta {delta} has no stored slots to apply to; dropped.");
                    return stored;
                }

                long capped = Math.Min(delta, MaxEntryValue);
                if (delta > MaxEntryValue)
                    DrawerPlugin.Log.LogWarning(
                        $"Drawer {_drawer.ZdoId}: view write of +{delta} into empty stored slots exceeds "
                        + $"the persisted cap ({MaxEntryValue}); clamped.");
                return new[] { (int)capped };
            }

            if (delta > 0)
            {
                long remaining = delta;
                for (int i = 0; i < stored.Length && remaining > 0; i++)
                {
                    long room = MaxEntryValue - stored[i];
                    if (room <= 0) continue;
                    long take = Math.Min(remaining, room);
                    stored[i] += (int)take;
                    remaining -= take;
                }

                if (remaining > 0)
                    DrawerPlugin.Log.LogWarning(
                        $"Drawer {_drawer.ZdoId}: view write of +{delta} exceeds what its stored slots can "
                        + $"hold (every slot already at the {MaxEntryValue} cap); {remaining} dropped.");

                return stored;
            }

            long removing = -delta;
            for (int i = 0; i < stored.Length && removing > 0; i++)
            {
                long take = Math.Min(removing, stored[i]);
                stored[i] -= (int)take;
                removing -= take;
            }

            if (removing > 0)
            {
                DrawerPlugin.Log.LogWarning(
                    $"Drawer {_drawer.ZdoId}: view write reports removing {-delta} but the stored view only "
                    + $"had {-delta - removing}; the rest could not be applied.");
            }

            return stored;
        }

        private static string SharedNameOf(string itemName)
        {
            var drop = ItemFacts.Drop(itemName);
            return drop != null ? drop.m_itemData.m_shared.m_name : null;
        }

        private ZDO ValidZdo() => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;

        // long, not int: values decoded from ViewSlots are bounded per-entry
        // (MaxEntryValue) but that bound exists precisely because summing
        // unbounded values here could overflow int and wrap to a small or
        // negative total -- see MaxEntryValue's comment.
        private static long Sum(IReadOnlyList<int> values)
        {
            long total = 0;
            for (int i = 0; i < values.Count; i++) total += values[i];
            return total;
        }
    }
}
