using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// Portals skip vanilla's fixed teleport delays: you are moved the moment
    /// you step in and arrive as soon as the destination has loaded. Only
    /// portal trips; other teleports keep vanilla's timing.
    ///
    /// Client scope: it changes only how long this player's own loading
    /// screen lasts; where they arrive and what loads is unchanged.
    /// </summary>
    internal sealed class InstantPortalsFeature : Feature
    {
        public const string FeatureName = "Portals/InstantPortals";

        public static InstantPortalsFeature Instance { get; private set; }

        public InstantPortalsFeature() => Instance = this;

        public override string Key => "InstantPortals";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Portals skip the fixed wait: you arrive as soon as the destination has loaded, instantly when it "
            + "already is. Other teleports are unchanged.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(InstantPortalStartPatch),
            typeof(InstantPortalUpdatePatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("TeleportWorld", "Teleport", "noticing that you used a portal"),
            new CompatMember("Player", "UpdateTeleport", "where vanilla waits before and during a teleport"),
            new CompatMember("Player", "m_teleporting", "knowing a teleport is in progress"),
            new CompatMember("Player", "m_teleportTimer", "skipping vanilla's fixed teleport delays"),
        };
    }
}
