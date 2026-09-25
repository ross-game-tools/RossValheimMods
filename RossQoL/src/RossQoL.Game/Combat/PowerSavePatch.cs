using System;
using HarmonyLib;
using RossQoL.Core.Combat;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// The second-power slot is otherwise written only when it changes (a stone
    /// assignment) or the power fires -- which captures its cooldown at FULL
    /// length. The per-frame <see cref="PowerActivationPatch"/> ticks that
    /// cooldown down in memory but never re-writes it, so vanilla's
    /// <see cref="Player.Save"/> would serialise the stale, full value and a
    /// relog would restore a cooldown that had almost expired.
    ///
    /// This prefix runs just before vanilla serialises the local player, writing
    /// the CURRENT remaining cooldown (and the power) into <c>m_customData</c>
    /// so what is saved matches what the player sees. It acts for
    /// <see cref="Player.m_localPlayer"/> only -- other players' saves are none
    /// of this client's business -- and its body can never throw into
    /// <see cref="Player.Save"/>.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Save")]
    internal static class PowerSavePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), "Save", MultiplePowersFeature.FeatureName);

        private static void Prefix(Player __instance)
        {
            if (MultiplePowersFeature.Instance?.IsActive != true) return;
            if (__instance == null || !ReferenceEquals(__instance, Player.m_localPlayer)) return;

            try
            {
                SecondPower slot = PlayerPowers.ForLocalPlayer();
                PlayerPowers.Persist(__instance, slot);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"MultiplePowers: could not persist live power cooldowns before save: {ex}");
            }
        }
    }
}
