namespace RossQoL.Core.Progression
{
    /// <summary>
    /// When a dungeon is due to come back, counted in whole in-game days.
    ///
    /// A dungeon carries the day of your last visit. An unstamped one is not
    /// due for anything: the caller stamps it with today instead, which is how
    /// a world full of dungeons cleared before this feature existed joins the
    /// cycle without every one of them resetting at once.
    /// </summary>
    public static class DungeonRespawnMath
    {
        /// <param name="lastVisitDay">The day stamped on the dungeon, or null when it carries no stamp.</param>
        /// <param name="today">Today's in-game day.</param>
        /// <param name="respawnDays">Days between resets; zero or less never resets.</param>
        public static bool IsDue(int? lastVisitDay, int today, int respawnDays)
        {
            if (lastVisitDay == null) return false;
            if (respawnDays <= 0) return false;

            // A clock that moved backwards -- an older save loaded, or a world
            // whose time was wound back -- waits rather than resetting now.
            if (today < lastVisitDay.Value) return false;

            return today - lastVisitDay.Value >= respawnDays;
        }

        /// <summary>Days still to wait, for the log and for tests. Zero once due.</summary>
        public static int DaysRemaining(int? lastVisitDay, int today, int respawnDays)
        {
            if (lastVisitDay == null || respawnDays <= 0) return respawnDays > 0 ? respawnDays : 0;

            int elapsed = today - lastVisitDay.Value;
            if (elapsed < 0) elapsed = 0;

            int remaining = respawnDays - elapsed;
            return remaining > 0 ? remaining : 0;
        }
    }
}
