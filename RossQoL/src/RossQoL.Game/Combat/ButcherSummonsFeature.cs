using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// The butcher knife cuts down your summons -- raised skeletons and the
    /// Spirit Caller's creatures -- the same way it butchers a tamed animal, a
    /// quick way to clear your own summons without harming anything else.
    ///
    /// Vanilla's butcher knife is the one weapon that ships with
    /// <c>m_tamedOnly</c>, so its swing skips every target that is not tamed
    /// (Attack.DoMeleeAttack / DoAreaAttack), and a summon reads as untamed --
    /// so today the knife passes straight through one.
    ///
    /// Client scope: the swing is resolved on the attacker's own client, so
    /// whether your butcher knife cuts summons is your own business and needs
    /// no agreement with the server.
    /// </summary>
    internal sealed class ButcherSummonsFeature : Feature
    {
        public const string FeatureName = "Combat/ButcherSummons";

        public static ButcherSummonsFeature Instance { get; private set; }

        public ButcherSummonsFeature() => Instance = this;

        public override string Key => "ButcherSummons";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "The butcher knife kills your summons -- raised skeletons and the Spirit Caller's creatures -- "
            + "the same way it butchers a tamed animal, so you can clear them quickly. A normal butcher "
            + "knife swings straight through a summon, since only tamed creatures can be butchered.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(ButcherSummonsPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Attack", "DoMeleeAttack", "the melee swing whose tamed-only target filter is widened to summons"),
            new CompatMember("Attack", "DoAreaAttack", "the area swing whose tamed-only target filter is widened to summons"),
            new CompatMember("Character", "IsTamed", "the tamed test the butcher knife's filter uses, read as tamed-or-summon"),
        };
    }
}
