using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// Procreation.Procreate plays m_birthEffects at the newborn, on the
    /// parent's owner. For the length of the call the list is swapped for a
    /// copy without its sound entries (effect prefabs carrying a ZSFX),
    /// then restored, so no prefab is changed and
    /// switching the feature off takes effect on the next birth.
    /// </summary>
    [HarmonyPatch(typeof(Procreation), nameof(Procreation.Procreate))]
    internal static class SilentBirthsPatch
    {
        // One silent copy per original list; prefabs share their lists.
        private static readonly ConditionalWeakTable<EffectList, EffectList> SilentCopies =
            new ConditionalWeakTable<EffectList, EffectList>();

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Procreation), nameof(Procreation.Procreate), SilentBirthsFeature.FeatureName);

        private static void Prefix(Procreation __instance, ref EffectList __state)
        {
            __state = null;
            if (SilentBirthsFeature.Instance?.IsActive != true) return;

            try
            {
                var original = __instance.m_birthEffects;
                if (original == null) return;

                __state = original;
                __instance.m_birthEffects = SilentCopies.GetValue(original, MakeSilent);
            }
            catch (Exception ex)
            {
                __state = null;
                RossQoLPlugin.Log.LogError($"SilentBirths: preparing a silent birth failed: {ex}");
            }
        }

        private static void Finalizer(Procreation __instance, EffectList __state)
        {
            if (__state != null && __instance) __instance.m_birthEffects = __state;
        }

        private static EffectList MakeSilent(EffectList original)
        {
            var effects = original.m_effectPrefabs ?? Array.Empty<EffectList.EffectData>();
            return new EffectList
            {
                m_effectPrefabs = effects.Where(e => e == null || !IsSound(e.m_prefab)).ToArray(),
            };
        }

        // Valheim plays every sound effect through ZSFX.
        private static bool IsSound(GameObject prefab) =>
            prefab != null && prefab.GetComponentInChildren<ZSFX>(true) != null;
    }
}
