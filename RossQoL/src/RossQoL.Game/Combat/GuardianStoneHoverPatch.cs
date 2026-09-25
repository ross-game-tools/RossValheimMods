using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// Appends the second-power option to a guardian stone's hover text, so the
    /// modifier interaction is discoverable rather than hidden. Vanilla's line
    /// is <c>[Use] activate power</c>; this adds <c>[Shift + Use] set as second
    /// power</c> beneath it, using the actual configured modifier and the
    /// player's own Use binding (<c>$KEY_Use</c>).
    ///
    /// Postfix on <see cref="ItemStand.GetHoverText"/>: only touches a
    /// guardian-power stand (<c>m_guardianPower != null</c>) whose hover vanilla
    /// actually drew (a non-empty result -- empty means mid-activation or no
    /// access), and never throws out of the hover.
    /// </summary>
    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.GetHoverText))]
    internal static class GuardianStoneHoverPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ItemStand), nameof(ItemStand.GetHoverText), MultiplePowersFeature.FeatureName);

        private static void Postfix(ItemStand __instance, ref string __result)
        {
            if (MultiplePowersFeature.Instance?.IsActive != true) return;
            if (string.IsNullOrEmpty(__result)) return;

            var power = __instance != null ? __instance.m_guardianPower : null;
            if (power == null || string.IsNullOrEmpty(power.name)) return;

            KeyCode modifier = MultiplePowersConfig.AssignSecondPowerModifier?.Value ?? KeyCode.LeftShift;
            if (modifier == KeyCode.None) return;   // second-slot assignment is disabled

            try
            {
                // $KEY_Use resolves to the player's own Use binding; the modifier
                // is ours, shown by a friendly name (Shift/Ctrl/Alt) where one
                // fits. Localize resolves the token and leaves the rest as-is.
                string hint = Localization.instance.Localize(
                    "\n[<color=yellow><b>" + ModifierKeys.Label(modifier) + " + $KEY_Use</b></color>] set as second power");
                __result += hint;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"MultiplePowers: could not add the second-power hover hint: {ex}");
            }
        }
    }

    /// <summary>
    /// Friendly display names for the modifier keys. Kept out of the
    /// <see cref="GuardianStoneHoverPatch"/> Harmony class so the Harmony
    /// analyzer does not mistake its parameter for a patch injection.
    /// </summary>
    internal static class ModifierKeys
    {
        public static string Label(KeyCode code)
        {
            switch (code)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    return "Shift";
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    return "Ctrl";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return "Alt";
                default:
                    return code.ToString();
            }
        }
    }
}
