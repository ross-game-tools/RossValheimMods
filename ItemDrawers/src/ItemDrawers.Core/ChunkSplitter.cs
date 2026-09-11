using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Splits a quantity into inventory-stack-sized pieces. Used everywhere
    /// a drawer hands a bulk amount to something that only understands one
    /// stack at a time (giving items to a player, spilling contents to the
    /// ground) so that splitting logic exists in exactly one, tested place.
    /// </summary>
    public static class ChunkSplitter
    {
        /// <summary>
        /// Returns chunk sizes summing to <paramref name="amount"/>, none
        /// exceeding <paramref name="stackSize"/>. Empty for a
        /// non-positive amount. A non-positive stack size is treated as 1,
        /// the same defensive fallback DrawerState.WithdrawStack uses.
        /// </summary>
        public static IReadOnlyList<int> Chunks(int amount, int stackSize)
        {
            var result = new List<int>();
            if (amount <= 0) return result;

            int stack = stackSize < 1 ? 1 : stackSize;
            int remaining = amount;
            while (remaining > 0)
            {
                int chunk = remaining < stack ? remaining : stack;
                result.Add(chunk);
                remaining -= chunk;
            }
            return result;
        }
    }
}
