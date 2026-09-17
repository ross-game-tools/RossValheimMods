using System;
using System.Collections.Generic;

namespace RossQoL.Core.Progression
{
    /// <summary>
    /// Which boss answers for a biome. The same rule the teleport unlocks
    /// use, asked the other way round: there by the item, here by the ground
    /// it comes out of.
    ///
    /// Biomes are named as Valheim's own Heightmap.Biome spells them, so the
    /// game side can pass an enum's name straight in and this stays free of
    /// game types.
    ///
    /// Four biomes, because those are the four with a boss whose death means
    /// anything for what you dig up. Meadows, Mistlands, the Ashlands and the
    /// Deep North are absent on purpose.
    /// </summary>
    public static class BiomeBosses
    {
        private static readonly Dictionary<string, string> Bosses =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "BlackForest", TeleportUnlocks.Elder },
                { "Swamp", TeleportUnlocks.Bonemass },
                { "Mountain", TeleportUnlocks.Moder },
                { "Plains", TeleportUnlocks.Yagluth },
            };

        /// <summary>The global key set when this biome's boss dies, or null when it has none.</summary>
        public static string KeyFor(string biomeName)
        {
            if (string.IsNullOrEmpty(biomeName)) return null;

            return Bosses.TryGetValue(biomeName, out string key) ? key : null;
        }

        /// <summary>Whether this biome's boss is dead.</summary>
        /// <param name="isKeySet">Reads a global key; the game's ZoneSystem in play, a stub in tests.</param>
        public static bool IsCleared(string biomeName, Func<string, bool> isKeySet)
        {
            if (isKeySet == null) return false;

            string key = KeyFor(biomeName);
            return key != null && isKeySet(key);
        }

        /// <summary>Every biome that answers to a boss, for tests and descriptions.</summary>
        public static IEnumerable<string> KnownBiomes => Bosses.Keys;
    }
}
