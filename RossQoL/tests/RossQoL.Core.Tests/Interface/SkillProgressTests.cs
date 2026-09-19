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
    }
}
