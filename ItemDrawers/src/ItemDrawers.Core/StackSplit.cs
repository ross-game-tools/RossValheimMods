using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>How an oversized incoming stack is spread over an inventory.</summary>
    public readonly struct StackSplitPlan
    {
        /// <summary>Items added to each existing partial stack, same order as the input.</summary>
        public readonly int[] TopUps;

        /// <summary>Sizes of the new stacks, in placement order, each at most the max stack size.</summary>
        public readonly int[] NewStacks;

        /// <summary>Items that did not fit anywhere and stay on the incoming item.</summary>
        public readonly int Remainder;

        public StackSplitPlan(int[] topUps, int[] newStacks, int remainder)
        {
            TopUps = topUps;
            NewStacks = newStacks;
            Remainder = remainder;
        }

        public int Placed
        {
            get
            {
                int total = 0;
                foreach (int t in TopUps) total += t;
                foreach (int s in NewStacks) total += s;
                return total;
            }
        }
    }

    /// <summary>
    /// Pure planning for the oversized-stack guard: an item whose stack is
    /// larger than its max stack size must never land in a real inventory
    /// as one stack, because Inventory.Load truncates it on the next load.
    /// </summary>
    public static class StackSplit
    {
        public static StackSplitPlan Plan(IReadOnlyList<int> partialFreeSpace, int emptySlots, int incoming, int maxStackSize, bool firstStackToTarget)
        {
            int max = maxStackSize < 1 ? 1 : maxStackSize;
            int partials = partialFreeSpace == null ? 0 : partialFreeSpace.Count;
            var topUps = new int[partials];
            var stacks = new List<int>();
            int remaining = incoming < 0 ? 0 : incoming;
            int slots = emptySlots < 0 ? 0 : emptySlots;

            if (firstStackToTarget && remaining > 0 && slots > 0)
            {
                int first = remaining < max ? remaining : max;
                stacks.Add(first);
                remaining -= first;
                slots--;
            }

            for (int i = 0; i < partials && remaining > 0; i++)
            {
                int free = partialFreeSpace[i];
                if (free <= 0) continue;
                int add = remaining < free ? remaining : free;
                topUps[i] = add;
                remaining -= add;
            }

            while (remaining > 0 && slots > 0)
            {
                int stack = remaining < max ? remaining : max;
                stacks.Add(stack);
                remaining -= stack;
                slots--;
            }

            return new StackSplitPlan(topUps, stacks.ToArray(), remaining);
        }
    }
}
