using System;

namespace RossQoL.Core.Portals
{
    /// <summary>
    /// A 3D point, engine-free.
    ///
    /// Core exists to hold the rules that are worth testing without a running
    /// game, which means it cannot reference UnityEngine and therefore cannot
    /// use Vector3. This is the whole of the geometry Core needs; the Game
    /// layer converts at its own boundary.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vec3 WithY(float y) => new Vec3(X, y, Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        /// <summary>
        /// Squared distance, because every caller here compares against a
        /// radius and a square root would be thrown away.
        /// </summary>
        public static float DistanceSquared(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }
}
