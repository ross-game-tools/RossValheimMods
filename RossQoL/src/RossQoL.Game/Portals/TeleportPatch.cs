using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// Notices that the local player has been teleported, by whatever means.
    ///
    /// Patched on Player.TeleportTo rather than on TeleportWorld.Teleport,
    /// because that one method is the whole of Valheim's teleporting. A portal
    /// calls it (TeleportWorld.Teleport -> player.TeleportTo(pos, rot,
    /// distantTeleport: true)) and so does a dungeon door (Teleport.Interact ->
    /// character.TeleportTo(m_targetPoint.GetTeleportPoint(), ...,
    /// distantTeleport: false)); both directions of a dungeon door share that
    /// single call, which branches on Character.InInterior() only to pick which
    /// stat to increment. Patching the shared method means followers come with
    /// you through a crypt door exactly as they do through a portal, and means
    /// there is one capture path to get right rather than two.
    ///
    /// POSTFIX on the return value, which is deliberate. Player.TeleportTo
    /// returns false for every refusal -- not the owner, already teleporting,
    /// still inside the two-second cooldown -- and true only once m_teleporting
    /// has actually been set. Running before that decision, or ignoring the
    /// result, would capture tames for a journey that never happens.
    ///
    /// __instance and __result are Harmony's own names for the receiver and the
    /// return value, not Valheim's parameter names, so this does not couple the
    /// patch to a signature a game update could rename underneath it. The
    /// instance is still checked against Player.m_localPlayer, because
    /// RPC_TeleportTo can drive this method for a player object this client
    /// happens to own without that player being the one at the keyboard.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
    internal static class TeleportPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), nameof(Player.TeleportTo), "Portals/TamesFollow");

        private static void Postfix(Player __instance, bool __result)
        {
            try
            {
                // False means vanilla declined the teleport. Nothing left, so
                // nothing to follow.
                if (!__result) return;

                if (TamesFollowFeature.Instance?.IsActive != true) return;

                var player = Player.m_localPlayer;
                if (player == null || __instance == null) return;
                if (__instance != player) return;

                var manager = PortalTamesManager.Instance;
                if (manager == null) return;

                manager.CaptureDeparture();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"TamesFollow: could not capture your followers for this teleport: {ex}");
            }
        }
    }
}
