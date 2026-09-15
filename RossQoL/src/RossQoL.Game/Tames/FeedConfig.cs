using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>FeedFromContainers' settings beyond its toggle. Read at use, so live reloads apply.</summary>
    public static class FeedConfig
    {
        public static ConfigEntry<float> FeedRadius;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            FeedRadius = config.Bind(section, "FeedRadius", 10f,
                ConfigText.Description(
                    "How far from a hungry tame, in metres, to look for food in containers. Measured in three dimensions.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 50f)));
        }
    }
}
