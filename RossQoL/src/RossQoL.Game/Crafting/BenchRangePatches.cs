using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RossQoL.Core.Production;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// A station's reach is m_rangeBuild, which GetExtensions turns into the
    /// m_buildRange that everything else reads: the placement check, the area
    /// marker and the effect collider. It recalculates every two seconds, so
    /// writing m_rangeBuild just before that leaves one lever that moves all
    /// of them, and a config edit shows up within a tick rather than needing
    /// a reload.
    ///
    /// Each station's own prefab value is remembered the first time it is
    /// seen, so switching the feature off puts back exactly what it shipped
    /// with, whatever mod or prefab set it.
    /// </summary>
    [HarmonyPatch(typeof(CraftingStation), "GetExtensions")]
    internal static class BenchRangePatch
    {
        // Keyed by the instance, so an entry dies with its station.
        private static readonly ConditionalWeakTable<CraftingStation, StrongBox<float>> Original =
            new ConditionalWeakTable<CraftingStation, StrongBox<float>>();

        private static string _perTypeText;
        private static Dictionary<string, int> _perType = new Dictionary<string, int>(FeedRules.NameComparer);

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(CraftingStation), "GetExtensions", BenchRangeFeature.FeatureName);

        private static void Prefix(CraftingStation __instance)
        {
            try
            {
                float vanilla = Remember(__instance);

                if (BenchRangeFeature.Instance?.IsActive != true)
                {
                    __instance.m_rangeBuild = vanilla;
                    return;
                }

                __instance.m_rangeBuild = RangeFor(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"BenchRange: leaving this station's range alone: {ex}");
            }
        }

        private static float Remember(CraftingStation station)
        {
            if (Original.TryGetValue(station, out var seen)) return seen.Value;

            Original.Add(station, new StrongBox<float>(station.m_rangeBuild));
            return station.m_rangeBuild;
        }

        private static float RangeFor(CraftingStation station)
        {
            string prefab = Utils.GetPrefabName(station.gameObject);
            if (PerType().TryGetValue(prefab, out int own)) return own;

            return BenchRangeConfig.BuildRange?.Value ?? 40f;
        }

        private static Dictionary<string, int> PerType()
        {
            string text = BenchRangeConfig.BuildRangePerType?.Value ?? string.Empty;
            if (text != _perTypeText)
            {
                _perType = FeedRules.ParseAmounts(text);
                _perTypeText = text;
            }
            return _perType;
        }
    }

    /// <summary>
    /// An attachment has to sit within m_maxStationDistance of its station, a
    /// plain field read from several places. It is set as each attachment
    /// wakes, which is why the setting says a reload is needed for the ones
    /// already built around you.
    /// </summary>
    [HarmonyPatch(typeof(StationExtension), nameof(StationExtension.Awake))]
    internal static class ExtensionRangePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(StationExtension), nameof(StationExtension.Awake), BenchRangeFeature.FeatureName);

        private static void Postfix(StationExtension __instance)
        {
            if (BenchRangeFeature.Instance?.IsActive != true) return;

            // An exception escaping here would break every attachment's Awake.
            try
            {
                float range = BenchRangeConfig.ExtensionRange?.Value ?? 10f;
                if (range > __instance.m_maxStationDistance) __instance.m_maxStationDistance = range;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"BenchRange: leaving this attachment's distance alone: {ex}");
            }
        }
    }
}
