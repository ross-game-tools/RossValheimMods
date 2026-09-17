using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using TMPro;
using UnityEngine;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// The requirement list counts only what is in your pack -- vanilla's
    /// SetupRequirement asks player.GetInventory() and flashes the amount red
    /// when it comes up short. With materials in a chest that is a lie: the
    /// craft button works and the list says you cannot afford it.
    ///
    /// The postfix asks the same question again with the nearby containers
    /// counted, and calms the number down when they cover it. Only the colour
    /// changes; the amount still reads what the recipe costs.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    internal static class RequirementDisplayPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement), CraftFromChestsFeature.FeatureName);

        private static void Postfix(
            Transform elementRoot, Piece.Requirement req, Player player, bool craft, int quality,
            int craftMultiplier, bool __result)
        {
            if (!__result || !ChestCraftingRules.Active(player)) return;
            if (req?.m_resItem == null) return;
            if (!craft && CraftFromChestsConfig.BuildFromChests?.Value != true) return;

            try
            {
                int need = req.GetAmount(quality) * Mathf.Max(1, craftMultiplier);
                if (need <= 0) return;

                string name = req.m_resItem.m_itemData.m_shared.m_name;

                // Already affordable from the pack alone: vanilla has it right.
                if (player.m_inventory.CountItems(name) >= need) return;

                var boxes = ChestCrafting.Near(player.transform.position);
                if (boxes.Count == 0) return;
                if (player.m_inventory.CountItems(name) + ChestCrafting.Count(boxes, name, -1) < need) return;

                var amount = elementRoot.transform.Find("res_amount")?.GetComponent<TMP_Text>();
                if (amount != null) amount.color = Color.white;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: leaving this requirement's colour to vanilla: {ex}");
            }
        }
    }
}
