using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Beehives, sap collectors and fermenters empty themselves into nearby
    /// containers. Runs on the client that owns each loaded producer, the
    /// peer vanilla already lets write it, so a base harvests while a
    /// player is near it.
    ///
    /// Synced scope: harvesting writes shared containers and producers, so
    /// whether it happens, and how far it reaches, is the server's rule.
    /// </summary>
    internal sealed class AutoHarvestFeature : Feature
    {
        public const string FeatureName = "Production/AutoHarvest";

        public static AutoHarvestFeature Instance { get; private set; }

        public AutoHarvestFeature() => Instance = this;

        public override string Key => "AutoHarvest";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Beehives, sap collectors and fermenters near a player empty themselves into containers within "
            + "HarvestRadius: first containers already holding that item, then the nearest with room. "
            + "Output that fits nowhere stays in the producer.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(ContainerAwakeRegistryPatch),
            typeof(BeehiveHarvestPatch),
            typeof(SapCollectorHarvestPatch),
            typeof(FermenterHarvestPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Container", "Awake", "finding the containers output can go into"),
            new CompatMember("Container", "m_nview", "checking who owns a container"),
            new CompatMember("Container", "m_wagon", "leaving cart storage alone"),
            new CompatMember("Container", "m_rootObjectOverride", "leaving ship and cart storage alone"),
            new CompatMember("Container", "m_privacy", "respecting private containers"),
            new CompatMember("Container", "m_piece", "respecting private containers"),
            new CompatMember("Container", "CheckAccess", "respecting private containers"),
            new CompatMember("Container", "IsInUse", "leaving open containers alone"),
            new CompatMember("Container", "GetInventory", "putting output into containers"),
            new CompatMember("Inventory", "FindFreeStackSpace", "measuring a container's room"),
            new CompatMember("Inventory", "GetEmptySlots", "measuring a container's room"),
            new CompatMember("Inventory", "CountItems", "counting what landed in a container"),
            new CompatMember("Inventory", "HaveItem", "preferring containers that already hold the item"),
            new CompatMember("PrivateArea", "CheckAccess", "respecting wards"),
            new CompatMember("Beehive", "UpdateBees", "the moment a beehive is harvested"),
            new CompatMember("Beehive", "m_nview", "harvesting only beehives this client owns"),
            new CompatMember("Beehive", "m_honeyItem", "what a beehive produces"),
            new CompatMember("Beehive", "m_spawnPoint", "where rounding leftovers drop"),
            new CompatMember("SapCollector", "UpdateTick", "the moment a sap collector is harvested"),
            new CompatMember("SapCollector", "m_nview", "harvesting only sap collectors this client owns"),
            new CompatMember("SapCollector", "m_spawnItem", "what a sap collector produces"),
            new CompatMember("SapCollector", "m_spawnPoint", "where rounding leftovers drop"),
            new CompatMember("SapCollector", "RPC_UpdateEffects", "updating a sap collector's look after emptying it"),
            new CompatMember("Fermenter", "SlowUpdate", "the moment a fermenter is harvested"),
            new CompatMember("Fermenter", "m_nview", "harvesting only fermenters this client owns"),
            new CompatMember("Fermenter", "m_fermentationDuration", "knowing when a batch is ready"),
            new CompatMember("Fermenter", "GetItemConversion", "what a batch turns into"),
            new CompatMember("Fermenter", "m_outputPoint", "where an unplaced part of a batch drops"),
            new CompatMember("Game", "ScaleDrops", "matching vanilla's honey and sap per level"),
            new CompatMember("PlayerProfile", "s_bypassCheatChecks", "keeping vanilla's cheated flag on output"),
            new CompatMember("ZDOVars", "s_level", "reading and emptying beehives and sap collectors"),
            new CompatMember("ZDOVars", "s_content", "reading and emptying fermenters"),
            new CompatMember("ZDOVars", "s_startTime", "reading and emptying fermenters"),
            new CompatMember("ZDOVars", "s_cheatedQueued", "keeping vanilla's cheated flag on output"),
            new CompatMember("ZDOVars", "s_cheated", "keeping vanilla's cheated flag on output"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            ProductionConfig.Bind(config, section, Scope);
    }
}
