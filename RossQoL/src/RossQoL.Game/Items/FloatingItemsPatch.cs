using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Gives a dropped item vanilla's own Floating component as it wakes, so
    /// it behaves exactly like wood in water: buoyancy on the owner's
    /// machine, splash effects, and vanilla's own tar handling.
    ///
    /// Left alone: items that already float (their prefab has Floating), a
    /// live fish (its prefab carries an ItemDrop too, and it swims by its own
    /// rules), and anything with no body or collider for buoyancy to act on.
    /// </summary>
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Awake))]
    internal static class FloatingItemsPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ItemDrop), nameof(ItemDrop.Awake), FloatingItemsFeature.FeatureName);

        private static void Postfix(ItemDrop __instance)
        {
            if (FloatingItemsFeature.Instance?.IsActive != true) return;

            // An exception escaping here would break every item that spawns.
            try
            {
                if (__instance.GetComponent<Floating>() != null) return;
                if (__instance.GetComponent<Fish>() != null) return;
                if (__instance.GetComponent<Rigidbody>() == null) return;
                if (__instance.GetComponentInChildren<Collider>() == null) return;

                var floating = __instance.gameObject.AddComponent<Floating>();
                floating.m_waterLevelOffset = WorldConfig.FloatDepth?.Value ?? 0.4f;

                // The field vanilla's own Awake read before this component
                // existed; ItemDrop.InTar and others go through it.
                __instance.m_floating = floating;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"FloatingItems: could not make {__instance.name} float: {ex}");
            }
        }
    }
}
