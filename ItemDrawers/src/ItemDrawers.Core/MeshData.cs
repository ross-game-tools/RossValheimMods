using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Geometry as plain arrays. Deliberately not a UnityEngine.Mesh: the
    /// adapter converts it, which keeps generation testable.
    /// </summary>
    public sealed class MeshData
    {
        public float[] Vertices { get; }
        public float[] Normals { get; }
        public float[] Uvs { get; }

        public int VertexCount => Vertices.Length / 3;
        public int TriangleCount => VertexCount / 3;

        public MeshData(float[] vertices, float[] normals, float[] uvs)
        {
            Vertices = vertices;
            Normals = normals;
            Uvs = uvs;
        }

        internal sealed class Builder
        {
            private readonly List<float> _pos = new List<float>(4096);
            private readonly List<float> _nor = new List<float>(4096);
            private readonly List<float> _uv = new List<float>(2731);

            public void Triangle(float[] a, float[] b, float[] c, float uvScale)
            {
                float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];

                float nx = uy * vz - uz * vy;
                float ny = uz * vx - ux * vz;
                float nz = ux * vy - uy * vx;

                float len = (float)System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-9f) return;              // degenerate; drop it
                nx /= len; ny /= len; nz /= len;

                // Box-project UVs off the dominant axis so grain tiles at a
                // consistent world scale whichever face we are on. Axis selection uses
                // absolute normal components, so opposite faces mirror the grain — a
                // considered decision for non-directional wood textures.
                float ax = System.Math.Abs(nx), ay = System.Math.Abs(ny), az = System.Math.Abs(nz);
                int i0, i1;
                if (ax >= ay && ax >= az) { i0 = 2; i1 = 1; }
                else if (ay >= az) { i0 = 0; i1 = 2; }
                else { i0 = 0; i1 = 1; }

                foreach (var p in new[] { a, b, c })
                {
                    _pos.Add(p[0]); _pos.Add(p[1]); _pos.Add(p[2]);
                    _nor.Add(nx); _nor.Add(ny); _nor.Add(nz);
                    _uv.Add(p[i0] * uvScale); _uv.Add(p[i1] * uvScale);
                }
            }

            /// <summary>Winds the quad so its normal agrees with <paramref name="hint"/>.</summary>
            public void Quad(float[] a, float[] b, float[] c, float[] d, float[] hint, float uvScale)
            {
                float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;

                if (nx * hint[0] + ny * hint[1] + nz * hint[2] < 0f)
                {
                    var t = b; b = d; d = t;
                }
                Triangle(a, b, c, uvScale);
                Triangle(a, c, d, uvScale);
            }

            public void TriangleHinted(float[] a, float[] b, float[] c, float[] hint, float uvScale)
            {
                float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;

                if (nx * hint[0] + ny * hint[1] + nz * hint[2] < 0f)
                {
                    var t = b; b = c; c = t;
                }
                Triangle(a, b, c, uvScale);
            }

            public MeshData Build() => new MeshData(_pos.ToArray(), _nor.ToArray(), _uv.ToArray());
        }
    }
}
