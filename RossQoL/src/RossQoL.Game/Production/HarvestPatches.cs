using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RossQoL.Core.Production;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Whether a producer harvests on this call: feature on, kind on, a
    /// local player, the producer owned by this client, and HarvestInterval
    /// passed since its last attempt. Config is read here on every call.
    /// </summary>
    internal static class HarvestGate
    {
        // Keyed by the instance itself, so an entry dies with its producer.
        private static readonly ConditionalWeakTable<MonoBehaviour, StrongBox<double>> LastAttempt =
            new ConditionalWeakTable<MonoBehaviour, StrongBox<double>>();

        public static bool ShouldRun(MonoBehaviour producer, ZNetView nview, ConfigEntry<bool> kindEnabled)
        {
            if (AutoHarvestFeature.Instance?.IsActive != true) return false;
            if (kindEnabled == null || !kindEnabled.Value) return false;
            if (Player.m_localPlayer == null) return false;
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return false;

            double now = Time.time;
            float interval = ProductionConfig.HarvestInterval?.Value ?? 10f;
            bool seen = LastAttempt.TryGetValue(producer, out var last);
            if (!HarvestMath.IsDue(seen ? last.Value : (double?)null, now, interval)) return false;

            if (seen) last.Value = now;
            else LastAttempt.Add(producer, new StrongBox<double>(now));
            return true;
        }

        // The exception type last logged per producer, so a failure that
        // repeats every interval is logged once, not once per attempt.
        private static readonly ConditionalWeakTable<MonoBehaviour, StrongBox<string>> LastFailure =
            new ConditionalWeakTable<MonoBehaviour, StrongBox<string>>();

        /// <summary>
        /// Logs the full exception the first time a producer fails with it;
        /// silent for that producer until a different exception type or a
        /// harvest that completes (Succeeded).
        /// </summary>
        public static void LogFailure(MonoBehaviour producer, ZNetView nview, Exception ex)
        {
            string type = ex.GetType().FullName;
            if (producer != null)
            {
                if (LastFailure.TryGetValue(producer, out var last))
                {
                    if (last.Value == type) return;
                    last.Value = type;
                }
                else
                {
                    LastFailure.Add(producer, new StrongBox<string>(type));
                }
            }

            string name = producer ? producer.name : "a destroyed producer";
            string id = nview != null && nview.GetZDO() != null ? nview.GetZDO().m_uid.ToString() : "no ZDO";
            RossQoLPlugin.Log.LogError($"AutoHarvest: harvesting {name} ({id}) failed and was skipped (repeats of this error are not logged): {ex}");
        }

        /// <summary>A harvest attempt completed: the next failure is logged in full again.</summary>
        public static void Succeeded(MonoBehaviour producer)
        {
            LastFailure.Remove(producer);
        }
    }

    /// <summary>
    /// Beehive.UpdateBees runs every 10 s on every peer (InvokeRepeating
    /// from Awake) and adds honey levels on the owner. The postfix runs
    /// after that, so a level added this tick is harvested this tick.
    /// </summary>
    [HarmonyPatch(typeof(Beehive), nameof(Beehive.UpdateBees))]
    internal static class BeehiveHarvestPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Beehive), nameof(Beehive.UpdateBees), AutoHarvestFeature.FeatureName);

        private static void Postfix(Beehive __instance)
        {
            if (AutoHarvestFeature.Instance?.IsActive != true) return;

            // An exception escaping here would stop this beehive's update.
            try
            {
                if (!HarvestGate.ShouldRun(__instance, __instance.m_nview, ProductionConfig.HarvestBeehives)) return;
                Harvester.HarvestBeehive(__instance);
                HarvestGate.Succeeded(__instance);
            }
            catch (Exception ex)
            {
                HarvestGate.LogFailure(__instance, __instance.m_nview, ex);
            }
        }
    }

    /// <summary>SapCollector.UpdateTick runs every 5 s on every peer and adds sap levels on the owner.</summary>
    [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.UpdateTick))]
    internal static class SapCollectorHarvestPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(SapCollector), nameof(SapCollector.UpdateTick), AutoHarvestFeature.FeatureName);

        private static void Postfix(SapCollector __instance)
        {
            if (AutoHarvestFeature.Instance?.IsActive != true) return;

            try
            {
                if (!HarvestGate.ShouldRun(__instance, __instance.m_nview, ProductionConfig.HarvestSapCollectors)) return;
                Harvester.HarvestSapCollector(__instance);
                HarvestGate.Succeeded(__instance);
            }
            catch (Exception ex)
            {
                HarvestGate.LogFailure(__instance, __instance.m_nview, ex);
            }
        }
    }

    /// <summary>Fermenter.SlowUpdate runs every 2 s on every peer; readiness is read from the ZDO.</summary>
    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.SlowUpdate))]
    internal static class FermenterHarvestPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Fermenter), nameof(Fermenter.SlowUpdate), AutoHarvestFeature.FeatureName);

        private static void Postfix(Fermenter __instance)
        {
            if (AutoHarvestFeature.Instance?.IsActive != true) return;

            try
            {
                if (!HarvestGate.ShouldRun(__instance, __instance.m_nview, ProductionConfig.HarvestFermenters)) return;
                Harvester.HarvestFermenter(__instance);
                HarvestGate.Succeeded(__instance);
            }
            catch (Exception ex)
            {
                HarvestGate.LogFailure(__instance, __instance.m_nview, ex);
            }
        }
    }
}
