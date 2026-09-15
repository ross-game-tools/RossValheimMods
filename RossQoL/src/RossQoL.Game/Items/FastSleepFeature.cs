using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Sleeping is quicker: the night runs past in a couple of seconds
    /// instead of twelve, and the fade to and from black is short.
    ///
    /// Synced scope: the night is skipped by the server for everyone, so how
    /// long that takes is the server's rule. The fade is drawn on each
    /// client from the same setting.
    /// </summary>
    internal sealed class FastSleepFeature : Feature
    {
        public const string FeatureName = "World/FastSleep";

        public static FastSleepFeature Instance { get; private set; }

        public FastSleepFeature() => Instance = this;

        public override string Key => "FastSleep";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Sleeping is quicker: the night passes in SleepSkipSeconds instead of vanilla's twelve, and the "
            + "black screen fades in and out in SleepFadeSeconds instead of three.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(FastSleepSkipPatch),
            typeof(FastSleepFadePatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("EnvMan", "SkipToMorning", "how long the night takes to pass"),
            new CompatMember("EnvMan", "m_timeSkipSpeed", "how long the night takes to pass"),
            new CompatMember("EnvMan", "m_skipToTime", "how long the night takes to pass"),
            new CompatMember("Hud", "GetFadeDuration", "how long the black screen fades"),
            new CompatMember("Player", "IsSleeping", "fading quickly only while sleeping"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            WorldConfig.BindSleep(config, section, Scope);
    }
}
