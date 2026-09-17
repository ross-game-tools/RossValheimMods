using System;
using System.Collections.Generic;
using RossQoL.Core.Progression;
using Xunit;

namespace RossQoL.Core.Tests.Progression
{
    public class BiomeBossesTests
    {
        private static Func<string, bool> Killed(params string[] keys)
        {
            var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            return key => set.Contains(key);
        }

        [Fact]
        public void Each_biome_answers_to_its_own_boss()
        {
            Assert.Equal(TeleportUnlocks.Elder, BiomeBosses.KeyFor("BlackForest"));
            Assert.Equal(TeleportUnlocks.Bonemass, BiomeBosses.KeyFor("Swamp"));
            Assert.Equal(TeleportUnlocks.Moder, BiomeBosses.KeyFor("Mountain"));
            Assert.Equal(TeleportUnlocks.Yagluth, BiomeBosses.KeyFor("Plains"));
        }

        [Fact]
        public void A_biome_is_cleared_only_by_its_own_boss()
        {
            Assert.True(BiomeBosses.IsCleared("Swamp", Killed(TeleportUnlocks.Bonemass)));
            Assert.False(BiomeBosses.IsCleared("Swamp", Killed(TeleportUnlocks.Yagluth)));
            Assert.False(BiomeBosses.IsCleared("Swamp", Killed()));
        }

        [Fact]
        public void Biomes_without_a_boss_of_their_own_are_never_cleared()
        {
            var all = Killed(
                TeleportUnlocks.Elder, TeleportUnlocks.Bonemass,
                TeleportUnlocks.Moder, TeleportUnlocks.Yagluth);

            foreach (string biome in new[] { "Meadows", "Mistlands", "AshLands", "DeepNorth", "Ocean", "None" })
            {
                Assert.Null(BiomeBosses.KeyFor(biome));
                Assert.False(BiomeBosses.IsCleared(biome, all), biome + " should have no boss");
            }
        }

        [Fact]
        public void Every_listed_biome_clears_when_all_bosses_are_dead()
        {
            var all = Killed(
                TeleportUnlocks.Elder, TeleportUnlocks.Bonemass,
                TeleportUnlocks.Moder, TeleportUnlocks.Yagluth);

            foreach (string biome in BiomeBosses.KnownBiomes)
                Assert.True(BiomeBosses.IsCleared(biome, all), biome + " should be cleared");
        }

        [Fact]
        public void Names_are_matched_however_they_are_spelled()
        {
            Assert.Equal(TeleportUnlocks.Elder, BiomeBosses.KeyFor("blackforest"));
            Assert.True(BiomeBosses.IsCleared("MOUNTAIN", Killed(TeleportUnlocks.Moder)));
        }

        [Fact]
        public void Missing_inputs_clear_nothing()
        {
            Assert.Null(BiomeBosses.KeyFor(null));
            Assert.Null(BiomeBosses.KeyFor(""));
            Assert.False(BiomeBosses.IsCleared("Swamp", null));
        }
    }
}
