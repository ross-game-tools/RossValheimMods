using System;
using System.Collections.Generic;
using RossQoL.Core.Progression;
using Xunit;

namespace RossQoL.Core.Tests.Progression
{
    public class DungeonUnlockTests
    {
        private static Func<string, bool> Killed(params string[] keys)
        {
            var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            return key => set.Contains(key);
        }

        private static readonly Func<string, bool> EveryBoss = Killed(
            TeleportUnlocks.Elder, TeleportUnlocks.Bonemass, TeleportUnlocks.Moder,
            TeleportUnlocks.Yagluth, DungeonBosses.Queen);

        [Fact]
        public void A_declared_biome_names_its_own_boss()
        {
            Assert.Equal(new[] { DungeonBosses.Queen }, DungeonUnlock.KeysForBiomes("Mistlands"));
            Assert.Equal(new[] { TeleportUnlocks.Bonemass }, DungeonUnlock.KeysForBiomes("Swamp"));
            Assert.Equal(new[] { TeleportUnlocks.Moder }, DungeonUnlock.KeysForBiomes("Mountain"));
            Assert.Equal(new[] { TeleportUnlocks.Elder }, DungeonUnlock.KeysForBiomes("BlackForest"));
        }

        [Fact]
        public void An_infested_mine_waits_for_the_queen_and_nobody_else()
        {
            // The bug this rule exists for: every other boss dead, the Queen
            // alive, and the mine must stay as the player left it.
            var allButTheQueen = Killed(
                TeleportUnlocks.Elder, TeleportUnlocks.Bonemass,
                TeleportUnlocks.Moder, TeleportUnlocks.Yagluth);

            Assert.False(DungeonUnlock.IsCleared("Mistlands", "DvergerTown", allButTheQueen));
            Assert.True(DungeonUnlock.IsCleared("Mistlands", "DvergerTown", EveryBoss));
        }

        [Fact]
        public void A_biome_with_no_dungeon_boss_blocks_the_whole_answer()
        {
            Assert.Null(DungeonUnlock.KeysForBiomes("Plains"));
            Assert.Null(DungeonUnlock.KeysForBiomes("Meadows"));

            // A location placed for either the Black Forest or the Plains has
            // no boss that covers it, so it is never rebuilt.
            Assert.Null(DungeonUnlock.KeysForBiomes("BlackForest, Plains"));
            Assert.False(DungeonUnlock.IsCleared("BlackForest, Plains", "", EveryBoss));
        }

        [Fact]
        public void Two_declared_biomes_ask_for_both_bosses()
        {
            Assert.Equal(
                new[] { TeleportUnlocks.Elder, TeleportUnlocks.Moder },
                DungeonUnlock.KeysForBiomes("BlackForest, Mountain"));

            Assert.False(DungeonUnlock.IsCleared("BlackForest, Mountain", "", Killed(TeleportUnlocks.Elder)));
            Assert.True(DungeonUnlock.IsCleared(
                "BlackForest, Mountain", "", Killed(TeleportUnlocks.Elder, TeleportUnlocks.Moder)));
        }

        [Fact]
        public void The_theme_answers_when_the_biome_is_missing()
        {
            Assert.Equal(new[] { DungeonBosses.Queen }, DungeonUnlock.RequiredKeys("", "DvergerTown"));
            Assert.Equal(new[] { DungeonBosses.Queen }, DungeonUnlock.RequiredKeys("None", "DvergerBoss"));
            Assert.Equal(new[] { TeleportUnlocks.Bonemass }, DungeonUnlock.RequiredKeys(null, "SunkenCrypt"));
        }

        [Fact]
        public void The_declared_biome_is_preferred_to_the_theme()
        {
            // A frost cave's Mountain beats its Cave theme, which cannot tell
            // a frost cave from a troll cave.
            Assert.Equal(new[] { TeleportUnlocks.Moder }, DungeonUnlock.RequiredKeys("Mountain", "Cave"));
            Assert.True(DungeonUnlock.IsCleared("Mountain", "Cave", Killed(TeleportUnlocks.Moder)));
        }

        [Fact]
        public void An_ambiguous_theme_asks_for_every_boss_it_might_mean()
        {
            Assert.Equal(
                new[] { TeleportUnlocks.Elder, TeleportUnlocks.Moder },
                DungeonUnlock.KeysForThemes("Cave"));

            Assert.Equal(
                new[] { TeleportUnlocks.Elder, TeleportUnlocks.Bonemass },
                DungeonUnlock.KeysForThemes("Crypt"));

            Assert.False(DungeonUnlock.IsCleared("", "Cave", Killed(TeleportUnlocks.Elder)));
            Assert.True(DungeonUnlock.IsCleared("", "Cave", Killed(TeleportUnlocks.Elder, TeleportUnlocks.Moder)));
        }

        [Fact]
        public void A_surface_camp_is_never_rebuilt()
        {
            foreach (string theme in new[] { "GoblinCamp", "MeadowsVillage", "MeadowsFarm", "PlainsFortHildir" })
            {
                Assert.Null(DungeonUnlock.KeysForThemes(theme));
                Assert.False(DungeonUnlock.IsCleared("", theme, EveryBoss), theme + " should never unlock");
            }
        }

        [Fact]
        public void One_unknown_theme_sinks_the_rest()
        {
            Assert.Null(DungeonUnlock.KeysForThemes("SunkenCrypt, MorkHalla"));
            Assert.False(DungeonUnlock.IsCleared("", "SunkenCrypt, MorkHalla", EveryBoss));
        }

        [Fact]
        public void Nothing_known_means_no_respawn()
        {
            foreach (string biome in new[] { null, "", "None", "AshLands", "DeepNorth", "Ocean" })
            {
                Assert.Null(DungeonUnlock.RequiredKeys(biome, null));
                Assert.False(DungeonUnlock.IsCleared(biome, "", EveryBoss), (biome ?? "null") + " should stay shut");
            }

            Assert.False(DungeonUnlock.IsCleared("Mistlands", "DvergerTown", null));
        }

        [Fact]
        public void Names_are_matched_however_they_are_spelled_and_separated()
        {
            Assert.Equal(new[] { DungeonBosses.Queen }, DungeonUnlock.KeysForBiomes("mistlands"));
            Assert.Equal(new[] { DungeonBosses.Queen }, DungeonUnlock.KeysForThemes("dvergertown"));
            Assert.Equal(
                new[] { TeleportUnlocks.Elder, TeleportUnlocks.Moder },
                DungeonUnlock.KeysForBiomes("BlackForest | Mountain"));
        }

        [Fact]
        public void Every_known_theme_resolves_to_bosses_that_exist()
        {
            foreach (string theme in DungeonUnlock.KnownThemes)
            {
                var keys = DungeonUnlock.KeysForThemes(theme);
                Assert.NotNull(keys);
                Assert.NotEmpty(keys);
                Assert.True(DungeonUnlock.IsCleared("", theme, EveryBoss), theme + " should unlock once all bosses are dead");
            }
        }

        [Fact]
        public void What_a_dungeon_waits_for_can_be_said_in_a_log_line()
        {
            Assert.Equal(DungeonBosses.Queen, DungeonUnlock.Describe("Mistlands", "DvergerTown"));
            Assert.Equal(
                TeleportUnlocks.Elder + " + " + TeleportUnlocks.Moder,
                DungeonUnlock.Describe("", "Cave"));
            Assert.Equal("nothing we recognise", DungeonUnlock.Describe("Plains", "GoblinCamp"));
        }
    }
}
