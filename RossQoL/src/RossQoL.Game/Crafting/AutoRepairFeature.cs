using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// Opening a crafting station repairs what that station can repair,
    /// saving the click-per-item on the repair button.
    ///
    /// What counts as repairable is vanilla's own rule, not ours: the
    /// station must be one the item's recipe names as its crafting or repair
    /// station, and its level must meet the recipe's minimum. A workbench
    /// therefore still cannot mend a bronze axe, and a level 1 forge still
    /// cannot mend what needs level 2.
    ///
    /// Synced scope: repairing changes durability on real items, so whether
    /// it happens at all is the server's rule, as it is for hammer repairs.
    /// </summary>
    internal sealed class AutoRepairFeature : Feature
    {
        public const string FeatureName = "Crafting/AutoRepair";

        public static AutoRepairFeature Instance { get; private set; }

        public AutoRepairFeature() => Instance = this;

        public override string Key => "AutoRepair";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Opening a crafting station repairs everything it is able to repair, in one go. A station only "
            + "mends what its own type and level allow, exactly as the repair button does.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(AutoRepairPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("InventoryGui", "Show", "the moment a crafting station is opened"),
            new CompatMember("InventoryGui", "CanRepair", "vanilla's rule for what this station may repair"),
            new CompatMember("Player", "GetCurrentCraftingStation", "the station being opened"),
            new CompatMember("Player", "RaiseSkill", "the crafting skill a repair gives"),
            new CompatMember("Inventory", "GetWornItems", "the damaged items to repair"),
            new CompatMember("CraftingStation", "m_canRepair", "stations that repair nothing are left alone"),
            new CompatMember("CraftingStation", "CheckUsable", "respecting a station you may not use"),
            new CompatMember("CraftingStation", "m_repairItemDoneEffects", "the repair sound, played once"),
        };
    }
}
