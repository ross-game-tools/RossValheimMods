using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// The shield generator burns through cores the same way a fire burns
    /// wood, and is the one building where running dry costs a base rather
    /// than a batch. It takes any of the fuels its own prefab lists.
    /// </summary>
    [HarmonyPatch(typeof(ShieldGenerator), "UpdateShield")]
    internal static class ShieldGeneratorFeedPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ShieldGenerator), "UpdateShield", AutoFeedFeature.FeatureName);

        private static void Postfix(ShieldGenerator __instance)
        {
            if (AutoFeedFeature.Instance?.IsActive != true) return;

            // An exception escaping here would stop the shield updating.
            try
            {
                if (__instance.m_fuelItems == null || __instance.m_fuelItems.Count == 0) return;
                if (!FeedGate.ShouldRun(__instance, __instance.m_nview, AutoFeedFeature.Instance, AutoFeedConfig.FeedShieldGenerators)) return;

                Feed(__instance);
                FeedGate.Succeeded(__instance);
            }
            catch (Exception ex)
            {
                FeedGate.LogFailure(__instance, ex);
            }
        }

        private static void Feed(ShieldGenerator generator)
        {
            float fuel = generator.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel, generator.m_defaultFuel);
            if (generator.m_maxFuel - Mathf.CeilToInt(fuel) <= 0) return;

            var origin = generator.transform.position;
            foreach (var fuelItem in generator.m_fuelItems)
            {
                if (fuelItem == null) continue;

                // One core per attempt, whichever of its fuels comes first.
                if (ContainerSource.Take(origin, fuelItem, 1, out _) <= 0) continue;

                generator.m_nview.InvokeRPC("RPC_AddFuel");
                return;
            }
        }
    }
}
