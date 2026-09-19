using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// Tames following you come through portals with you.
    ///
    /// Client scope: every setting only affects which of YOUR OWN tames follow
    /// YOU, and there is nothing a client can grant itself that it could not
    /// already do by walking its wolves to the portal.
    /// </summary>
    internal sealed class TamesFollowFeature : Feature
    {
        public static TamesFollowFeature Instance { get; private set; }

        public TamesFollowFeature() => Instance = this;

        public override string Key => "TamesFollow";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description => "Tames following you come through portals with you.";

        public override IEnumerable<Type> PatchClasses =>
            new[] { typeof(PortalPatch), typeof(TameableUnsummonPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("TeleportWorld", "Teleport",
                "patched to notice when you use a portal, which is the only trigger this feature has"),
            new CompatMember("Character", "GetAllCharacters", "the list every candidate tame is drawn from"),
            new CompatMember("Character", "IsTamed", "whether a candidate is yours rather than wild"),
            new CompatMember("Character", "IsAttached", "whether a candidate is busy and should be left alone"),
            new CompatMember("MonsterAI", "GetFollowTarget", "whether a tame is following you right now"),
            new CompatMember("ZDOVars", "s_follow", "the saved follow name a tame another client owns still carries"),
            new CompatMember("ZDOVars", "s_user", "whether a creature is currently ridden"),
            new CompatMember("ZNetView", "GetZDO", "reading and moving a candidate's ZDO"),
            new CompatMember("Player", "GetPlayerName", "matching a tame's saved follow name to you"),
            new CompatMember("Tameable", "Update",
                "patched to stop vanilla unsummoning a summon mid-hop, which destroys its ZDO outright"),
            new CompatMember("Tameable", "m_unsummonDistance",
                "the rule being held off: a portal hop is further than it allows"),
            new CompatMember("ZNetView", "IsValid", "skipping creatures whose networking is not ready"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            PortalTamesConfig.Bind(config, section, Scope);

        public override void OnActivated(GameObject host) => host.AddComponent<PortalTamesManager>();
    }
}
