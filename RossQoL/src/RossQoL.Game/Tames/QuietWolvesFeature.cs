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
    /// Client scope: the howl is silenced as this machine plays it, so it
    /// changes only what this player hears, and works for wolves another
    /// player owns as well as this client's own.
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

        public override IEnumerable<Type> PatchClasses =>
            new[] { typeof(QuietWolvesPatch), typeof(QuietWolvesSoundPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("BaseAI", "DoIdleSound", "where a creature's idle sound is played"),
            new CompatMember("BaseAI", "m_character", "telling a tamed wolf from a wild one"),
            new CompatMember("Character", "IsTamed", "telling a tamed wolf from a wild one"),
            new CompatMember("ZSFX", "Play", "silencing a howl another client sent"),
            new CompatMember("Character", "GetAllCharacters", "finding the wolf a howl came from"),
        };
    }
}
