using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// A search box above the crafting recipe list.
    ///
    /// Client scope: it only changes which recipes YOUR list shows.
    /// </summary>
    internal sealed class CraftingSearchFeature : Feature
    {
        public const string FeatureName = "Crafting/RecipeSearch";

        public static CraftingSearchFeature Instance { get; private set; }

        public CraftingSearchFeature() => Instance = this;

        public override string Key => "RecipeSearch";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Adds a search box above the crafting recipe list. Typing narrows the list to recipes whose name "
            + "contains the text, ignoring case and spaces. The box clears when the panel closes.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(RecipeListFilterPatch),
            typeof(SearchBoxShowPatch),
            typeof(SearchBoxHidePatch),
            typeof(SearchBoxFocusPatch),
            typeof(TypingReleasesGameKeysPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("InventoryGui", "UpdateRecipeList", "filtering the recipe list"),
            new CompatMember("InventoryGui", "UpdateCraftingPanel", "refreshing the list as you type"),
            new CompatMember("InventoryGui", "Show", "adding the search box and focusing it"),
            new CompatMember("InventoryGui", "Hide", "clearing the search when the panel closes"),
            new CompatMember("InventoryGui", "m_recipeListRoot", "placing the search box above the recipe list"),
            new CompatMember("InventoryGui", "m_recipeListBaseSize", "keeping the recipe list's scroll height right"),
            new CompatMember("InventoryGui", "m_recipeListScroll", "scrolling back to the top after a search"),
            new CompatMember("Hud", "m_buildUi", "finding the build menu's search box to copy its look"),
            new CompatMember("BuildUi", "m_searchField", "copying the build menu's search box"),
            new CompatMember("Player", "GetCurrentCraftingStation", "auto-focusing only at a crafting station"),
            new CompatMember("InventoryGui", "Update", "auto-focusing the box, and keeping E and Tab from closing the panel while you type"),
            new CompatMember("PlayerController", "FixedUpdate", "keeping movement keys from moving you while you type"),
            new CompatMember("Player", "Update", "keeping hotbar and other game keys from firing while you type"),
            new CompatMember("ZInput", "ResetAllButtonStates", "keeping game keys from firing while you type"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            CraftingSearchConfig.Bind(config, section, Scope);
    }
}
