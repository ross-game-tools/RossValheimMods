using System;
using System.Collections.Generic;
using System.Linq;

namespace RossQoL.Core.Production
{
    /// <summary>A place output could go, as seen at planning time.</summary>
    public readonly struct DestinationCandidate
    {
        public int Index { get; }
        public bool HoldsItem { get; }
        public int Room { get; }
        public float DistanceSquared { get; }

        public DestinationCandidate(int index, bool holdsItem, int room, float distanceSquared)
        {
            Index = index;
            HoldsItem = holdsItem;
            Room = room;
            DistanceSquared = distanceSquared;
        }
    }

    public readonly struct Placement
    {
        public int Index { get; }
        public int Amount { get; }

        public Placement(int index, int amount)
        {
            Index = index;
            Amount = amount;
        }
    }

    /// <summary>
    /// Where output goes: containers already holding the item first, so like
    /// items stay together, then the nearest with room.
    /// </summary>
    public static class HarvestPlan
    {
        public static List<DestinationCandidate> Rank(IEnumerable<DestinationCandidate> candidates)
        {
            if (candidates == null) return new List<DestinationCandidate>();

            return candidates
                .Where(c => c.Room > 0)
                .OrderByDescending(c => c.HoldsItem)
                .ThenBy(c => c.DistanceSquared)
                .ThenBy(c => c.Index)
                .ToList();
        }

        /// <summary>
        /// Placements are whole units because a producer's level counts units
        /// (one honey level may be several items under a world resource
        /// modifier); a fraction of a unit cannot be taken back out of it.
        /// </summary>
        public static List<Placement> Plan(int amount, int unitSize, IReadOnlyList<DestinationCandidate> ranked, bool wholeOnly)
        {
            var plan = new List<Placement>();
            if (amount <= 0 || ranked == null) return plan;

            int unit = Math.Max(1, unitSize);
            int remaining = amount - amount % unit;

            foreach (var candidate in ranked)
            {
                if (remaining <= 0) break;

                int fits = Math.Min(remaining, candidate.Room);
                fits -= fits % unit;
                if (fits <= 0) continue;

                plan.Add(new Placement(candidate.Index, fits));
                remaining -= fits;
            }

            if (wholeOnly && plan.Sum(p => p.Amount) < amount) plan.Clear();
            return plan;
        }
    }
}
