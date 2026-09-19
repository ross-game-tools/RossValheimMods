using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// A corpse run buff that grows with how far your grave still is: more
    /// stamina regeneration and cheaper running and jumping the further out
    /// you died, and nothing at all within CorpseRunMinDistance. It goes
    /// when the grave is emptied.
    ///
    /// Synced scope: the strength curve is a world-balance knob like every
    /// other death-penalty setting in this category.
    /// </summary>
    internal sealed class CorpseRunFeature : Feature
    {
        public const string FeatureName = "Death/CorpseRun";

        public static CorpseRunFeature Instance { get; private set; }

        public CorpseRunFeature() => Instance = this;

        public override string Key => "CorpseRun";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "A corpse run buff that grows with how far your grave still is: more stamina regeneration and "
            + "cheaper running and jumping the further out you died, and nothing at all within "
            + "CorpseRunMinDistance. It goes when the grave is emptied.";

        // The buff needs no patch of its own; it only reads what
        // GraveRecordingPatch already records. FeatureActivator applies a
        // shared patch class once no matter how many features list it, so
        // this is correct and safe alongside GraveMarker and RespawnRested.
        public override IEnumerable<Type> PatchClasses => new[] { typeof(GraveRecordingPatch) };

        /// <summary>
        /// This feature's own members, plus the two lists it shares: the
        /// recording patch's and the looted-grave watcher's, each declared
        /// beside the code that reaches for them.
        /// </summary>
        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Player", "GetSEMan", "reaching the status effect manager to grant and remove the buff"),
            new CompatMember("SEMan", "AddStatusEffect", "granting the buff via the instance overload, which neither touches ObjectDB nor goes through an RPC"),
            new CompatMember("SEMan", "RemoveStatusEffect", "taking the buff away once the grave is close, gone, or the feature is off"),
            new CompatMember("SEMan", "GetStatusEffect", "fetching the live clone when AddStatusEffect reports it is already present"),
            new CompatMember("SEMan", "s_statusEffectSoftDeath", "the hash used to borrow vanilla's own post-death icon"),
            new CompatMember("SEMan", "s_statusEffectRested", "the hash used to borrow the Rested icon as a fallback if SoftDeath's is missing"),
            new CompatMember("SE_Stats", "m_staminaRegenMultiplier", "the stamina regen half of the buff"),
            new CompatMember("SE_Stats", "m_runStaminaDrainModifier", "the cheaper-running half of the buff"),
            new CompatMember("SE_Stats", "m_jumpStaminaUseModifier", "the cheaper-jumping half of the buff"),
            new CompatMember("StatusEffect", "m_icon", "without it the HUD skips the buff entirely"),
            new CompatMember("StatusEffect", "NameHash", "the hashed, unique identity a custom status effect needs"),
            new CompatMember("ObjectDB", "GetStatusEffect", "looking up the vanilla Rested effect to borrow its icon"),
        }
            .Concat(GraveRecordingPatch.RequiredMembers)
            .Concat(GraveCleanupWatcher.RequiredMembers);

        public override void BindSettings(ConfigFile config, string section) =>
            DeathConfig.BindCorpseRun(config, section, Scope);

        /// <summary>
        /// The watcher comes with the buff, not with the marker: the buff
        /// ends when the grave is cleared, and a server enabling this as a
        /// world rule cannot depend on each client leaving a cosmetic marker
        /// switched on for that to ever happen.
        /// </summary>
        public override void OnActivated(GameObject host)
        {
            host.AddComponent<CorpseRunEffect>();
            GraveCleanupWatcher.EnsureOn(host);
        }
    }
}
