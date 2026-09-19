using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class CorpseRunStrengthTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(50f, 0f)]
        [InlineData(51f, 0.001f)]
        [InlineData(550f, 0.5f)]
        [InlineData(1050f, 1f)]
        [InlineData(5000f, 1f)]
        public void Strength_ramps_beyond_the_minimum_distance(float distance, float expected)
        {
            Assert.Equal(expected, CorpseRunStrength.For(distance, 50f, 1000f), 3);
        }

        [Fact]
        public void A_zero_or_negative_ramp_is_all_or_nothing_rather_than_a_divide_by_zero()
        {
            Assert.Equal(0f, CorpseRunStrength.For(50f, 50f, 0f), 3);
            Assert.Equal(1f, CorpseRunStrength.For(51f, 50f, 0f), 3);
            Assert.Equal(1f, CorpseRunStrength.For(51f, 50f, -100f), 3);
        }

        [Fact]
        public void A_negative_minimum_distance_is_treated_as_none()
        {
            Assert.Equal(0.5f, CorpseRunStrength.For(500f, -10f, 1000f), 3);
        }

        [Fact]
        public void Full_strength_gives_the_configured_bonus_and_nothing_at_rest()
        {
            Assert.Equal(1f, CorpseRunStrength.RegenMultiplier(0f, 1f), 3);
            Assert.Equal(1.5f, CorpseRunStrength.RegenMultiplier(0.5f, 1f), 3);
            Assert.Equal(2f, CorpseRunStrength.RegenMultiplier(1f, 1f), 3);

            Assert.Equal(0f, CorpseRunStrength.DrainModifier(0f, 0.5f), 3);
            Assert.Equal(-0.25f, CorpseRunStrength.DrainModifier(0.5f, 0.5f), 3);
            Assert.Equal(-0.5f, CorpseRunStrength.DrainModifier(1f, 0.5f), 3);
        }

        [Fact]
        public void A_drain_reduction_can_never_pay_stamina_back()
        {
            Assert.Equal(-1f, CorpseRunStrength.DrainModifier(1f, 5f), 3);
        }

        [Fact]
        public void A_zero_baseline_leaves_the_ramp_unchanged()
        {
            Assert.Equal(0f, CorpseRunStrength.WithBaseline(0f, 0f), 3);
            Assert.Equal(0.5f, CorpseRunStrength.WithBaseline(0.5f, 0f), 3);
            Assert.Equal(1f, CorpseRunStrength.WithBaseline(1f, 0f), 3);
        }

        [Fact]
        public void A_baseline_of_one_pins_every_strength_to_full()
        {
            Assert.Equal(1f, CorpseRunStrength.WithBaseline(0f, 1f), 3);
            Assert.Equal(1f, CorpseRunStrength.WithBaseline(0.5f, 1f), 3);
        }

        [Fact]
        public void A_mid_range_ramp_lands_strictly_between_the_baseline_and_full_strength()
        {
            // The whole point of the remap over a clamp: distances inside
            // the old floor's reach must still move, not sit pinned flat.
            float result = CorpseRunStrength.WithBaseline(0.5f, 0.5f);
            Assert.True(result > 0.5f && result < 1f, $"expected strictly between 0.5 and 1, got {result}");
            Assert.Equal(0.75f, result, 3);
        }

        [Fact]
        public void A_baseline_spans_the_full_range_from_itself_to_full_strength()
        {
            Assert.Equal(0.25f, CorpseRunStrength.WithBaseline(0f, 0.25f), 3);
            Assert.Equal(1f, CorpseRunStrength.WithBaseline(1f, 0.25f), 3);
            Assert.Equal(0.625f, CorpseRunStrength.WithBaseline(0.5f, 0.25f), 3);
        }

        [Fact]
        public void A_baseline_outside_zero_to_one_is_clamped_rather_than_trusted()
        {
            Assert.Equal(0f, CorpseRunStrength.WithBaseline(0f, -1f), 3);
            Assert.Equal(1f, CorpseRunStrength.WithBaseline(0f, 5f), 3);
        }

        [Fact]
        public void The_result_is_never_below_the_baseline_nor_above_full_strength()
        {
            Assert.Equal(0.3f, CorpseRunStrength.WithBaseline(0f, 0.3f), 3);
            Assert.Equal(1f, CorpseRunStrength.WithBaseline(1f, 0.3f), 3);
        }
    }
}
