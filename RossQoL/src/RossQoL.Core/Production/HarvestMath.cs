using System;

namespace RossQoL.Core.Production
{
    /// <summary>The arithmetic of one harvest, kept here so it is tested without a game.</summary>
    public static class HarvestMath
    {
        /// <summary>
        /// How many items an inventory can take, by the rule
        /// Inventory.CanAddItem uses: free space on matching stacks below
        /// their max, plus each empty slot as one full stack.
        /// </summary>
        public static int Room(int freeStackSpace, int emptySlots, int maxStackSize)
        {
            long room = Math.Max(0, freeStackSpace) + (long)Math.Max(0, emptySlots) * Math.Max(1, maxStackSize);
            return room > int.MaxValue ? int.MaxValue : (int)room;
        }

        /// <summary>
        /// Producer units (levels, batches) given up for the items that
        /// landed. Rounded up, so nothing that landed is ever left in the
        /// producer to be harvested twice. Not capped at the producer's
        /// units: callers clamp to what the producer holds.
        /// </summary>
        public static int UnitsTaken(int placed, int unitSize)
        {
            if (placed <= 0) return 0;
            long unit = Math.Max(1, unitSize);
            return (int)((placed + unit - 1) / unit);
        }

        /// <summary>Items owed for units taken but not placed; they drop at the producer.</summary>
        public static int Shortfall(int placed, int unitSize)
        {
            if (placed <= 0) return 0;
            long owed = (long)UnitsTaken(placed, unitSize) * Math.Max(1, unitSize) - placed;
            return (int)owed;
        }

        /// <summary>
        /// True on a producer's first attempt, then once every interval.
        /// Time running backwards (a clock reset after a reload) also counts
        /// as due, so harvesting never stalls waiting for the old time.
        /// </summary>
        public static bool IsDue(double? lastAttempt, double now, double intervalSeconds)
        {
            if (lastAttempt == null) return true;
            double elapsed = now - lastAttempt.Value;
            return elapsed < 0.0 || elapsed >= Math.Max(0.0, intervalSeconds);
        }
    }
}
