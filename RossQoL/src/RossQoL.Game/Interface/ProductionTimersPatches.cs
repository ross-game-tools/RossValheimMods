using System;
using HarmonyLib;
using RossQoL.Core.Interface;
using UnityEngine;
using ValheimCompat = RossQoL.Game.Framework.ValheimCompat;

namespace RossQoL.Game.Interface
{
    /// <summary>
    /// Shared text for the three producer hover patches. Every value read
    /// here is on the producer's ZDO, which every client has, so the
    /// countdown is the same for everyone.
    /// </summary>
    internal static class ProductionTimerText
    {
        /// <summary>
        /// A level counter's lines: time to the next unit, and to the cap.
        /// Empty while the producer is full or its rate is unusable.
        /// </summary>
        public static string ForLevels(ZNetView nview, float secPerUnit, int maxLevel, string unit)
        {
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null || secPerUnit <= 0f) return string.Empty;

            int level = zdo.GetInt(ZDOVars.s_level);
            if (level >= maxLevel) return string.Empty;

            double product = zdo.GetFloat(ZDOVars.s_product);
            string next = ProductionTimer.Format(ProductionTimer.SecondsToNextUnit(product, secPerUnit));
            string full = ProductionTimer.Format(ProductionTimer.SecondsToFull(product, secPerUnit, level, maxLevel));

            string text = $"\nNext {unit} in {next}";
            if (level + 1 < maxLevel) text += $", full in {full}";
            return text;
        }

        /// <summary>A fermenter's line, while a batch is still brewing.</summary>
        public static string ForFermenter(ZNetView nview, float duration)
        {
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null || duration <= 0f) return string.Empty;

            long start = zdo.GetLong(ZDOVars.s_startTime, 0L);
            double remaining = ProductionTimer.SecondsUntilReady(start, ZNet.instance.GetTime().Ticks, duration);
            if (remaining < 0d) return string.Empty;
            if (remaining <= 0d) return string.Empty;

            return $"\nReady in {ProductionTimer.Format(remaining)}";
        }

        /// <summary>Hover text is built every frame; a failure must not spam the log.</summary>
        public static void LogOnce(ref bool logged, string what, Exception ex)
        {
            if (logged) return;
            logged = true;
            RossQoLPlugin.Log.LogError($"ProductionTimers: could not add the {what} countdown: {ex}");
        }
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
    internal static class BeehiveTimerPatch
    {
        private static bool _logged;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Beehive), nameof(Beehive.GetHoverText), ProductionTimersFeature.FeatureName);

        private static void Postfix(Beehive __instance, ref string __result)
        {
            if (ProductionTimersFeature.Instance?.IsActive != true) return;
            if (!PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false)) return;

            try
            {
                __result += Localization.instance.Localize(
                    ProductionTimerText.ForLevels(__instance.m_nview, __instance.m_secPerUnit, __instance.m_maxHoney, "honey"));
            }
            catch (Exception ex)
            {
                ProductionTimerText.LogOnce(ref _logged, "beehive", ex);
            }
        }
    }

    [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.GetHoverText))]
    internal static class SapCollectorTimerPatch
    {
        private static bool _logged;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(SapCollector), nameof(SapCollector.GetHoverText), ProductionTimersFeature.FeatureName);

        private static void Postfix(SapCollector __instance, ref string __result)
        {
            if (ProductionTimersFeature.Instance?.IsActive != true) return;

            try
            {
                __result += Localization.instance.Localize(
                    ProductionTimerText.ForLevels(__instance.m_nview, __instance.m_secPerUnit, __instance.m_maxLevel, "sap"));
            }
            catch (Exception ex)
            {
                ProductionTimerText.LogOnce(ref _logged, "sap collector", ex);
            }
        }
    }

    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
    internal static class FermenterTimerPatch
    {
        private static bool _logged;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Fermenter), nameof(Fermenter.GetHoverText), ProductionTimersFeature.FeatureName);

        private static void Postfix(Fermenter __instance, ref string __result)
        {
            if (ProductionTimersFeature.Instance?.IsActive != true) return;
            if (!PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false)) return;

            try
            {
                __result += Localization.instance.Localize(
                    ProductionTimerText.ForFermenter(__instance.m_nview, __instance.m_fermentationDuration));
            }
            catch (Exception ex)
            {
                ProductionTimerText.LogOnce(ref _logged, "fermenter", ex);
            }
        }
    }
}
