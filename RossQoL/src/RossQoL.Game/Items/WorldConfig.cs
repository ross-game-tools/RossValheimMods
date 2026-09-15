using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// FloatingItems' settings beyond its toggle. Read when an item spawns.
    /// The namespace is Items, not World: Valheim has its own World class,
    /// and a RossQoL.Game.World namespace hides it from every file here.
    /// </summary>
    public static class WorldConfig
    {
        public static ConfigEntry<float> FloatDepth;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            FloatDepth = config.Bind(section, "FloatDepth", 0.4f,
                ConfigText.Description(
                    "How deep a floating item sits, in metres below the surface. Higher sinks it further.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 2f)));
        }
    }
}
