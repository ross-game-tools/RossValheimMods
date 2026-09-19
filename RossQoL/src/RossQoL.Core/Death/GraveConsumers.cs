using System;
using System.Collections.Generic;

namespace RossQoL.Core.Death
{
    /// <summary>
    /// Which features in the Death category need a death remembered, and what
    /// each of them needs remembered.
    ///
    /// The recording hook is shared: one patch class listed by four features,
    /// three of them Synced, and Synced features are patched whether or not
    /// they are switched on. Without this rule a category turned off entirely
    /// would still write graves and the died flag into the character file on
    /// every death, and custom data is merged on load and never cleared, so
    /// those writes would outlive the decision to switch the category off.
    ///
    /// The rule lives here, keyed by feature key, so it is unit-tested with no
    /// game running and stays in one place rather than becoming a chain of ORs
    /// copied into a patch body.
    /// </summary>
    public static class GraveConsumers
    {
        public const string GraveMarker = "GraveMarker";
        public const string RespawnFood = "RespawnFood";
        public const string RespawnRested = "RespawnRested";
        public const string CorpseRun = "CorpseRun";

        /// <summary>
        /// Needs the grave RECORD: where the tombstone is and which ZDO it is.
        /// SkillLoss is deliberately absent -- it changes what a death costs
        /// and has no interest in where the body fell.
        /// </summary>
        public static bool NeedsGraveRecord(string featureKey) =>
            featureKey == GraveMarker || featureKey == CorpseRun;

        /// <summary>
        /// Needs the died FLAG only: the one-shot "this spawn follows a death"
        /// marker. The respawn handouts fire once on the next spawn and never
        /// look at a grave, so the flag is tested apart from the record -- a
        /// world running only the handouts writes one short key that the next
        /// spawn removes again, rather than a list of graves nothing reads.
        /// </summary>
        public static bool NeedsDiedFlag(string featureKey) =>
            featureKey == RespawnFood || featureKey == RespawnRested;

        /// <summary>True when any of the given active feature keys needs the grave record.</summary>
        public static bool AnyNeedsGraveRecord(IEnumerable<string> activeFeatureKeys) =>
            Any(activeFeatureKeys, NeedsGraveRecord);

        /// <summary>True when any of the given active feature keys needs the died flag.</summary>
        public static bool AnyNeedsDiedFlag(IEnumerable<string> activeFeatureKeys) =>
            Any(activeFeatureKeys, NeedsDiedFlag);

        private static bool Any(IEnumerable<string> keys, Func<string, bool> wants)
        {
            if (keys == null) return false;

            foreach (string key in keys)
                if (key != null && wants(key))
                    return true;

            return false;
        }
    }
}
