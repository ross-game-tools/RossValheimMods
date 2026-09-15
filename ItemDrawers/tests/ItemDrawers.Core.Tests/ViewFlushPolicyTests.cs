using ItemDrawers.Core;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class ViewFlushPolicyTests
    {
        private const float Settle = ViewFlushPolicy.SettleSeconds;
        private const float Retry = ViewFlushPolicy.ClaimRetrySeconds;

        [Fact]
        public void An_owner_whose_ownership_has_settled_writes()
        {
            Assert.Equal(ViewFlushAction.Reconcile,
                ViewFlushPolicy.Decide(isOwner: true, ownerRevisionAge: Settle, claimAlreadyMade: true, jitterSeconds: 0f));
        }

        [Fact]
        public void An_owner_whose_ownership_just_changed_waits()
        {
            Assert.Equal(ViewFlushAction.Wait,
                ViewFlushPolicy.Decide(isOwner: true, ownerRevisionAge: Settle - 0.01f, claimAlreadyMade: true, jitterSeconds: 0f));
        }

        [Fact]
        public void A_peer_that_has_not_asked_yet_claims_immediately()
        {
            // Waiting first would add the retry interval to every ordinary
            // uncontested deposit.
            Assert.Equal(ViewFlushAction.Claim,
                ViewFlushPolicy.Decide(isOwner: false, ownerRevisionAge: 0f, claimAlreadyMade: false, jitterSeconds: 0f));
        }

        [Fact]
        public void A_peer_that_already_asked_waits_out_the_retry_interval()
        {
            Assert.Equal(ViewFlushAction.Wait,
                ViewFlushPolicy.Decide(isOwner: false, ownerRevisionAge: Retry - 0.01f, claimAlreadyMade: true, jitterSeconds: 0f));
            Assert.Equal(ViewFlushAction.Claim,
                ViewFlushPolicy.Decide(isOwner: false, ownerRevisionAge: Retry, claimAlreadyMade: true, jitterSeconds: 0f));
        }

        [Fact]
        public void The_retry_interval_is_strictly_longer_than_the_settle_interval()
        {
            // The property the livelock turned on. If a later edit makes these
            // equal again, the two-peer test below fails too -- this one just
            // names the reason.
            Assert.True(Retry > Settle,
                $"claim retry ({Retry}s) must exceed settle ({Settle}s), or two peers can take the drawer from each other forever");
        }

        [Fact]
        public void A_rival_claiming_at_the_settle_boundary_would_livelock_two_peers()
        {
            // The bug, reproduced against the OLD rule: a rival re-claims at
            // exactly the age the owner needs to settle. Simulated rather
            // than played, because observing it in game needs a second
            // player on a server.
            Assert.False(Simulate(rivalRetrySeconds: Settle, out int reconciles),
                "with retry == settle and the rival's frame landing first, no peer should ever reconcile");
            Assert.Equal(0, reconciles);
        }

        [Fact]
        public void The_shipped_intervals_let_one_peer_reconcile()
        {
            Assert.True(Simulate(rivalRetrySeconds: Retry, out int reconciles),
                "with the shipped intervals a peer must reach Reconcile");
            Assert.True(reconciles > 0);
        }

        /// <summary>
        /// Two peers, both holding an unflushed change, stepped frame by
        /// frame over ten seconds. Ownership is a single shared value; a
        /// claim moves it and resets BOTH peers' observed age, which is what
        /// a replicated OwnerRevision bump does. Returns whether anyone ever
        /// reconciled.
        /// </summary>
        private static bool Simulate(float rivalRetrySeconds, out int reconciles)
        {
            const float Step = 0.05f;
            int owner = 0;                       // peer index currently owning
            var age = new float[2];              // each peer's seconds since it saw ownership change
            var claimed = new bool[2];
            reconciles = 0;

            for (float t = 0f; t < 10f; t += Step)
            {
                // The NON-OWNER is evaluated first, and that ordering is the
                // point rather than a convenience. Two peers run their own
                // frames on their own machines, so every interleaving occurs;
                // the one that matters is the rival's claim landing just
                // before the owner's settle check. Evaluating the owner first
                // hands it every tie and hides the bug -- which is exactly
                // what the first version of this test did.
                int[] order = owner == 0 ? new[] { 1, 0 } : new[] { 0, 1 };
                foreach (int peer in order)
                {
                    bool isOwner = owner == peer;

                    ViewFlushAction action;
                    if (isOwner)
                    {
                        action = age[peer] >= Settle ? ViewFlushAction.Reconcile : ViewFlushAction.Wait;
                    }
                    else if (!claimed[peer])
                    {
                        action = ViewFlushAction.Claim;
                    }
                    else
                    {
                        action = age[peer] >= rivalRetrySeconds ? ViewFlushAction.Claim : ViewFlushAction.Wait;
                    }

                    if (action == ViewFlushAction.Reconcile)
                    {
                        reconciles++;
                        return true;              // one write is all it takes; the view stops being dirty
                    }

                    if (action == ViewFlushAction.Claim)
                    {
                        owner = peer;
                        claimed[peer] = true;
                        age[0] = 0f;              // an ownership change is seen by everyone
                        age[1] = 0f;
                    }
                }

                age[0] += Step;
                age[1] += Step;
            }

            return false;
        }
    }
}
