using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// A raised skeleton cannot be petted or told to stay, so it always
    /// follows its summoner.
    ///
    /// Why that matters: vanilla's summon cap (Tameable.UnsummonMaxInstances)
    /// counts only creatures whose ZDO follow string matches the summoner's
    /// name, and telling one to stay clears that string. A skeleton left
    /// standing somewhere therefore stops being counted, and the next cast
    /// raises another one over the cap. Removing the interaction closes that
    /// without touching the cap itself.
    ///
    /// Synced scope: the cap governs how many creatures exist in the shared
    /// world, and the count runs on whichever machine owns the creature, so
    /// one player opting out would change what everyone else sees.
    /// </summary>
    internal sealed class NoSummonCommandsFeature : Feature
    {
        public const string FeatureName = "Tames/NoSummonCommands";

        public static NoSummonCommandsFeature Instance { get; private set; }

        public NoSummonCommandsFeature() => Instance = this;

        public override string Key => "NoSummonCommands";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Raised skeletons cannot be petted or told to stay, so they always follow you and always count "
            + "against how many you may have at once. Tamed creatures are unaffected.";

        public override IEnumerable<Type> PatchClasses =>
            new[] { typeof(NoSummonCommandsPatch), typeof(NoSummonCommandsHoverPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Tameable", "Interact", "the petting and follow/stay command a raised skeleton loses"),
            new CompatMember("Tameable", "GetHoverText", "removing the prompt for an interaction that no longer happens"),
            new CompatMember("Tameable", "IsTamed", "leaving the hover text alone for anything not shown as tame"),
            new CompatMember("Tameable", "GetName", "rebuilding the hover text without the prompt"),
            new CompatMember("Tameable", "GetStatusString", "rebuilding the hover text without the prompt"),
            new CompatMember("Tameable", "m_character", "telling a raised skeleton from every other tame"),
            new CompatMember("Localization", "Localize", "showing the creature's status in the player's language"),
        };
    }
}
