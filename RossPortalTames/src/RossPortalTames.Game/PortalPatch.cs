using HarmonyLib;

namespace RossPortalTames.Game
{
    /// <summary>
    /// Notices that the local player has used a portal.
    ///
    /// A POSTFIX with no injected parameters, which is deliberate on both
    /// counts. Postfix, because vanilla's Teleport decides whether a teleport
    /// actually happens -- a portal with no destination (XPortal's unconfigured
    /// state, among others) simply does not teleport anyone, and running before
    /// that decision would capture tames for a journey that never occurs.
    ///
    /// No parameters, because injecting `Player player` couples this patch to
    /// the exact parameter name in Valheim's signature, which a game update can
    /// change without removing the method -- the quiet kind of break. Asking
    /// Player.m_localPlayer whether IT is now teleporting answers the only
    /// question that matters (did MY player just go through?) and is immune to
    /// that.
    /// </summary>
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class PortalPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(TeleportWorld), nameof(TeleportWorld.Teleport));

        private static void Postfix()
        {
            if (!PortalTamesConfig.Enabled.Value) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            // The teleport is in progress exactly when it was accepted. If the
            // portal refused -- no destination, or vanilla declined for any
            // other reason -- there is nothing to follow.
            if (!player.IsTeleporting()) return;

            PortalTamesManager.Instance?.CaptureDeparture();
        }
    }
}
