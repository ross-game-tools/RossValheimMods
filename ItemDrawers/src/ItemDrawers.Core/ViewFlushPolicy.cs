namespace ItemDrawers.Core
{
    /// <summary>What a peer with a pending view change should do this frame.</summary>
    public enum ViewFlushAction
    {
        /// <summary>Wait: either ownership has not settled, or it is not our turn to take it.</summary>
        Wait,

        /// <summary>Take ownership so the change can be written.</summary>
        Claim,

        /// <summary>We own the drawer and ownership has settled; write the change.</summary>
        Reconcile,
    }

    /// <summary>
    /// When a peer holding an unflushed container-view change may claim a
    /// drawer, and when it may write.
    ///
    /// This lives in Core, away from the engine, because the interesting
    /// behaviour is the interaction between TWO peers over time, and that is
    /// not something a running game makes easy to observe -- it needs a
    /// second player on a server. As pure arithmetic it can be simulated
    /// directly; see the two-peer tests.
    /// </summary>
    public static class ViewFlushPolicy
    {
        /// <summary>
        /// How long a peer must have held ownership, with no ownership change
        /// at all, before writing. Longer than a round trip, so writes the
        /// previous owner sent before it saw the claim have arrived.
        /// </summary>
        public const float SettleSeconds = 1f;

        /// <summary>
        /// How long a peer that wants the drawer waits before taking it from
        /// the current owner again.
        ///
        /// STRICTLY LONGER than <see cref="SettleSeconds"/>, and the gap is
        /// the point. When both were the same value, two contending peers
        /// livelocked: the owner needed that much unchanged ownership before
        /// it could reconcile, and the rival re-claimed at exactly that age,
        /// resetting ownership at the instant the owner was about to settle.
        /// Neither ever wrote, the view stayed dirty so both kept contending,
        /// and hand withdrawals on the current owner were refused the whole
        /// time.
        /// </summary>
        public const float ClaimRetrySeconds = SettleSeconds * 2f;

        /// <summary>
        /// <paramref name="ownerRevisionAge"/> is seconds since this peer
        /// last saw ownership change. <paramref name="jitterSeconds"/> is a
        /// small per-peer offset so two peers do not stay in lockstep; pass
        /// zero for a deterministic decision.
        /// </summary>
        public static ViewFlushAction Decide(
            bool isOwner, float ownerRevisionAge, bool claimAlreadyMade, float jitterSeconds)
        {
            if (isOwner)
                return ownerRevisionAge >= SettleSeconds ? ViewFlushAction.Reconcile : ViewFlushAction.Wait;

            // A peer that has not yet asked for the drawer asks immediately;
            // waiting first would delay every ordinary uncontested case by
            // the retry interval.
            if (!claimAlreadyMade) return ViewFlushAction.Claim;

            return ownerRevisionAge >= ClaimRetrySeconds + jitterSeconds
                ? ViewFlushAction.Claim
                : ViewFlushAction.Wait;
        }
    }
}
