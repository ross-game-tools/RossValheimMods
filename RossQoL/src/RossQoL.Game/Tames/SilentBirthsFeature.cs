using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// Tames give birth without the birth sound. The birth's other effects
    /// still play.
    ///
    /// Synced scope: the birth effect is created by the game that owns the
    /// parent and heard by everyone near it, so one setting has to decide
    /// for everyone.
    /// </summary>
    internal sealed class SilentBirthsFeature : Feature
    {
        public const string FeatureName = "Tames/SilentBirths";

        public static SilentBirthsFeature Instance { get; private set; }

        public SilentBirthsFeature() => Instance = this;

        public override string Key => "SilentBirths";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Tames give birth without the birth sound. The birth's other effects still play.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(SilentBirthsPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Procreation", "Procreate", "the moment a tame gives birth"),
            new CompatMember("Procreation", "m_birthEffects", "the effects played at a birth"),
            new CompatMember("EffectList", "m_effectPrefabs", "leaving out the sound from a birth's effects"),
        };
    }
}
