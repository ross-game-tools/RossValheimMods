using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Dropped items float instead of sinking, using the same component
    /// vanilla gives wood.
    ///
    /// Synced scope: where dropped items end up is shared by everyone in the
    /// world, and an item floats or sinks on whichever machine owns it.
    /// </summary>
    internal sealed class FloatingItemsFeature : Feature
    {
        public const string FeatureName = "World/FloatingItems";

        public static FloatingItemsFeature Instance { get; private set; }

        public FloatingItemsFeature() => Instance = this;

        public override string Key => "FloatingItems";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Dropped items float on water instead of sinking, as wood does. Live fish are unchanged.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(FloatingItemsPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("ItemDrop", "Awake", "the moment a dropped item appears"),
            new CompatMember("ItemDrop", "m_floating", "telling the item it now floats"),
            new CompatMember("Floating", "m_waterLevelOffset", "how deep a floating item sits"),
            new CompatMember("Fish", "GetHoverText", "leaving live fish alone"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            WorldConfig.Bind(config, section, Scope);
    }
}
