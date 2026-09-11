using System;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class PeriodicNoiseTests
    {
        [Theory]
        [InlineData(0f, 0f, 8, 1)]
        [InlineData(2.3f, 5.7f, 8, 42)]
        [InlineData(-3.1f, 4.4f, 5, 7)]
        [InlineData(0.5f, 0.5f, 16, 99)]
        public void Sample_is_exactly_periodic_over_one_cell_period(float x, float y, int cells, int seed)
        {
            float a = PeriodicNoise.Sample(x, y, cells, seed);
            float b = PeriodicNoise.Sample(x + cells, y, cells, seed);
            float c = PeriodicNoise.Sample(x, y + cells, cells, seed);
            float d = PeriodicNoise.Sample(x + cells, y + cells, cells, seed);

            Assert.Equal(a, b, 4);
            Assert.Equal(a, c, 4);
            Assert.Equal(a, d, 4);
        }

        [Fact]
        public void Sample_is_periodic_over_several_wraps_not_just_one()
        {
            float a = PeriodicNoise.Sample(1.7f, 3.2f, 10, 5);
            float b = PeriodicNoise.Sample(1.7f + 10 * 5, 3.2f - 10 * 3, 10, 5);
            Assert.Equal(a, b, 3);
        }

        [Fact]
        public void Sample_stays_in_expected_range()
        {
            for (int i = 0; i < 500; i++)
            {
                float v = PeriodicNoise.Sample(i * 0.37f, i * 1.11f, 12, 3);
                Assert.InRange(v, -1.0001f, 1.0001f);
                Assert.False(float.IsNaN(v));
            }
        }

        [Fact]
        public void Sample_is_deterministic_for_same_inputs()
        {
            float a = PeriodicNoise.Sample(4.2f, 8.1f, 9, 123);
            float b = PeriodicNoise.Sample(4.2f, 8.1f, 9, 123);
            Assert.Equal(a, b);
        }

        [Fact]
        public void Fbm_is_exactly_periodic_over_one_full_texture_width_and_height()
        {
            const int width = 64, height = 64, seed = 17;

            for (int y = 0; y < height; y += 7)
            {
                for (int x = 0; x < width; x += 5)
                {
                    float a = PeriodicNoise.Fbm(x, y, width, height, seed, octaves: 4, baseCells: 6);
                    float b = PeriodicNoise.Fbm(x + width, y, width, height, seed, octaves: 4, baseCells: 6);
                    float c = PeriodicNoise.Fbm(x, y + height, width, height, seed, octaves: 4, baseCells: 6);
                    float d = PeriodicNoise.Fbm(x + width, y + height, width, height, seed, octaves: 4, baseCells: 6);

                    Assert.Equal(a, b, 4);
                    Assert.Equal(a, c, 4);
                    Assert.Equal(a, d, 4);
                }
            }
        }

        [Fact]
        public void ToroidalDistance_wraps_around_the_shorter_way()
        {
            // Two points near opposite edges of a 100-wide ring are actually
            // close together once you wrap, not far apart.
            float d = PeriodicNoise.ToroidalDistance(1f, 1f, 98f, 1f, 100, 100);
            Assert.True(d < 5f, $"expected wrapped distance < 5, got {d}");
        }
    }
}
