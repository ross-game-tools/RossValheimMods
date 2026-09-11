using System;
using System.Linq;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class DrawerTextureGeneratorTests
    {
        private static readonly TextureTier[] AllTiers =
            { TextureTier.Wood, TextureTier.Stone, TextureTier.BlackMarble };

        // ---- shape ---------------------------------------------------------

        [Theory]
        [MemberData(nameof(TiersAndSeeds))]
        public void Output_arrays_are_width_times_height_times_4(TextureTier tier, int seed)
        {
            var tex = DrawerTextureGenerator.Generate(tier, 64, 32, seed);
            Assert.Equal(64 * 32 * 4, tex.Albedo.Length);
            Assert.Equal(64 * 32 * 4, tex.Normal.Length);
        }

        public static TheoryData<TextureTier, int> TiersAndSeeds()
        {
            var data = new TheoryData<TextureTier, int>();
            foreach (var tier in AllTiers)
            {
                data.Add(tier, 1);
                data.Add(tier, 999);
            }
            return data;
        }

        // ---- determinism -----------------------------------------------------

        [Theory]
        [MemberData(nameof(Tiers))]
        public void Same_seed_produces_byte_identical_output(TextureTier tier)
        {
            var a = DrawerTextureGenerator.Generate(tier, 128, 128, seed: 4242);
            var b = DrawerTextureGenerator.Generate(tier, 128, 128, seed: 4242);

            Assert.Equal(a.Albedo, b.Albedo);
            Assert.Equal(a.Normal, b.Normal);
        }

        [Theory]
        [MemberData(nameof(Tiers))]
        public void Different_seeds_produce_different_output(TextureTier tier)
        {
            var a = DrawerTextureGenerator.Generate(tier, 128, 128, seed: 1);
            var b = DrawerTextureGenerator.Generate(tier, 128, 128, seed: 2);

            Assert.NotEqual(a.Albedo, b.Albedo);
            // The normal map is derived from the same height field as the
            // albedo (Sobel gradient of it) -- if it ever stopped depending on
            // the seed (e.g. a stray hardcoded height field, or a normal pass
            // that reads a cached buffer from a previous call), this is the
            // test that would catch it; the albedo assertion above would not.
            Assert.NotEqual(a.Normal, b.Normal);
        }

        public static TheoryData<TextureTier> Tiers()
        {
            var data = new TheoryData<TextureTier>();
            foreach (var tier in AllTiers) data.Add(tier);
            return data;
        }

        // ---- seamlessness: the whole point of generating on a wrapped lattice ---

        [Theory]
        [MemberData(nameof(Tiers))]
        public void Opposite_horizontal_edges_are_continuous_not_merely_similar(TextureTier tier)
        {
            const int w = 256, h = 256;
            var tex = DrawerTextureGenerator.Generate(tier, w, h, seed: 55);

            // The wrap step (column w-1 -> column 0, as they sit when the
            // texture repeats) should look like any other single-pixel step
            // across the image, not an outlier. This bounds a seam from being
            // too LARGE. It does not, by itself, rule out mirroring: a
            // mirrored image has avgWrapStep == 0, which trivially satisfies
            // "not too large". Ruling out mirroring specifically is what
            // Texture_is_not_mirrored_horizontally/vertically below does.
            double interiorStepSum = 0;
            int interiorSteps = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    interiorStepSum += ChannelDiff(tex.Albedo, w, x, y, x + 1, y);
                    interiorSteps++;
                }
            }
            double avgInteriorStep = interiorStepSum / interiorSteps;

            double wrapStepSum = 0;
            for (int y = 0; y < h; y++)
                wrapStepSum += ChannelDiff(tex.Albedo, w, w - 1, y, 0, y);
            double avgWrapStep = wrapStepSum / h;

            Assert.True(avgWrapStep < avgInteriorStep * 3.0,
                $"{tier}: wrap-around step ({avgWrapStep:F2}) is much larger than the typical " +
                $"interior step ({avgInteriorStep:F2}); the texture has a visible seam.");
        }

        [Theory]
        [MemberData(nameof(Tiers))]
        public void Opposite_vertical_edges_are_continuous_not_merely_similar(TextureTier tier)
        {
            const int w = 256, h = 256;
            var tex = DrawerTextureGenerator.Generate(tier, w, h, seed: 55);

            double interiorStepSum = 0;
            int interiorSteps = 0;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h - 1; y++)
                {
                    interiorStepSum += ChannelDiff(tex.Albedo, w, x, y, x, y + 1);
                    interiorSteps++;
                }
            }
            double avgInteriorStep = interiorStepSum / interiorSteps;

            double wrapStepSum = 0;
            for (int x = 0; x < w; x++)
                wrapStepSum += ChannelDiff(tex.Albedo, w, x, h - 1, x, 0);
            double avgWrapStep = wrapStepSum / w;

            Assert.True(avgWrapStep < avgInteriorStep * 3.0,
                $"{tier}: wrap-around step ({avgWrapStep:F2}) is much larger than the typical " +
                $"interior step ({avgInteriorStep:F2}); the texture has a visible seam.");
        }

        private static double ChannelDiff(byte[] rgba, int width, int x0, int y0, int x1, int y1)
        {
            int o0 = (y0 * width + x0) * 4;
            int o1 = (y1 * width + x1) * 4;
            int dr = Math.Abs(rgba[o0] - rgba[o1]);
            int dg = Math.Abs(rgba[o0 + 1] - rgba[o1 + 1]);
            int db = Math.Abs(rgba[o0 + 2] - rgba[o1 + 2]);
            return (dr + dg + db) / 3.0;
        }

        // ---- seamlessness must not be faked by mirroring ------------------------

        // Mirroring is the standard cheap way to fake a tileable texture: it
        // makes both edges identical (avgWrapStep == 0 above, which trivially
        // satisfies "not too large") and it tiles perfectly, but it is
        // obviously symmetric in game. These tests reject it directly, as a
        // check independent of the continuity checks above -- they test a
        // different property (absence of symmetry) rather than a tighter bound
        // on the same one.
        //
        // Note this is NOT a rare-coincidence check: colour here comes from a
        // smoothly-varying, quantized-to-byte height field, so a pixel and an
        // unrelated pixel elsewhere in the same texture land on the identical
        // byte triple far more often than three independent random bytes
        // would (measured on the real generator below: roughly 5-8% of
        // pixels happen to match their "mirror partner" position purely by
        // this quantization, for every tier). A mirrored image, by contrast,
        // matches at EVERY pixel -- 100% -- because it is the same data
        // reflected, not merely drawn from the same smooth palette. 50% sits
        // comfortably above the measured ~5-8% natural coincidence rate and
        // comfortably below 100%, so it cleanly separates "smoothly varying
        // but independent" from "literally mirrored" without being a
        // hair-trigger on the former.
        private const double MaxMirrorMatchFraction = 0.5;

        [Theory]
        [MemberData(nameof(Tiers))]
        public void Texture_is_not_mirrored_horizontally(TextureTier tier)
        {
            const int w = 128, h = 128;
            var tex = DrawerTextureGenerator.Generate(tier, w, h, seed: 55);

            double frac = MirrorMatchFraction(tex.Albedo, w, h, horizontal: true);
            Assert.True(frac < MaxMirrorMatchFraction,
                $"{tier}: {frac:P1} of pixels exactly match their horizontal mirror partner -- " +
                "this looks mirrored, not independently generated on both sides.");
        }

        [Theory]
        [MemberData(nameof(Tiers))]
        public void Texture_is_not_mirrored_vertically(TextureTier tier)
        {
            const int w = 128, h = 128;
            var tex = DrawerTextureGenerator.Generate(tier, w, h, seed: 55);

            double frac = MirrorMatchFraction(tex.Albedo, w, h, horizontal: false);
            Assert.True(frac < MaxMirrorMatchFraction,
                $"{tier}: {frac:P1} of pixels exactly match their vertical mirror partner -- " +
                "this looks mirrored, not independently generated on both sides.");
        }

        // Proves the two tests above can actually fail: builds a buffer that
        // genuinely IS mirrored (right half is a reversed copy of the left,
        // random per-pixel colour) and confirms MirrorMatchFraction reports it
        // as fully mirrored on the mirrored axis, while NOT flagging the other
        // axis -- so the check is both sensitive and axis-specific, not a
        // tautology that always reports "mirrored".
        [Fact]
        public void Mirror_detection_catches_a_deliberately_horizontally_mirrored_buffer()
        {
            var rgba = BuildMirroredBuffer(64, 64, horizontal: true, seed: 1);

            Assert.Equal(1.0, MirrorMatchFraction(rgba, 64, 64, horizontal: true));
            Assert.True(MirrorMatchFraction(rgba, 64, 64, horizontal: false) < MaxMirrorMatchFraction);
        }

        [Fact]
        public void Mirror_detection_catches_a_deliberately_vertically_mirrored_buffer()
        {
            var rgba = BuildMirroredBuffer(64, 64, horizontal: false, seed: 2);

            Assert.Equal(1.0, MirrorMatchFraction(rgba, 64, 64, horizontal: false));
            Assert.True(MirrorMatchFraction(rgba, 64, 64, horizontal: true) < MaxMirrorMatchFraction);
        }

        /// <summary>Fraction of pixels whose RGB exactly matches their mirror-partner pixel's RGB.</summary>
        private static double MirrorMatchFraction(byte[] rgba, int width, int height, bool horizontal)
        {
            int total = 0, matches = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int mx = horizontal ? width - 1 - x : x;
                    int my = horizontal ? y : height - 1 - y;

                    int o = (y * width + x) * 4;
                    int mo = (my * width + mx) * 4;

                    total++;
                    if (rgba[o] == rgba[mo] && rgba[o + 1] == rgba[mo + 1] && rgba[o + 2] == rgba[mo + 2])
                        matches++;
                }
            }
            return matches / (double)total;
        }

        /// <summary>Builds an RGBA buffer with random colour on one half, mirrored onto the other.</summary>
        private static byte[] BuildMirroredBuffer(int width, int height, bool horizontal, int seed)
        {
            var rgba = new byte[width * height * 4];
            var rng = new Random(seed);

            int halfW = horizontal ? width / 2 : width;
            int halfH = horizontal ? height : height / 2;

            for (int y = 0; y < halfH; y++)
            {
                for (int x = 0; x < halfW; x++)
                {
                    byte r = (byte)rng.Next(256), g = (byte)rng.Next(256), b = (byte)rng.Next(256);

                    int mx = horizontal ? width - 1 - x : x;
                    int my = horizontal ? y : height - 1 - y;

                    int o = (y * width + x) * 4;
                    int mo = (my * width + mx) * 4;

                    rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = 255;
                    rgba[mo] = r; rgba[mo + 1] = g; rgba[mo + 2] = b; rgba[mo + 3] = 255;
                }
            }
            return rgba;
        }

        // ---- style guardrails: "vanilla-like" as numbers ------------------------

        // A single shared ceiling across tiers is decorative for whichever
        // tiers sit far below it -- Stone and BlackMarble measured at ~0.07
        // would have had to become five times MORE saturated before a shared
        // 0.35 ceiling caught anything. Per-tier thresholds close to what is
        // actually measured are what makes the guardrail mean something for
        // every tier, not just the one that happens to be closest to it.
        //
        // Measured on the shipped generator at 128x128, seed 7. Saturation is
        // an exact constant per tier by construction -- HueRatio returns a
        // vector with one component at exactly 1, and every pixel is that
        // vector times a lightness scalar, so (max-min)/max is identical for
        // every pixel -- which is why these are round numbers and why they do
        // not vary run to run or with resolution.
        //
        // Raised for Wood and BlackMarble in the tier-matching pass: the
        // tiers were re-hued to read as the material each is built from,
        // which necessarily costs saturation headroom. The ceiling still
        // means something for each tier because it tracks what that tier
        // actually measures -- a single shared ceiling would now be slack
        // enough for Stone to turn bright orange without failing:
        //   Wood         meanSat = 0.3400   ceiling 0.44 (29% headroom)
        //   Stone        meanSat = 0.0300   ceiling 0.04 (33% headroom)
        //   BlackMarble  meanSat = 0.1400   ceiling 0.18 (29% headroom)
        //
        // Stone's ceiling TIGHTENED, from 0.09 to 0.04. It is the one tier
        // that should read as a neutral grey, and it previously carried a
        // warm cream hue that this pass removed; a ceiling left at 0.09
        // would happily admit that mistake back.
        private static double MaxMeanSaturation(TextureTier tier)
        {
            switch (tier)
            {
                case TextureTier.Wood: return 0.44;
                case TextureTier.Stone: return 0.04;
                case TextureTier.BlackMarble: return 0.18;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, null);
            }
        }

        // Measured value (HSV V = max channel / 255) range on the same
        // generation as above. Contrast is what the "textures look busy"
        // report was about, and this pass did not relax any of it -- Wood
        // and BlackMarble both narrowed slightly, as a side effect of their
        // lightness bands tightening alongside the re-hue:
        //   Wood         range = 0.0980   ceiling 0.14 (43% headroom)
        //   Stone        range = 0.0824   ceiling 0.11 (33% headroom)
        //   BlackMarble  range = 0.0630   ceiling 0.10 (59% headroom)
        //
        // Ceilings deliberately left where they were rather than re-tightened
        // around the new figures. They are a "do not get busier than the
        // version that was signed off" guardrail, and that version's numbers
        // are these ceilings; ratcheting them down every time a pass happens
        // to land lower would eventually fail a change nobody objected to.
        private static double MaxValueRange(TextureTier tier)
        {
            switch (tier)
            {
                case TextureTier.Wood: return 0.14;
                case TextureTier.Stone: return 0.11;
                case TextureTier.BlackMarble: return 0.10;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, null);
            }
        }

        [Theory]
        [MemberData(nameof(Tiers))]
        public void Mean_saturation_is_low_desaturated_like_vanilla_art(TextureTier tier)
        {
            var tex = DrawerTextureGenerator.Generate(tier, 128, 128, seed: 7);
            double meanSat = MeanSaturation(tex.Albedo);
            double ceiling = MaxMeanSaturation(tier);
            Assert.True(meanSat < ceiling,
                $"{tier}: mean saturation {meanSat:F3} is not below {ceiling:F2} -- looks too colourful for vanilla-style art.");
        }

        [Theory]
        [MemberData(nameof(Tiers))]
        public void Value_range_is_narrow_low_contrast_like_vanilla_art(TextureTier tier)
        {
            var tex = DrawerTextureGenerator.Generate(tier, 128, 128, seed: 7);
            var (min, max) = ValueRange(tex.Albedo);
            double ceiling = MaxValueRange(tier);
            Assert.True(max - min < ceiling,
                $"{tier}: value range {max - min:F3} (min {min:F3}, max {max:F3}) is not below {ceiling:F2} -- looks too high-contrast for vanilla-style art.");
        }

        private static double MeanSaturation(byte[] rgba)
        {
            double sum = 0;
            int n = rgba.Length / 4;
            for (int i = 0; i < n; i++)
            {
                int o = i * 4;
                float r = rgba[o] / 255f, g = rgba[o + 1] / 255f, b = rgba[o + 2] / 255f;
                float max = Math.Max(r, Math.Max(g, b));
                float min = Math.Min(r, Math.Min(g, b));
                sum += max <= 0.0001f ? 0 : (max - min) / max;
            }
            return sum / n;
        }

        private static (double min, double max) ValueRange(byte[] rgba)
        {
            double min = 1, max = 0;
            int n = rgba.Length / 4;
            for (int i = 0; i < n; i++)
            {
                int o = i * 4;
                float v = Math.Max(rgba[o], Math.Max(rgba[o + 1], rgba[o + 2])) / 255f;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return (min, max);
        }

        // ---- data sanity: no NaN, no out-of-range channels ----------------------

        [Theory]
        [MemberData(nameof(Tiers))]
        public void Normal_map_vectors_are_unit_length_and_finite(TextureTier tier)
        {
            const int w = 32, h = 32;
            var tex = DrawerTextureGenerator.Generate(tier, w, h, seed: 3);

            for (int i = 0; i < w * h; i++)
            {
                int o = i * 4;
                float nx = tex.Normal[o] / 255f * 2f - 1f;
                float ny = tex.Normal[o + 1] / 255f * 2f - 1f;
                float nz = tex.Normal[o + 2] / 255f * 2f - 1f;

                Assert.False(float.IsNaN(nx) || float.IsNaN(ny) || float.IsNaN(nz));

                float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                Assert.InRange(len, 0.9, 1.1);
                // Encoded from a height field with a positive-Z bias, so the
                // surface should always face generally outward.
                Assert.True(nz > 0f, $"{tier}: normal at pixel {i} faces backward (nz={nz}).");
            }
        }

        [Theory]
        [MemberData(nameof(Tiers))]
        public void All_channel_bytes_are_in_range_and_alpha_is_opaque(TextureTier tier)
        {
            var tex = DrawerTextureGenerator.Generate(tier, 32, 32, seed: 9);

            // byte[] cannot go out of [0,255] by type, so this checks the
            // meaningful invariant instead: alpha must be fully opaque on
            // both maps (nothing here should ever be treated as transparent).
            for (int i = 3; i < tex.Albedo.Length; i += 4)
                Assert.Equal(255, tex.Albedo[i]);
            for (int i = 3; i < tex.Normal.Length; i += 4)
                Assert.Equal(255, tex.Normal[i]);
        }

        [Fact]
        public void Rejects_degenerate_dimensions()
        {
            Assert.Throws<ArgumentException>(() => DrawerTextureGenerator.Generate(TextureTier.Wood, 1, 64, 1));
            Assert.Throws<ArgumentException>(() => DrawerTextureGenerator.Generate(TextureTier.Wood, 64, 0, 1));
        }
    }
}
