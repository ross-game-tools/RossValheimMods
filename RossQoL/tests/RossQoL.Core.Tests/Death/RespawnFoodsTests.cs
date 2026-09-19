using System;
using System.Collections.Generic;
using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class RespawnFoodsTests
    {
        private static Func<string, bool> Has(params string[] names)
        {
            var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            return name => name != null && set.Contains(name);
        }

        private const string Table = "Meadows:Honey|Raspberry,BlackForest:CarrotSoup|Honey,Swamp:TurnipStew|CarrotSoup";

        [Fact]
        public void The_tiers_first_food_is_used_when_it_exists()
        {
            Assert.Equal("CarrotSoup", RespawnFoods.Pick(Table, "BlackForest", Has("CarrotSoup", "Honey")));
        }

        [Fact]
        public void A_food_the_game_does_not_have_falls_back_within_the_tier()
        {
            Assert.Equal("Honey", RespawnFoods.Pick(Table, "BlackForest", Has("Honey")));
        }

        [Fact]
        public void An_exhausted_tier_drops_to_the_tier_below()
        {
            Assert.Equal("Raspberry", RespawnFoods.Pick(Table, "Swamp", Has("Raspberry")));
        }

        [Fact]
        public void Nothing_at_all_gives_nothing_rather_than_a_wrong_food()
        {
            Assert.Null(RespawnFoods.Pick(Table, "Swamp", Has()));
        }

        [Fact]
        public void An_unknown_tier_starts_from_the_bottom_of_the_table()
        {
            Assert.Equal("Honey", RespawnFoods.Pick(Table, "Nowhere", Has("Honey", "TurnipStew")));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("total nonsense")]
        [InlineData("Swamp:")]
        [InlineData(":::")]
        public void A_broken_table_gives_nothing_rather_than_throwing(string table)
        {
            Assert.Null(RespawnFoods.Pick(table, "Swamp", Has("Honey")));
        }

        [Fact]
        public void An_unreadable_tier_is_skipped_and_the_others_still_work()
        {
            Assert.Equal("Honey", RespawnFoods.Pick("nonsense,Meadows:Honey", "Meadows", Has("Honey")));
        }

        [Fact]
        public void Whitespace_around_entries_is_forgiven()
        {
            Assert.Equal("Honey", RespawnFoods.Pick(" Meadows : Honey | Raspberry ", "Meadows", Has("Honey")));
        }

        [Fact]
        public void The_default_table_covers_every_tier()
        {
            foreach (string tier in RossQoL.Core.Progression.WorldFrontier.Tiers)
                Assert.NotNull(RespawnFoods.Pick(RespawnFoods.DefaultTable, tier, _ => true));
        }
    }
}
