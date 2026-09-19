using RossQoL.Core.Crafting;
using Xunit;

namespace RossQoL.Core.Tests.Crafting
{
    public class CraftPaymentTests
    {
        [Fact]
        public void Nothing_held_anywhere_chooses_no_tier()
        {
            var choice = CraftPayment.ChooseTier(new[] { 0, 0, 0 }, 5);
            Assert.Equal(0, choice.Quality);
            Assert.Equal(0, choice.Available);
            Assert.False(choice.Covers(1));
        }

        [Fact]
        public void A_null_tier_list_chooses_no_tier()
        {
            var choice = CraftPayment.ChooseTier(null, 5);
            Assert.Equal(0, choice.Quality);
            Assert.Equal(0, choice.Available);
        }

        [Fact]
        public void The_lowest_tier_that_covers_the_cost_is_the_one_charged()
        {
            // Quality 1 has exactly enough, quality 3 has more: the craft is
            // charged the plain stack, not the better one.
            var choice = CraftPayment.ChooseTier(new[] { 0, 10, 0, 40 }, 10);
            Assert.Equal(1, choice.Quality);
        }

        [Fact]
        public void Affordability_is_the_fullest_single_tier_not_the_sum()
        {
            var choice = CraftPayment.ChooseTier(new[] { 0, 4, 4, 4 }, 10);
            Assert.Equal(4, choice.Available);
            Assert.False(choice.Covers(10));
        }

        [Fact]
        public void When_no_tier_covers_it_the_fullest_tier_is_charged()
        {
            var choice = CraftPayment.ChooseTier(new[] { 0, 2, 7, 3 }, 10);
            Assert.Equal(2, choice.Quality);
            Assert.Equal(7, choice.Available);
        }

        [Fact]
        public void Equally_full_tiers_charge_the_lower_one()
        {
            var choice = CraftPayment.ChooseTier(new[] { 0, 5, 5 }, 10);
            Assert.Equal(1, choice.Quality);
        }

        [Fact]
        public void A_cost_of_nothing_still_names_the_fullest_tier()
        {
            var choice = CraftPayment.ChooseTier(new[] { 0, 1, 9 }, 0);
            Assert.Equal(2, choice.Quality);
            Assert.Equal(9, choice.Available);
        }

        [Fact]
        public void The_pack_pays_first_and_the_containers_owe_the_rest()
        {
            Assert.Equal(7, CraftPayment.Owing(10, 3));
            Assert.Equal(0, CraftPayment.Owing(10, 10));
            Assert.Equal(0, CraftPayment.Owing(10, 12));
        }

        [Fact]
        public void A_cost_of_nothing_is_never_owed()
        {
            Assert.Equal(0, CraftPayment.Owing(0, 0));
            Assert.Equal(0, CraftPayment.Owing(-3, 0));
        }

        [Fact]
        public void A_pack_that_grew_during_the_craft_counts_as_having_paid_nothing()
        {
            // The crafted item lands in the pack before the charging step, so
            // a same-named ingredient can read as negative. Charge in full.
            Assert.Equal(10, CraftPayment.Owing(10, -5));
            Assert.Equal(10, CraftPayment.Shortfall(10, -5, 0));
        }

        [Fact]
        public void Containers_covering_what_is_owed_leave_no_shortfall()
        {
            Assert.Equal(0, CraftPayment.Shortfall(10, 3, 7));
            Assert.Equal(0, CraftPayment.Shortfall(10, 3, 9));
        }

        [Fact]
        public void What_the_containers_could_not_pay_is_the_shortfall()
        {
            Assert.Equal(4, CraftPayment.Shortfall(10, 3, 3));
            Assert.Equal(7, CraftPayment.Shortfall(10, 3, 0));
        }

        [Fact]
        public void A_negative_take_counts_as_nothing_taken()
        {
            Assert.Equal(7, CraftPayment.Shortfall(10, 3, -4));
        }

        [Fact]
        public void The_shortfall_is_never_larger_than_the_cost()
        {
            Assert.Equal(10, CraftPayment.Shortfall(10, 0, 0));
            Assert.Equal(0, CraftPayment.Shortfall(0, 0, 0));
        }
    }
}
