using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// The Items category's settings beyond its toggles. Read when item
    /// definitions are built, so a change applies on the next world load.
    /// </summary>
    public static class ItemsConfig
    {
        public static ConfigEntry<int> MeadBaseStackSize;

        internal static void BindStacking(ConfigFile config, string section, FeatureScope scope)
        {
            MeadBaseStackSize = config.Bind(section, "MeadBaseStackSize", 20,
                ConfigText.Description(
                    "How many mead bases and barley wine bases fit in one slot. Vanilla is 1. Unstack them "
                    + "before turning StackableMeadBases off, or a stack larger than vanilla allows is left "
                    + "in your chest.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<int>(1, 100)));
        }
    }
}
