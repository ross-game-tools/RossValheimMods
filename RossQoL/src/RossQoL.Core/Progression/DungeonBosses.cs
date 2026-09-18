using System;
using System.Collections.Generic;

namespace RossQoL.Core.Progression
{
    /// <summary>
    /// Which boss has to be dead before a biome's dungeons start coming back.
    ///
    /// Deliberately its own table rather than <see cref="BiomeBosses"/>, which
    /// answers the same question for ore and teleporting. That one covers the
    /// four biomes whose materials a boss frees, and the Mistlands is absent
    /// from it on purpose. Dungeons are a different list: the Mistlands has
    /// infested mines and belongs here, while the Plains has camps but no
    /// interiors and does not. Sharing one table would have changed what
    /// mining and smelting do, which is not what respawning dungeons is for.
    ///
    /// Biomes are named as Valheim's own Heightmap.Biome spells them, so the
    /// game side can pass an enum's name straight in and this stays free of
    /// game types.
    /// </summary>
    public static class DungeonBosses
    {
        /// <summary>The Queen, as ZoneSystem stores her key. The other four live on TeleportUnlocks.</summary>
        public const string Queen = "defeated_queen";

        private static readonly Dictionary<string, string> Bosses =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Burial chambers and troll caves.
                { "BlackForest", TeleportUnlocks.Elder },
                // Sunken crypts.
                { "Swamp", TeleportUnlocks.Bonemass },
                // Frost caves.
                { "Mountain", TeleportUnlocks.Moder },
                // Infested mines.
                { "Mistlands", Queen },
            };

        /// <summary>The global key set when this biome's boss dies, or null when its dungeons never respawn.</summary>
        public static string KeyFor(string biomeName)
        {
            if (string.IsNullOrEmpty(biomeName)) return null;

            return Bosses.TryGetValue(biomeName, out string key) ? key : null;
        }

        /// <summary>Whether this biome's dungeons are unlocked, i.e. its boss is dead.</summary>
        /// <param name="isKeySet">Reads a global key; the game's ZoneSystem in play, a stub in tests.</param>
        public static bool IsCleared(string biomeName, Func<string, bool> isKeySet)
        {
            if (isKeySet == null) return false;

            string key = KeyFor(biomeName);
            return key != null && isKeySet(key);
        }

        /// <summary>Every biome whose dungeons respawn, for tests and descriptions.</summary>
        public static IEnumerable<string> KnownBiomes => Bosses.Keys;
    }
}
