using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// Tames following you come through portals and dungeon doors with you.
    ///
    /// One patch on Player.TeleportTo covers both: a portal and a crypt
    /// doorway are the same call with a different distantTeleport flag. See
    /// TeleportPatch.
    ///
    /// Client scope: every setting only affects which of YOUR OWN tames follow
    /// YOU, and there is nothing a client can grant itself that it could not
    /// already do by walking its wolves through the door.
    /// </summary>
    internal sealed class TamesFollowFeature : Feature
    {
        public static TamesFollowFeature Instance { get; private set; }

        public TamesFollowFeature() => Instance = this;

        public override string Key => "TamesFollow";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Tames and summons following you come through portals and dungeon doors with you.";

        public override IEnumerable<Type> PatchClasses =>
            new[] { typeof(TeleportPatch), typeof(TameableUnsummonPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Player", "TeleportTo",
                "patched to notice when you teleport, which is the only trigger this feature has; "
                + "portals and dungeon doors both go through it"),
            new CompatMember("Player", "IsTeleporting", "whether the journey is still in progress, so arrivals wait for its end"),
            new CompatMember("TeleportWorld", "Teleport", "the portal that calls Player.TeleportTo"),
            new CompatMember("Teleport", "Interact", "the dungeon door that calls Player.TeleportTo, in both directions"),
            new CompatMember("Teleport", "m_targetPoint", "the far side of a dungeon door, which is what makes it a pair"),
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
                "the rule being held off: a portal hop is further than it allows, and a dungeon interior "
                + "sits 5000 m above its door, further still"),
            new CompatMember("ZNetView", "IsValid", "skipping creatures whose networking is not ready"),
            new CompatMember("Character", "InInterior", "telling a dungeon interior from the overworld, which the arrival search has to ask differently"),
            new CompatMember("ZoneSystem", "IsBlocked", "whether a candidate arrival spot is obstructed, outdoors"),
            new CompatMember("ZoneSystem", "GetSolidHeight", "the floor under a candidate arrival spot, and whether there is one at all at the height you are standing at"),
            new CompatMember("ZoneSystem", "GetGroundHeight", "the terrain surface over a candidate arrival spot, which vanilla would lift a creature up to"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            PortalTamesConfig.Bind(config, section, Scope);

        public override void OnActivated(GameObject host) => host.AddComponent<PortalTamesManager>();
    }
}
