using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using RossQoL.Game.Production;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// Crafting and building draw on the chests around you, not just what is
    /// in your pack.
    ///
    /// Vanilla asks two questions and this answers both the same way: can you
    /// afford this (HaveRequirements), and take what it costs
    /// (ConsumeResources). Your pack is always spent first; only the
    /// shortfall comes out of containers, so nothing is taken from a chest
    /// that your own inventory could have paid for.
    ///
    /// Synced scope: it moves real items out of shared containers, so whether
    /// it happens, and how far it reaches, is the server's rule.
    /// </summary>
    internal sealed class CraftFromChestsFeature : Feature
    {
        public const string FeatureName = "Crafting/CraftFromChests";

        public static CraftFromChestsFeature Instance { get; private set; }

        public CraftFromChestsFeature() => Instance = this;

        public override string Key => "CraftFromChests";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Crafting, upgrading and building may draw materials from containers within CraftRadius as well as "
            + "from your pack. Your own inventory is spent first, and only what it cannot cover comes out of a "
            + "chest. Containers you may not open, and other players' warded chests, are left alone.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(ContainerAwakeRegistryPatch),
            typeof(CraftRequirementsPatch),
            typeof(BuildRequirementsPatch),
            typeof(ConsumeFromChestsPatch),
            typeof(CraftScopePatch),
            typeof(SingleIngredientSourcePatch),
            typeof(SingleIngredientRemovePatch),
            typeof(RequirementDisplayPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Container", "Awake", "finding the containers to craft from"),
            new CompatMember("Container", "GetInventory", "reading and taking from containers"),
            new CompatMember("Container", "Save", "writing a container back after taking from it"),
            new CompatMember("Inventory", "CountItems", "how much a container holds"),
            new CompatMember("Inventory", "RemoveItem", "taking materials out of containers"),
            new CompatMember("Player", "HaveRequirements", "whether a recipe or piece can be afforded"),
            new CompatMember("Player", "ConsumeResources", "paying for it"),
            new CompatMember("Piece", "m_resources", "what a building piece costs"),
            new CompatMember("Recipe", "m_resources", "what a recipe costs"),
            new CompatMember("Recipe", "m_requireOnlyOneIngredient", "recipes that take any one of their ingredients"),
            new CompatMember("Player", "GetFirstRequiredItem", "sourcing the one ingredient such a recipe needs"),
            new CompatMember("Inventory", "GetItem", "finding that ingredient in a container"),
            new CompatMember("InventoryGui", "SetupRequirement", "showing a requirement as affordable"),
            new CompatMember("Player", "GetCurrentCraftingStation", "skipping the requirements vanilla skips"),
            new CompatMember("Player", "RequiredCraftingStation", "keeping the bench and its level required"),
            new CompatMember("CraftingStation", "HaveBuildStationInRange", "keeping a bench required to build"),
            new CompatMember("CraftingStation", "m_upgrader", "telling an upgrader's requirements from a station's"),
            new CompatMember("Container", "m_nview", "reaching a container's networking before writing it"),
            new CompatMember("ZNetView", "IsValid", "ignoring a container whose networking is not up yet"),
            new CompatMember("ZNetView", "GetZDO", "checking a container has stored state to be written"),
            new CompatMember("ZNetView", "ClaimOwnership", "taking charge of a container before taking from it"),
            new CompatMember("ZNetView", "IsOwner", "confirming that claim stuck before anything is written"),
            new CompatMember("InventoryGui", "DoCrafting", "knowing a craft is being made, not merely displayed"),
            new CompatMember("InventoryGui", "m_selectedRecipe", "naming the craft in a log line when payment falls short"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            CraftFromChestsConfig.Bind(config, section, Scope);
    }
}
