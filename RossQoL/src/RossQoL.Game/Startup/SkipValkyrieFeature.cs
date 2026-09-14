using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Startup
{
    internal sealed class SkipValkyrieFeature : Feature
    {
        public const string FeatureName = "Startup/SkipValkyrie";

        public static SkipValkyrieFeature Instance { get; private set; }

        public SkipValkyrieFeature() => Instance = this;

        public override string Key => "SkipValkyrie";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Skips the Valkyrie flight and the intro text on a new character's first spawn.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(ValkyrieIntroPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Game", "Start", "stopping the intro from being queued"),
            new CompatMember("Game", "m_queuedIntro", "stopping the intro from being queued"),
        };
    }
}
