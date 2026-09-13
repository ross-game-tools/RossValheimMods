using System.Collections.Generic;

namespace RossPortalTames.Core
{
    /// <summary>
    /// Which following tames come through the portal.
    /// </summary>
    public static class TameEligibility
    {
        public static bool Qualifies(TameCandidate candidate, Vec3 playerPosition, float radius)
        {
            // A non-positive radius means nothing comes, rather than
            // everything. A config edit to 0 reads naturally as "off", and
            // reading it as "unlimited" would teleport a player's entire
            // tamed population across the world.
            if (radius <= 0f) return false;

            if (!candidate.IsTamed) return false;
            if (!candidate.IsFollowingPlayer) return false;
            if (candidate.IsBusy) return false;

            // Inclusive at the edge, and in three dimensions: a tame 30m
            // straight up a cliff is not nearby, however close it looks on a
            // map.
            return Vec3.DistanceSquared(candidate.Position, playerPosition) <= radius * radius;
        }

        /// <summary>
        /// Indices rather than the candidates themselves, so Core never has to
        /// know about ZDOIDs or GameObjects: the caller keeps its own parallel
        /// list and maps the answers back.
        /// </summary>
        public static List<int> SelectIndices(
            IReadOnlyList<TameCandidate> candidates, Vec3 playerPosition, float radius)
        {
            var selected = new List<int>();
            if (candidates == null) return selected;

            for (int i = 0; i < candidates.Count; i++)
                if (Qualifies(candidates[i], playerPosition, radius))
                    selected.Add(i);

            return selected;
        }
    }
}
