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

        // THE EMPTY-SLOT CASE. Inventory.RemoveItem removes the ItemData
        // from the slot when the removal takes the whole stack, rather than
        // leaving it at m_stack 0 -- so DrawerComponent.OnMirrorChanged
        // reports an emptied mirror as ("", 0), never as (item, 0). See its
        // `slot != null ? ... : ""`.
        //
        // Every test below feeds "" the way the real caller does. The
        // pre-existing drain test passes the item name with a zero count,
        // which is a shape the caller cannot produce -- it asserted the
        // drain path worked while never exercising it, which is exactly why
        // this bug shipped: crafting an amount equal to the drawer's entire
        // contents took nothing from the drawer and still produced the item.
        [Fact]
        public void An_emptied_slot_is_a_full_withdrawal_not_a_different_item()
        {
            var r = MirrorReconciliation.Compute("", 0, Coal, 100, Coal, 100);
            Assert.Equal(MirrorDelta.WithdrawOf(100), r);
        }

        [Fact]
        public void An_emptied_slot_still_clamps_to_what_the_ZDO_holds()
        {
            var r = MirrorReconciliation.Compute("", 0, Coal, 100, Coal, 65);
            Assert.Equal(MirrorDelta.WithdrawOf(65), r);
        }

        [Fact]
        public void A_null_item_name_is_treated_the_same_as_an_empty_one()
        {
            // PrefabNameOf can return null, which the caller coalesces to ""
            // -- but a future caller might not, and the two mean the same
            // thing here.
            var r = MirrorReconciliation.Compute(null, 0, Coal, 100, Coal, 100);
            Assert.Equal(MirrorDelta.WithdrawOf(100), r);
        }

        [Fact]
        public void An_emptied_slot_against_an_already_empty_baseline_is_no_delta()
        {
            // Nothing was in the mirror to begin with, so nothing was taken.
            // Guards against the empty-slot handling inventing a withdrawal
            // out of a drawer that was already drained.
            var r = MirrorReconciliation.Compute("", 0, "", 0, Coal, 100);
            Assert.Equal(MirrorDelta.None, r);
        }

        [Fact]
        public void An_emptied_slot_does_not_withdraw_when_the_ZDO_holds_a_different_item()
        {
            // The drawer was reassigned since the baseline was taken. The
            // baseline is not comparable to ZDO truth, so debiting it would
            // remove the wrong item.
            var r = MirrorReconciliation.Compute("", 0, Coal, 100, "Iron", 100);
            Assert.Equal(MirrorDelta.None, r);
        }

        [Fact]
        public void A_populated_slot_holding_a_different_item_is_still_no_delta()
        {
            // The empty-slot allowance must not soften the genuine
            // item-mismatch guard: a slot holding Iron where the baseline
            // says Coal is a mismatch, not a withdrawal of Coal.
            var r = MirrorReconciliation.Compute("Iron", 40, Coal, 100, Coal, 100);
            Assert.Equal(MirrorDelta.None, r);
        }

        [Fact]
        public void Over_removal_relative_to_the_ZDO_clamps_to_what_the_ZDO_holds()
        {
            // Baseline says 100 was mirrored, the mirror now reports a
            // partial amount left, but the ZDO -- ground truth -- only has
            // 65 (something else already reduced it). Cannot withdraw more
            // than the ZDO actually holds.
            //
            // Previously this passed (Coal, 0): an item name with a zero
            // count, which the caller cannot produce, since an emptied slot
            // reports ("", 0). The emptied case now has its own tests above.
            var r = MirrorReconciliation.Compute(Coal, 10, Coal, 100, Coal, 65);
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
