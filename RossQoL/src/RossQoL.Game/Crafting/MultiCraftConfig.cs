using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// MultiCraft's settings beyond its toggle. Read live, so no restart.
    /// </summary>
    public static class MultiCraftConfig
    {
        public const int MinAmount = 2;
        public const int MaxAmount = 100;

        public static ConfigEntry<int> MultiCraftAmount;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            MultiCraftAmount = config.Bind(section, "MultiCraftAmount", 10,
                ConfigText.Description(
                    "The number the craft-many box starts at. Type any amount over it; the craft costs the "
                    + "materials for every one, and stops if they will not fit in your inventory.",
                    scope, requiresRestart: false,
                    range: new AcceptableValueRange<int>(MinAmount, MaxAmount)));
        }
    }
}
