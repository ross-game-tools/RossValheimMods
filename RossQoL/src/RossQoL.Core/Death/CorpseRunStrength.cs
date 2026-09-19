using System;

namespace RossQoL.Core.Death
{
    /// <summary>
    /// How strong the corpse run buff is at a given distance from the grave.
    ///
    /// Nothing within the minimum distance, ramping to full over the stretch
    /// BEYOND it: a death at the door is not worth a buff, and a death across
    /// the map is a long quiet run that is.
    /// </summary>
    public static class CorpseRunStrength
    {
        /// <summary>The strength of the buff at a given distance from the grave, from 0 to 1.</summary>
        public static float For(float distance, float minDistance, float fullDistance)
        {
            float floor = Math.Max(0f, minDistance);
            float past = distance - floor;
            if (past <= 0f) return 0f;

            // A ramp of zero would divide by zero: treat it as a step instead.
            if (fullDistance <= 0f) return 1f;

            return Clamp01(past / fullDistance);
        }

        /// <summary>
        /// Remaps a strength onto the range from a baseline up to full
        /// strength, so the buff never fades all the way to nothing while
        /// the grave it is tied to still stands, yet still keeps easing off
        /// in perceptible steps the whole way in rather than going flat
        /// once <paramref name="strength"/> is small. Baseline 0 leaves
        /// <paramref name="strength"/> unchanged; baseline 1 gives full
        /// strength everywhere. A baseline outside 0-1 is clamped rather
        /// than trusted, same as every other fraction here, and the result
        /// is always within [baseline, 1].
        /// </summary>
        public static float WithBaseline(float strength, float baseline)
        {
            float b = Clamp01(baseline);
            return b + (1f - b) * Clamp01(strength);
        }

        /// <summary>For SE_Stats.m_staminaRegenMultiplier: 1 leaves regen alone.</summary>
        public static float RegenMultiplier(float strength, float maxRegenBonus) =>
            1f + Clamp01(strength) * Math.Max(0f, maxRegenBonus);

        /// <summary>
        /// For SE_Stats.m_runStaminaDrainModifier and m_jumpStaminaUseModifier,
        /// which ADD their fraction of the base cost: negative removes cost.
        /// Clamped at -1 so a mistyped setting cannot pay stamina back.
        /// </summary>
        public static float DrainModifier(float strength, float maxDrainReduction) =>
            -Clamp01(strength) * Clamp01(maxDrainReduction);

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
