using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// A number box beside the Craft button: whatever it holds is how many
    /// that button makes. It starts at 1, which is vanilla crafting.
    ///
    /// Synced scope: a craft spends real materials and puts real items in the
    /// world, so how many a player may make at once is the server's rule, as
    /// it is for the other tweaks that change what a world contains.
    /// </summary>
    internal sealed class MultiCraftFeature : Feature
    {
        public const string FeatureName = "Crafting/MultiCraft";

        public static MultiCraftFeature Instance { get; private set; }

        public MultiCraftFeature() => Instance = this;

        public override string Key => "MultiCraft";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Adds a number box beside the Craft button. Type how many you want and the Craft button makes that "
            + "many, with the button, the ingredient list and the greying-out all following the number. The box "
            + "appears only for items that stack, and starts at 1, where crafting is vanilla's.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(MultiCraftSyncPatch),
            typeof(MultiCraftHidePatch),
            typeof(MultiCraftTypingPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("InventoryGui", "UpdateRecipe", "where the amount reaches the button and the ingredient list"),
            new CompatMember("InventoryGui", "Hide", "releasing the number box when the panel closes"),
            new CompatMember("InventoryGui", "m_craftButton", "making room for the box beside it"),
            new CompatMember("InventoryGui", "m_multiCraftAmount", "how many one craft makes"),
            new CompatMember("InventoryGui", "m_touchMultiCrafting", "telling vanilla this craft is a multi-craft"),
            new CompatMember("InventoryGui", "m_selectedRecipe", "telling a stackable recipe from an upgrade"),
            new CompatMember("Hud", "m_buildUi", "finding the build menu's search box to copy its look"),
            new CompatMember("BuildUi", "m_searchField", "copying the build menu's search box"),
            new CompatMember("ZInput", "ResetAllButtonStates", "keeping game keys from firing while you type"),
            new CompatMember("PlayerController", "FixedUpdate", "keeping movement keys from moving you while you type"),
            new CompatMember("Player", "Update", "keeping hotbar and other game keys from firing while you type"),
            new CompatMember("InventoryGui", "Update", "keeping E and Tab from closing the panel while you type"),
        };
    }
}
