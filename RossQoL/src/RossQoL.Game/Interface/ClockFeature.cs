using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Interface
{
    /// <summary>
    /// The in-game day and time under the minimap.
    ///
    /// Client scope: display only, and it reads the same world time every
    /// player already has.
    /// </summary>
    internal sealed class ClockFeature : Feature
    {
        public const string FeatureName = "Interface/Clock";

        public static ClockFeature Instance { get; private set; }

        public ClockFeature() => Instance = this;

        public override string Key => "Clock";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Shows the in-game day and time under the minimap, e.g. \"Day 42  14:30\". Midnight is 00:00, "
            + "sunrise 06:00, sunset 18:00. Hidden whenever the minimap is, including on worlds without a map.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(ClockMinimapPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Minimap", "Start", "adding the clock when the minimap is created"),
            new CompatMember("Minimap", "m_smallRoot", "placing the clock under the minimap"),
            new CompatMember("Minimap", "m_biomeNameSmall", "matching the minimap's text style"),
            new CompatMember("EnvMan", "GetDayFraction", "reading the time of day"),
            new CompatMember("EnvMan", "GetCurrentDay", "reading the same day number vanilla announces"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            ClockConfig.Bind(config, section, Scope);
    }
}
