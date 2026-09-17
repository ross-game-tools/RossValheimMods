using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using RossQoL.Game.Production;
using UnityEngine;

namespace RossQoL.Game.Fires
{
    /// <summary>
    /// Fires burn down in Fireplace.UpdateFireplace, which ticks every two
    /// seconds on the owner. Feeding rides the same tick and hands fuel over
    /// with vanilla's RPC, one piece at a time as a player would.
    ///
    /// Covers everything built on Fireplace: fire pits, hearths, standing and
    /// wall torches, braziers and the bathtub.
    /// </summary>
    [HarmonyPatch(typeof(Fireplace), "UpdateFireplace")]
    internal static class FireplaceFeedPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Fireplace), "UpdateFireplace", FiresFeedFeature.FeatureName);

        private static void Postfix(Fireplace __instance)
        {
            if (FiresFeedFeature.Instance?.IsActive != true) return;

            // An exception escaping here would stop this fire's update.
            try
            {
                if (!FeedGate.ShouldRun(__instance, __instance.m_nview, FiresFeedFeature.Instance)) return;

                Feed(__instance);
                FeedGate.Succeeded(__instance);
            }
            catch (Exception ex)
            {
                FeedGate.LogFailure(__instance, ex);
            }
        }

        private static void Feed(Fireplace fireplace)
        {
            // A fire that burns nothing has nothing to feed. A fire that is
            // full and held there by InfiniteFuel has no room either, and
            // vanilla refuses fuel while that switch is on.
            if (fireplace.m_fuelItem == null || fireplace.m_infiniteFuel) return;

            float fuel = fireplace.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel);
            if (Mathf.FloorToInt(fireplace.m_maxFuel) - Mathf.CeilToInt(fuel) <= 0) return;

            // One piece per attempt, as a player feeding a fire would.
            if (ContainerSource.Take(fireplace.transform.position, fireplace.m_fuelItem, 1, out _) > 0)
                fireplace.m_nview.InvokeRPC("RPC_AddFuel");
        }
    }
}
