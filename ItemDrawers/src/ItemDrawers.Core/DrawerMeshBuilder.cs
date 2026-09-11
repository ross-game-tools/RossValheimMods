using System;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Builds the drawer from chamfered boxes. Sharp ninety-degree edges
    /// catch no specular and read as computer-generated, so every box is
    /// bevelled — that single detail is most of what makes it look like wood.
    /// </summary>
    public static class DrawerMeshBuilder
    {
        private const float UvScale = 1.0f;
        private static readonly int[] Signs = { -1, 1 };

        public static MeshData Build(DrawerProportions p)
        {
            var gb = new MeshData.Builder();

            float w = p.Width, h = p.Height, d = p.Depth;
            float frame = Math.Min(p.FrameThickness, Math.Min(w, h) / 2f - 0.02f);
            float recess = Math.Min(p.RecessDepth, d * 0.5f);
            float bevel = p.Bevel;

            // Carcass, pulled back by the recess. Its own front face becomes
            // the recessed panel, so the recess costs no extra geometry.
            AddChamferBox(gb, 0f, 0f, -recess / 2f, w, h, d - recess, bevel);

            // Frame planks standing proud around the opening.
            float fz = d / 2f - recess / 2f;
            AddChamferBox(gb, 0f, h / 2f - frame / 2f, fz, w, frame, recess, bevel);
            AddChamferBox(gb, 0f, -h / 2f + frame / 2f, fz, w, frame, recess, bevel);

            float stileHeight = h - 2f * frame;
            // Guard is unreachable: frame is clamped to min(FrameThickness, min(w,h)/2 - 0.02),
            // so frame < h/2, so stileHeight > h - h = 0. But we keep it as a safety check anyway.
            if (stileHeight > 0.01f)
            {
                AddChamferBox(gb, -w / 2f + frame / 2f, 0f, fz, frame, stileHeight, recess, bevel);
                AddChamferBox(gb, w / 2f - frame / 2f, 0f, fz, frame, stileHeight, recess, bevel);
            }

            AddHandle(gb, p, frame, recess);
            return gb.Build();
        }

        private static void AddHandle(MeshData.Builder gb, DrawerProportions p, float frame, float recess)
        {
            float panelZ = p.Depth / 2f - recess;
            float openH = p.Height - 2f * frame;

            switch (p.Handle)
            {
                case HandleStyle.Bar:
                {
                    float bw = Math.Min(p.HandleWidth, p.Width - 2f * frame - 0.02f);
                    float by = -openH / 2f + p.HandleSection * 1.4f;
                    // HandleProud is the block's depth, not protrusion past the frame.
                    // The recess consumes RecessDepth of it, leaving net protrusion of
                    // HandleProud - RecessDepth past the frame face. The handle sits in
                    // the recessed panel and stands proud of the frame.
                    AddChamferBox(gb, 0f, by, panelZ + p.HandleProud / 2f,
                                  bw, p.HandleSection, p.HandleProud,
                                  Math.Min(p.Bevel, p.HandleSection / 2.5f));
                    break;
                }
                case HandleStyle.Knobs:
                {
                    float kx = Math.Min(p.HandleWidth / 2f, p.Width / 2f - frame - p.HandleSection);
                    for (int s = -1; s <= 1; s += 2)
                        AddChamferBox(gb, kx * s, 0f, panelZ + p.HandleProud / 2f,
                                      p.HandleSection, p.HandleSection, p.HandleProud,
                                      p.HandleSection / 3f);
                    break;
                }
                case HandleStyle.Pull:
                {
                    float pw = Math.Min(p.HandleWidth, p.Width - 2f * frame);
                    AddChamferBox(gb, 0f, -p.Height / 2f + frame + p.HandleSection / 2f,
                                  p.Depth / 2f - p.HandleProud / 2f,
                                  pw, p.HandleSection, p.HandleProud, p.Bevel);
                    break;
                }
                case HandleStyle.None:
                default:
                    break;
            }
        }

        /// <summary>Standalone chamfered box, exposed for testing winding and extents.</summary>
        public static MeshData ChamferBox(float cx, float cy, float cz,
                                          float w, float h, float d, float chamfer)
        {
            var gb = new MeshData.Builder();
            AddChamferBox(gb, cx, cy, cz, w, h, d, chamfer);
            return gb.Build();
        }

        private static void AddChamferBox(MeshData.Builder gb, float cx, float cy, float cz,
                                          float w, float h, float d, float chamfer)
        {
            float hx = w / 2f, hy = h / 2f, hz = d / 2f;
            float c = Math.Max(0f, Math.Min(chamfer, Math.Min(hx, Math.Min(hy, hz)) * 0.98f));

            var extent = new[] { hx, hy, hz };
            var inner = new[] { hx - c, hy - c, hz - c };
            var centre = new[] { cx, cy, cz };

            // The corner point that lies on `axis`, in octant `s`.
            float[] Point(int axis, int[] s)
            {
                var v = new float[3];
                for (int i = 0; i < 3; i++)
                    v[i] = centre[i] + s[i] * (i == axis ? extent[i] : inner[i]);
                return v;
            }

            // Six faces, each inset by the chamfer on its two in-plane axes.
            var order = new[] { new[] { -1, -1 }, new[] { 1, -1 }, new[] { 1, 1 }, new[] { -1, 1 } };
            for (int axis = 0; axis < 3; axis++)
            {
                foreach (int s in Signs)
                {
                    int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                    var corner = new float[4][];
                    for (int q = 0; q < 4; q++)
                    {
                        var sg = new int[3];
                        sg[axis] = s; sg[a1] = order[q][0]; sg[a2] = order[q][1];
                        corner[q] = Point(axis, sg);
                    }
                    var hint = new float[3];
                    hint[axis] = s;
                    gb.Quad(corner[0], corner[1], corner[2], corner[3], hint, UvScale);
                }
            }

            // Twelve edge chamfers.
            for (int e = 0; e < 3; e++)
            {
                int b1 = (e + 1) % 3, b2 = (e + 2) % 3;
                foreach (int s1 in Signs)
                foreach (int s2 in Signs)
                {
                    var qa = new float[2][];
                    var qb = new float[2][];
                    for (int j = 0; j < 2; j++)
                    {
                        var g = new int[3];
                        g[e] = Signs[j]; g[b1] = s1; g[b2] = s2;
                        qa[j] = Point(b1, g);
                        qb[j] = Point(b2, g);
                    }
                    var hint = new float[3];
                    hint[b1] = s1; hint[b2] = s2;
                    gb.Quad(qa[0], qa[1], qb[1], qb[0], hint, UvScale);
                }
            }

            // Eight corner triangles.
            foreach (int sx in Signs)
            foreach (int sy in Signs)
            foreach (int sz in Signs)
            {
                var sg = new[] { sx, sy, sz };
                var hint = new float[] { sx, sy, sz };
                gb.TriangleHinted(Point(0, sg), Point(1, sg), Point(2, sg), hint, UvScale);
            }
        }
    }
}
