using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using TMPro;
using UnityEngine;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// A requirement row says what a material costs and nothing about what you
    /// have -- vanilla writes the cost into `res_amount` and only flashes it
    /// red when your pack comes up short. With materials in a chest that red
    /// is a lie, and the bare cost leaves you opening chests to find out
    /// whether a craft is close.
    ///
    /// The postfix rewrites the row as "have/need", where "have" is your pack
    /// plus every nearby container the craft may draw on, and calms the
    /// number down when the two together cover the cost. The same rows serve
    /// the crafting panel, the upgrade panel and the build HUD, all through
    /// this one vanilla method.
    ///
    /// "Have" is counted the way the craft itself counts: a recipe spends one
    /// quality tier, so the best single tier is what it can call on, while a
    /// building piece takes any quality. That keeps the number honest against
    /// the craft button beside it -- a row that reads as covered belongs to a
    /// craft that can actually happen.
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
            // Vanilla hid this row, or there is nothing for us to say about it.
            if (!__result || !ChestCraftingRules.Active(player)) return;
            if (req?.m_resItem == null) return;

            // Building cannot draw on chests when that is switched off, so the
            // row stays exactly as vanilla drew it rather than advertising
            // materials this piece may not spend.
            if (!craft && CraftFromChestsConfig.BuildFromChests?.Value != true) return;

            try
            {
                int need = req.GetAmount(quality) * Mathf.Max(1, craftMultiplier);
                if (need <= 0) return;

                var amount = elementRoot.transform.Find("res_amount")?.GetComponent<TMP_Text>();
                if (amount == null) return;

                int have = craft ? BestTier(player, req) : AnyQuality(player, req);

                amount.text = have + "/" + need;

                // Vanilla's red was about the pack alone. Only the covered case
                // is overruled; a row the chests cannot cover keeps flashing.
                if (have >= need) amount.color = Color.white;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: leaving this requirement's row to vanilla: {ex}");
            }
        }

        /// <summary>
        /// Pack and containers together, on the best single quality tier --
        /// the rule <see cref="ChestCraftingRules.AvailableBestTier"/> applies
        /// when deciding whether a recipe can be afforded, counted here
        /// through the frame cache because this runs every frame per row.
        /// </summary>
        private static int BestTier(Player player, Piece.Requirement req)
        {
            var shared = req.m_resItem.m_itemData.m_shared;
            var origin = player.transform.position;
            int best = 0;

            for (int tier = 1; tier <= shared.m_maxQuality; tier++)
            {
                int held = player.m_inventory.CountItems(shared.m_name, tier)
                    + ChestDisplayCounts.InContainers(origin, shared.m_name, tier);
                if (held > best) best = held;
            }

            return best;
        }

        /// <summary>Pack and containers together at any quality, as a building piece counts.</summary>
        private static int AnyQuality(Player player, Piece.Requirement req)
        {
            string name = req.m_resItem.m_itemData.m_shared.m_name;
            return player.m_inventory.CountItems(name)
                + ChestDisplayCounts.InContainers(player.transform.position, name, -1);
        }
    }
}
