using System;
using System.Globalization;

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

        /// <summary>The smallest raw gain one decimal place can state.</summary>
        private const float SmallestValue = 0.1f;

        /// <summary>The smallest share whole percent can state.</summary>
        private const float SmallestPercent = 1f;

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

        /// <summary>
        /// How a skill gain reads on screen: what the game actually added,
        /// then what share of the next level that is, e.g. "+12 (3%)".
        ///
        /// The raw figure is the game's own internal progress unit rather
        /// than anything Valheim shows a player, so its scale is arbitrary --
        /// but it is consistent within a skill, which makes it worth seeing
        /// next to the share it works out to. One decimal place at most: a
        /// single action adds around one unit and the smallest actions around
        /// a tenth, so that is the whole useful range, and a whole number
        /// loses its pointless ".0".
        /// </summary>
        public static string FormatGain(float gain, float percent) =>
            "+" + FormatValue(gain) + " (" + FormatPercent(percent) + ")";

        /// <summary>
        /// The raw gain to one decimal place. Anything real but smaller than
        /// that says so rather than rounding down to a flat "0", which would
        /// read as "that did nothing".
        /// </summary>
        public static string FormatValue(float gain)
        {
            if (gain <= 0f) return "0";
            if (gain < SmallestValue) return "<0.1";

            return Math.Round(gain, 1, MidpointRounding.AwayFromZero)
                .ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The share of a level in whole percent. A real gain that rounds to
        /// nothing reads as "&lt;1%" for the same reason.
        /// </summary>
        public static string FormatPercent(float percent)
        {
            if (percent <= 0f) return "0%";
            if (percent < SmallestPercent) return "<1%";

            return ((int)Math.Round(percent, MidpointRounding.AwayFromZero))
                .ToString(CultureInfo.InvariantCulture) + "%";
        }
    }
}
