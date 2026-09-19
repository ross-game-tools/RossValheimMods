using System.Collections.Generic;
using RossQoL.Core.Death;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// The Death category itself, plus the one answer to "is anything here
    /// still interested in a death being remembered?".
    ///
    /// The category is built here rather than inline in
    /// <see cref="FeatureRegistry"/> so that the feature list the registry
    /// uses and the list this rule walks are the same array. A feature added
    /// to the category is therefore counted by the rule automatically; there
    /// is no second list to forget to update.
    /// </summary>
    internal static class DeathCategory
    {
        private const string Section = "Death";

        /// <summary>The features as constructed for the registry; null until <see cref="Create"/> runs.</summary>
        private static Feature[] _features;

        public static Category Create()
        {
            _features = new Feature[]
            {
                new GraveMarkerFeature(),
                new SkillLossFeature(),
                new RespawnFoodFeature(),
                new RespawnRestedFeature(),
                new CorpseRunFeature(),
            };

            return new Category(Section, "All death and corpse run tweaks.", _features);
        }

        /// <summary>
        /// True while any active feature needs to know where a grave is --
        /// the marker and the corpse run buff today. Gates both the recording
        /// of a grave and the clearing of one.
        /// </summary>
        public static bool NeedsGraveRecord => GraveConsumers.AnyNeedsGraveRecord(ActiveKeys());

        /// <summary>
        /// True while any active feature needs the one-shot died flag -- the
        /// respawn handouts, which never look at a grave.
        /// </summary>
        public static bool NeedsDiedFlag => GraveConsumers.AnyNeedsDiedFlag(ActiveKeys());

        private static IEnumerable<string> ActiveKeys()
        {
            var features = _features;
            if (features == null) yield break;

            foreach (var feature in features)
                if (feature.IsActive)
                    yield return feature.Key;
        }
    }
}
