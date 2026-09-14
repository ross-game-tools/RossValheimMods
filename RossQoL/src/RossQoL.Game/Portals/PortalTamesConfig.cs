using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// TamesFollow's settings beyond its toggle. Read live, so no restart.
    /// </summary>
    public static class PortalTamesConfig
    {
        public static ConfigEntry<float> FollowRadius;
        public static ConfigEntry<float> SearchDistance;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            FollowRadius = config.Bind(section, "TameFollowRadius", 20f,
                ConfigText.Description(
                    "How close a following tame must be, in metres, to come along. "
                    + "Measured in three dimensions, so a tame on a roof is as far as one across the ground. "
                    + "Set to 0 to bring nothing.",
                    scope, requiresRestart: false));

            SearchDistance = config.Bind(section, "TameSearchDistance", 6f,
                ConfigText.Description(
                    "How far from your arrival point to look for a clear spot to place each tame. "
                    + "Anything that cannot be placed within this distance is put at your own position "
                    + "instead, where creatures separate themselves naturally. Lower it for tight portal huts.",
                    scope, requiresRestart: false));
        }
    }
}
