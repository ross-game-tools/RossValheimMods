using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// WearNTear's "weathered"/"worn" wood look is not a separate aging
    /// effect -- it is a swap between whole GameObjects keyed off health
    /// percentage (see docs/valheim-api/wear-and-tear.md), and rain is one of
    /// several things that erode that health over time. Suppressing rain's
    /// contribution keeps health near full, which keeps the new-looking model
    /// active, without touching structural collapse or combat/creature
    /// damage, both of which reach the same health value through unrelated
    /// code paths.
    ///
    /// Synced scope: a piece's health is real, persisted world state written
    /// by whichever client currently owns its ZDO, not a per-viewer visual --
    /// so whether rain erodes it is the server's rule.
    /// </summary>
    internal sealed class NoWeatheringFeature : Feature
    {
        public const string FeatureName = "World/NoWeathering";

        public static NoWeatheringFeature Instance { get; private set; }

        public NoWeatheringFeature() => Instance = this;

        public override string Key => "NoWeathering";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Rain no longer wears down or greys your buildings. Pieces still collapse if they lose support, "
            + "and still take damage from creatures and players. Applies to whoever owns the piece, so "
            + "everyone on a server needs it for consistent results.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(NoWeatheringPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("WearNTear", "UpdateWear", "the periodic tick rain damage is computed in"),
            new CompatMember("WearNTear", "m_noRoofWear", "the flag that gates rain wear, cleared for the length of each tick"),
        };
    }
}
