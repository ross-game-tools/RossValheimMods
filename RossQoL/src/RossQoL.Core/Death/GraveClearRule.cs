namespace RossQoL.Core.Death
{
    /// <summary>What a single pass over one remembered grave concluded.</summary>
    public enum GraveVerdict
    {
        /// <summary>Nothing can be said about this grave right now.</summary>
        Ignore,

        /// <summary>The tombstone is standing right there: remember having seen it.</summary>
        Observe,

        /// <summary>It stood here earlier this session and is gone now: the player emptied it.</summary>
        Forget,
    }

    /// <summary>
    /// When a remembered grave may be forgotten for having been looted.
    ///
    /// The signal is the tombstone OBJECT standing near the recorded spot,
    /// not its ZDO. A ZDO's id is not stable across a save and load --
    /// `ZDO.Load` re-keys every loaded ZDO with `m_uid.SetID(++ZDOID.m_loadID)`
    /// -- so a ZDOID written down in one session identifies nothing at all in
    /// the next, and a lookup by it can only ever fail. Looking the tombstone
    /// up by where it is, and who it belongs to, is the part that survives a
    /// relog. See docs/valheim-api/death-and-respawn.md §12.
    ///
    /// The safety property this rule exists for is unchanged: a grave whose
    /// zone has not streamed in must never be deleted, because that silently
    /// costs the player the location of their gear. So a grave is only ever
    /// forgotten when its tombstone was seen STANDING earlier in this same
    /// world-session and has since gone while the player was close enough for
    /// the zone to be loaded. "Nothing there" on its own never deletes
    /// anything -- an unstreamed zone and an emptied grave look identical.
    /// </summary>
    public static class GraveClearRule
    {
        /// <summary>
        /// How far from the recorded spot a tombstone may be and still be
        /// that grave. A tombstone is spawned at the player's centre point,
        /// which is what gets recorded, and vanilla's own `PositionCheck`
        /// drags it back whenever it drifts more than 4m horizontally from
        /// its spawn point; the extra room covers the fall to the ground and
        /// bobbing in water.
        ///
        /// Erring large is the safe direction: matching a neighbouring grave
        /// too keeps BOTH records until both tombstones are gone, which is
        /// remembering for too long. Erring small would delete a record while
        /// the gear is still in the ground.
        /// </summary>
        public const float MatchRadius = 8f;

        /// <summary>True when a tombstone at (tx,ty,tz) is close enough to be the grave recorded at (gx,gy,gz).</summary>
        public static bool Matches(float gx, float gy, float gz, float tx, float ty, float tz)
        {
            float dx = gx - tx, dy = gy - ty, dz = gz - tz;
            return dx * dx + dy * dy + dz * dz <= MatchRadius * MatchRadius;
        }

        /// <summary>
        /// Decides what one pass over one grave has established.
        /// </summary>
        /// <param name="inRange">Player close enough that the grave's zone is certainly loaded.</param>
        /// <param name="tombstoneStanding">A tombstone of this player's was found at the recorded spot.</param>
        /// <param name="seenThisSession">This grave's tombstone has been observed standing earlier this world-session.</param>
        /// <param name="armed">The world has been joined long enough for a judgement to mean anything.</param>
        public static GraveVerdict Decide(bool inRange, bool tombstoneStanding, bool seenThisSession, bool armed)
        {
            // Out of range says nothing either way: an unloaded zone has no
            // tombstone in it, looted or not.
            if (!inRange) return GraveVerdict.Ignore;

            // Sightings are always worth taking, including inside the arming
            // window -- it is only the forgetting that waits.
            if (tombstoneStanding) return GraveVerdict.Observe;

            if (seenThisSession && armed) return GraveVerdict.Forget;

            // Nothing there, and nothing ever saw it there this session. That
            // is either a grave looted before this session, or one whose
            // objects have not spawned yet. The two are indistinguishable, so
            // the record stays -- a marker that outstays its grave is a
            // nuisance, a deleted record is lost gear.
            return GraveVerdict.Ignore;
        }
    }
}
