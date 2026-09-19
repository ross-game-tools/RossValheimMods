using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// Whether a craft is actually being made right now, and which one.
    ///
    /// The crafting panel asks vanilla the same questions every frame it is
    /// open -- what does this cost, which ingredient would pay for it -- and
    /// those questions run through the very same methods a real craft does.
    /// Anything the mod arms on the strength of being asked would therefore
    /// be armed continuously while the panel is merely open, and could fire
    /// for an unrelated inventory change that happened to land in the same
    /// frame. This narrows "a craft is happening" to what it actually means:
    /// InventoryGui.DoCrafting is on the stack.
    ///
    /// It also carries the crafted item's name, so a payment that comes up
    /// short can say what the player got for free.
    /// </summary>
    internal static class CraftScope
    {
        private static int _depth;
        private static string _name;

        /// <summary>True only while vanilla is making the craft, not while the panel is drawing it.</summary>
        public static bool InProgress => _depth > 0;

        public static void Begin(string name)
        {
            _depth++;
            if (_depth == 1) _name = name;
        }

        public static void End()
        {
            if (_depth > 0) _depth--;
            if (_depth == 0) _name = null;
        }

        /// <summary>How to name the craft in a log line.</summary>
        public static string Describe() =>
            string.IsNullOrEmpty(_name) ? "a craft or build" : $"crafting {_name}";
    }

    /// <summary>
    /// Marks the window in which a craft is really being made. A finalizer
    /// closes it, so an exception inside vanilla's crafting cannot leave the
    /// window stuck open.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class CraftScopePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(
                typeof(InventoryGui), nameof(InventoryGui.DoCrafting), CraftFromChestsFeature.FeatureName);

        private static void Prefix(InventoryGui __instance, out bool __state)
        {
            __state = false;
            try
            {
                if (!ChestCraftingRules.Active(Player.m_localPlayer)) return;

                CraftScope.Begin(SelectedName(__instance));
                __state = true;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: could not mark this craft as under way: {ex}");
            }
        }

        private static void Finalizer(bool __state)
        {
            try
            {
                if (__state) CraftScope.End();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CraftFromChests: could not close this craft's window: {ex}");
            }
        }

        /// <summary>The name of whatever is being made, for the log. Never load-bearing.</summary>
        private static string SelectedName(InventoryGui gui)
        {
            var recipe = gui.m_selectedRecipe.Recipe;
            if (recipe == null) return null;

            var item = recipe.m_item;
            if (item == null) return null;

            return item.m_itemData.m_shared.m_name;
        }
    }
}
