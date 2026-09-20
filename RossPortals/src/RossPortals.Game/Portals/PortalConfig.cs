using BepInEx.Configuration;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// The mod's only persisted setting: the folder separator used inside portal
    /// names. Client-side and personal — it changes how YOUR list is grouped,
    /// nothing about the world. Sort order is deliberately NOT a config entry:
    /// it has an on-screen control, and the repo's rule is one source of truth,
    /// not a file value that drifts from the button next to it.
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
