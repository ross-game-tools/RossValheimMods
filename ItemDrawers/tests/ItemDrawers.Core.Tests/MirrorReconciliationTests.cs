using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class MirrorReconciliationTests
    {
        private const string Coal = "Coal";

        // ---------- straightforward cases ----------

        [Fact]
        public void No_change_reports_no_delta()
        {
            var r = MirrorReconciliation.Compute(Coal, 100, Coal, 100, Coal, 100);
            Assert.Equal(MirrorDelta.None, r);
        }

        [Fact]
        public void A_plain_removal_reports_a_withdrawal_of_the_amount_removed()
        {
            // Mirror built at 100, external mod removed 30 -> mirror now 70.
            var r = MirrorReconciliation.Compute(Coal, 70, Coal, 100, Coal, 100);
            Assert.Equal(MirrorDelta.WithdrawOf(30), r);
        }

        // ---------- C1: stale baseline ----------
        // The bug this guards against: diffing against the live ZDO amount
        // instead of the mirror's own last-known baseline inverts the sign
        // whenever the ZDO moved for an unrelated reason since the mirror
        // was last reconciled.

        [Fact]
        public void Stale_baseline_removal_is_still_a_withdrawal_not_a_deposit()
        {
            // Mirror was built when the ZDO held 100. Since then a player
            // interaction (unrelated to the mirror) dropped the ZDO to 40.
            // Now an external mod removes 50 from the (still-100) mirror.
            var r = MirrorReconciliation.Compute(Coal, 50, Coal, 100, Coal, 40);

            // Correct: a 50-unit withdrawal, clamped to what the ZDO
            // actually holds (40). The buggy formula (mirrorTotal - zdoAmount
            // = 50-40 = +10) would report a 10-unit DEPOSIT-shaped delta
            // instead -- creating items.
            Assert.Equal(MirrorDelta.WithdrawOf(40), r);
        }

        // ---------- over-removal / partial removal ----------

        [Fact]
        public void Over_removal_relative_to_the_ZDO_clamps_to_what_the_ZDO_holds()
        {
            // Baseline says 100 was mirrored, mirror now reports 0 removed
            // (all of it), but the ZDO -- ground truth -- only has 65 left
            // (something else already reduced it). Cannot withdraw more
            // than the ZDO actually holds.
            var r = MirrorReconciliation.Compute(Coal, 0, Coal, 100, Coal, 65);
            Assert.Equal(MirrorDelta.WithdrawOf(65), r);
        }

        [Fact]
        public void Partial_removal_reports_exactly_the_amount_removed()
        {
            var r = MirrorReconciliation.Compute(Coal, 91, Coal, 100, Coal, 100);
            Assert.Equal(MirrorDelta.WithdrawOf(9), r);
        }

        // ---------- I4: name mismatch must not be treated as "no change" ----------

        [Fact]
        public void Mismatched_mirror_item_reports_no_delta_even_with_equal_counts()
        {
            // Same numeric total, but the mirror's actual item no longer
            // matches what the baseline was recorded for -- e.g. the drawer
            // was reassigned. A naive count-only comparison would call this
            // "no change"; it must not apply a numeric delta across a
            // change of item identity.
            var r = MirrorReconciliation.Compute("Iron", 100, Coal, 100, Coal, 100);
            Assert.Equal(MirrorDelta.None, r);
        }

        [Fact]
        public void Baseline_item_disagreeing_with_ZDO_item_reports_no_delta()
        {
            var r = MirrorReconciliation.Compute(Coal, 70, Coal, 100, "Iron", 100);
            Assert.Equal(MirrorDelta.None, r);
        }
    }
}
