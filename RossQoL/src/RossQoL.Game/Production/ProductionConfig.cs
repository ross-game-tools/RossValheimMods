using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// AutoHarvest's settings beyond its toggle. Read at use on every
    /// harvest attempt, so live reloads apply without a restart.
    /// </summary>
    public static class ProductionConfig
    {
        public static ConfigEntry<bool> HarvestBeehives;
        public static ConfigEntry<bool> HarvestSapCollectors;
        public static ConfigEntry<bool> HarvestFermenters;
        public static ConfigEntry<bool> HarvestWindmills;
        public static ConfigEntry<float> HarvestRadius;
        public static ConfigEntry<float> HarvestInterval;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            HarvestBeehives = config.Bind(section, "HarvestBeehives", true,
                ConfigText.Description("Beehives empty their honey into nearby containers.",
                    scope, requiresRestart: false));

            HarvestSapCollectors = config.Bind(section, "HarvestSapCollectors", true,
                ConfigText.Description("Sap collectors empty their sap into nearby containers.",
                    scope, requiresRestart: false));

            HarvestFermenters = config.Bind(section, "HarvestFermenters", true,
                ConfigText.Description(
                    "Fermenters empty finished batches into nearby containers. A batch moves whole or not at all.",
                    scope, requiresRestart: false));

            HarvestWindmills = config.Bind(section, "HarvestWindmills", true,
                ConfigText.Description(
                    "Windmills put finished flour into nearby containers instead of dropping it. A stack moves whole or not at all.",
                    scope, requiresRestart: false));

            HarvestRadius = config.Bind(section, "HarvestRadius", 40f,
                ConfigText.Description(
                    "How far from a producer, in metres, to look for containers. Measured in three dimensions.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 100f)));

            HarvestInterval = config.Bind(section, "HarvestInterval", 10f,
                ConfigText.Description(
                    "Seconds between harvest attempts for each producer.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 3600f)));
        }
    }
}
