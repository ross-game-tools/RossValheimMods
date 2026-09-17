using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// CraftFromChests' settings beyond its toggle. Read at use, so live
    /// reloads apply without a restart.
    /// </summary>
    public static class CraftFromChestsConfig
    {
        public static ConfigEntry<float> CraftRadius;
        public static ConfigEntry<bool> BuildFromChests;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            CraftRadius = config.Bind(section, "CraftRadius", 40f,
                ConfigText.Description(
                    "How far from you, in metres, to look for containers to craft from. Measured in three "
                    + "dimensions.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 100f)));

            BuildFromChests = config.Bind(section, "BuildFromChests", true,
                ConfigText.Description(
                    "Building with the hammer draws on nearby containers too, not just crafting at a station.",
                    scope, requiresRestart: false));
        }
    }
}
