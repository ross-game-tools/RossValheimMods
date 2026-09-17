using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// UpdateRecipe runs every frame the crafting panel is open and is where
    /// vanilla decides what the Craft button says, what the requirement list
    /// shows, and whether the button may be pressed. It reads
    /// m_touchMultiCrafting -- the flag its own touch UI sets -- together with
    /// m_multiCraftAmount to answer all three for a multi-craft.
    ///
    /// The prefix sets those two from the amount box before vanilla reads
    /// them, so the button says "Craft x 12", the requirement list shows what
    /// twelve cost, the button greys out when twelve is unaffordable, and
    /// pressing it makes twelve. Nothing is reimplemented and nothing is drawn
    /// over the ingredients.
    ///
    /// At an amount of 1 the flag stays off and the panel is vanilla's.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class MultiCraftSyncPatch
    {
        /// <summary>Vanilla's own multi-craft amount, read before it is first changed.</summary>
        private static int? _vanillaAmount;

        /// <summary>
        /// Whether the flag on the panel is ours. Vanilla clears it only when
        /// a craft is pressed, so leaving it set would carry the amount to the
        /// next recipe selected -- including one that does not stack, which
        /// vanilla would then happily multi-craft.
        /// </summary>
        private static bool _armed;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(InventoryGui), "UpdateRecipe", MultiCraftFeature.FeatureName);

        private static void Prefix(InventoryGui __instance)
        {
            try
            {
                _vanillaAmount = _vanillaAmount ?? __instance.m_multiCraftAmount;

                bool apply = MultiCraftFeature.Instance?.IsActive == true
                             && __instance.m_selectedRecipe.Recipe
                             && MultiCraftBox.Allows(__instance)
                             && MultiCraftBox.Amount > 1;

                if (apply)
                {
                    __instance.m_multiCraftAmount = MultiCraftBox.Amount;
                    __instance.m_touchMultiCrafting = true;
                    _armed = true;
                    return;
                }

                // Only ever disarm what this set: vanilla's own touch UI uses
                // the same flag, and clearing it under that would swallow a
                // multi-craft a player asked for.
                if (!_armed) return;

                // Never while a craft is in flight. DoCrafting reads
                // m_multiCraftAmount when the timer finishes, for both the
                // items it makes and the materials it spends, so putting the
                // amount back mid-craft would hand out vanilla's five and
                // charge for five.
                if (__instance.m_craftTimer >= 0f) return;

                __instance.m_touchMultiCrafting = false;
                __instance.m_multiCraftAmount = _vanillaAmount.Value;
                _armed = false;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"MultiCraft: could not set the craft amount, leaving vanilla's: {ex}");
            }
        }

        private static void Postfix(InventoryGui __instance)
        {
            // An exception escaping here would break the crafting panel.
            try
            {
                MultiCraftBox.Sync(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"MultiCraft: updating the amount box failed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class MultiCraftHidePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(InventoryGui), nameof(InventoryGui.Hide), MultiCraftFeature.FeatureName);

        // No IsActive check: the toggle can be switched off while the box
        // holds the cursor, and it must still be released on close.
        private static void Postfix()
        {
            try
            {
                MultiCraftBox.OnHide();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"MultiCraft: releasing the amount box failed: {ex}");
            }
        }
    }

    /// <summary>
    /// While the number box has the cursor, typing digits must not also fire
    /// the hotbar, movement or the keys that close the panel. Every game
    /// button is released just before each game-key reader that still runs
    /// with the panel open, as the recipe search box does for the same reason:
    /// ZInput.GetButton* are one-line methods Mono inlines into their callers,
    /// so patching those directly would be skipped.
    /// </summary>
    [HarmonyPatch]
    internal static class MultiCraftTypingPatch
    {
        private static readonly (Type Type, string Method)[] Readers =
        {
            (typeof(PlayerController), "FixedUpdate"),
            (typeof(Player), "Update"),
            (typeof(InventoryGui), "Update"),
        };

        private static bool Prepare()
        {
            bool all = ValheimCompat.RequireMethod(
                typeof(ZInput), nameof(ZInput.ResetAllButtonStates), MultiCraftFeature.FeatureName);
            foreach (var (type, method) in Readers)
                all &= ValheimCompat.RequireMethod(type, method, MultiCraftFeature.FeatureName);
            return all;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var (type, method) in Readers)
                yield return AccessTools.Method(type, method);
        }

        private static void Prefix()
        {
            if (MultiCraftBox.IsTyping) ZInput.ResetAllButtonStates();
        }
    }
}
