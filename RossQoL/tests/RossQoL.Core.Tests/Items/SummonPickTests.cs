using System.Collections.Generic;
using RossQoL.Core.Items;
using Xunit;

namespace RossQoL.Core.Tests.Items
{
    public class SummonPickTests
    {
        private static SummonKind Out(int following, float weakest) => new SummonKind(true, following, weakest);

        private static SummonKind None() => new SummonKind(true, 0, 1f);

        private static SummonKind Capped(int following = 0, float weakest = 1f) => new SummonKind(false, following, weakest);

        /// <summary>A "random" source that records the range it was asked for and answers a fixed roll.</summary>
        private sealed class Dice
        {
            private readonly int _roll;
            public Dice(int roll) => _roll = roll;
            public List<int> Asked { get; } = new List<int>();
            public int Roll(int n) { Asked.Add(n); return _roll; }
        }

        [Fact]
        public void A_kind_you_have_none_of_comes_first_even_over_a_badly_wounded_one()
        {
            var kinds = new[] { Out(1, 0.1f), None(), Out(1, 1f), Out(1, 1f) };
            Assert.Equal(1, SummonPick.Choose(kinds, new Dice(0).Roll));
        }

        [Fact]
        public void Several_missing_kinds_are_chosen_between_at_random()
        {
            var kinds = new[] { None(), Out(1, 0.2f), None(), None() };
            var dice = new Dice(2);

            Assert.Equal(3, SummonPick.Choose(kinds, dice.Roll));
            Assert.Equal(new[] { 3 }, dice.Asked);
        }

        [Fact]
        public void With_every_kind_out_the_most_wounded_kind_is_raised()
        {
            var kinds = new[] { Out(1, 0.9f), Out(1, 0.4f), Out(1, 0.7f), Out(1, 1f) };
            var dice = new Dice(0);

            Assert.Equal(1, SummonPick.Choose(kinds, dice.Roll));
            Assert.Empty(dice.Asked);
        }

        [Fact]
        public void All_at_full_health_falls_back_to_random_across_every_kind()
        {
            var kinds = new[] { Out(1, 1f), Out(1, 1f), Out(1, 1f), Out(1, 1f) };
            var dice = new Dice(2);

            Assert.Equal(2, SummonPick.Choose(kinds, dice.Roll));
            Assert.Equal(new[] { 4 }, dice.Asked);
        }

        [Fact]
        public void Only_the_kinds_tied_for_most_wounded_are_rolled_between()
        {
            var kinds = new[] { Out(1, 1f), Out(1, 0.5f), Out(1, 0.5f + SummonPick.HealthTieTolerance / 2f), Out(1, 0.8f) };
            var dice = new Dice(1);

            Assert.Equal(2, SummonPick.Choose(kinds, dice.Roll));
            Assert.Equal(new[] { 2 }, dice.Asked);
        }

        [Fact]
        public void A_kind_the_staff_would_refuse_is_never_picked()
        {
            var kinds = new[] { Capped(), Out(1, 1f), Out(1, 0.3f), Capped(1, 0.05f) };
            Assert.Equal(2, SummonPick.Choose(kinds, new Dice(0).Roll));
        }

        [Fact]
        public void Nothing_available_leaves_the_cast_to_vanilla()
        {
            Assert.Equal(-1, SummonPick.Choose(new[] { Capped(), Capped(1, 0.5f) }, new Dice(0).Roll));
            Assert.Equal(-1, SummonPick.Choose(new SummonKind[0], new Dice(0).Roll));
            Assert.Equal(-1, SummonPick.Choose(null, new Dice(0).Roll));
        }

        [Fact]
        public void An_out_of_range_roll_cannot_index_past_the_candidates()
        {
            var kinds = new[] { None(), None() };
            Assert.Equal(0, SummonPick.Choose(kinds, new Dice(7).Roll));
            Assert.Equal(0, SummonPick.Choose(kinds, new Dice(-1).Roll));
        }

        [Theory]
        [InlineData(1, 1, 1)]   // Random.Range(1, 1) returns 1
        [InlineData(1, 2, 1)]   // upper bound exclusive: always 1
        [InlineData(1, 3, 2)]
        [InlineData(2, 2, 2)]
        public void Most_per_cast_follows_the_int_Range_bounds(int min, int max, int expected)
        {
            Assert.Equal(expected, SummonPick.MostPerCast(min, max));
        }
    }
}
