using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RossQoL.Core.Crafting;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// Filters before vanilla builds the rows, so sorting, scroll height,
    /// selection and both the Craft and Upgrade tabs all work unchanged.
    /// Swaps in a filtered copy rather than editing the caller's list.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList))]
    internal static class RecipeListFilterPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList), CraftingSearchFeature.FeatureName);

        private static void Prefix(ref List<Recipe> recipes)
        {
            if (CraftingSearchFeature.Instance?.IsActive != true || recipes == null) return;

            string term = CraftingSearchBox.Term;
            if (!RecipeSearch.IsActive(term)) return;

            recipes = CraftingSearchBox.Filter(recipes, term);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class SearchBoxShowPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(InventoryGui), nameof(InventoryGui.Show), CraftingSearchFeature.FeatureName);

        private static void Postfix(InventoryGui __instance)
        {
            if (CraftingSearchFeature.Instance?.IsActive != true) return;

            // An exception escaping here would break opening the inventory.
            try
            {
                CraftingSearchBox.OnShow(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"RecipeSearch: showing the search box failed: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class SearchBoxHidePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(InventoryGui), nameof(InventoryGui.Hide), CraftingSearchFeature.FeatureName);

        // No IsActive check: the toggle can be switched off while the box
        // holds the cursor, and it must still be cleared on close.
        private static void Postfix()
        {
            try
            {
                CraftingSearchBox.OnHide();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"RecipeSearch: clearing the search box failed: {ex}");
            }
        }
    }

    /// <summary>
    /// While the box has the cursor, every game button is released just
    /// before each game-key reader that still runs with the panel open:
    /// movement and jump (PlayerController.FixedUpdate), hotbar 1-8,
    /// guardian power and auto-pickup (Player.Update), and E and Tab closing
    /// the panel (InventoryGui.Update).
    ///
    /// Clearing state, not patching ZInput.GetButton*: those are one-line
    /// methods Mono inlines into callers, which then skip any patch on them.
    /// Unity calls these three methods directly, so they cannot be inlined.
    ///
    /// Raw key reads (ZInput.GetKeyDown) are untouched, so Esc still closes
    /// the panel. The text box reads the keyboard itself, not ZInput.
    /// </summary>
    [HarmonyPatch]
    internal static class TypingReleasesGameKeysPatch
    {
        private static readonly (Type Type, string Method)[] Readers =
        {
            (typeof(PlayerController), "FixedUpdate"),
            (typeof(Player), "Update"),
            (typeof(InventoryGui), "Update"),
        };

        private static bool Prepare()
        {
            bool all = ValheimCompat.RequireMethod(typeof(ZInput), nameof(ZInput.ResetAllButtonStates), CraftingSearchFeature.FeatureName);
            foreach (var (type, method) in Readers)
                all &= ValheimCompat.RequireMethod(type, method, CraftingSearchFeature.FeatureName);
            return all;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var (type, method) in Readers)
                yield return AccessTools.Method(type, method);
        }

        private static void Prefix()
        {
            if (CraftingSearchBox.IsTyping) ZInput.ResetAllButtonStates();
        }
    }

    /// <summary>
    /// Auto-focus has to wait: when Show runs, the crafting panel is not yet
    /// active, and a text box refuses focus while inactive.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class SearchBoxFocusPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(InventoryGui), "Update", CraftingSearchFeature.FeatureName);

        private static void Postfix()
        {
            if (CraftingSearchFeature.Instance?.IsActive != true) return;

            try
            {
                CraftingSearchBox.TryPendingFocus();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"RecipeSearch: focusing the search box failed: {ex}");
            }
        }
    }
}
