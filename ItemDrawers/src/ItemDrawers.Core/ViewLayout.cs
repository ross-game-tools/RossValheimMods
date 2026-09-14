using System;
using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// How many of a drawer's items each slot of its container view holds.
    /// Pure: no game, no side effects.
    ///
    /// The view never has an empty slot while the drawer has stock, so a
    /// wrong item or a non-stackable is refused by vanilla AddItem with no
    /// special code. Slot 1 (index 0) holds everything not shown elsewhere
    /// and may exceed the max stack size; the other slots start at one item
    /// each and are raised evenly, never above the max stack size, until the
    /// deposit room vanilla would see -- the free space of EVERY slot below
    /// the max stack size, slot 1 included -- is at most the drawer's
    /// remaining capacity. When no layout with at least two slots can meet
    /// that, fewer slots are used; a single slot is the last resort.
    /// </summary>
    public static class ViewLayout
    {
        public const int DefaultSlotCount = 8;

        public static int[] Compute(bool assigned, int amount, int capacity, int maxStackSize, int slotCount = DefaultSlotCount)
        {
            if (!assigned || amount <= 0 || slotCount <= 0) return Array.Empty<int>();

            int max = maxStackSize < 1 ? 1 : maxStackSize;
            long remaining = (long)capacity - amount;
            long allowedRoom = remaining < 0 ? 0 : remaining;
            int largest = amount < slotCount ? amount : slotCount;

            for (int n = largest; n >= 2; n--)
            {
                int[] slots = Distribute(amount, max, n, allowedRoom);
                if (slots != null && Room(slots, max) <= allowedRoom) return slots;
            }

            return new[] { amount };
        }

        /// <summary>Deposit room vanilla AddItem would find: Σ max(0, M − count) over every slot.</summary>
        public static long Room(IReadOnlyList<int> slots, int maxStackSize)
        {
            int max = maxStackSize < 1 ? 1 : maxStackSize;
            long room = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] < max) room += max - slots[i];
            return room;
        }

        private static int[] Distribute(int amount, int max, int n, long allowedRoom)
        {
            int others = n - 1;
            long needed = (long)others * max - allowedRoom;
            long total = needed > others ? needed : others;
            if (total > amount - 1) return null;

            int baseCount = (int)(total / others);
            int extra = (int)(total % others);

            var slots = new int[n];
            slots[0] = amount - (int)total;
            for (int i = 1; i < n; i++)
                slots[i] = baseCount + (i - 1 < extra ? 1 : 0);
            return slots;
        }
    }
}
