using System;

namespace ItemDrawers.Core
{
    /// <summary>
    /// What a drawer's ZDO should withdraw after an external mod's
    /// Inventory call removed from the Container-bridge mirror.
    /// <see cref="Withdraw"/> is zero when nothing needs to be committed.
    ///
    /// There is deliberately no deposit case here. A foreign mod adding to
    /// the mirror is never credited to the ZDO -- see
    /// DrawerComponent.OnMirrorChanged for why (vanilla's own move idiom
    /// adds to the destination before removing from the source, so by the
    /// time this could fire the source removal has not happened yet) -- so
    /// computing a deposit amount here would be dead code with nothing to
    /// consume it.
    /// </summary>
    public readonly struct MirrorDelta : IEquatable<MirrorDelta>
    {
        public readonly int Withdraw;

        private MirrorDelta(int withdraw)
        {
            Withdraw = withdraw;
        }

        public static readonly MirrorDelta None = new MirrorDelta(0);
        public static MirrorDelta WithdrawOf(int amount) => new MirrorDelta(amount);

        public bool Equals(MirrorDelta other) => Withdraw == other.Withdraw;
        public override bool Equals(object obj) => obj is MirrorDelta d && Equals(d);
        public override int GetHashCode() => Withdraw;
        public override string ToString() => Withdraw > 0 ? $"Withdraw({Withdraw})" : "None";
    }

    /// <summary>
    /// Pure arithmetic for reconciling the Container-bridge mirror inventory
    /// against ZDO truth after an external mod's Inventory call removed
    /// from the mirror. No game, no network, no side effects -- see
    /// DrawerComponent.OnMirrorChanged for how this is wired to a live
    /// Inventory/ZDO.
    /// </summary>
    public static class MirrorReconciliation
    {
        /// <summary>
        /// Computes how much an external mod actually removed from the
        /// mirror since it was last reconciled.
        ///
        /// The delta is measured as (mirrorTotal - mirroredBaselineAmount)
        /// -- what changed in the mirror since the last time this method
        /// (or a mirror rebuild) recorded a baseline for it -- never
        /// against zdoAmount directly. zdoAmount can have moved for reasons
        /// that have nothing to do with this particular Inventory call (a
        /// normal player interaction committing to the ZDO without
        /// touching the mirror at all, for instance); diffing against it
        /// inverts the sign whenever it has moved on its own: mirror built
        /// at 100, ZDO drops to 40 elsewhere, mod removes 50 from the
        /// mirror (mirrorTotal now 50) -- diffing against zdoAmount
        /// computes 50-40=+10, which would misread as items appearing,
        /// where the true event was a 50-unit withdrawal. Diffing against
        /// the baseline gets 50-100=-50, correctly a withdrawal, then
        /// clamped below to the 40 the ZDO can actually give up.
        ///
        /// zdoAmount is used as the ceiling: a withdrawal can never remove
        /// more than the ZDO currently holds, regardless of what the stale
        /// baseline implied.
        ///
        /// A mismatch between any of the three item-name parameters means
        /// the numeric baseline is not comparable to what the mirror
        /// currently holds (e.g. the drawer was reassigned to a different
        /// item since the baseline was recorded) -- reported as no delta.
        /// The caller still resyncs the mirror to ZDO truth eventually, but
        /// not synchronously from within this computation's caller -- that
        /// resync happens on the mirror's next safe access (the next
        /// RefreshMirror, off whatever call stack triggered this), never
        /// inside the event handler that led here.
        /// </summary>
        public static MirrorDelta Compute(
            string mirrorItemName, int mirrorTotal,
            string mirroredBaselineItemName, int mirroredBaselineAmount,
            string zdoItemName, int zdoAmount)
        {
            // An emptied slot is the baseline item with none left, NOT a
            // different item.
            //
            // Inventory.RemoveItem removes the ItemData from the slot
            // outright when the removal takes the whole stack, rather than
            // leaving it behind at m_stack 0. DrawerComponent.
            // OnMirrorChanged therefore reports an emptied mirror as
            // ("", 0) -- see its `slot != null ? ... : ""`.
            //
            // Read naively, that "" is an item-name mismatch, and the
            // mismatch guard below returns None: no withdrawal is
            // committed, the foreign mod keeps the items it took, and the
            // drawer keeps its count. That is a duplication bug, and it
            // fires on the exact boundary where a craft consumes precisely
            // what the drawer had left -- the case a player hits routinely
            // when emptying a drawer into a recipe.
            //
            // Only a slot that is BOTH nameless and empty gets this
            // treatment. A slot holding a different item with a real count
            // is still a genuine mismatch and still returns None.
            bool mirrorSlotEmpty = string.IsNullOrEmpty(mirrorItemName) && mirrorTotal == 0;
            string effectiveMirrorItemName = mirrorSlotEmpty ? mirroredBaselineItemName : mirrorItemName;

            if (effectiveMirrorItemName != mirroredBaselineItemName) return MirrorDelta.None;
            if (mirroredBaselineItemName != zdoItemName) return MirrorDelta.None;

            int delta = mirrorTotal - mirroredBaselineAmount;
            if (delta >= 0) return MirrorDelta.None;

            int amount = -delta;
            int clamped = amount < zdoAmount ? amount : zdoAmount;
            return clamped <= 0 ? MirrorDelta.None : MirrorDelta.WithdrawOf(clamped);
        }
    }
}
