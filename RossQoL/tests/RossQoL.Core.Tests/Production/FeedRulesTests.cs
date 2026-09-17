using RossQoL.Core.Production;
using Xunit;

namespace RossQoL.Core.Tests.Production
{
    public class FeedRulesTests
    {
        [Fact]
        public void Amounts_parse_name_and_number()
        {
            var amounts = FeedRules.ParseAmounts("Wood:50, Barley:20");

            Assert.Equal(50, amounts["Wood"]);
            Assert.Equal(20, amounts["Barley"]);
            Assert.Equal(2, amounts.Count);
        }

        [Fact]
        public void Amounts_ignore_case_and_spacing()
        {
            var amounts = FeedRules.ParseAmounts("  wood : 50 ");

            Assert.Equal(50, amounts["WOOD"]);
        }

        [Fact]
        public void Amounts_skip_entries_that_are_not_a_name_and_a_number()
        {
            var amounts = FeedRules.ParseAmounts("Wood, Barley:, :20, Iron:abc, Coal:-5, Stone:0");

            Assert.False(amounts.ContainsKey("Wood"));
            Assert.False(amounts.ContainsKey("Barley"));
            Assert.False(amounts.ContainsKey("Iron"));
            Assert.False(amounts.ContainsKey("Coal"));
            Assert.Equal(0, amounts["Stone"]);
        }

        [Fact]
        public void Amounts_of_nothing_are_empty_not_null()
        {
            Assert.Empty(FeedRules.ParseAmounts(null));
            Assert.Empty(FeedRules.ParseAmounts("   "));
        }

        [Fact]
        public void Repeated_name_keeps_the_last_value()
        {
            Assert.Equal(9, FeedRules.ParseAmounts("Wood:3, Wood:9")["Wood"]);
        }

        [Fact]
        public void Names_parse_as_a_set()
        {
            var names = FeedRules.ParseNames(" Wood , FineWood ,, RoundLog ");

            Assert.Equal(3, names.Count);
            Assert.Contains("roundlog", names);
        }

        [Fact]
        public void An_empty_allow_list_allows_everything()
        {
            Assert.True(FeedRules.Allowed(FeedRules.ParseNames(""), "Wood"));
            Assert.True(FeedRules.Allowed(null, "Wood"));
        }

        [Fact]
        public void An_allow_list_allows_only_what_it_names()
        {
            var fuels = FeedRules.ParseNames("Wood, FineWood");

            Assert.True(FeedRules.Allowed(fuels, "wood"));
            Assert.False(FeedRules.Allowed(fuels, "RoundLog"));
            Assert.False(FeedRules.Allowed(fuels, null));
        }

        [Fact]
        public void Minimum_prefers_the_item_over_the_default()
        {
            var perItem = FeedRules.ParseAmounts("Wood:50");

            Assert.Equal(50, FeedRules.MinimumFor(perItem, "Wood", 10));
            Assert.Equal(10, FeedRules.MinimumFor(perItem, "Barley", 10));
            Assert.Equal(10, FeedRules.MinimumFor(null, "Barley", 10));
        }

        [Fact]
        public void Negative_minimums_count_as_none()
        {
            Assert.Equal(0, FeedRules.MinimumFor(FeedRules.ParseAmounts(""), "Wood", -5));
        }

        [Fact]
        public void Takeable_leaves_the_minimum_behind()
        {
            Assert.Equal(10, FeedRules.Takeable(available: 60, wanted: 10, minimum: 50));
            Assert.Equal(5, FeedRules.Takeable(available: 55, wanted: 10, minimum: 50));
            Assert.Equal(0, FeedRules.Takeable(available: 50, wanted: 10, minimum: 50));
            Assert.Equal(0, FeedRules.Takeable(available: 20, wanted: 10, minimum: 50));
        }

        [Fact]
        public void Takeable_never_exceeds_what_is_wanted()
        {
            Assert.Equal(3, FeedRules.Takeable(available: 500, wanted: 3, minimum: 0));
        }

        [Fact]
        public void Takeable_of_nothing_wanted_is_nothing()
        {
            Assert.Equal(0, FeedRules.Takeable(available: 500, wanted: 0, minimum: 0));
            Assert.Equal(0, FeedRules.Takeable(available: 500, wanted: -2, minimum: 0));
        }

        [Fact]
        public void A_cap_stops_a_product_once_reached()
        {
            var caps = FeedRules.ParseAmounts("Coal:200");

            Assert.False(FeedRules.AtCap(caps, "Coal", 199));
            Assert.True(FeedRules.AtCap(caps, "Coal", 200));
            Assert.True(FeedRules.AtCap(caps, "coal", 500));
        }

        [Fact]
        public void An_uncapped_product_is_never_at_its_cap()
        {
            var caps = FeedRules.ParseAmounts("Coal:200");

            Assert.False(FeedRules.AtCap(caps, "Flour", 10000));
            Assert.False(FeedRules.AtCap(null, "Coal", 10000));
            Assert.False(FeedRules.AtCap(caps, null, 10000));
        }

        [Fact]
        public void A_cap_of_zero_means_no_limit_not_no_production()
        {
            var caps = FeedRules.ParseAmounts("Coal:0");

            Assert.False(FeedRules.AtCap(caps, "Coal", 10000));
        }
    }
}
