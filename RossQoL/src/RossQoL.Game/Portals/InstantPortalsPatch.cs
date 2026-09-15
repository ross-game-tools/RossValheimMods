using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// Marks a teleport as a portal trip: TeleportWorld.Teleport calls
    /// Player.TeleportTo, and the local player is teleporting afterwards only
    /// when that call started a new teleport.
    /// </summary>
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class InstantPortalStartPatch
    {
        /// <summary>True from a portal starting the local player's teleport until that teleport ends.</summary>
        internal static bool PortalTeleport;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(TeleportWorld), nameof(TeleportWorld.Teleport), InstantPortalsFeature.FeatureName);

        private static void Prefix(ref bool __state)
        {
            var player = Player.m_localPlayer;
            __state = player != null && player.IsTeleporting();
        }

        private static void Postfix(bool __state)
        {
            if (InstantPortalsFeature.Instance?.IsActive != true) return;

            var player = Player.m_localPlayer;
            if (player == null || __state || !player.IsTeleporting()) return;

            PortalTeleport = true;
        }
    }

    /// <summary>
    /// Player.UpdateTeleport waits until m_teleportTimer passes 2 s before
    /// moving the player, and for a distant teleport (every portal) until it
    /// passes 8 s before letting them arrive, however quickly the destination
    /// loads. For a portal trip the timer starts just past both, so the
    /// player moves at once and arrives as soon as vanilla's own checks pass:
    /// the destination area is ready and a floor is found. Vanilla's
    /// no-floor fallback at 15 s still applies, about 7 s after the move.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.UpdateTeleport))]
    internal static class InstantPortalUpdatePatch
    {
        private const float PastFixedDelays = 8.01f;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), nameof(Player.UpdateTeleport), InstantPortalsFeature.FeatureName);

        private static void Prefix(Player __instance)
        {
            if (!InstantPortalStartPatch.PortalTeleport) return;

            try
            {
                if (__instance != Player.m_localPlayer) return;

                if (!__instance.m_teleporting || InstantPortalsFeature.Instance?.IsActive != true)
                {
                    InstantPortalStartPatch.PortalTeleport = false;
                    return;
                }

                if (__instance.m_teleportTimer < PastFixedDelays) __instance.m_teleportTimer = PastFixedDelays;
            }
            catch (Exception ex)
            {
                InstantPortalStartPatch.PortalTeleport = false;
                RossQoLPlugin.Log.LogError($"InstantPortals: skipping the teleport delay failed: {ex}");
            }
        }
    }
}
