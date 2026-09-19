namespace RossQoL.Core.Items
{
    /// <summary>
    /// What a recall must do about the weapon's last attack before it plays
    /// its cast animation. See <see cref="RecallAttackHandover"/>.
    /// </summary>
    public enum RecallAttackAction
    {
        /// <summary>Nothing is in the way; play the cast straight away.</summary>
        PlayNow,

        /// <summary>
        /// A finished attack is still held as "current". It must be retired
        /// first, or replaying the attack animation fires that finished
        /// attack's payload a second time, for free.
        /// </summary>
        RetireThenPlay,

        /// <summary>
        /// An attack is still running. The recall must not fire at all --
        /// retiring an unfinished attack would rob it of its own payload
        /// after its cost has already been paid.
        /// </summary>
        Refuse,
    }

    /// <summary>
    /// The rule for handing a weapon over from its last attack to a recall
    /// cast.
    ///
    /// Vanilla holds onto the last attack it started (a character's
    /// "current attack") long after that attack has finished, and its
    /// animation-event handler fires that held attack's payload whenever the
    /// attack animation plays -- it only checks that an attack is held, never
    /// that it is still running. A feature that plays an attack animation
    /// without going through vanilla's own attack start must therefore decide
    /// what to do about whatever is being held, which is what this answers.
    ///
    /// Stated as a pure rule, with plain bools in and an answer out, so it is
    /// testable without a running game.
    /// </summary>
    public static class RecallAttackHandover
    {
        /// <param name="hasCurrentAttack">Whether the character is holding an attack at all.</param>
        /// <param name="currentAttackDone">
        /// Whether that held attack has finished. Meaningless when
        /// <paramref name="hasCurrentAttack"/> is false, and ignored then.
        /// </param>
        public static RecallAttackAction Decide(bool hasCurrentAttack, bool currentAttackDone)
        {
            if (!hasCurrentAttack) return RecallAttackAction.PlayNow;
            if (!currentAttackDone) return RecallAttackAction.Refuse;

            return RecallAttackAction.RetireThenPlay;
        }
    }
}
