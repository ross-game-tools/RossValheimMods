using System;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Generates a matched albedo + normal map pair for a drawer material
    /// tier, entirely from a single height field.
    ///
    /// Style target is Valheim's own art, not a distinctive look of our own:
    /// desaturated, low contrast, irregular. Every tier is built the same
    /// way -- a height field (large-scale variation + fine detail + any
    /// tier-specific feature such as plank seams or marble veins) mapped to
    /// a low-saturation hue by a single lightness scalar, plus a normal map
    /// that is a Sobel gradient of that same height field. There is
    /// deliberately no separate material for the recessed panel or handle;
    /// the geometry and lighting already read as distinct.
    ///
    /// Seamlessness is inherited, not bolted on: every noise call below goes
    /// through <see cref="PeriodicNoise"/>, which is periodic on the wrapped
    /// lattice by construction, and the Sobel pass below samples its
    /// neighbours with wrapped indices too. No mirroring anywhere.
    /// </summary>
    public static class DrawerTextureGenerator
    {
        public static TextureData Generate(TextureTier tier, int width, int height, int seed)
        {
            if (width < 2 || height < 2)
                throw new ArgumentException($"Texture must be at least 2x2, got {width}x{height}.");

            float[] heights = BuildHeightField(tier, width, height, seed);

            byte[] albedo = BuildAlbedo(tier, heights, width, height);
            byte[] normal = BuildNormal(heights, width, height, NormalStrength(tier));

            return new TextureData(width, height, albedo, normal);
        }

        // ---- height fields --------------------------------------------------

        private static float[] BuildHeightField(TextureTier tier, int width, int height, int seed)
        {
            switch (tier)
            {
                case TextureTier.Wood: return BuildWoodHeight(width, height, seed);
                case TextureTier.Stone: return BuildStoneHeight(width, height, seed);
                case TextureTier.BlackMarble: return BuildMarbleHeight(width, height, seed);
                default: return BuildWoodHeight(width, height, seed);
            }
        }

        private static float[] BuildWoodHeight(int width, int height, int seed)
        {
            var field = new float[width * height];

            // Plank count is an integer cell count, which is what keeps the
            // seam pattern exactly periodic (see PeriodicNoise.Fbm). The
            // warp below is what keeps the spacing looking irregular rather
            // than a ruled grid -- it nudges each seam's position by a
            // slowly-varying, still-periodic amount.
            const int plankCount = 7;
            // Widened from 0.05 -- "soften the plank-seam definition" --
            // this is the fraction of a plank's own width the dark seam
            // line fades in over, so a bigger number is a softer, more
            // gradual seam rather than a crisp line.
            const float seamWidth = 0.08f;

            // Fewer, smaller, and softer than before (was count: 5,
            // minRadius: 10f, maxRadius: 24f): the user reported knots
            // landing right where the count-text digits sit, hurting
            // readability. This material is applied triplanar (world-
            // position projected), not unwrapped per face, so there is no
            // single fixed pixel region guaranteed to sit "under every
            // label" on every placement -- MakeKnots instead biases
            // placement away from the tile's own centre (see there), the
            // best available proxy for "not right behind the label"
            // without reworking the projection scheme.
            var knots = MakeKnots(seed, width, height, count: 3, minRadius: 6f, maxRadius: 14f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float warp = PeriodicNoise.Fbm(x, y, width, height, seed + 11, octaves: 2,
                        baseCells: plankCount, lacunarity: 2f, gain: 0.6f) * 1.3f;

                    float bandPos = (float)y * plankCount / height + warp;
                    float frac = bandPos - (float)Math.Floor(bandPos);
                    float edgeDist = Math.Min(frac, 1f - frac);
                    float seam = 1f - Clamp01(edgeDist / seamWidth);

                    // Amplitude lowered again, from 0.03 -- "the textures...
                    // look very busy... reduce grain contrast" -- this is
                    // the second reduction of this same knob (was 0.05
                    // originally).
                    float grain = PeriodicNoise.Fbm(x, y, width, height, seed + 23, octaves: 4,
                        baseCells: 48, lacunarity: 2.1f, gain: 0.55f);

                    // Lowered from 0.12 -- "lower the overall variation" --
                    // this is the large-scale patch variation across the
                    // whole plank, independent of grain/seam/knots.
                    float large = PeriodicNoise.Fbm(x, y, width, height, seed + 37, octaves: 3,
                        baseCells: 5, lacunarity: 2f, gain: 0.5f);

                    float knotBump = KnotContribution(knots, x, y, width, height);

                    // Seam weight lowered from 0.22 -- alongside the wider
                    // seamWidth above, this is "soften the plank-seam
                    // definition": a wider AND shallower dark line reads as
                    // a soft, low-relief board edge instead of a hard-
                    // painted stripe. Vanilla furniture does this kind of
                    // detail through geometry/lighting, not high-contrast
                    // albedo -- see the class docstring.
                    float h = 0.55f + large * 0.08f + grain * 0.018f - seam * 0.14f + knotBump;
                    field[y * width + x] = Clamp01(h);
                }
            }

            return field;
        }

        private static float[] BuildStoneHeight(int width, int height, int seed)
        {
            var field = new float[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float blotch = PeriodicNoise.Fbm(x, y, width, height, seed + 5, octaves: 3,
                        baseCells: 6, lacunarity: 2f, gain: 0.5f);

                    float fine = PeriodicNoise.Fbm(x, y, width, height, seed + 61, octaves: 3,
                        baseCells: 34, lacunarity: 2f, gain: 0.5f);

                    // Blotch/fine amplitudes lowered from 0.22/0.07 --
                    // "reduce grain contrast... lower the overall
                    // variation" -- so this reads as a calm, evenly-lit
                    // stone slab rather than a mottled one.
                    float h = 0.5f + blotch * 0.15f + fine * 0.045f;
                    field[y * width + x] = Clamp01(h);
                }
            }

            return field;
        }

        private static float[] BuildMarbleHeight(int width, int height, int seed)
        {
            var field = new float[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float n = PeriodicNoise.Fbm(x, y, width, height, seed + 7, octaves: 3,
                        baseCells: 5, lacunarity: 2.3f, gain: 0.5f);

                    // Ridged: veins sit where the fbm crosses near zero, which
                    // draws thin bright lines through an otherwise flat, dark
                    // field rather than broad marbled blobs. Kept restrained
                    // (small vein weight below) per the brief: Valheim's
                    // black marble is nearly black, not white-veined marble.
                    float ridge = 1f - Math.Abs(n);
                    float vein = (float)Math.Pow(Clamp01((ridge - 0.82f) / 0.18f), 2.0);

                    float speck = PeriodicNoise.Fbm(x, y, width, height, seed + 89, octaves: 2,
                        baseCells: 40, lacunarity: 2f, gain: 0.5f);

                    // Vein/speck amplitudes lowered from 0.30/0.03 -- same
                    // "reduce contrast, lower variation" brief -- the veins
                    // stay visible (a marble tier with no veins at all is
                    // just a flat dark slab) but no longer stand out as
                    // starkly against the near-black base.
                    float h = 0.16f + vein * 0.22f + speck * 0.02f;
                    field[y * width + x] = Clamp01(h);
                }
            }

            return field;
        }

        // ---- sparse knots (wood only) ---------------------------------------

        private readonly struct Knot
        {
            public readonly float X, Y, Radius, Strength;
            public Knot(float x, float y, float radius, float strength)
            {
                X = x; Y = y; Radius = radius; Strength = strength;
            }
        }

        private static Knot[] MakeKnots(int seed, int width, int height, int count, float minRadius, float maxRadius)
        {
            var rng = new Random(seed + 999);
            var knots = new Knot[count];

            // Bias placement away from the tile's own centre -- a rejection
            // sample against a central keep-out box, not a hard rule, so
            // the placement stays randomised rather than becoming an
            // obvious ring. Bounded attempts (not "until it succeeds") so a
            // pathological seed/count combination can never loop forever;
            // falling back to whatever the last attempt landed on is a
            // knot slightly more central than ideal, not a hang.
            float cx = width / 2f, cy = height / 2f;
            float keepOutHalfW = width * 0.22f;
            float keepOutHalfH = height * 0.22f;

            for (int i = 0; i < count; i++)
            {
                float x = 0f, y = 0f;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    x = (float)(rng.NextDouble() * width);
                    y = (float)(rng.NextDouble() * height);
                    bool inCentre = Math.Abs(x - cx) < keepOutHalfW && Math.Abs(y - cy) < keepOutHalfH;
                    if (!inCentre) break;
                }

                float r = minRadius + (float)rng.NextDouble() * (maxRadius - minRadius);
                // Lowered from 0.05-0.11 -- fewer, smaller, and now also
                // softer knots, per the readability report (knots were
                // landing under the count-text digits).
                float s = 0.03f + (float)rng.NextDouble() * 0.03f;
                knots[i] = new Knot(x, y, r, s);
            }
            return knots;
        }

        private static float KnotContribution(Knot[] knots, int x, int y, int width, int height)
        {
            float total = 0f;
            for (int i = 0; i < knots.Length; i++)
            {
                var k = knots[i];
                float d = PeriodicNoise.ToroidalDistance(x, y, k.X, k.Y, width, height);
                if (d >= k.Radius) continue;

                float t = d / k.Radius;
                // Smooth radial falloff (soft, not a hard disc); slightly
                // darker ring near the edge, a touch lighter at the core --
                // "soft knots", not sharp rings.
                float falloff = (float)Math.Pow(1f - t, 2.0);
                total -= k.Strength * falloff;
            }
            return total;
        }

        // ---- albedo -----------------------------------------------------------

        /// <summary>
        /// A fixed, low-saturation hue ratio (each component <= 1, one at exactly
        /// 1) times a single lightness scalar derived from the height field. Every
        /// pixel's colour is a scaling of the same ratio, so saturation cannot
        /// drift away from the tier's chosen value no matter how the lightness
        /// varies -- which is what "same material throughout" and "muted, low
        /// contrast" come down to numerically.
        /// </summary>
        private static (float r, float g, float b) HueRatio(TextureTier tier)
        {
            switch (tier)
            {
                // Re-hued to read as the material each tier is BUILT from --
                // Fine Wood, Stone, Black Marble -- rather than as three
                // shades of the same pale tan. The "more muted" pass before
                // this one lowered saturation across the board and left Wood
                // at (1.0, 0.91, 0.83), which is barely brown, and Stone at
                // (1.0, 0.97, 0.93), which is a WARM cream -- the wrong
                // direction entirely for Valheim's grey stone.
                //
                // Muting is preserved where it was actually asked for: this
                // moves hue, and only lifts saturation on Wood, which had
                // over-corrected past "muted" into "colourless". Stone moves
                // from warm to neutral-cool at essentially the same
                // saturation, so it reads as stone without becoming louder.
                //
                // Each vector is still normalised with one component at
                // exactly 1, so the invariant the rest of this file depends
                // on -- saturation fixed per tier, independent of lightness
                // -- is unchanged.
                case TextureTier.Wood: return (1.0f, 0.84f, 0.66f);
                case TextureTier.Stone: return (0.97f, 0.98f, 1.0f);
                case TextureTier.BlackMarble: return (0.86f, 0.95f, 1.0f);
                default: return (1.0f, 0.80f, 0.56f);
            }
        }

        /// <summary>
        /// The midpoint of a tier's lightness band -- what that tier's
        /// albedo averages out to.
        ///
        /// Exposed so callers deriving a LIGHTER variant of a tier (the
        /// front faceplate, the metal handle) can compute the factor that
        /// reaches a target lightness, instead of carrying a hardcoded
        /// multiplier per tier. Those multipliers were wrong the moment the
        /// tiers were re-hued, and wrong silently -- a factor tuned against
        /// the old pale-tan Wood means something different against a
        /// mid-brown one. Anything derived from this number re-derives
        /// itself when the bands change.
        /// </summary>
        public static float MidLightness(TextureTier tier)
        {
            var (lo, hi) = LightnessRange(tier);
            return (lo + hi) / 2f;
        }

        /// <summary>Lightness band the height field (already in [0, 1]) is mapped into.</summary>
        private static (float min, float max) LightnessRange(TextureTier tier)
        {
            switch (tier)
            {
                // Wood darkened slightly alongside its warmer hue: Fine Wood
                // is a mid-brown, and the old upper bound left it reading as
                // pale pine once the brown was actually there. Stone's band
                // is unchanged -- it was never the problem. Black Marble
                // pulled down a little further, since "black" marble that
                // tops out at 0.34 lightness is really dark grey.
                case TextureTier.Wood: return (0.24f, 0.60f);
                case TextureTier.Stone: return (0.34f, 0.64f);
                case TextureTier.BlackMarble: return (0.04f, 0.30f);
                default: return (0.28f, 0.66f);
            }
        }

        private static byte[] BuildAlbedo(TextureTier tier, float[] heights, int width, int height)
        {
            var (hr, hg, hb) = HueRatio(tier);
            var (lo, hi) = LightnessRange(tier);
            var pixels = new byte[width * height * 4];

            for (int i = 0; i < heights.Length; i++)
            {
                float l = lo + heights[i] * (hi - lo);
                int o = i * 4;
                pixels[o + 0] = ToByte(l * hr);
                pixels[o + 1] = ToByte(l * hg);
                pixels[o + 2] = ToByte(l * hb);
                pixels[o + 3] = 255;
            }

            return pixels;
        }

        // ---- normal map ---------------------------------------------------------

        private static float NormalStrength(TextureTier tier)
        {
            switch (tier)
            {
                case TextureTier.Wood: return 3.0f;
                case TextureTier.Stone: return 1.6f;
                case TextureTier.BlackMarble: return 1.1f;
                default: return 2.0f;
            }
        }

        /// <summary>
        /// Sobel gradient of the height field, encoded as a tangent-space normal
        /// map. Neighbour lookups wrap (mod width/height), so the normal map
        /// inherits the same seamlessness as the height field it is derived
        /// from -- there is no separate seam to fix here.
        /// </summary>
        private static byte[] BuildNormal(float[] heights, int width, int height, float strength)
        {
            var pixels = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float h00 = At(heights, x - 1, y - 1, width, height);
                    float h10 = At(heights, x, y - 1, width, height);
                    float h20 = At(heights, x + 1, y - 1, width, height);
                    float h01 = At(heights, x - 1, y, width, height);
                    float h21 = At(heights, x + 1, y, width, height);
                    float h02 = At(heights, x - 1, y + 1, width, height);
                    float h12 = At(heights, x, y + 1, width, height);
                    float h22 = At(heights, x + 1, y + 1, width, height);

                    float gx = (h20 + 2f * h21 + h22) - (h00 + 2f * h01 + h02);
                    float gy = (h02 + 2f * h12 + h22) - (h00 + 2f * h10 + h20);

                    float nx = -gx * strength;
                    float ny = -gy * strength;
                    float nz = 1f;

                    float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    nx /= len; ny /= len; nz /= len;

                    int o = (y * width + x) * 4;
                    pixels[o + 0] = ToByte(nx * 0.5f + 0.5f);
                    pixels[o + 1] = ToByte(ny * 0.5f + 0.5f);
                    pixels[o + 2] = ToByte(nz * 0.5f + 0.5f);
                    pixels[o + 3] = 255;
                }
            }

            return pixels;
        }

        private static float At(float[] field, int x, int y, int width, int height)
        {
            int wx = ((x % width) + width) % width;
            int wy = ((y % height) + height) % height;
            return field[wy * width + wx];
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static byte ToByte(float v)
        {
            v = Clamp01(v);
            return (byte)Math.Round(v * 255f);
        }
    }
}
