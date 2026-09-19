namespace RossQoL.Core.Items
{
    /// <summary>
    /// Whether enough time has passed since the last recall to allow another.
    /// Times are passed in rather than read from a clock, so the rule is
    /// testable without a running game and without any notion of a Unity
    /// frame.
    /// </summary>
    public static class RecallCooldown
    {
        /// <param name="lastUsed">
        /// The time the recall last fired, or null if it has never fired --
        /// the very first press must not be blocked by a cooldown that has
        /// nothing to count from.
        /// </param>
        /// <param name="now">The current time, in the same units as <paramref name="lastUsed"/>.</param>
        /// <param name="cooldownSeconds">
        /// How long a recall must wait before firing again. Zero or negative
        /// means "no cooldown" rather than "always blocked" -- a config typo
        /// should not brick the feature.
        /// </param>
        public static bool CanFire(float? lastUsed, float now, float cooldownSeconds)
        {
            if (cooldownSeconds <= 0f) return true;
            if (lastUsed == null) return true;

            return now - lastUsed.Value >= cooldownSeconds;
        }
    }
}
