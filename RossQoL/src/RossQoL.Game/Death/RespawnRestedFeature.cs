using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Gives at least RestedMinutes of the Rested buff when you respawn
    /// after a death, so the run back is not also a slog on empty stamina.
    /// A better Rested from your own house is left alone. Never fires on a
    /// plain login.
    ///
    /// Synced scope: the minutes handed out is a world-balance knob like
    /// every other death-penalty setting.
    /// </summary>
    internal sealed class RespawnRestedFeature : Feature
    {
        public const string FeatureName = "Death/RespawnRested";

        public static RespawnRestedFeature Instance { get; private set; }

        public RespawnRestedFeature() => Instance = this;

        public override string Key => "RespawnRested";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Respawning after a death gives you at least RestedMinutes of Rested, so the run back is not "
            + "also a slog on empty stamina. A better Rested from your own house is left alone.";

        public override IEnumerable<Type> PatchClasses =>
            new[] { typeof(RespawnPatch), typeof(GraveRecordingPatch) };

        /// <summary>
        /// This feature's own members, plus the shared recording patch's own
        /// list, declared beside that patch so a rename there disables every
        /// feature depending on it rather than just one of them.
        /// </summary>
        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Player", "OnSpawned", "the hook shared with RespawnFood to tell a death-respawn from a login"),
            new CompatMember("Player", "GetSEMan", "reaching the status effect manager to grant and read Rested"),
            new CompatMember("SEMan", "AddStatusEffect", "granting Rested if the player does not already have it"),
            new CompatMember("SEMan", "GetStatusEffect", "reading the live Rested instance, never the ObjectDB asset"),
            new CompatMember("SEMan", "s_statusEffectRested", "the hash identifying the Rested status effect"),
            new CompatMember("StatusEffect", "GetRemaningTime", "checking whether Rested is already at or above the minimum"),
            new CompatMember("StatusEffect", "m_ttl", "raising Rested's remaining time up to the minimum"),
        }
            .Concat(GraveRecordingPatch.RequiredMembers);

        public override void BindSettings(ConfigFile config, string section) =>
            DeathConfig.BindRespawnRested(config, section, Scope);
    }
}
