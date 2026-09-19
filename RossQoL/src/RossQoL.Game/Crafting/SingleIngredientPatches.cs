using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Core.Crafting;
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
        // The containers the craft was costed against, kept so the removal is
        // charged to exactly those and not to whatever happens to be in range
        // a moment later.
        private static readonly List<Container> Sourced = new List<Container>();

        private static Inventory _inventory;
        private static string _name;
        private static int _quality;
        private static int _amount;
        private static int _frame = -1;
        private static bool _armed;

        /// <summary>The containers the armed craft counted.</summary>
        public static List<Container> SourcedFrom => Sourced;

        /// <summary>
        /// Notes which ingredient was sourced, for which craft, so the one
        /// removal that belongs to it can be topped up.
        ///
        /// Only ever armed while a craft is really being made: the crafting
        /// panel asks the same question every frame it is open, and arming on
        /// those would leave any same-named removal in the pack -- dropping
        /// the item, feeding it to a tame, a different mod moving it -- topped
        /// up out of a chest for a craft that never happened.
        /// </summary>
        public static void Expect(Inventory inventory, string name, int quality, int amount, List<Container> boxes)
        {
            if (!CraftScope.InProgress) return;

            Sourced.Clear();
            if (boxes != null) Sourced.AddRange(boxes);

            _inventory = inventory;
            _name = name;
            _quality = quality;
            _amount = amount;
            _frame = Time.frameCount;
            _armed = true;
        }

        /// <summary>
        /// True only for the exact removal this was armed for: the same
        /// inventory object, the same item, the same quality, the same amount,
        /// inside the same craft, in the same frame.
        /// </summary>
        public static bool Matches(Inventory inventory, string name, int quality, int amount) =>
            _armed
            && CraftScope.InProgress
            && _frame == Time.frameCount
            && ReferenceEquals(_inventory, inventory)
            && string.Equals(_name, name, StringComparison.Ordinal)
            && _quality == quality
            && _amount == amount;

        /// <summary>
        /// Disarms. Called the moment the removal is seen, so a second removal
        /// of the same thing in the same craft can never match. The container
        /// snapshot deliberately survives: the postfix still has to charge it.
        /// </summary>
        public static void Clear()
        {
            _armed = false;
            _inventory = null;
            _name = null;
            _amount = 0;
            _quality = -1;
            _frame = -1;
        }
    }

    /// <summary>
    /// Finds the single ingredient in a nearby container when the pack cannot
    /// cover it, and hands back a real item for vanilla to measure quality
    /// from.
    ///
    /// The containers offered here are the ones a craft may also be charged
    /// for, so the quality vanilla reads off the item it is given always
    /// belongs to something the craft can actually pay with.
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

                    int maxQuality = Mathf.Max(1, requirement.m_resItem.m_itemData.m_shared.m_maxQuality);
                    for (int quality = 1; quality <= maxQuality; quality++)
                    {
                        if (inventory.CountItems(name, quality) + ChestCrafting.Count(boxes, name, quality) < need)
                            continue;

                        var item = Find(inventory, boxes, name, quality);
                        if (item == null) continue;

                        amount = need;
                        extraAmount = requirement.m_extraAmountOnlyOneIngredient;
                        __result = item;
                        SingleIngredient.Expect(inventory, name, quality, need, boxes);
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
                if (container == null) continue;

                var containerInventory = container.GetInventory();
                if (containerInventory == null) continue;

                var item = containerInventory.GetItem(name, quality);
                if (item != null) return item;
            }

            return null;
        }
    }

    /// <summary>
    /// Charges the containers for whatever the pack could not pay of a
    /// one-ingredient craft. Measured the same way as ConsumeResources: what
    /// vanilla actually removed, not what it meant to.
    ///
    /// This fires for one removal only -- the one the craft was armed for --
    /// and disarms itself the instant it sees it.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem),
        typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class SingleIngredientRemovePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Inventory), nameof(Inventory.RemoveItem), CraftFromChestsFeature.FeatureName);

        private static void Prefix(Inventory __instance, string name, int amount, int itemQuality, out int __state)
        {
            __state = -1;
            try
            {
                if (!SingleIngredient.Matches(__instance, name, itemQuality, amount)) return;

                // Disarmed before vanilla runs: whatever else is removed
                // during this craft is not this craft's ingredient.
                SingleIngredient.Clear();
                __state = __instance.CountItems(name, itemQuality);
            }
            catch (Exception ex)
            {
                __state = -1;
                RossQoLPlugin.Log.LogError($"CraftFromChests: leaving this removal to vanilla: {ex}");
            }
        }

        private static void Postfix(Inventory __instance, string name, int amount, int itemQuality, int __state)
        {
            if (__state < 0) return;

            try
            {
                int removed = __state - __instance.CountItems(name, itemQuality);
                int owing = CraftPayment.Owing(amount, removed);
                if (owing <= 0) return;

                var boxes = SingleIngredient.SourcedFrom;
                int taken = ChestCrafting.Take(boxes, name, owing, itemQuality);

                int unpaid = CraftPayment.Shortfall(amount, removed, taken);
                if (unpaid <= 0) return;

                // Nothing can refuse the craft by this point, and the pack has
                // already been emptied of this ingredient at this quality, so
                // there is nothing left to charge: say so loudly.
                RossQoLPlugin.Log.LogError(
                    $"CraftFromChests: {CraftScope.Describe()} cost {amount} {name} (quality {itemQuality}) but only "
                    + $"{removed + taken} were paid -- {removed} from your pack, {taken} from {boxes.Count} nearby "
                    + $"container(s). {unpaid} went unpaid; that craft was cheaper than it should have been.");
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: could not charge containers for this craft: {ex}");
            }
        }
    }
}
