using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Fires
{
    /// <summary>
    /// Fires take fuel from nearby containers: fire pits, hearths, standing
    /// and wall torches, braziers and the bathtub, everything vanilla builds
    /// on Fireplace.
    ///
    /// How far it reaches, how often it tries, and how much fuel it leaves in
    /// your chests are the Production feed settings, shared with the workshop
    /// so a reserve of wood is set in one place.
    ///
    /// Synced scope: feeding moves real items out of real containers, so
    /// whether it happens is the server's rule.
    /// </summary>
    internal sealed class FiresFeedFeature : Feature
    {
        public const string FeatureName = "Fires/AutoFeed";

        public static FiresFeedFeature Instance { get; private set; }

        public FiresFeedFeature() => Instance = this;

        public override string Key => "AutoFeed";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Fires take fuel from containers within the Production FeedRadius: fire pits, hearths, torches, "
            + "braziers and bathtubs. The Production MinimumLeftBehind and MinimumPerItem settings decide how "
            + "much wood stays in your chests.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(FireplaceFeedPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Fireplace", "UpdateFireplace", "the moment a fire is fed"),
            new CompatMember("Fireplace", "m_fuelItem", "what a fire burns"),
            new CompatMember("Fireplace", "m_maxFuel", "how much fuel a fire still has room for"),
            new CompatMember("Fireplace", "m_infiniteFuel", "fires that never burn down are left alone"),
            new CompatMember("ZDOVars", "s_fuel", "how much fuel a fire holds"),
        };
    }
}
