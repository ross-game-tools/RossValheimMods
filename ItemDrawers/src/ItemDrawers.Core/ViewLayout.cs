using System;

namespace ItemDrawers.Core
{
    /// <summary>
    /// How a drawer's container view is laid out. Pure: no game, no side
    /// effects.
    ///
    /// An assigned drawer shows its whole stock as ONE stack whose max stack
    /// size is the drawer's capacity (StackLimit), so the deposit room
    /// vanilla AddItem finds -- the stack's max minus its count -- is exactly
    /// the drawer's remaining capacity, and container-reading mods see one
    /// entry per drawer (issue #17). Before 1.1.0 the view spread the stock
    /// over up to eight item-max stacks to fake that room, which mods that
    /// list every stack (EpicLoot's sacrifice tab) showed as one row each.
    ///
    /// The view never has an empty slot while the drawer has stock, so a
    /// wrong item or a non-stackable is refused by vanilla AddItem with no
    /// special code. An assigned but empty drawer exposes a single empty slot
    /// so an external deposit can place the first item. An unassigned drawer
    /// exposes no slots.
    /// </summary>
    public static class ViewLayout
    {
        /// <summary>
        /// The most slots any version has written to ViewSlots (the pre-1.1.0
        /// spread layout). Decoding still accepts that many, so a drawer last
        /// published by an older version loads before it is republished.
        /// </summary>
        public const int LegacySlotCount = 8;

        public static int[] Compute(bool assigned, int amount)
        {
            if (!assigned) return Array.Empty<int>();
            return new[] { amount < 0 ? 0 : amount };
        }

        /// <summary>
        /// The max stack size the view's stack carries: the drawer's capacity,
        /// or the amount itself when a lowered capacity left the drawer over
        /// it, so the room is never negative. At least 1.
        /// </summary>
        public static int StackLimit(int capacity, int amount)
        {
            int limit = Math.Max(capacity, amount);
            return limit < 1 ? 1 : limit;
        }
    }
}
