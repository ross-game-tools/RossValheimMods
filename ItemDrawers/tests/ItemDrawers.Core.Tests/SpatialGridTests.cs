using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class SpatialGridTests
    {
        [Fact]
        public void Finds_an_item_inside_the_radius()
        {
            var grid = new SpatialGrid<string>(cellSize: 8f);
            grid.Insert("a", 1f, 0f, 1f);

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Equal(new[] { "a" }, hits);
        }

        [Fact]
        public void Excludes_an_item_outside_the_radius()
        {
            var grid = new SpatialGrid<string>(cellSize: 8f);
            grid.Insert("far", 100f, 0f, 0f);

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Empty(hits);
        }

        [Fact]
        public void Radius_is_spherical_not_cubic()
        {
            var grid = new SpatialGrid<string>(cellSize: 8f);
            grid.Insert("corner", 4f, 4f, 4f);   // ~6.93 away, outside a radius of 5

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Empty(hits);
        }

        [Fact]
        public void Agrees_with_brute_force_over_random_data()
        {
            // The grid is an optimisation. Its only job is to return exactly
            // what a linear scan would.
            var rng = new Random(20260909);
            var grid = new SpatialGrid<int>(cellSize: 10f);
            var points = new List<(int id, float x, float y, float z)>();

            for (int i = 0; i < 2000; i++)
            {
                float x = (float)(rng.NextDouble() * 400 - 200);
                float y = (float)(rng.NextDouble() * 60 - 30);
                float z = (float)(rng.NextDouble() * 400 - 200);
                points.Add((i, x, y, z));
                grid.Insert(i, x, y, z);
            }

            var hits = new List<int>();
            for (int q = 0; q < 100; q++)
            {
                float qx = (float)(rng.NextDouble() * 400 - 200);
                float qy = (float)(rng.NextDouble() * 60 - 30);
                float qz = (float)(rng.NextDouble() * 400 - 200);
                float r = (float)(rng.NextDouble() * 30 + 1);

                hits.Clear();
                grid.Query(qx, qy, qz, r, hits);

                var expected = points
                    .Where(p => (p.x - qx) * (p.x - qx) + (p.y - qy) * (p.y - qy) + (p.z - qz) * (p.z - qz) <= r * r)
                    .Select(p => p.id)
                    .OrderBy(i => i)
                    .ToArray();

                Assert.Equal(expected, hits.OrderBy(i => i).ToArray());
            }
        }

        [Fact]
        public void Removed_items_stop_being_found()
        {
            var grid = new SpatialGrid<string>(8f);
            grid.Insert("a", 0f, 0f, 0f);

            Assert.True(grid.Remove("a"));
            Assert.False(grid.Remove("a"));       // idempotent second call

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 50f, hits);
            Assert.Empty(hits);
            Assert.Equal(0, grid.Count);
        }

        [Fact]
        public void Moving_an_item_relocates_it_rather_than_duplicating_it()
        {
            var grid = new SpatialGrid<string>(8f);
            grid.Insert("a", 0f, 0f, 0f);
            grid.Move("a", 100f, 0f, 0f);

            var near = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, near);
            Assert.Empty(near);

            var far = new List<string>();
            grid.Query(100f, 0f, 0f, 5f, far);
            Assert.Equal(new[] { "a" }, far);
            Assert.Equal(1, grid.Count);
        }

        [Fact]
        public void Query_appends_without_clearing_the_caller_s_list()
        {
            // The manager reuses one list across many queries to avoid
            // allocating every frame, so this behaviour is depended upon.
            var grid = new SpatialGrid<string>(8f);
            grid.Insert("a", 0f, 0f, 0f);

            var hits = new List<string> { "pre-existing" };
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Equal(new[] { "pre-existing", "a" }, hits);
        }

        [Fact]
        public void Inserting_the_same_item_twice_keeps_one_entry()
        {
            var grid = new SpatialGrid<string>(8f);
            grid.Insert("a", 0f, 0f, 0f);
            grid.Insert("a", 1f, 0f, 0f);

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Single(hits);
            Assert.Equal(1, grid.Count);
        }

        [Fact]
        public void Agrees_with_brute_force_after_removals_and_moves()
        {
            // After a sequence of operations, the grid's state may have fragmented
            // empty buckets and relocated items. This tests that the internal state
            // remains consistent: Query still returns exactly what a linear scan would.
            var rng = new Random(20260909);
            var grid = new SpatialGrid<int>(cellSize: 10f);
            var points = new List<(int id, float x, float y, float z)>();

            // Populate with random points
            for (int i = 0; i < 2000; i++)
            {
                float x = (float)(rng.NextDouble() * 400 - 200);
                float y = (float)(rng.NextDouble() * 60 - 30);
                float z = (float)(rng.NextDouble() * 400 - 200);
                points.Add((i, x, y, z));
                grid.Insert(i, x, y, z);
            }

            // Apply a randomized sequence of removals and moves
            for (int op = 0; op < 300; op++)
            {
                int choice = rng.Next(3);
                if (choice == 0 && points.Count > 1500)
                {
                    // Remove a random item
                    int idx = rng.Next(points.Count);
                    int id = points[idx].id;
                    grid.Remove(id);
                    points.RemoveAt(idx);
                }
                else if (choice == 1 && points.Count > 0)
                {
                    // Move a random item to a new position
                    int idx = rng.Next(points.Count);
                    float newX = (float)(rng.NextDouble() * 400 - 200);
                    float newY = (float)(rng.NextDouble() * 60 - 30);
                    float newZ = (float)(rng.NextDouble() * 400 - 200);
                    int id = points[idx].id;
                    grid.Move(id, newX, newY, newZ);
                    points[idx] = (id, newX, newY, newZ);
                }
                else
                {
                    // Insert a new item
                    int newId = 2000 + op;
                    float x = (float)(rng.NextDouble() * 400 - 200);
                    float y = (float)(rng.NextDouble() * 60 - 30);
                    float z = (float)(rng.NextDouble() * 400 - 200);
                    grid.Insert(newId, x, y, z);
                    points.Add((newId, x, y, z));
                }
            }

            // Now run queries against the final state
            var hits = new List<int>();
            for (int q = 0; q < 100; q++)
            {
                float qx = (float)(rng.NextDouble() * 400 - 200);
                float qy = (float)(rng.NextDouble() * 60 - 30);
                float qz = (float)(rng.NextDouble() * 400 - 200);
                float r = (float)(rng.NextDouble() * 30 + 1);

                hits.Clear();
                grid.Query(qx, qy, qz, r, hits);

                var expected = points
                    .Where(p => (p.x - qx) * (p.x - qx) + (p.y - qy) * (p.y - qy) + (p.z - qz) * (p.z - qz) <= r * r)
                    .Select(p => p.id)
                    .OrderBy(i => i)
                    .ToArray();

                Assert.Equal(expected, hits.OrderBy(i => i).ToArray());
            }
        }

        [Fact]
        public void Finds_item_at_exact_radius_across_cell_boundary()
        {
            // Worst-case boundary: item exactly `radius` away, with both
            // positioned to straddle a cell edge. This verifies the ceiling-based
            // cell range calculation.
            float cellSize = 8f;
            var grid = new SpatialGrid<string>(cellSize);

            // Query at origin (cell 0, 0, 0)
            // Item at exactly radius=16 away, positioned at boundary of cells
            // Distance = 16 (exactly on the radius sphere)
            // Positioned at (16, 0, 0) - this crosses from cell 1 into cell 2
            float queryX = 0f, queryY = 0f, queryZ = 0f;
            float radius = 16f;
            float itemX = radius, itemY = 0f, itemZ = 0f;

            grid.Insert("boundary", itemX, itemY, itemZ);

            var hits = new List<string>();
            grid.Query(queryX, queryY, queryZ, radius, hits);

            Assert.Single(hits);
            Assert.Equal("boundary", hits[0]);
        }

        [Fact]
        public void Moving_item_within_same_cell_maintains_findability()
        {
            // Moving an item within the same cell it occupies goes through
            // Remove (which may drop the empty bucket) then Insert (which recreates it).
            var grid = new SpatialGrid<string>(8f);
            grid.Insert("a", 0f, 0f, 0f);

            // Move within the same cell (both positions hash to cell 0, 0, 0)
            grid.Move("a", 2f, 1f, 3f);

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Single(hits);
            Assert.Equal("a", hits[0]);
            Assert.Equal(1, grid.Count);
        }

        [Fact]
        public void Handles_negative_coordinates()
        {
            // Valheim worlds are centred at origin. Test both sides.
            float cellSize = 10f;
            var grid = new SpatialGrid<string>(cellSize);

            // Both item and query on negative side
            grid.Insert("neg", -5f, -3f, -7f);

            var hits = new List<string>();
            grid.Query(-4f, -2f, -6f, 3f, hits);

            Assert.Single(hits);
            Assert.Equal("neg", hits[0]);
        }

        [Fact]
        public void Query_radius_straddles_origin()
        {
            // Query radius spans cells -1 and 0. This verifies that Math.Floor
            // correctly handles the origin transition and that symmetric range
            // iteration covers both sides.
            float cellSize = 10f;
            var grid = new SpatialGrid<string>(cellSize);

            // Insert items just on each side of the origin
            grid.Insert("pos", 3f, 0f, 0f);      // cell 0, 0, 0
            grid.Insert("neg", -3f, 0f, 0f);    // cell -1, 0, 0

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Equal(2, hits.Count);
            Assert.Contains("pos", hits);
            Assert.Contains("neg", hits);
        }
    }
}
