using System;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class DrawerMeshBuilderTests
    {
        private static (float x, float y, float z) V(MeshData m, int i) =>
            (m.Vertices[i * 3], m.Vertices[i * 3 + 1], m.Vertices[i * 3 + 2]);

        private static (float x, float y, float z) N(MeshData m, int i) =>
            (m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);

        private static void AssertMeshIsManifold(MeshData m)
        {
            // Helper: check that every edge in a mesh is shared by exactly two triangles.
            // Merge vertices by Euclidean proximity (epsilon = 1e-5f).
            var representatives = new System.Collections.Generic.List<int>();
            var repIndex = new int[m.VertexCount];

            for (int i = 0; i < m.VertexCount; i++)
            {
                var (x, y, z) = V(m, i);
                int rep = -1;

                foreach (int r in representatives)
                {
                    var (rx, ry, rz) = V(m, r);
                    float dx = x - rx, dy = y - ry, dz = z - rz;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (dist < 1e-5f)
                    {
                        rep = r;
                        break;
                    }
                }

                if (rep == -1)
                {
                    rep = i;
                    representatives.Add(i);
                }
                repIndex[i] = rep;
            }

            // Build edge occurrence map (each edge as (min, max) representative pair).
            var edgeCounts = new System.Collections.Generic.Dictionary<(int, int), int>();

            for (int t = 0; t < m.TriangleCount; t++)
            {
                int v0 = t * 3;
                int v1 = t * 3 + 1;
                int v2 = t * 3 + 2;

                int r0 = repIndex[v0];
                int r1 = repIndex[v1];
                int r2 = repIndex[v2];

                var edges = new[] { (r0, r1), (r1, r2), (r2, r0) };

                foreach (var (a, b) in edges)
                {
                    // Normalize edge key: always put smaller index first.
                    var key = a < b ? (a, b) : (b, a);
                    if (!edgeCounts.ContainsKey(key))
                        edgeCounts[key] = 0;
                    edgeCounts[key]++;
                }
            }

            // Assert every edge is used exactly twice.
            foreach (var kvp in edgeCounts)
            {
                var (a, b) = kvp.Key;
                int count = kvp.Value;
                Assert.True(count == 2,
                    $"Edge ({a}, {b}) used {count} time(s), not 2. " +
                    $"(Vertex {a} at ({V(m, a).x}, {V(m, a).y}, {V(m, a).z}), " +
                    $"Vertex {b} at ({V(m, b).x}, {V(m, b).y}, {V(m, b).z}))");
            }
        }

        [Fact]
        public void Output_arrays_are_consistent_and_triangulated()
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions());

            Assert.Equal(0, m.VertexCount % 3);
            Assert.Equal(m.VertexCount * 3, m.Vertices.Length);
            Assert.Equal(m.VertexCount * 3, m.Normals.Length);
            Assert.Equal(m.VertexCount * 2, m.Uvs.Length);
            Assert.Equal(m.VertexCount / 3, m.TriangleCount);
        }

        [Fact]
        public void Every_normal_is_unit_length()
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions());

            for (int i = 0; i < m.VertexCount; i++)
            {
                var (x, y, z) = N(m, i);
                double len = Math.Sqrt(x * x + y * y + z * z);
                Assert.InRange(len, 0.999, 1.001);
            }
        }

        [Fact]
        public void No_vertex_or_normal_is_NaN()
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions());

            foreach (var f in m.Vertices) Assert.False(float.IsNaN(f) || float.IsInfinity(f));
            foreach (var f in m.Normals) Assert.False(float.IsNaN(f) || float.IsInfinity(f));
        }

        [Theory]
        [InlineData(HandleStyle.None)]
        [InlineData(HandleStyle.Bar)]
        [InlineData(HandleStyle.Knobs)]
        [InlineData(HandleStyle.Pull)]
        public void Geometry_stays_inside_the_declared_footprint(HandleStyle style)
        {
            // A wall of drawers only tiles if nothing escapes the 1m cube,
            // except the handle, which is allowed to stand proud of the front.
            var p = new DrawerProportions { Handle = style };
            var m = DrawerMeshBuilder.Build(p);

            for (int i = 0; i < m.VertexCount; i++)
            {
                var (x, y, z) = V(m, i);
                Assert.InRange(x, -p.Width / 2 - 1e-4, p.Width / 2 + 1e-4);
                Assert.InRange(y, -p.Height / 2 - 1e-4, p.Height / 2 + 1e-4);
                Assert.InRange(z, -p.Depth / 2 - 1e-4, p.Depth / 2 + p.HandleProud + 1e-4);
            }
        }

        [Fact]
        public void A_chamfer_box_faces_outward_everywhere()
        {
            // For a convex box centred on the origin, every triangle's normal
            // must point away from the centre. This catches winding bugs,
            // which are otherwise invisible until the mesh renders inside out.
            var m = DrawerMeshBuilder.ChamferBox(0, 0, 0, 1f, 1f, 1f, 0.05f);

            for (int t = 0; t < m.TriangleCount; t++)
            {
                int i = t * 3;
                var (ax, ay, az) = V(m, i);
                var (bx, by, bz) = V(m, i + 1);
                var (cx, cy, cz) = V(m, i + 2);

                float mx = (ax + bx + cx) / 3f, my = (ay + by + cy) / 3f, mz = (az + bz + cz) / 3f;
                var (nx, ny, nz) = N(m, i);

                Assert.True(mx * nx + my * ny + mz * nz > 0f,
                    $"Triangle {t} is wound inward.");
            }
        }

        [Fact]
        public void Chamfering_cuts_inward_and_never_grows_the_box()
        {
            var sharp = DrawerMeshBuilder.ChamferBox(0, 0, 0, 1f, 1f, 1f, 0f);
            var beveled = DrawerMeshBuilder.ChamferBox(0, 0, 0, 1f, 1f, 1f, 0.1f);

            float ExtentX(MeshData m)
            {
                float max = 0f;
                for (int i = 0; i < m.VertexCount; i++) max = Math.Max(max, Math.Abs(m.Vertices[i * 3]));
                return max;
            }

            Assert.Equal(ExtentX(sharp), ExtentX(beveled), 4);
            Assert.Equal(0.5f, ExtentX(beveled), 4);
        }

        [Fact]
        public void A_zero_bevel_still_produces_valid_geometry()
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions { Bevel = 0f });

            Assert.True(m.TriangleCount > 0);
            foreach (var f in m.Normals) Assert.False(float.IsNaN(f));
        }

        [Theory]
        [InlineData(HandleStyle.None)]
        [InlineData(HandleStyle.Bar)]
        [InlineData(HandleStyle.Knobs)]
        [InlineData(HandleStyle.Pull)]
        public void Every_handle_style_builds(HandleStyle style)
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions { Handle = style });
            Assert.True(m.TriangleCount > 0);
        }

        [Fact]
        public void A_drawer_stays_cheap_enough_for_a_hundred_of_them()
        {
            // Budget, not a measurement: 100 drawers should be a few hundred
            // thousand triangles at worst. If this trips, something is
            // generating far more geometry than the design intends.
            var m = DrawerMeshBuilder.Build(new DrawerProportions());
            Assert.InRange(m.TriangleCount, 1, 3000);
        }

        [Fact]
        public void Triangle_count_is_pinned_at_264_for_default_proportions()
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions());
            Assert.Equal(264, m.TriangleCount);
        }

        [Theory]
        [InlineData(HandleStyle.Bar)]
        [InlineData(HandleStyle.Knobs)]
        public void Handle_has_meaningful_protrusion(HandleStyle style)
        {
            // Net handle protrusion = HandleProud - RecessDepth. The handle
            // should not sit flush with or behind the frame. Computed from
            // p's own fields, not a hardcoded literal: an earlier version
            // hardcoded the expected Z for the original 1m-cube defaults
            // (0.5 + 0.005 = 0.505), which silently became the wrong
            // assertion the moment the drawer was resized to the measured
            // 0.66m cube -- the test kept passing-or-failing on stale
            // arithmetic instead of tracking whatever DrawerProportions
            // actually declares.
            var p = new DrawerProportions { Handle = style };
            var m = DrawerMeshBuilder.Build(p);

            float maxZ = 0f;
            for (int i = 0; i < m.VertexCount; i++)
                maxZ = Math.Max(maxZ, m.Vertices[i * 3 + 2]);

            // Frame face is at p.Depth / 2. The handle's front should sit at
            // that plus its net protrusion. Allow a small tolerance for
            // numerical precision and chamfering.
            float expected = p.Depth / 2f + (p.HandleProud - p.RecessDepth);
            Assert.InRange(maxZ, expected - 0.001f, expected + 0.001f);
        }

        [Fact]
        public void Winding_is_outward_at_arbitrary_offset_and_scale()
        {
            // Winding is translation-invariant: a box at any position generates
            // normals pointing outward from its own centre, not the origin.
            // Mirror a real stile: 50mm thick, 900mm tall, 30mm deep, offset position.
            var m = DrawerMeshBuilder.ChamferBox(0.3f, -0.2f, 0.1f, 0.05f, 0.9f, 0.03f, 0.006f);

            for (int t = 0; t < m.TriangleCount; t++)
            {
                int i = t * 3;
                var (ax, ay, az) = V(m, i);
                var (bx, by, bz) = V(m, i + 1);
                var (cx, cy, cz) = V(m, i + 2);

                float mx = (ax + bx + cx) / 3f, my = (ay + by + cy) / 3f, mz = (az + bz + cz) / 3f;
                var (nx, ny, nz) = N(m, i);

                // Centroid relative to this box's centre.
                float dx = mx - 0.3f, dy = my - (-0.2f), dz = mz - 0.1f;

                Assert.True(dx * nx + dy * ny + dz * nz > 0f,
                    $"Triangle {t} at offset is wound inward.");
            }
        }

        [Fact]
        public void Watertightness_check_rejects_a_mesh_with_a_dangling_edge()
        {
            // The watertightness check must reject meshes with cracks (edges used only once).
            // This test exercises the failure path of AssertMeshIsManifold.
            var m = DrawerMeshBuilder.ChamferBox(0, 0, 0, 1f, 1f, 1f, 0.05f);

            // Corrupt: remove the last triangle (3 vertices = 9 floats for positions, 9 for normals, 6 for UVs)
            var truncatedVertices = new float[m.Vertices.Length - 9];
            var truncatedNormals = new float[m.Normals.Length - 9];
            var truncatedUvs = new float[m.Uvs.Length - 6];

            System.Array.Copy(m.Vertices, truncatedVertices, truncatedVertices.Length);
            System.Array.Copy(m.Normals, truncatedNormals, truncatedNormals.Length);
            System.Array.Copy(m.Uvs, truncatedUvs, truncatedUvs.Length);

            var corruptMesh = new MeshData(truncatedVertices, truncatedNormals, truncatedUvs);

            // Expect the watertightness check to reject this mesh.
            var ex = Assert.Throws<Xunit.Sdk.TrueException>(() => AssertMeshIsManifold(corruptMesh));

            // Verify the exception message mentions the edge or its count to ensure the helper
            // is actually checking edges and not silently succeeding for the wrong reason.
            Assert.Contains("used 1 time", ex.Message);
        }

        [Fact]
        public void Every_edge_in_a_chamfer_box_is_shared_by_exactly_two_triangles()
        {
            // In a closed manifold, every edge is shared by exactly two triangles.
            // Test only a single ChamferBox (the assembled drawer is six separate
            // interpenetrating boxes and is deliberately not manifold).
            var m = DrawerMeshBuilder.ChamferBox(0, 0, 0, 1f, 1f, 1f, 0.05f);
            AssertMeshIsManifold(m);
        }
    }
}
