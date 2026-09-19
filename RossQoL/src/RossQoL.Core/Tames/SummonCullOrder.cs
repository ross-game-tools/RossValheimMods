using System.Collections.Generic;

namespace RossQoL.Core.Tames
{
    /// <summary>
    /// The order the summon cap despawns creatures in.
    ///
    /// Vanilla sorts the creatures counted against the cap by age, oldest
    /// first, and despawns however many are over the limit off the front of
    /// that list. This replaces the ordering only: the worst wounded goes
    /// first, and everything else about the cap -- what counts as an instance,
    /// when the check runs, how many survive -- is untouched.
    ///
    /// Ties fall to the oldest, which is exactly vanilla's rule. That keeps
    /// the whole order total and deterministic: two creatures are never
    /// interchangeable on health alone, so the same cast always despawns the
    /// same creature and the sort can never oscillate.
    /// </summary>
    public static class SummonCullOrder
    {
        /// <summary>
        /// Negative when <paramref name="a"/> should be despawned before
        /// <paramref name="b"/>. Lowest health fraction first; equal fractions
        /// put the one alive longest first.
        /// </summary>
        public static int Compare(SummonCullCandidate a, SummonCullCandidate b)
        {
            int byWound = a.HealthFraction.CompareTo(b.HealthFraction);
            if (byWound != 0) return byWound;

            // Descending: the larger time-since-spawned is the older creature.
            return b.SecondsSinceSpawned.CompareTo(a.SecondsSinceSpawned);
        }

        /// <summary>
        /// The candidate indices in despawn order, first to be despawned
        /// first. Indices rather than the candidates themselves so the caller
        /// keeps its own parallel list of game objects and maps the answers
        /// back -- the same shape Portals/TameEligibility uses.
        /// </summary>
        public static List<int> Order(IReadOnlyList<SummonCullCandidate> candidates)
        {
            var order = new List<int>();
            if (candidates == null) return order;

            for (int i = 0; i < candidates.Count; i++)
                order.Add(i);

            // A stable insertion sort rather than List.Sort: List.Sort is not
            // stable, and the comparison above is only total because of the
            // age tie-break. Two creatures raised in the same frame can share
            // both a fraction and an age, and an unstable sort would pick
            // between them differently from one call to the next.
            for (int i = 1; i < order.Count; i++)
            {
                int index = order[i];
                var candidate = candidates[index];

                int j = i - 1;
                while (j >= 0 && Compare(candidates[order[j]], candidate) > 0)
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = index;
            }

            return order;
        }
    }
}
