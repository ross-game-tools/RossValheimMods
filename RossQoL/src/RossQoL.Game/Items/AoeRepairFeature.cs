using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// One hammer repair fixes every damaged piece around the one you
    /// clicked, for the cost of a single swing.
    ///
    /// Synced scope: repairing writes shared pieces, so whether it happens,
    /// and how far it reaches, is the server's rule.
    /// </summary>
    internal sealed class AoeRepairFeature : Feature
    {
        public const string FeatureName = "World/AoeRepair";

        public static AoeRepairFeature Instance { get; private set; }

        public AoeRepairFeature() => Instance = this;

        public override string Key => "AoeRepair";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Repairing with the hammer also repairs every damaged piece within RepairRadius of the one you "
            + "clicked, for one swing's stamina and durability.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(AoeRepairPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Player", "Repair", "the moment you repair with the hammer"),
            new CompatMember("Player", "GetHoveringPiece", "the piece the repair is centred on"),
            new CompatMember("WearNTear", "Repair", "repairing each piece the way vanilla does"),
            new CompatMember("WearNTear", "s_allInstances", "finding the damaged pieces nearby"),
            new CompatMember("PrivateArea", "CheckAccess", "respecting wards"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            WorldConfig.BindRepair(config, section, Scope);
    }
}
