using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Meals
{
    /// <summary>
    /// The Food category's settings beyond its toggles. Read at use, so live
    /// reloads apply without a restart.
    ///
    /// The namespace is Meals, not Food: Valheim has its own Food type and a
    /// RossQoL.Game.Food namespace would shadow it in every file here.
    /// </summary>
    public static class FoodConfig
    {
        public static ConfigEntry<float> WarningSeconds;

        internal static void BindWarning(ConfigFile config, string section, FeatureScope scope)
        {
            WarningSeconds = config.Bind(section, "WarningSeconds", 60f,
                ConfigText.Description(
                    "How long before a food runs out to warn you, in seconds.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(5f, 600f)));
        }
    }
}
