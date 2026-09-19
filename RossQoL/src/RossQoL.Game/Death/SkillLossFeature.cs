using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Softens the skill loss a death costs you, by SkillLossMultiplier
    /// against vanilla's own loss. The smallest feature in the Death
    /// category, so the category's shared grave-recording plumbing can be
    /// proved end to end in game before the bigger features are built on it.
    ///
    /// Synced scope: how much a death costs in skill is the server's rule,
    /// the same as every other death-penalty knob.
    /// </summary>
    internal sealed class SkillLossFeature : Feature
    {
        public const string FeatureName = "Death/SkillLoss";

        public static SkillLossFeature Instance { get; private set; }

        public SkillLossFeature() => Instance = this;

        public override string Key => "SkillLoss";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Dying costs SkillLossMultiplier times the skill vanilla would normally take, so 0.5 halves the "
            + "loss and 0 removes it entirely. A soft death still costs nothing, same as vanilla.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(SkillLossPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Skills", "LowerAllSkills", "scaling what a death costs in skill"),
            new CompatMember("Skills", "OnDeath", "the death that calls it"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            DeathConfig.BindSkillLoss(config, section, Scope);
    }
}
