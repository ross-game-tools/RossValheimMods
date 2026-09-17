using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// The one-ingredient recipes -- mead bases, cooked dishes, anything with
    /// m_requireOnlyOneIngredient -- do not pay through ConsumeResources.
    /// Recipe.GetAmount asks the player for one ingredient and DoCrafting then
    /// removes it from the pack by name, so the craft-from-chests work has to
    /// meet them on their own path.
    ///
    /// Vanilla's GetFirstRequiredItem looks only in the pack and returns null
    /// when nothing covers the cost -- and GetAmount reads m_quality off that
    /// result without checking it. Vanilla never trips over this because its
    /// own HaveRequirements said no first; ours says yes when a chest can pay,
    /// so the item has to be found or the craft throws.
    /// </summary>
    internal static class SingleIngredient
    {
        private static Inventory _inventory;
        private static string _name;
        private static int _quality;
        private static int _frame = -1;

        /// <summary>Notes which ingredient was sourced, so the removal that follows can be topped up.</summary>
        public static void Expect(Inventory inventory, string name, int quality)
        {
            _inventory = inventory;
            _name = name;
            _quality = quality;
            _frame = Time.frameCount;
        }

        /// <summary>
        /// True when this removal is the one that was just sourced. Vanilla
        /// asks for the amount and removes it in the same frame, so a stale
        /// note never matches a later removal.
        /// </summary>
        public static bool Matches(Inventory inventory, string name, int quality) =>
            _frame == Time.frameCount
            && ReferenceEquals(_inventory, inventory)
            && string.Equals(_name, name, StringComparison.Ordinal)
            && _quality == quality;

        public static void Clear()
        {
            _inventory = null;
            _name = null;
            _frame = -1;
        }
    }

    /// <summary>
    /// Finds the single ingredient in a nearby container when the pack cannot
    /// cover it, and hands back a real item for vanilla to measure quality
    /// from.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.GetFirstRequiredItem))]
    internal static class SingleIngredientSourcePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), nameof(Player.GetFirstRequiredItem), CraftFromChestsFeature.FeatureName);

        private static void Postfix(
            Player __instance, Inventory inventory, Recipe recipe, int qualityLevel,
            ref int amount, ref int extraAmount, int craftMultiplier, ref ItemDrop.ItemData __result)
        {
            if (__result != null || !ChestCraftingRules.Active(__instance)) return;
            if (recipe == null || recipe.m_resources == null || inventory == null) return;

            try
            {
                var boxes = ChestCrafting.Near(__instance.transform.position);
                if (boxes.Count == 0) return;

                foreach (var requirement in recipe.m_resources)
                {
                    if (requirement?.m_resItem == null) continue;
                    if (ChestCraftingRules.SkipForStation(__instance, requirement)) continue;

                    string name = requirement.m_resItem.m_itemData.m_shared.m_name;
                    int need = requirement.GetAmount(qualityLevel) * Mathf.Max(1, craftMultiplier);
                    if (need <= 0) continue;

                    int maxQuality = requirement.m_resItem.m_itemData.m_shared.m_maxQuality;
                    for (int quality = 0; quality <= maxQuality; quality++)
                    {
                        if (inventory.CountItems(name, quality) + ChestCrafting.Count(boxes, name, quality) < need)
                            continue;

                        var item = Find(inventory, boxes, name, quality);
                        if (item == null) continue;

                        amount = need;
                        extraAmount = requirement.m_extraAmountOnlyOneIngredient;
                        __result = item;
                        SingleIngredient.Expect(inventory, name, quality);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: leaving this one-ingredient recipe to vanilla: {ex}");
            }
        }

        /// <summary>The pack's copy if there is one, so vanilla measures the quality it would have used.</summary>
        private static ItemDrop.ItemData Find(Inventory inventory, List<Container> boxes, string name, int quality)
        {
            var own = inventory.GetItem(name, quality);
            if (own != null) return own;

            foreach (var container in boxes)
            {
                var item = container.GetInventory()?.GetItem(name, quality);
                if (item != null) return item;
            }

            return null;
        }
    }

    /// <summary>
    /// Charges the containers for whatever the pack could not pay of a
    /// one-ingredient craft. Measured the same way as ConsumeResources: what
    /// vanilla actually removed, not what it meant to.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem),
        typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class SingleIngredientRemovePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Inventory), nameof(Inventory.RemoveItem), CraftFromChestsFeature.FeatureName);

        private static void Prefix(Inventory __instance, string name, int itemQuality, out int __state)
        {
            __state = -1;
            if (!SingleIngredient.Matches(__instance, name, itemQuality)) return;

            __state = __instance.CountItems(name, itemQuality);
        }

        private static void Postfix(Inventory __instance, string name, int amount, int itemQuality, int __state)
        {
            if (__state < 0) return;

            SingleIngredient.Clear();

            try
            {
                int removed = __state - __instance.CountItems(name, itemQuality);
                int shortfall = amount - removed;
                if (shortfall <= 0) return;

                var player = Player.m_localPlayer;
                if (player == null) return;

                var boxes = ChestCrafting.Near(player.transform.position);
                if (boxes.Count == 0) return;

                int taken = ChestCrafting.Take(boxes, name, shortfall, itemQuality);
                if (taken < shortfall)
                    RossQoLPlugin.Log.LogWarning(
                        $"CraftFromChests: only {taken} of {shortfall} {name} came out of nearby containers.");
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: could not charge containers for this craft: {ex}");
            }
        }
    }
}
