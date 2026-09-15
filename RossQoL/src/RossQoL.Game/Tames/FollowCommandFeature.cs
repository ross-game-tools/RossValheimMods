using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// Every tamed creature can be told to follow or stay, as vanilla allows
    /// only for some (wolves). Pressing Use on a tame toggles it.
    ///
    /// Synced scope: which tames can be commanded is a world rule; a personal
    /// setting would let one player lead tames another player cannot.
    /// </summary>
    internal sealed class FollowCommandFeature : Feature
    {
        public const string FeatureName = "Tames/FollowCommand";

        public static FollowCommandFeature Instance { get; private set; }

        public FollowCommandFeature() => Instance = this;

        public override string Key => "FollowCommand";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Every tamed creature can be told to follow you or stay, like a wolf: press Use on it to switch. "
            + "Creatures vanilla already lets you command are unchanged.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(FollowCommandPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Tameable", "Interact", "switching follow and stay when a tame is used"),
            new CompatMember("Tameable", "m_commandable", "which tames vanilla lets you command"),
            new CompatMember("Tameable", "m_monsterAI", "only creatures that can follow are commanded"),
        };
    }
}
