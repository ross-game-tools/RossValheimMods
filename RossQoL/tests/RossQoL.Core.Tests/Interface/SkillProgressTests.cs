using RossQoL.Core.Interface;
using Xunit;

namespace RossQoL.Core.Tests.Interface
{
    public class SkillProgressTests
    {
        [Theory]
        // floor(level + 1) ^ 1.5 * 0.5 + 0.5, the game's own curve.
        [InlineData(0f, 1f)]
        [InlineData(1f, 1.914214f)]
        [InlineData(4f, 6.090170f)]
        [InlineData(99f, 500.5f)]
        public void The_next_level_costs_what_the_game_asks(float level, float expected)
        {
            Assert.Equal(expected, SkillProgress.NextLevelRequirement(level), 3);
        }

        [Fact]
        public void A_fraction_of_a_level_only_counts_up_to_the_next_level()
        {
            // Half of what level 1 asks for reads as half a level.
            float half = SkillProgress.NextLevelRequirement(1f) / 2f;

            Assert.Equal(50f, SkillProgress.PercentOfLevel(half, 1f), 3);
        }

        [Fact]
        public void The_same_raw_gain_is_worth_less_at_a_higher_level()
        {
            float low = SkillProgress.PercentOfLevel(1f, 5f);
            float high = SkillProgress.PercentOfLevel(1f, 80f);

            Assert.True(high < low);
            Assert.True(high > 0f);
        }

        [Fact]
        public void Nothing_gained_is_nothing_shown()
        {
            Assert.Equal(0f, SkillProgress.PercentOfLevel(0f, 10f));
            Assert.Equal(0f, SkillProgress.PercentOfLevel(-1f, 10f));
        }

        [Fact]
        public void A_negative_level_is_treated_as_the_bottom_of_the_curve()
        {
            Assert.Equal(SkillProgress.NextLevelRequirement(0f), SkillProgress.NextLevelRequirement(-5f));
        }

        [Fact]
        public void A_gain_reads_as_the_value_then_the_share_of_a_level()
        {
            Assert.Equal("+12 (3%)", SkillProgress.FormatGain(12f, 3f));
        }

        [Theory]
        // A whole number does not carry a pointless ".0".
        [InlineData(1f, "1")]
        [InlineData(12f, "12")]
        [InlineData(0.3f, "0.3")]
        // One decimal place and no more, whatever the float really is.
        [InlineData(0.25f, "0.3")]
        [InlineData(12.549f, "12.5")]
        [InlineData(1.0000001f, "1")]
        // Real, but smaller than one decimal place can say.
        [InlineData(0.099f, "<0.1")]
        [InlineData(0.04f, "<0.1")]
        [InlineData(0.0001f, "<0.1")]
        [InlineData(0.1f, "0.1")]
        [InlineData(0f, "0")]
        public void The_raw_value_never_prints_a_long_float(float gain, string expected)
        {
            Assert.Equal(expected, SkillProgress.FormatValue(gain));
        }

        [Theory]
        [InlineData(3f, "3%")]
        [InlineData(0.6f, "<1%")]
        // The boundary itself: rounding to a whole 1 is not "less than one".
        [InlineData(0.5f, "<1%")]
        [InlineData(0.999f, "<1%")]
        [InlineData(1f, "1%")]
        [InlineData(0f, "0%")]
        public void A_share_that_rounds_to_nothing_says_less_than_one(float percent, string expected)
        {
            Assert.Equal(expected, SkillProgress.FormatPercent(percent));
        }

        [Fact]
        public void A_tiny_real_gain_reads_as_something_rather_than_nothing()
        {
            // Both halves would otherwise round to zero and say a swing did
            // nothing, which is the opposite of what happened.
            Assert.Equal("+<0.1 (<1%)", SkillProgress.FormatGain(0.02f, 0.01f));
        }
    }
}
