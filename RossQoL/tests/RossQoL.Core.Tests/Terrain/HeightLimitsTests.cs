using RossQoL.Core.Terrain;
using Xunit;

namespace RossQoL.Core.Tests.Terrain
{
    public class HeightLimitsTests
    {
        [Fact]
        public void Off_gives_vanillas_eight_metres_whatever_is_configured()
        {
            Assert.Equal(8f, HeightLimits.Effective(active: false, configured: 200f));
            Assert.Equal(8f, HeightLimits.Effective(active: false, configured: 1f));
        }

        [Fact]
        public void On_gives_the_configured_limit()
        {
            Assert.Equal(200f, HeightLimits.Effective(active: true, configured: 200f));
            Assert.Equal(20f, HeightLimits.Effective(active: true, configured: 20f));
        }

        [Fact]
        public void On_may_also_be_stricter_than_vanilla()
        {
            Assert.Equal(3f, HeightLimits.Effective(active: true, configured: 3f));
        }

        [Theory]
        [InlineData(0f, 1f)]
        [InlineData(-50f, 1f)]
        [InlineData(500f, 200f)]
        public void Out_of_range_values_are_clamped(float configured, float expected)
        {
            Assert.Equal(expected, HeightLimits.Effective(active: true, configured: configured));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void Unusable_values_fall_back_to_vanilla(float configured)
        {
            Assert.Equal(8f, HeightLimits.Effective(active: true, configured: configured));
        }
    }
}
