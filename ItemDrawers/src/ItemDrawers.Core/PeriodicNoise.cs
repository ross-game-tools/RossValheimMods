using System;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Value noise on a lattice that wraps by construction, not by mirroring.
    ///
    /// <see cref="Sample"/> hashes its four surrounding lattice corners
    /// modulo <paramref name="cells"/> before ever looking at their value, so
    /// advancing the input coordinate by exactly <paramref name="cells"/>
    /// revisits the same four corners with the same fractional offset and
    /// therefore returns the bit-identical result. That is the whole
    /// seamlessness argument: no mirroring, no blending at the edge, the
    /// function is periodic because the lattice it is built from is a ring,
    /// not a line.
    ///
    /// Textures built on top of this must keep that property by only ever
    /// stepping an integer number of cells across one full texture
    /// dimension (see <see cref="Fbm"/>) -- a non-integer number of cells
    /// would reintroduce a seam.
    /// </summary>
    public static class PeriodicNoise
    {
        /// <summary>
        /// Smoothly interpolated periodic value noise, in roughly [-1, 1].
        /// (x, y) are continuous "cell" coordinates: Sample(x + cells, y, cells, seed)
        /// is guaranteed to equal Sample(x, y, cells, seed) exactly.
        /// </summary>
        public static float Sample(float x, float y, int cells, int seed)
        {
            if (cells < 1) cells = 1;

            float fx = (float)Math.Floor(x);
            float fy = (float)Math.Floor(y);

            int x0 = Mod((int)fx, cells);
            int y0 = Mod((int)fy, cells);
            int x1 = Mod(x0 + 1, cells);
            int y1 = Mod(y0 + 1, cells);

            float tx = x - fx;
            float ty = y - fy;
            float u = Fade(tx);
            float v = Fade(ty);

            float v00 = HashValue(x0, y0, seed);
            float v10 = HashValue(x1, y0, seed);
            float v01 = HashValue(x0, y1, seed);
            float v11 = HashValue(x1, y1, seed);

            float ix0 = Lerp(v00, v10, u);
            float ix1 = Lerp(v01, v11, u);
            return Lerp(ix0, ix1, v);
        }

        /// <summary>
        /// Fractal sum of <see cref="Sample"/> octaves, addressed directly in pixel
        /// space of a <paramref name="width"/> x <paramref name="height"/> texture.
        /// Each octave uses an integer cell count, so a pixel step of exactly
        /// <paramref name="width"/> (or <paramref name="height"/>) always advances
        /// every octave's coordinate by an integer number of its own cells -- which
        /// is what keeps the sum periodic over the full texture, not merely over one
        /// octave. Result is roughly in [-1, 1].
        /// </summary>
        public static float Fbm(float px, float py, int width, int height, int seed,
            int octaves, int baseCells, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, ampSum = 0f;
            int cells = Math.Max(1, baseCells);

            for (int o = 0; o < octaves; o++)
            {
                float nx = px * cells / width;
                float ny = py * cells / height;
                sum += Sample(nx, ny, cells, seed + o * 101) * amp;
                ampSum += amp;

                amp *= gain;
                cells = Math.Max(1, (int)Math.Round(cells * lacunarity));
            }

            return ampSum > 0f ? sum / ampSum : 0f;
        }

        /// <summary>Toroidal (wrap-around) distance between two points on a width x height ring.</summary>
        public static float ToroidalDistance(float ax, float ay, float bx, float by, int width, int height)
        {
            float dx = WrapDelta(ax - bx, width);
            float dy = WrapDelta(ay - by, height);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float WrapDelta(float d, int period)
        {
            d = d % period;
            if (d > period / 2f) d -= period;
            if (d < -period / 2f) d += period;
            return d;
        }

        private static int Mod(int a, int m)
        {
            int r = a % m;
            return r < 0 ? r + m : r;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static float Lerp(float a, float b, float t) => a + t * (b - a);

        /// <summary>Deterministic hash of an integer lattice point to a value in [-1, 1].</summary>
        private static float HashValue(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h / (float)uint.MaxValue) * 2f - 1f;
            }
        }
    }
}
