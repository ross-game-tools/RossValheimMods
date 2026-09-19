using System;
using System.Collections.Generic;
using RossQoL.Core.Progression;
using Xunit;

namespace RossQoL.Core.Tests.Progression
{
    public class TeleportUnlocksTests
    {
        private static Func<string, bool> Killed(params string[] keys)
        {
            var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            return key => set.Contains(key);
        }

        [Fact]
        public void Copper_waits_for_the_elder()
        {
            Assert.Equal(TeleportUnlocks.Elder, TeleportUnlocks.KeyFor("CopperOre"));
            Assert.True(TeleportUnlocks.IsUnlocked("CopperOre", Killed(TeleportUnlocks.Elder)));
            Assert.False(TeleportUnlocks.IsUnlocked("CopperOre", Killed()));
        }

        [Fact]
        public void Iron_waits_for_bonemass()
        {
            Assert.True(TeleportUnlocks.IsUnlocked("IronScrap", Killed(TeleportUnlocks.Bonemass)));
            Assert.False(TeleportUnlocks.IsUnlocked("IronScrap", Killed(TeleportUnlocks.Elder)));
        }

        [Fact]
        public void Silver_waits_for_moder_and_black_metal_for_yagluth()
        {
            Assert.True(TeleportUnlocks.IsUnlocked("Silver", Killed(TeleportUnlocks.Moder)));
            Assert.True(TeleportUnlocks.IsUnlocked("BlackMetal", Killed(TeleportUnlocks.Yagluth)));
            Assert.False(TeleportUnlocks.IsUnlocked("BlackMetal", Killed(TeleportUnlocks.Moder)));
        }

        [Fact]
        public void A_later_boss_does_not_unlock_an_earlier_biome()
        {
            // Killing Yagluth says nothing about the Black Forest: the rule is
            // per biome, deliberately, not a cumulative ladder.
            Assert.False(TeleportUnlocks.IsUnlocked("CopperOre", Killed(TeleportUnlocks.Yagluth)));
        }

        [Fact]
        public void Every_boss_killed_unlocks_every_listed_item()
        {
            var all = Killed(
                TeleportUnlocks.Elder, TeleportUnlocks.Bonemass,
                TeleportUnlocks.Moder, TeleportUnlocks.Yagluth, TeleportUnlocks.Queen);

            foreach (string item in TeleportUnlocks.KnownItems)
                Assert.True(TeleportUnlocks.IsUnlocked(item, all), item + " should be unlocked");
        }

        [Fact]
        public void Dragon_eggs_wait_for_moder()
        {
            Assert.Equal(TeleportUnlocks.Moder, TeleportUnlocks.KeyFor("DragonEgg"));
            Assert.True(TeleportUnlocks.IsUnlocked("DragonEgg", Killed(TeleportUnlocks.Moder)));
            Assert.False(TeleportUnlocks.IsUnlocked("DragonEgg", Killed()));
        }

        [Fact]
        public void Mechanical_spring_and_dvergr_extractor_wait_for_the_queen()
        {
            Assert.Equal(TeleportUnlocks.Queen, TeleportUnlocks.KeyFor("MechanicalSpring"));
            Assert.Equal(TeleportUnlocks.Queen, TeleportUnlocks.KeyFor("DvergrNeedle"));

            Assert.True(TeleportUnlocks.IsUnlocked("MechanicalSpring", Killed(TeleportUnlocks.Queen)));
            Assert.True(TeleportUnlocks.IsUnlocked("DvergrNeedle", Killed(TeleportUnlocks.Queen)));

            Assert.False(TeleportUnlocks.IsUnlocked("MechanicalSpring", Killed(TeleportUnlocks.Moder)));
            Assert.False(TeleportUnlocks.IsUnlocked("DvergrNeedle", Killed(TeleportUnlocks.Yagluth)));
        }

        [Fact]
        public void Names_are_matched_however_they_are_spelled()
        {
            Assert.True(TeleportUnlocks.IsUnlocked("copperore", Killed(TeleportUnlocks.Elder)));
            Assert.Equal(TeleportUnlocks.Moder, TeleportUnlocks.KeyFor("SILVERORE"));
        }

        [Fact]
        public void Ashlands_materials_are_not_listed()
        {
            // Nothing it holds is teleport-blocked, so there is nothing to
            // unlock and no guess about its boss key to get wrong.
            Assert.Null(TeleportUnlocks.KeyFor("FlametalOre"));
        }

        [Fact]
        public void Mistlands_materials_that_are_not_actually_blocked_stay_unlisted()
        {
            // BlackMarble and SoftTissue are ordinary Mistlands materials
            // that were never teleport-blocked in the first place; Sap was
            // specifically checked and confirmed teleportable, so it is not
            // in the table even though it looks similar to what is.
            Assert.Null(TeleportUnlocks.KeyFor("BlackMarble"));
            Assert.Null(TeleportUnlocks.KeyFor("SoftTissue"));
            Assert.Null(TeleportUnlocks.KeyFor("Sap"));
        }

        [Fact]
        public void An_item_nothing_unlocks_stays_vanilla()
        {
            Assert.Null(TeleportUnlocks.KeyFor("PowderedDragonEgg"));
            Assert.False(TeleportUnlocks.IsUnlocked("PowderedDragonEgg", Killed(TeleportUnlocks.Moder)));
        }

        [Fact]
        public void Missing_inputs_unlock_nothing()
        {
            Assert.Null(TeleportUnlocks.KeyFor(null));
            Assert.Null(TeleportUnlocks.KeyFor(""));
            Assert.False(TeleportUnlocks.IsUnlocked("CopperOre", null));
            Assert.False(TeleportUnlocks.IsUnlocked(null, Killed(TeleportUnlocks.Elder)));
        }
    }
}
