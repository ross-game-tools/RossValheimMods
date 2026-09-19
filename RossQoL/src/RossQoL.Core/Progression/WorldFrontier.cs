using System;
using System.Collections.Generic;

namespace RossQoL.Core.Progression
{
    /// <summary>
    /// How far a world has got: the furthest tier whose boss is dead.
    ///
    /// The Queen and Fader set their keys from a field on the boss prefab
    /// (m_defeatSetGlobalKey), not from Valheim's code, so their spellings
    /// could not be confirmed until read out of a live game. They are now
    /// verified (see docs/valheim-api/death-and-respawn.md) and wired in
    /// below.
    ///
    /// DeepNorth is the last rung. FrozenKing is the Deep North's own boss
    /// (its key is "defeated_frozenking"), so it opens no further tier here —
    /// there is nothing past DeepNorth to open.
    /// </summary>
    public static class WorldFrontier
    {
        /// <summary>The global key Valheim sets when Eikthyr dies. Spelled out here because Eikthyr is the only early boss not in TeleportUnlocks.</summary>
        public const string Eikthyr = "defeated_eikthyr";

        /// <summary>The global key set when the Mistlands boss, the Queen, dies. Opens Ashlands.</summary>
        public const string Queen = "defeated_queen";

        /// <summary>The global key set when the Ashlands boss, Fader, dies. Opens DeepNorth.</summary>
        public const string Fader = "defeated_fader";

        private static readonly (string Tier, string Key)[] Ladder =
        {
            ("Meadows", null),
            ("BlackForest", Eikthyr),
            ("Swamp", TeleportUnlocks.Elder),
            ("Mountain", TeleportUnlocks.Bonemass),
            ("Plains", TeleportUnlocks.Moder),
            ("Mistlands", TeleportUnlocks.Yagluth),
            ("Ashlands", Queen),
            ("DeepNorth", Fader),
        };

        /// <summary>The progression ladder in order: Meadows, then each tier opened by a boss kill.</summary>
        public static IReadOnlyList<string> Tiers
        {
            get
            {
                var names = new List<string>(Ladder.Length);
                foreach (var (tier, _) in Ladder) names.Add(tier);
                return names;
            }
        }

        /// <summary>The global key that opens the given tier. Null for Meadows (nothing opens it) or an unknown tier name.</summary>
        public static string KeyForTier(string tier)
        {
            foreach (var (name, key) in Ladder)
                if (string.Equals(name, tier, StringComparison.OrdinalIgnoreCase))
                    return key;

            return null;
        }

        /// <summary>How far the world has got: the furthest tier whose opening boss is dead, or Meadows when none is.</summary>
        /// <param name="isKeySet">Reads a global key; ZoneSystem in play, a stub in tests.</param>
        /// <returns>The name of the tier the world has reached.</returns>
        public static string TierFor(Func<string, bool> isKeySet)
        {
            if (isKeySet == null) return Ladder[0].Tier;

            string reached = Ladder[0].Tier;
            foreach (var (tier, key) in Ladder)
                if (key != null && isKeySet(key))
                    reached = tier;

            return reached;
        }
    }
}
