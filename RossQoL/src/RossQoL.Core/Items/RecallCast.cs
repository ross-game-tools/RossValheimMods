namespace RossQoL.Core.Items
{
    /// <summary>
    /// Whether a pending recall's cast time has elapsed. Times are passed in
    /// rather than read from a clock, for the same reason as
    /// <see cref="RecallCooldown"/>: testable without a running game and
    /// without any notion of a Unity frame.
    /// </summary>
    public static class RecallCast
    {
        /// <param name="startedAt">
        /// The time the cast began, in the same units as <paramref name="now"/>.
        /// </param>
        /// <param name="now">The current time.</param>
        /// <param name="castSeconds">
        /// How long the cast takes. Zero or negative means "instant" -- the
        /// caller is expected not to schedule anything at all in that case,
        /// but this still answers true so a stray call is never stuck
        /// waiting on a cast that was never meant to take time.
        /// </param>
        public static bool IsComplete(float startedAt, float now, float castSeconds)
        {
            if (castSeconds <= 0f) return true;

            return now - startedAt >= castSeconds;
        }
    }
}
