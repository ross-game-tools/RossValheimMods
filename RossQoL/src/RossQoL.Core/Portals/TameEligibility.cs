using System.Collections.Generic;

namespace RossQoL.Core.Portals
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

            // A summon is exempt from the tamed test, not from the following
            // test. Vanilla can silently fail to record a summon as tamed (see
            // TameCandidate.IsSummon), so requiring it here is what kept raised
            // skeletons from coming through portals. Nothing is loosened by the
            // exemption: a creature only ever follows a player because
            // Tameable.Command was called on it, and the sole routes to that are
            // petting an already-tamed creature and a staff raising a summon --
            // so "following me" already means "mine", and a wild creature can
            // never satisfy the test below.
            if (!candidate.IsTamed && !candidate.IsSummon) return false;
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
