using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Core.Crafting;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Crafting
{
    /// <summary>Shared rules for the three patches.</summary>
    internal static class ChestCraftingRules
    {
        public static bool Active(Player player) =>
            CraftFromChestsFeature.Instance?.IsActive == true
            && player != null
            && player == Player.m_localPlayer;

        /// <summary>
        /// Whether vanilla itself would ignore this requirement at the station
        /// you are standing at.
        ///
        /// Recipes carry upgrader-only entries -- the "$item_upgrader…" line on
        /// a weapon recipe -- which vanilla skips unless the station is an
        /// upgrader, and ordinary entries which it skips when it is. Counting
        /// one of those is counting a thing that does not exist: it reports
        /// zero held against one needed and fails the whole recipe.
        /// </summary>
        public static bool SkipForStation(Player player, Piece.Requirement requirement)
        {
            var station = player.GetCurrentCraftingStation();
            if (station != null) return station.m_upgrader != requirement.m_upgraderResource;

            return requirement.m_upgraderResource;
        }

        /// <summary>
        /// Everything vanilla checks about a recipe before it ever looks at
        /// materials: the right station, at a high enough level, and the DLC
        /// the item belongs to. Without this the chest count would answer a
        /// question that was never about materials, and a recipe would craft
        /// at the wrong bench or below the level it asks for.
        /// </summary>
        public static bool AllowedIgnoringMaterials(Player player, Recipe recipe, int qualityLevel)
        {
            if (!player.RequiredCraftingStation(recipe, qualityLevel, checkLevel: true)) return false;

            var dlc = recipe.m_item.m_itemData.m_shared.m_dlc;
            return dlc.Length == 0 || DLCMan.instance.IsDLCInstalled(dlc);
        }

        /// <summary>The same for a building piece: a station in range, and the DLC.</summary>
        public static bool AllowedIgnoringMaterials(Player player, Piece piece)
        {
            if (piece.m_craftingStation
                && !CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, player.transform.position)
                && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench)) return false;

            return piece.m_dlc.Length == 0 || DLCMan.instance.IsDLCInstalled(piece.m_dlc);
        }

        /// <summary>
        /// What one requirement can be paid from, counted as vanilla counts a
        /// recipe: the best single quality tier, not every tier added up. Two
        /// stacks of different quality are not interchangeable, and treating
        /// them as one pool would allow a craft vanilla refuses.
        /// </summary>
        public static int AvailableBestTier(
            Player player, List<Container> boxes, Piece.Requirement requirement) =>
            ChooseTier(player, boxes, requirement, need: 0).Available;

        /// <summary>
        /// Which quality tier this requirement is costed against, and what
        /// that costing can call on. Counting a craft and charging for it both
        /// come through here, so payment can only ever take the tier
        /// affordability counted -- the alternative is charging a better stack
        /// than the recipe was ever priced against, which loses the player
        /// materials, or charging a tier that had nothing, which gives the
        /// craft away.
        /// </summary>
        public static TierChoice ChooseTier(
            Player player, List<Container> boxes, Piece.Requirement requirement, int need)
        {
            var shared = requirement.m_resItem.m_itemData.m_shared;
            int maxQuality = Mathf.Max(1, shared.m_maxQuality);

            // Indexed by quality, so index 2 is quality 2. Quality 0 is not a
            // tier any item is at, and stays zero.
            var heldByQuality = new int[maxQuality + 1];
            for (int quality = 1; quality <= maxQuality; quality++)
                heldByQuality[quality] =
                    player.m_inventory.CountItems(shared.m_name, quality) + ChestCrafting.Count(boxes, shared.m_name, quality);

            return CraftPayment.ChooseTier(heldByQuality, need);
        }

        /// <summary>
        /// What one requirement can be paid from for a building piece, where
        /// vanilla asks for any quality at all.
        /// </summary>
        public static int AvailableAnyQuality(
            Player player, List<Container> boxes, Piece.Requirement requirement)
        {
            string name = requirement.m_resItem.m_itemData.m_shared.m_name;
            return player.m_inventory.CountItems(name) + ChestCrafting.Count(boxes, name, -1);
        }
    }

    /// <summary>
    /// Vanilla decides a recipe is unaffordable by looking only in your pack.
    /// The postfix asks the same question again with the nearby containers
    /// counted too, and says yes when they cover it. It never says no to
    /// something vanilla allowed.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Recipe), typeof(bool), typeof(int), typeof(int))]
    internal static class CraftRequirementsPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), nameof(Player.HaveRequirements), CraftFromChestsFeature.FeatureName);

        private static void Postfix(
            Player __instance, Recipe recipe, bool discover, int qualityLevel, int amount, ref bool __result)
        {
            if (__result || !ChestCraftingRules.Active(__instance)) return;
            if (recipe == null || recipe.m_resources == null) return;

            // "Do I know this recipe" is not a question about materials.
            if (discover) return;

            // Vanilla said no for a reason that has nothing to do with what is
            // in a chest -- wrong bench, bench too low, missing DLC -- and that
            // no stands.
            if (!ChestCraftingRules.AllowedIgnoringMaterials(__instance, recipe, qualityLevel)) return;

            try
            {
                var boxes = ChestCrafting.Near(__instance.transform.position);
                if (boxes.Count == 0) return;

                bool anyOne = recipe.m_requireOnlyOneIngredient;
                bool allCovered = true;

                foreach (var requirement in recipe.m_resources)
                {
                    if (requirement?.m_resItem == null) continue;
                    if (ChestCraftingRules.SkipForStation(__instance, requirement)) continue;

                    int need = requirement.GetAmount(qualityLevel) * Math.Max(1, amount);
                    if (need <= 0) continue;

                    bool covered = ChestCraftingRules.AvailableBestTier(__instance, boxes, requirement) >= need;

                    // One-ingredient recipes -- mead bases, cooked dishes --
                    // are satisfied by any single listed material.
                    if (anyOne && covered)
                    {
                        __result = true;
                        return;
                    }

                    if (!anyOne && !covered)
                    {
                        allCovered = false;
                        break;
                    }
                }

                if (!anyOne && allCovered) __result = true;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: leaving this recipe to vanilla: {ex}");
            }
        }
    }

    /// <summary>
    /// The same for building pieces. Only the mode that actually weighs
    /// materials is answered; the others ask whether a piece is known or
    /// nearly buildable, which containers have nothing to do with.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
    internal static class BuildRequirementsPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), nameof(Player.HaveRequirements), CraftFromChestsFeature.FeatureName);

        private static void Postfix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
        {
            if (__result || !ChestCraftingRules.Active(__instance)) return;
            if (CraftFromChestsConfig.BuildFromChests?.Value != true) return;
            if (mode != Player.RequirementMode.CanBuild || piece == null || piece.m_resources == null) return;

            try
            {
                // No bench in range, or missing DLC: vanilla's no was never
                // about materials, so it stands.
                if (!ChestCraftingRules.AllowedIgnoringMaterials(__instance, piece)) return;

                var boxes = ChestCrafting.Near(__instance.transform.position);
                if (boxes.Count == 0) return;

                foreach (var requirement in piece.m_resources)
                {
                    if (requirement?.m_resItem == null || requirement.m_amount <= 0) continue;

                    // As vanilla's CanBuild: the flat amount, any quality.
                    if (ChestCraftingRules.AvailableAnyQuality(__instance, boxes, requirement) < requirement.m_amount)
                        return;
                }

                __result = true;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: leaving this piece to vanilla: {ex}");
            }
        }
    }

    /// <summary>
    /// What one craft was costed against, taken down before vanilla spends
    /// anything: the containers it may be charged to, what the pack held, and
    /// the quality tier each requirement was priced at.
    /// </summary>
    internal sealed class CraftCharge
    {
        /// <summary>
        /// A copy, not the shared list: the same set has to be charged that
        /// was counted, and anything else asking for nearby containers in the
        /// meantime would otherwise rewrite it under us.
        /// </summary>
        public readonly List<Container> Boxes = new List<Container>();

        public int[] PackBefore;
        public int[] Tier;
    }

    /// <summary>
    /// Paying for it. Vanilla takes what it can from the pack; the prefix
    /// records what was there, and the postfix works out what it actually
    /// took and charges the rest to the containers.
    ///
    /// Measuring rather than predicting means the pack is always spent first
    /// and a chest is never charged for something the pack already paid.
    ///
    /// The prefix also fixes the containers and the quality tier the craft is
    /// costed against, because by the time the postfix runs vanilla has
    /// already handed the player the crafted item: nothing here can refuse a
    /// craft, only charge for it, so what it charges must be exactly what was
    /// counted. A shortfall after all that is an error, not a warning -- the
    /// player was given something for nothing.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class ConsumeFromChestsPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), nameof(Player.ConsumeResources), CraftFromChestsFeature.FeatureName);

        private static void Prefix(
            Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier,
            out CraftCharge __state)
        {
            __state = null;
            if (!ChestCraftingRules.Active(__instance) || requirements == null) return;

            try
            {
                var charge = new CraftCharge
                {
                    PackBefore = new int[requirements.Length],
                    Tier = new int[requirements.Length],
                };
                charge.Boxes.AddRange(ChestCrafting.Near(__instance.transform.position));

                // No containers counted means this craft was vanilla's own
                // from end to end. Nothing here has any business charging for
                // it, out of a chest or out of the pack.
                if (charge.Boxes.Count == 0) return;

                for (int i = 0; i < requirements.Length; i++)
                {
                    var requirement = requirements[i];
                    if (requirement?.m_resItem == null) continue;

                    string name = requirement.m_resItem.m_itemData.m_shared.m_name;
                    charge.PackBefore[i] = __instance.m_inventory.CountItems(name);

                    // A caller that named a quality is charged that quality.
                    // A caller that said "any" (-1) is charged the tier the
                    // craft was actually priced at, so payment cannot eat a
                    // better stack than affordability ever counted.
                    int need = requirement.GetAmount(qualityLevel) * Math.Max(1, multiplier);
                    charge.Tier[i] = itemQuality >= 1
                        ? itemQuality
                        : ChestCraftingRules.ChooseTier(__instance, charge.Boxes, requirement, need).Quality;
                }

                __state = charge;
            }
            catch (Exception ex)
            {
                __state = null;
                RossQoLPlugin.Log.LogError($"CraftFromChests: leaving this craft's cost to vanilla: {ex}");
            }
        }

        private static void Postfix(
            Player __instance, Piece.Requirement[] requirements, int qualityLevel, int multiplier,
            CraftCharge __state)
        {
            if (__state == null || requirements == null) return;

            try
            {
                for (int i = 0; i < requirements.Length && i < __state.PackBefore.Length; i++)
                {
                    var requirement = requirements[i];
                    if (requirement?.m_resItem == null) continue;
                    if (ChestCraftingRules.SkipForStation(__instance, requirement)) continue;

                    int owed = requirement.GetAmount(qualityLevel) * Math.Max(1, multiplier);
                    if (owed <= 0) continue;

                    string name = requirement.m_resItem.m_itemData.m_shared.m_name;
                    int tier = __state.Tier[i];
                    int paidFromPack = __state.PackBefore[i] - __instance.m_inventory.CountItems(name);

                    int owing = CraftPayment.Owing(owed, paidFromPack);
                    if (owing <= 0) continue;

                    int taken = tier >= 1 ? ChestCrafting.Take(__state.Boxes, name, owing, tier) : 0;

                    int shortfall = CraftPayment.Shortfall(owed, paidFromPack, taken);
                    if (shortfall <= 0) continue;

                    // Nothing further is taken. The craft cannot be undone by
                    // this point, but charging the player's own pack to make
                    // the sums add up would spend materials they never agreed
                    // to spend, over a race they cannot see -- a worse cost
                    // than the one it covers. The shortfall is reported and
                    // left alone.
                    RossQoLPlugin.Log.LogError(
                        $"CraftFromChests: {CraftScope.Describe()} cost {owed} {name} (quality {tier}) but only "
                        + $"{paidFromPack + taken} were paid -- {paidFromPack} from your pack, {taken} from "
                        + $"{__state.Boxes.Count} nearby container(s). {shortfall} went unpaid; that craft was "
                        + "cheaper than it should have been.");
                }
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: could not charge containers for this craft: {ex}");
            }
        }
    }
}
