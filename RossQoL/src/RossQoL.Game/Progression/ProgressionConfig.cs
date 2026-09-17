using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// Settings for the progression rewards. Read at use, so live reloads
    /// apply without a restart.
    /// </summary>
    public static class ProgressionConfig
    {
        public static ConfigEntry<float> MiningMultiplier;
        public static ConfigEntry<int> SmeltingMultiplier;

        internal static void BindMining(ConfigFile config, string section, FeatureScope scope)
        {
            MiningMultiplier = config.Bind(section, "MiningMultiplier", 2f,
                ConfigText.Description(
                    "How much harder you hit rock and ore in a biome whose boss you have killed. 2 means twice "
                    + "the damage, so half the swings.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 10f)));
        }

        internal static void BindSmelting(ConfigFile config, string section, FeatureScope scope)
        {
            SmeltingMultiplier = config.Bind(section, "SmeltingMultiplier", 2,
                ConfigText.Description(
                    "How many bars one ore yields once the boss of the biome it comes from is dead. 2 doubles "
                    + "the output for the same ore and fuel.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<int>(1, 10)));
        }
    }
}
