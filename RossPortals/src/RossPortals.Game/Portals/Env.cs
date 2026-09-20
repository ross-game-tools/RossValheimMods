using Jotunn.Managers;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// Thin questions about the running instance the portal engine keeps
    /// asking: are we the server, are we headless, is the game up, who is the
    /// server peer. Kept in one place so the RPC code reads cleanly.
    /// </summary>
    internal static class Env
    {
        /// <summary>True when ZNet says this instance is the authority
        /// (dedicated server, or the host of a local game).</summary>
        public static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        /// <summary>True on a dedicated server with no graphics — never build
        /// UI or read input there.</summary>
        public static bool IsHeadless => GUIManager.IsHeadless();

        /// <summary>Set true by the Game.Start patch; the portal list only
        /// exists once a world is loading.</summary>
        public static bool GameStarted { get; set; }

        /// <summary>Logout/quit is in progress. Guards against touching the
        /// portal list while the world is being torn down.</summary>
        public static bool ShuttingDown => global::Game.instance != null && global::Game.instance.m_shuttingDown;

        public static long ServerPeerId => ZRoutedRpc.instance.GetServerPeerID();
    }
}
