using System;

namespace RossQoL.Core.Interface
{
    /// <summary>
    /// Turns Valheim's internal skill progress numbers into something a player
    /// can read.
    ///
    /// The game accumulates a raw figure towards a target that itself grows
    /// with the level, so the same raw gain is a big step at level 5 and
    /// almost nothing at level 80. Only the gain expressed as a share of the
    /// next level means anything on screen.
    /// </summary>
    public static class SkillProgress
    {
        /// <summary>Valheim's highest skill level; there is no progress past it.</summary>
        public const float MaxLevel = 100f;

        /// <summary>
        /// How much accumulated progress the game wants before the level after
        /// this one is reached. This mirrors the game's own curve exactly.
        /// </summary>
        public static float NextLevelRequirement(float level)
        {
            if (level < 0f) level = 0f;
            return (float)(Math.Pow(Math.Floor(level + 1f), 1.5) * 0.5 + 0.5);
        }

        /// <summary>
        /// The share of a level, in whole-percent units, that a raw gain of
        /// <paramref name="gain"/> represents at this level. Zero when nothing
        /// was gained or the level cannot advance.
        /// </summary>
        public static float PercentOfLevel(float gain, float level)
        {
            if (gain <= 0f) return 0f;

            float requirement = NextLevelRequirement(level);
            if (requirement <= 0f) return 0f;

            return gain / requirement * 100f;
        }
    }
}
