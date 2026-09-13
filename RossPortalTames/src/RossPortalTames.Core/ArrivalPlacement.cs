using System;
using System.Collections.Generic;

namespace RossPortalTames.Core
{
    /// <summary>
    /// Where each arriving tame is put down.
    ///
    /// A ring around the player was the obvious design and is wrong: portals
    /// are commonly built against a wall inside a small hut, and a ring puts
    /// half the pack inside the geometry. This searches instead, and validates
    /// every candidate against the world through a predicate the caller
    /// supplies -- which is also what makes the wall case directly testable.
    /// </summary>
    public static class ArrivalPlacement
    {
        // Distance between successive rings, and the angles tried within each.
        // The angle order fans outward from straight ahead so the first free
        // spot found is the one most nearly in front of the player.
        private const float RingStep = 1.5f;
        private static readonly float[] AngleOffsetsDegrees =
            { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f, 130f, -130f, 160f, -160f, 180f };

        public static Vec3[] Compute(
            Vec3 arrival, Vec3 facing, int count, float searchDistance, Func<Vec3, bool> isFree)
        {
            if (count <= 0) return Array.Empty<Vec3>();

            // A null predicate means the caller has no way to ask the world;
            // treat that as open rather than as blocked, so a missing
            // integration degrades to "they land near you" instead of "they
            // all pile on your head".
            if (isFree == null) isFree = _ => true;

            var (fx, fz) = Normalise(facing);
            var placed = new Vec3[count];
            var taken = new List<Vec3>(count);

            for (int i = 0; i < count; i++)
            {
                placed[i] = FindSpot(arrival, fx, fz, searchDistance, isFree, taken);
                taken.Add(placed[i]);
            }

            return placed;
        }

        private static Vec3 FindSpot(
            Vec3 arrival, float fx, float fz, float searchDistance,
            Func<Vec3, bool> isFree, List<Vec3> taken)
        {
            // A searchDistance smaller than RingStep must still try one ring
            // -- at searchDistance itself -- rather than skip the loop
            // entirely and silently disable the search. Tight portal huts
            // are documented advice for lowering this value below RingStep.
            if (searchDistance <= 0f) return arrival;

            float firstRadius = Math.Min(RingStep, searchDistance);
            for (float radius = firstRadius; radius <= searchDistance + 0.001f; radius += RingStep)
            {
                foreach (float degrees in AngleOffsetsDegrees)
                {
                    var candidate = Rotate(arrival, fx, fz, radius, degrees);

                    if (!isFree(candidate)) continue;
                    if (IsTaken(taken, candidate)) continue;

                    return candidate;
                }
            }

            // Nothing free within range. The player's own arrival position is
            // the one point guaranteed to be somewhere a body can stand.
            return arrival;
        }

        private static bool IsTaken(List<Vec3> taken, Vec3 candidate)
        {
            const float MinSeparation = 1.0f;
            for (int i = 0; i < taken.Count; i++)
                if (Vec3.DistanceSquared(taken[i], candidate) < MinSeparation * MinSeparation)
                    return true;
            return false;
        }

        private static Vec3 Rotate(Vec3 origin, float fx, float fz, float radius, float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            double cos = Math.Cos(radians), sin = Math.Sin(radians);

            float dx = (float)(fx * cos - fz * sin);
            float dz = (float)(fx * sin + fz * cos);

            // Y is left at the arrival height. The Game layer snaps it to the
            // ground, which is knowledge Core does not have.
            return new Vec3(origin.X + dx * radius, origin.Y, origin.Z + dz * radius);
        }

        /// <summary>
        /// Horizontal facing as a unit vector. A degenerate facing (straight
        /// up, an uninitialised zero, or a NaN/infinite component) would
        /// divide by zero or propagate NaN and write it into a ZDO position,
        /// so it falls back to a fixed direction. A comparison against NaN
        /// is always false, so the length check alone would not catch it --
        /// float.IsFinite is checked explicitly.
        /// </summary>
        private static (float X, float Z) Normalise(Vec3 facing)
        {
            float length = (float)Math.Sqrt(facing.X * facing.X + facing.Z * facing.Z);
            if (!float.IsFinite(length) || length < 0.0001f) return (0f, 1f);
            return (facing.X / length, facing.Z / length);
        }
    }
}
