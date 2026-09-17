using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Comfort counts furniture further from where you stand, so a hall can
    /// be furnished like a hall rather than crowded around the fire.
    ///
    /// Only the radius changes. What counts, how pieces of the same kind are
    /// collapsed, and the requirement to be under shelter are all vanilla's.
    ///
    /// Synced scope: comfort decides the rested buff every player in the
    /// world gets, so the server sets the reach.
    /// </summary>
    internal sealed class ComfortRangeFeature : Feature
    {
        public const string FeatureName = "World/ComfortRange";

        public static ComfortRangeFeature Instance { get; private set; }

        public ComfortRangeFeature() => Instance = this;

        public override string Key => "ComfortRange";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Furniture counts toward comfort from ComfortRadius metres away instead of vanilla's 10. Which "
            + "pieces count, and the need to be under shelter, are unchanged.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(ComfortRangePatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("SE_Rested", "GetNearbyComfortPieces", "where comfort furniture is gathered"),
            new CompatMember("Piece", "GetAllComfortPiecesInRadius", "gathering it at the configured reach"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            WorldConfig.BindComfort(config, section, Scope);
    }
}
