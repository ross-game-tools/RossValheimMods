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

        public override IEnumerable<Type> PatchClasses => new[] { typeof(PortalPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("TeleportWorld", "Teleport",
                "patched to notice when you use a portal, which is the only trigger this feature has"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            PortalTamesConfig.Bind(config, section, Scope);

        public override void OnActivated(GameObject host) => host.AddComponent<PortalTamesManager>();
    }
}
