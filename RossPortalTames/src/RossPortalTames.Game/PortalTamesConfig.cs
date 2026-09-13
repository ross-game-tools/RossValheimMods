using BepInEx.Configuration;

namespace RossPortalTames.Game
{
    /// <summary>
    /// Three knobs, all local to this client.
    ///
    /// Not server-synced, and that is a consequence of having no Jotunn
    /// dependency rather than an oversight. It is acceptable because every
    /// setting here only affects which of YOUR OWN tames follow YOU: there is
    /// nothing a client can grant itself that it could not already do by
    /// walking its wolves to the portal.
    /// </summary>
    public static class PortalTamesConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> FollowRadius;
        public static ConfigEntry<float> SearchDistance;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Tames following you come through portals with you.");

            FollowRadius = config.Bind("General", "FollowRadius", 20f,
                "How close a following tame must be, in metres, to come along. "
                + "Measured in three dimensions, so a tame on a roof is as far as one across the ground. "
                + "Set to 0 to bring nothing.");

            SearchDistance = config.Bind("General", "SearchDistance", 6f,
                "How far from your arrival point to look for a clear spot to place each tame. "
                + "Anything that cannot be placed within this distance is put at your own position "
                + "instead, where creatures separate themselves naturally. Lower it for tight portal huts.");
        }
    }
}
