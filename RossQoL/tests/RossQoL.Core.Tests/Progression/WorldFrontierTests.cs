using System;
using System.Collections.Generic;
using RossQoL.Core.Progression;
using Xunit;

namespace RossQoL.Core.Tests.Progression
{
    public class WorldFrontierTests
    {
        private static Func<string, bool> Killed(params string[] keys)
        {
            var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            return key => key != null && set.Contains(key);
        }

        [Fact]
        public void A_fresh_world_is_at_the_meadows()
        {
            Assert.Equal("Meadows", WorldFrontier.TierFor(Killed()));
        }

        [Fact]
        public void Each_boss_opens_the_next_tier()
        {
            Assert.Equal("BlackForest", WorldFrontier.TierFor(Killed(WorldFrontier.Eikthyr)));
            Assert.Equal("Swamp", WorldFrontier.TierFor(Killed(WorldFrontier.Eikthyr, TeleportUnlocks.Elder)));
            Assert.Equal("Mountain", WorldFrontier.TierFor(
                Killed(WorldFrontier.Eikthyr, TeleportUnlocks.Elder, TeleportUnlocks.Bonemass)));
        }

        [Fact]
        public void The_furthest_boss_decides_even_when_an_earlier_one_was_skipped()
        {
            // Yagluth dead means the world has reached the Mistlands, whatever
            // was skipped on the way.
            Assert.Equal("Mistlands", WorldFrontier.TierFor(Killed(TeleportUnlocks.Yagluth)));
        }

        [Fact]
        public void Every_boss_dead_reaches_the_deep_north()
        {
            var all = Killed(
                WorldFrontier.Eikthyr, TeleportUnlocks.Elder, TeleportUnlocks.Bonemass,
                TeleportUnlocks.Moder, TeleportUnlocks.Yagluth, WorldFrontier.Queen, WorldFrontier.Fader);

            Assert.Equal("DeepNorth", WorldFrontier.TierFor(all));
            Assert.Equal(WorldFrontier.Queen, WorldFrontier.KeyForTier("Ashlands"));
            Assert.Equal(WorldFrontier.Fader, WorldFrontier.KeyForTier("DeepNorth"));
            Assert.Equal(TeleportUnlocks.Yagluth, WorldFrontier.KeyForTier("Mistlands"));
        }

        [Fact]
        public void A_missing_reader_is_the_meadows_rather_than_a_crash()
        {
            Assert.Equal("Meadows", WorldFrontier.TierFor(null));
        }

        [Fact]
        public void Every_tier_is_listed_in_order()
        {
            Assert.Equal(
                new[] { "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands", "DeepNorth" },
                WorldFrontier.Tiers);
        }
    }
}
