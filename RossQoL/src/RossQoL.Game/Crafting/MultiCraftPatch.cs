using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>Shared plumbing for the craft-many widgets.</summary>
    internal static class MultiCraftPatches
    {
        private static MethodInfo s_craftPressed;

        /// <summary>
        /// Presses vanilla's own craft path. Private, so it is called by
        /// reflection rather than copied: everything a craft does -- the timer,
        /// the materials, the station effects, the skill, the inventory-full
        /// check -- stays vanilla's.
        /// </summary>
        public static void InvokeCraftPressed(InventoryGui gui)
        {
            s_craftPressed = s_craftPressed ?? AccessTools.Method(typeof(InventoryGui), "OnCraftPressed");
            if (s_craftPressed == null)
                throw new InvalidOperationException("InventoryGui.OnCraftPressed not found");

            s_craftPressed.Invoke(gui, null);
        }
    }

    /// <summary>
    /// UpdateRecipe runs every frame the crafting panel is open and is where
    /// vanilla decides what the Craft button says and whether it is usable.
    /// The postfix keeps the craft-many widgets in step with it.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class MultiCraftSyncPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(InventoryGui), "UpdateRecipe", MultiCraftFeature.FeatureName);

        private static void Postfix(InventoryGui __instance)
        {
            // An exception escaping here would break the crafting panel.
            try
            {
                MultiCraftBox.Sync(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"MultiCraft: updating the craft box failed: {ex}");
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
                RossQoLPlugin.Log.LogError($"MultiCraft: releasing the craft box failed: {ex}");
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
