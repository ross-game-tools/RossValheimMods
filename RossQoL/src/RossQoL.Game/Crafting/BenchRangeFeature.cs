using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// Crafting stations reach further, so a workshop can be a room rather
    /// than a huddle. One setting moves every station; a per-station list
    /// overrides it where one bench wants a different radius.
    ///
    /// Synced scope: build range decides where pieces may be placed in a
    /// shared world, so it is the server's rule.
    /// </summary>
    internal sealed class BenchRangeFeature : Feature
    {
        public const string FeatureName = "Crafting/BenchRange";

        public static BenchRangeFeature Instance { get; private set; }

        public BenchRangeFeature() => Instance = this;

        public override string Key => "BenchRange";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Crafting stations reach BuildRange metres instead of vanilla's 10, with BuildRangePerType for "
            + "stations that want their own, and ExtensionRange for how far their attachments may sit. "
            + "Switching it off puts every station back to the range its prefab shipped with.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(BenchRangePatch),
            typeof(ExtensionRangePatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("CraftingStation", "GetExtensions", "where a station recalculates its own reach"),
            new CompatMember("CraftingStation", "m_rangeBuild", "the range every other part of the game reads"),
            new CompatMember("StationExtension", "Awake", "the moment an attachment learns its distance"),
            new CompatMember("StationExtension", "m_maxStationDistance", "how far an attachment may sit from its station"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            BenchRangeConfig.Bind(config, section, Scope);
    }
}
