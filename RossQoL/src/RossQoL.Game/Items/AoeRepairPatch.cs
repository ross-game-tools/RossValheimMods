using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Player.Repair repairs the piece under the crosshair and charges one
    /// swing of stamina and durability. Afterwards every other damaged piece
    /// within RepairRadius of that piece is repaired too, for no extra cost.
    ///
    /// Each one goes through vanilla's own WearNTear.Repair, which ignores
    /// undamaged pieces, keeps its own one-second cooldown, and asks the
    /// piece's owner to set the health, so pieces another player's game
    /// looks after are repaired the same way.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Repair")]
    internal static class AoeRepairPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), "Repair", AoeRepairFeature.FeatureName);

        private static void Postfix(Player __instance)
        {
            if (AoeRepairFeature.Instance?.IsActive != true) return;

            try
            {
                var hovering = __instance.GetHoveringPiece();
                if (hovering == null) return;

                float radius = WorldConfig.RepairRadius?.Value ?? 15f;
                float limit = radius * radius;
                var centre = hovering.transform.position;
                var hoveringWear = hovering.GetComponent<WearNTear>();

                int repaired = 0;
                var all = WearNTear.s_allInstances;
                for (int i = 0; i < all.Count; i++)
                {
                    var wear = all[i];
                    if (wear == null || wear == hoveringWear) continue;
                    if ((wear.transform.position - centre).sqrMagnitude > limit) continue;
                    if (!PrivateArea.CheckAccess(wear.transform.position, 0f, flash: false)) continue;

                    if (wear.Repair()) repaired++;
                }

                if (repaired > 0)
                    __instance.Message(MessageHud.MessageType.TopLeft, $"Repaired {repaired} more piece(s) nearby");
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"AoeRepair: repairing nearby pieces failed: {ex}");
            }
        }
    }
}
