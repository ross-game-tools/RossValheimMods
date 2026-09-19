namespace RossQoL.Core.Items
{
    /// <summary>
    /// Whether a dropped item should be destroyed for being old litter,
    /// mirroring vanilla's own <c>ItemDrop.TimedDestruction</c> except that
    /// the base exemption can be switched off.
    ///
    /// Vanilla skips every other check once an item is inside a base -- its
    /// age, whether a player stands nearby, tar, and building debris are
    /// never even evaluated in that case, which is why litter inside a base
    /// never decays. This rule keeps every one of those conditions and only
    /// changes whether the base exemption itself applies.
    /// </summary>
    public static class ItemDecayRule
    {
        /// <summary>Vanilla's own one-hour clock; <c>ItemDrop.c_AutoDestroyTimeout</c>.</summary>
        public const double AutoDestroySeconds = 3600.0;

        /// <summary>Vanilla's own presence radius; the literal in <c>TimedDestruction</c>.</summary>
        public const float PlayerRangeMetres = 25f;

        /// <summary>
        /// True when the item has earned vanilla's ordinary decay: old
        /// enough, no player nearby, not in tar, not building debris. The
        /// base exemption is applied by the caller -- pass
        /// <paramref name="ignoreBaseExemption"/> as true only once it has
        /// already established the item is inside a base and the feature
        /// that lifts the exemption is active; outside a base, vanilla's own
        /// check already covers this and this rule should not be asked at
        /// all.
        /// </summary>
        public static bool ShouldDestroy(
            double ageSeconds, bool playerNearby, bool inTar, bool isDebris, bool ignoreBaseExemption)
        {
            if (!ignoreBaseExemption) return false;
            if (ageSeconds < AutoDestroySeconds) return false;
            if (playerNearby) return false;
            if (inTar) return false;
            if (isDebris) return false;

            return true;
        }
    }
}
