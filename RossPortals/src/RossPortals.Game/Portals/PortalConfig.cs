using BepInEx.Configuration;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// The mod's only persisted setting: the folder separator used inside portal
    /// names. Client-side and personal — it changes how YOUR list is grouped,
    /// nothing about the world. Everything else (destination, default flag,
    /// show-on-map flag) is a per-portal property stored in the portal's ZDO and
    /// shared across the server, not a config value.
    /// </summary>
    internal static class PortalConfig
    {
        private static ConfigEntry<string> _separator;

        public static void Bind(ConfigFile config)
        {
            _separator = config.Bind(
                "Grouping", "FolderSeparator", "/",
                "The character in a portal's name that starts a folder. "
                + "\"Mines/Copper\" puts portal \"Copper\" in folder \"Mines\". "
                + "Client-side; affects only how your own list is grouped.");
        }

        /// <summary>First character of the configured separator, falling back to
        /// '/' if it was cleared to empty in the file.</summary>
        public static char Separator
        {
            get
            {
                var s = _separator?.Value;
                return string.IsNullOrEmpty(s) ? Core.PortalName.DefaultSeparator : s[0];
            }
        }
    }
}
