using System;

namespace RossQoL.Core.Terrain
{
    /// <summary>
    /// How far terrain may be raised or dug from its original height. Vanilla
    /// hard-codes 8 metres in both directions.
    /// </summary>
    public static class HeightLimits
    {
        public const float Vanilla = 8f;
        public const float MinSetting = 1f;
        public const float MaxSetting = 200f;

        /// <summary>
        /// The limit in effect: vanilla's while the feature is off, otherwise
        /// the configured value kept within the allowed range.
        /// </summary>
        public static float Effective(bool active, float configured)
        {
            if (!active) return Vanilla;
            if (float.IsNaN(configured) || float.IsInfinity(configured)) return Vanilla;
            return Math.Min(Math.Max(configured, MinSetting), MaxSetting);
        }
    }
}
