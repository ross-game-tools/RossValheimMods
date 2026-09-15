using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using RossQoL.Game.Production;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// A hungry tame that finds no food on the ground eats one item it would
    /// eat from a nearby container. Runs on the client that owns the tame,
    /// the peer vanilla already lets run its AI, so it happens while a
    /// player is near. Only creatures already tame.
    ///
    /// Synced scope: it takes items from shared containers, so whether it
    /// happens, and how far it reaches, is the server's rule.
    /// </summary>
    internal sealed class FeedFromContainersFeature : Feature
    {
        public const string FeatureName = "Tames/FeedFromContainers";

        public static FeedFromContainersFeature Instance { get; private set; }

        public FeedFromContainersFeature() => Instance = this;

        public override string Key => "FeedFromContainers";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "A hungry tame with no food on the ground near it eats one item it likes from a container within "
            + "FeedRadius. Only creatures that are already tame.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(ContainerAwakeRegistryPatch),
            typeof(FeedFromContainersPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Container", "Awake", "finding the containers food can come from"),
            new CompatMember("Container", "m_nview", "checking who owns a container"),
            new CompatMember("Container", "CheckAccess", "respecting private containers"),
            new CompatMember("Container", "m_privacy", "respecting private containers"),
            new CompatMember("Container", "m_piece", "respecting private containers"),
            new CompatMember("Container", "m_checkGuardStone", "respecting wards"),
            new CompatMember("ZDO", "DataRevision", "reading a container's latest contents"),
            new CompatMember("ZDO", "OwnerRevision", "waiting for a container's ownership to settle"),            new CompatMember("Container", "IsInUse", "leaving open containers alone"),
            new CompatMember("Container", "GetInventory", "taking food from containers"),
            new CompatMember("Container", "Load", "reading a container's latest contents"),
            new CompatMember("Container", "m_lastRevision", "reading a container's latest contents"),
            new CompatMember("Inventory", "RemoveOneItem", "taking one item of food"),
            new CompatMember("Inventory", "CountItems", "confirming the food was taken"),
            new CompatMember("PrivateArea", "CheckAccess", "respecting wards"),
            new CompatMember("MonsterAI", "UpdateConsumeItem", "the moment a tame looks for food"),
            new CompatMember("MonsterAI", "m_consumeItems", "what a tame eats"),
            new CompatMember("MonsterAI", "m_consumeSearchTimer", "looking in containers only when vanilla looks on the ground"),
            new CompatMember("MonsterAI", "m_onConsumedItem", "feeding a tame the way eating does"),
            new CompatMember("BaseAI", "m_tamable", "only tame creatures eat from containers"),
            new CompatMember("BaseAI", "m_nview", "only the tame's owner feeds it"),
            new CompatMember("BaseAI", "m_animator", "playing the eating animation"),
            new CompatMember("Humanoid", "m_consumeItemEffects", "playing the eating effect"),
            new CompatMember("Tameable", "IsHungry", "feeding only hungry tames"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            FeedConfig.Bind(config, section, Scope);
    }
}
