using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Ovens -- and the Deep North Frost Foundry -- take their fuel from nearby
    /// containers. Patched on CookingStation.UpdateCooking, NOT UpdateFuel:
    /// UpdateCooking is the 1 Hz InvokeRepeating tick that always runs, whereas
    /// it only calls UpdateFuel once the station already holds fuel
    /// (GetFuel() > 0). Hooking UpdateFuel could therefore never fill an EMPTY
    /// station -- exactly what a Frost Foundry starts as -- so it was fed only
    /// if a player primed it by hand first. Only the fuel is fed: what to cook
    /// is a choice, and a station filled with whatever was nearest is worse
    /// than an empty one.
    /// </summary>
    [HarmonyPatch(typeof(CookingStation), "UpdateCooking")]
    internal static class CookingStationFeedPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(CookingStation), "UpdateCooking", AutoFeedFeature.FeatureName);

        private static void Postfix(CookingStation __instance)
        {
            if (AutoFeedFeature.Instance?.IsActive != true) return;

            // An exception escaping here would stop this oven's update.
            try
            {
                if (!__instance.m_useFuel || __instance.m_fuelItem == null) return;
                if (!FeedGate.ShouldRun(__instance, __instance.m_nview, AutoFeedFeature.Instance, AutoFeedConfig.FeedOvens)) return;

                Feed(__instance);
                FeedGate.Succeeded(__instance);
            }
            catch (Exception ex)
            {
                FeedGate.LogFailure(__instance, ex);
            }
        }

        private static void Feed(CookingStation oven)
        {
            if (oven.m_maxFuel - Mathf.CeilToInt(oven.GetFuel()) <= 0) return;

            // One piece per attempt, as a player at the switch does it.
            if (ContainerSource.Take(oven.transform.position, oven.m_fuelItem, 1, out _) > 0)
                oven.m_nview.InvokeRPC("RPC_AddFuel");
        }
    }
}
