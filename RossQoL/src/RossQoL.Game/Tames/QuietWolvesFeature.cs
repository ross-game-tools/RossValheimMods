using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// Tamed wolves stop howling. Wild wolves are unchanged, and so is every
    /// other creature.
    ///
    /// Client scope: idle sounds are played by each machine for the creatures
    /// it can hear, so silencing them changes only what this player hears.
    /// </summary>
    internal sealed class QuietWolvesFeature : Feature
    {
        public const string FeatureName = "Tames/QuietWolves";

        public static QuietWolvesFeature Instance { get; private set; }

        public QuietWolvesFeature() => Instance = this;

        public override string Key => "QuietWolves";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Tamed wolves stop howling. Wild wolves still howl, and other creatures keep their own sounds.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(QuietWolvesPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("BaseAI", "DoIdleSound", "where a creature's idle sound is played"),
            new CompatMember("BaseAI", "m_character", "telling a tamed wolf from a wild one"),
            new CompatMember("Character", "IsTamed", "telling a tamed wolf from a wild one"),
        };
    }
}
