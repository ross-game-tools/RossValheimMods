using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// When a new summon takes you over your limit, the one that disappears is
    /// the most badly wounded rather than the one raised longest ago.
    ///
    /// Vanilla's summon cap (Tameable.UnsummonMaxInstances) sorts the creatures
    /// it counts by age and despawns however many are over the limit off the
    /// front of that list. Only that ordering changes: what counts as an
    /// instance, when the check runs and how many survive are all left exactly
    /// as vanilla has them.
    ///
    /// Health as a fraction of the creature's maximum, not as a raw number: a
    /// starred creature carries a bigger pool, and the one worth replacing is
    /// the wounded one whatever its tier. (The staff cannot raise a starred
    /// skeleton today, so the two rules pick the same creature in practice --
    /// the fraction is the one that stays right if that ever changes.)
    ///
    /// Client scope: the cap runs on whichever machine owns the new creature,
    /// which is the summoning player's own, and it despawns the same number of
    /// creatures either way. Turning it on changes which of your own summons
    /// you lose, and nothing about anyone else's.
    /// </summary>
    internal sealed class CullWoundedSummonsFeature : Feature
    {
        public const string FeatureName = "Tames/CullWoundedSummons";

        public static CullWoundedSummonsFeature Instance { get; private set; }

        public CullWoundedSummonsFeature() => Instance = this;

        public override string Key => "CullWoundedSummons";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "When raising a new skeleton puts you over your summon limit, the one that disappears is the most "
            + "badly wounded rather than the oldest. Equally hurt summons fall back to the oldest, as vanilla "
            + "does. How many you may have at once is unchanged.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(CullWoundedSummonsPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Tameable", "UnsummonMaxInstances",
                "the summon cap whose choice of which creature to despawn is being reordered"),
            new CompatMember("BaseAI", "GetTimeSinceSpawned",
                "breaking a tie between two equally wounded summons by age, as vanilla orders them"),
            new CompatMember("Character", "GetHealth",
                "reading how hurt a summon is"),
            new CompatMember("Character", "GetMaxHealth",
                "turning that health into a fraction, so a starred creature's larger pool is accounted for"),
        };
    }
}
