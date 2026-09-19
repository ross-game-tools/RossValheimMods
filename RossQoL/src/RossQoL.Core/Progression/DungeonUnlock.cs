using System;
using System.Collections.Generic;

namespace RossQoL.Core.Progression
{
    /// <summary>
    /// Which bosses have to be dead before one particular dungeon comes back.
    ///
    /// The answer comes from the dungeon's own identity, never from the
    /// terrain under it. A dungeon is placed by the world generator for a
    /// declared biome (<c>ZoneSystem.ZoneLocation.m_biome</c>, mirrored on the
    /// location prefab's <c>Location.m_biome</c>), and its rooms are drawn from
    /// a declared theme (<c>DungeonGenerator.m_themes</c>, a Room.Theme bit
    /// mask). Both travel with the dungeon. Sampling the biome at a position
    /// instead answers for whatever ground happens to be there, which is a
    /// different question and a dangerous one to get wrong: a rebuild destroys
    /// everything inside, so a gate that opens by mistake destroys a player's
    /// chests, while a gate that stays shut only disappoints.
    ///
    /// So everything here fails CLOSED. An identity we do not recognise, a
    /// biome with no boss of its own, a theme we have no rule for, a missing
    /// or empty identity: all of them mean "no respawn", never "respawn".
    ///
    /// Names arrive as Valheim spells them -- Heightmap.Biome members for
    /// biomes, Room.Theme members for themes -- one per set bit, comma
    /// separated, so this stays free of game types.
    /// </summary>
    public static class DungeonUnlock
    {
        private static readonly char[] Separators = { ',', ' ', '\t', '|' };

        /// <summary>
        /// Boss keys by dungeon theme.
        ///
        /// A theme that could belong to more than one biome lists every
        /// candidate's boss and so requires all of them, because being early
        /// costs a player their dungeon and being late costs nothing:
        ///
        /// - Cave covers troll caves in the Black Forest and frost caves in
        ///   the Mountains, so it asks for the Elder and Moder both.
        /// - Crypt is asked for the Elder and Bonemass both: which crypt
        ///   family it names cannot be established from the assemblies, only
        ///   from prefab data, and the wrong guess would rebuild sunken crypts
        ///   the moment the Elder fell.
        ///
        /// Themes that are not dungeons at all -- GoblinCamp, MeadowsVillage,
        /// MeadowsFarm and the rest -- are deliberately absent: a surface camp
        /// must never be torn down and rebuilt, and absence means no.
        /// </summary>
        private static readonly Dictionary<string, string[]> ThemeBosses =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "SunkenCrypt", new[] { TeleportUnlocks.Bonemass } },
                { "ForestCrypt", new[] { TeleportUnlocks.Elder } },
                { "ForestCryptHildir", new[] { TeleportUnlocks.Elder } },
                { "Crypt", new[] { TeleportUnlocks.Elder, TeleportUnlocks.Bonemass } },
                { "Cave", new[] { TeleportUnlocks.Elder, TeleportUnlocks.Moder } },
                { "CaveHildir", new[] { TeleportUnlocks.Elder, TeleportUnlocks.Moder } },
                { "DvergerTown", new[] { DungeonBosses.Queen } },
                { "DvergerBoss", new[] { DungeonBosses.Queen } },
            };

        /// <summary>
        /// The keys required by a dungeon's declared biomes, or null when that
        /// tells us nothing we can act on.
        /// </summary>
        /// <param name="biomeNames">Heightmap.Biome member names, comma separated. "None" is ignored.</param>
        public static IReadOnlyList<string> KeysForBiomes(string biomeNames)
        {
            var names = Split(biomeNames);
            if (names.Count == 0) return null;

            var keys = new List<string>();
            foreach (string name in names)
            {
                string key = DungeonBosses.KeyFor(name);

                // One biome without a dungeon boss of its own sinks the whole
                // answer: a location that can stand in the Plains as well as
                // the Mistlands has no boss that covers it.
                if (key == null) return null;
                if (!keys.Contains(key)) keys.Add(key);
            }

            return keys.Count > 0 ? keys : null;
        }

        /// <summary>The keys required by a dungeon's themes, or null when no rule covers them.</summary>
        /// <param name="themeNames">Room.Theme member names, comma separated. "None" is ignored.</param>
        public static IReadOnlyList<string> KeysForThemes(string themeNames)
        {
            var names = Split(themeNames);
            if (names.Count == 0) return null;

            var keys = new List<string>();
            foreach (string name in names)
            {
                if (!ThemeBosses.TryGetValue(name, out string[] bosses)) return null;

                foreach (string key in bosses)
                    if (!keys.Contains(key)) keys.Add(key);
            }

            return keys.Count > 0 ? keys : null;
        }

        /// <summary>
        /// Every key that must be set before this dungeon may be rebuilt, or
        /// null when we do not recognise it and so will not touch it.
        ///
        /// The declared biome is asked first because it is the rule the world
        /// generator itself placed the dungeon by. The theme answers only when
        /// the biome is missing or unrecognised, which is why the two are not
        /// merged: a frost cave's Mountain biome is a better answer than its
        /// Cave theme, which cannot tell a frost cave from a troll cave.
        /// </summary>
        public static IReadOnlyList<string> RequiredKeys(string biomeNames, string themeNames) =>
            KeysForBiomes(biomeNames) ?? KeysForThemes(themeNames);

        /// <summary>Whether this dungeon is unlocked: recognised, and every boss it asks for dead.</summary>
        /// <param name="isKeySet">Reads a global key; the game's ZoneSystem in play, a stub in tests.</param>
        public static bool IsCleared(string biomeNames, string themeNames, Func<string, bool> isKeySet)
        {
            if (isKeySet == null) return false;

            var keys = RequiredKeys(biomeNames, themeNames);
            if (keys == null || keys.Count == 0) return false;

            foreach (string key in keys)
                if (!isKeySet(key)) return false;

            return true;
        }

        /// <summary>A description of what a dungeon is waiting for, for the log.</summary>
        public static string Describe(string biomeNames, string themeNames)
        {
            var keys = RequiredKeys(biomeNames, themeNames);
            return keys == null || keys.Count == 0 ? "nothing we recognise" : string.Join(" + ", ToArray(keys));
        }

        /// <summary>Themes with a rule of their own, for tests and descriptions.</summary>
        public static IEnumerable<string> KnownThemes => ThemeBosses.Keys;

        private static List<string> Split(string names)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(names)) return parts;

            foreach (string part in names.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                // "None" is what an unset bit mask says, and it is neither a
                // biome nor a theme; nothing else is dropped.
                if (string.Equals(part, "None", StringComparison.OrdinalIgnoreCase)) continue;

                parts.Add(part);
            }

            return parts;
        }

        private static string[] ToArray(IReadOnlyList<string> keys)
        {
            var array = new string[keys.Count];
            for (int i = 0; i < keys.Count; i++) array[i] = keys[i];

            return array;
        }
    }
}
