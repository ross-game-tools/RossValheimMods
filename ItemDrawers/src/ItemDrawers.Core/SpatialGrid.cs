using System;
using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Uniform cell hash. Drawers are static once built, so insert and remove
    /// are rare and query is the operation that matters.
    /// </summary>
    public sealed class SpatialGrid<T>
    {
        private readonly struct Cell : IEquatable<Cell>
        {
            public readonly int X, Y, Z;
            public Cell(int x, int y, int z) { X = x; Y = y; Z = z; }

            public bool Equals(Cell o) => X == o.X && Y == o.Y && Z == o.Z;
            public override bool Equals(object o) => o is Cell c && Equals(c);
            public override int GetHashCode()
            {
                unchecked { return ((X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791)); }
            }
        }

        private readonly float _cellSize;
        private readonly Dictionary<Cell, List<T>> _cells = new Dictionary<Cell, List<T>>();
        private readonly Dictionary<T, (Cell cell, float x, float y, float z)> _index =
            new Dictionary<T, (Cell, float, float, float)>();

        public SpatialGrid(float cellSize)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            _cellSize = cellSize;
        }

        public int Count => _index.Count;

        private Cell CellOf(float x, float y, float z) => new Cell(
            (int)Math.Floor(x / _cellSize),
            (int)Math.Floor(y / _cellSize),
            (int)Math.Floor(z / _cellSize));

        public void Insert(T item, float x, float y, float z)
        {
            if (_index.ContainsKey(item)) { Move(item, x, y, z); return; }

            var cell = CellOf(x, y, z);
            if (!_cells.TryGetValue(cell, out var bucket))
            {
                bucket = new List<T>(4);
                _cells[cell] = bucket;
            }
            bucket.Add(item);
            _index[item] = (cell, x, y, z);
        }

        public bool Remove(T item)
        {
            if (!_index.TryGetValue(item, out var entry)) return false;

            if (_cells.TryGetValue(entry.cell, out var bucket))
            {
                bucket.Remove(item);
                if (bucket.Count == 0) _cells.Remove(entry.cell);
            }
            _index.Remove(item);
            return true;
        }

        public void Move(T item, float x, float y, float z)
        {
            Remove(item);
            Insert(item, x, y, z);
        }

        /// <summary>Appends every item within <paramref name="radius"/>. Does not clear the list.</summary>
        public void Query(float x, float y, float z, float radius, List<T> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (radius <= 0f) return;

            // Symmetric and rounded up. A floor-based range is asymmetric and
            // silently skips the far cell: at radius 5 with cellSize 8 it
            // yields -1..0, missing an item one cell over and well inside
            // the radius.
            int range = (int)Math.Ceiling(radius / _cellSize);
            var origin = CellOf(x, y, z);
            float r2 = radius * radius;

            for (int dx = -range; dx <= range; dx++)
            for (int dy = -range; dy <= range; dy++)
            for (int dz = -range; dz <= range; dz++)
            {
                var cell = new Cell(origin.X + dx, origin.Y + dy, origin.Z + dz);
                if (!_cells.TryGetValue(cell, out var bucket)) continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    var item = bucket[i];
                    var p = _index[item];
                    float ex = p.x - x, ey = p.y - y, ez = p.z - z;
                    if (ex * ex + ey * ey + ez * ez <= r2) results.Add(item);
                }
            }
        }
    }
}
