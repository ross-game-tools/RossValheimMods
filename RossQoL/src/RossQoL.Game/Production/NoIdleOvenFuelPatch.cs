using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Vanilla already has the switch. CookingStation.UpdateCooking burns fuel
    /// only when <c>m_useFuel &amp;&amp; GetFuel() &gt; 0f &amp;&amp;
    /// (m_useFueldWhileEmpty || HaveUncookedItem())</c> -- so turning
    /// <c>m_useFueldWhileEmpty</c> off makes the burn need something to cook,
    /// and an idle oven simply stops spending fuel. Vanilla does the rest, and
    /// nothing has to fake or block the burn.
    ///
    /// The prefix runs before that check each tick. The original value is
    /// remembered per instance so the switch is handed back exactly as it was
    /// when the feature is turned off (a field on the live component, not
    /// persisted, so it costs nothing on reload).
    /// </summary>
    [HarmonyPatch(typeof(CookingStation), "UpdateCooking")]
    internal static class NoIdleOvenFuelPatch
    {
        // Keyed by the instance, so an entry dies with its oven.
        private static readonly ConditionalWeakTable<CookingStation, StrongBox<bool>> Original =
            new ConditionalWeakTable<CookingStation, StrongBox<bool>>();

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(CookingStation), "UpdateCooking", NoIdleOvenFuelFeature.FeatureName);

        private static void Prefix(CookingStation __instance)
        {
            try
            {
                // A station that burns no fuel has nothing to idle-burn.
                if (__instance == null || !__instance.m_useFuel) return;

                if (NoIdleOvenFuelFeature.Instance?.IsActive != true)
                {
                    // Switched off mid-session: hand the oven's own switch back.
                    if (Original.TryGetValue(__instance, out var was))
                        __instance.m_useFueldWhileEmpty = was.Value;
                    return;
                }

                if (!Original.TryGetValue(__instance, out _))
                    Original.Add(__instance, new StrongBox<bool>(__instance.m_useFueldWhileEmpty));

                __instance.m_useFueldWhileEmpty = false;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"NoIdleOvenFuel: could not switch an oven's idle fuel use: {ex}");
            }
        }
    }
}
