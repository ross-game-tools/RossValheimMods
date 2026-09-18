using System;
using System.Collections.Generic;
using RossQoL.Core.Progression;
using Xunit;

namespace RossQoL.Core.Tests.Progression
{
    public class DungeonBossesTests
    {
        private static Func<string, bool> Killed(params string[] keys)
        {
            var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            return key => set.Contains(key);
        }

        [Fact]
        public void Each_biome_answers_to_its_own_boss()
        {
            Assert.Equal(TeleportUnlocks.Elder, DungeonBosses.KeyFor("BlackForest"));
            Assert.Equal(TeleportUnlocks.Bonemass, DungeonBosses.KeyFor("Swamp"));
            Assert.Equal(TeleportUnlocks.Moder, DungeonBosses.KeyFor("Mountain"));
            Assert.Equal(DungeonBosses.Queen, DungeonBosses.KeyFor("Mistlands"));
        }

        [Fact]
        public void A_biome_is_unlocked_only_by_its_own_boss()
        {
            Assert.True(DungeonBosses.IsCleared("Mistlands", Killed(DungeonBosses.Queen)));
            Assert.False(DungeonBosses.IsCleared("Mistlands", Killed(TeleportUnlocks.Yagluth)));
            Assert.False(DungeonBosses.IsCleared("Mistlands", Killed()));
        }

        [Fact]
        public void Biomes_without_dungeons_of_their_own_never_unlock()
        {
            var all = Killed(
                TeleportUnlocks.Elder, TeleportUnlocks.Bonemass, TeleportUnlocks.Moder,
                TeleportUnlocks.Yagluth, DungeonBosses.Queen);

            // The Plains has camps but no interiors; the rest have no boss.
            foreach (string biome in new[] { "Plains", "Meadows", "AshLands", "DeepNorth", "Ocean", "None" })
            {
                Assert.Null(DungeonBosses.KeyFor(biome));
                Assert.False(DungeonBosses.IsCleared(biome, all), biome + " should have no dungeon boss");
            }
        }

        [Fact]
        public void Mining_and_teleporting_are_left_as_they_were()
        {
            // The Mistlands belongs to dungeons only: adding it to BiomeBosses
            // would have changed what MiningPower and SmeltingYield do.
            Assert.Null(BiomeBosses.KeyFor("Mistlands"));
            Assert.Equal(DungeonBosses.Queen, DungeonBosses.KeyFor("Mistlands"));
        }

        [Fact]
        public void Every_listed_biome_unlocks_when_all_bosses_are_dead()
        {
            var all = Killed(
                TeleportUnlocks.Elder, TeleportUnlocks.Bonemass,
                TeleportUnlocks.Moder, DungeonBosses.Queen);

            foreach (string biome in DungeonBosses.KnownBiomes)
                Assert.True(DungeonBosses.IsCleared(biome, all), biome + " should be unlocked");
        }

        [Fact]
        public void Names_are_matched_however_they_are_spelled()
        {
            Assert.Equal(TeleportUnlocks.Elder, DungeonBosses.KeyFor("blackforest"));
            Assert.True(DungeonBosses.IsCleared("MISTLANDS", Killed(DungeonBosses.Queen)));
        }

        [Fact]
        public void Missing_inputs_unlock_nothing()
        {
            Assert.Null(DungeonBosses.KeyFor(null));
            Assert.Null(DungeonBosses.KeyFor(""));
            Assert.False(DungeonBosses.IsCleared("Swamp", null));
        }
    }
}
