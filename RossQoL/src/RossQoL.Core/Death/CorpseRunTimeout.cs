namespace RossQoL.Core.Death
{
    /// <summary>
    /// Whether the corpse run buff's time limit has run out, given when it
    /// started for the grave it is currently tracking and the current time.
    ///
    /// Both timestamps are in the same clock the caller chooses -- this
    /// makes no assumption about which one, only that "now" is never earlier
    /// than "start". The limit is a cap measured from when the buff first
    /// applied for that grave, not a countdown that pauses or resets with
    /// anything the player does; only a new grave (tracked by the caller,
    /// in RossQoL.Game's CorpseRunEffect) restarts it.
    /// </summary>
    public static class CorpseRunTimeout
    {
        /// <summary>True once <paramref name="limitMinutes"/> have passed since <paramref name="startSeconds"/>. 0 never expires.</summary>
        public static bool Expired(float startSeconds, float nowSeconds, float limitMinutes)
        {
            if (limitMinutes <= 0f) return false;

            float elapsedSeconds = nowSeconds - startSeconds;
            return elapsedSeconds >= limitMinutes * 60f;
        }
    }
}
