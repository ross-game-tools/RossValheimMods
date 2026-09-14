using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Core.Terrain;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Terrain
{
    /// <summary>
    /// UnlimitedHeight's limits. Read live on every terrain edit and redraw,
    /// so no restart.
    /// </summary>
    public static class TerrainConfig
    {
        public static ConfigEntry<float> MaxRaise;
        public static ConfigEntry<float> MaxDig;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            var range = new AcceptableValueRange<float>(HeightLimits.MinSetting, HeightLimits.MaxSetting);

            MaxRaise = config.Bind(section, "MaxRaise", HeightLimits.MaxSetting,
                ConfigText.Description(
                    "How high, in metres, ground can be raised above its original height. Vanilla is 8.",
                    scope, requiresRestart: false, range: range));

            MaxDig = config.Bind(section, "MaxDig", HeightLimits.MaxSetting,
                ConfigText.Description(
                    "How deep, in metres, ground can be dug below its original height. Vanilla is 8.",
                    scope, requiresRestart: false, range: range));
        }
    }
}
