using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Inside WearNTear.UpdateWear, rain damage is the only thing gated by
    /// m_noRoofWear (docs/valheim-api/wear-and-tear.md): support-loss
    /// collapse, DeepNorth snow, AshLands ash/lava, persistent-event damage
    /// and required-biome damage each read their own, unrelated fields.
    /// Clearing the instance's m_noRoofWear for the length of one call turns
    /// only the rain branch off; the finalizer puts it back afterwards, even
    /// if UpdateWear throws, so an error elsewhere in the method can never
    /// leave a piece permanently weatherproofed.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
    internal static class NoWeatheringPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(WearNTear), nameof(WearNTear.UpdateWear), NoWeatheringFeature.FeatureName);

        private static void Prefix(WearNTear __instance, ref bool __state)
        {
            __state = false;
            if (NoWeatheringFeature.Instance?.IsActive != true) return;

            try
            {
                __state = __instance.m_noRoofWear;
                __instance.m_noRoofWear = false;
            }
            catch (Exception ex)
            {
                __state = false;
                RossQoLPlugin.Log.LogError($"NoWeathering: could not suppress rain wear: {ex}");
            }
        }

        // __state carries the flag's original value: true means we cleared
        // it and must put it back, false means either we never touched it
        // (feature off, or Prefix failed before writing) or it was already
        // false to begin with, in which case there is nothing to restore.
        private static void Finalizer(WearNTear __instance, bool __state)
        {
            try
            {
                if (__state && __instance) __instance.m_noRoofWear = true;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"NoWeathering: could not restore m_noRoofWear: {ex}");
            }
        }
    }
}
