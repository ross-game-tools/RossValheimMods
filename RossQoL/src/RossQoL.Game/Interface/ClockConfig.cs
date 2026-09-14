using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Interface
{
    /// <summary>Clock's settings beyond its toggle. Read live, so no restart.</summary>
    public static class ClockConfig
    {
        public static ConfigEntry<bool> Use24Hour;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            Use24Hour = config.Bind(section, "Clock24Hour", true,
                ConfigText.Description(
                    "Show the clock as 24-hour time (14:30). Off shows 12-hour time (2:30 PM).",
                    scope, requiresRestart: false));
        }
    }
}
