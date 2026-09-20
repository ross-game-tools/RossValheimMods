using System;

namespace RossPortals.Core
{
    /// <summary>
    /// A plain 3D point. Core cannot reference UnityEngine (the architecture
    /// test enforces that), so it carries its own tiny vector rather than
    /// <c>UnityEngine.Vector3</c>; the Game layer converts at the boundary.
    /// </summary>
    public readonly struct Vec3
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Straight-line distance to another point.</summary>
        public float DistanceTo(Vec3 other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
