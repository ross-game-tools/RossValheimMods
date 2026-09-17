using System;
using System.Collections.Generic;

namespace RossQoL.Core.Progression
{
    /// <summary>
    /// Which boss has to be dead before an item vanilla refuses to teleport
    /// may go through a portal.
    ///
    /// One rule: the boss of the biome the item comes from. Copper and tin
    /// are Black Forest, so they wait for the Elder; iron is the swamp, so it
    /// waits for Bonemass. Killing a later boss does not unlock an earlier
    /// biome's metal, because the point is that a biome opens up once you
    /// have beaten it, not that progress is a ladder.
    ///
    /// Four bosses, because those are the four biomes whose materials vanilla
    /// refuses to carry: Mistlands and Ashlands have nothing teleport-blocked
    /// to free.
    ///
    /// Names are prefab names, matched however they are spelled. An item that
    /// is not listed is not unlocked by anything: vanilla's own rule stands.
    /// </summary>
    public static class TeleportUnlocks
    {
        /// <summary>Global keys as ZoneSystem stores them.</summary>
        public const string Elder = "defeated_gdking";
        public const string Bonemass = "defeated_bonemass";
        public const string Moder = "defeated_dragon";
        public const string Yagluth = "defeated_goblinking";

        private static readonly Dictionary<string, string> Unlocks =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Black Forest: the Elder.
                { "CopperOre", Elder },
                { "TinOre", Elder },
                { "Copper", Elder },
                { "Tin", Elder },
                { "Bronze", Elder },
                { "CopperScrap", Elder },

                // Swamp: Bonemass.
                { "IronOre", Bonemass },
                { "IronScrap", Bonemass },
                { "Iron", Bonemass },

                // Mountain: Moder.
                { "SilverOre", Moder },
                { "Silver", Moder },

                // Plains: Yagluth.
                { "BlackMetalScrap", Yagluth },
                { "BlackMetal", Yagluth },

                // Mistlands and Ashlands are not listed: nothing they hold is
                // teleport-blocked in the first place, so there is nothing to
                // unlock.
            };

        /// <summary>The global key that frees an item, or null when nothing does.</summary>
        public static string KeyFor(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;

            return Unlocks.TryGetValue(prefabName, out string key) ? key : null;
        }

        /// <summary>
        /// Whether an item vanilla refuses to teleport may go anyway.
        /// </summary>
        /// <param name="prefabName">The item's prefab name.</param>
        /// <param name="isKeySet">Reads a global key; the game's ZoneSystem in play, a stub in tests.</param>
        public static bool IsUnlocked(string prefabName, Func<string, bool> isKeySet)
        {
            if (isKeySet == null) return false;

            string key = KeyFor(prefabName);
            return key != null && isKeySet(key);
        }

        /// <summary>Every item this can unlock, for the feature's description and tests.</summary>
        public static IEnumerable<string> KnownItems => Unlocks.Keys;
    }
}
