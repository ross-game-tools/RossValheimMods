using System.Linq;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class ViewReconciliationTests
    {
        private static DrawerSnapshot Wood(int n) => new DrawerSnapshot("Wood", n);

        [Fact]
        public void Deposit_through_the_view_is_credited()
        {
            var r = ViewReconciliation.Apply(Wood(500), capacity: 1000, baseline: 500, viewItemTotal: 520, foreignCount: 0);

            Assert.Equal(Wood(520), r.Result);
            Assert.Equal(20, r.Credited);
            Assert.Equal(0, r.Debited);
            Assert.Equal(0, r.SpillDrawerItem);
            Assert.True(r.AmountChanged);
        }

        [Fact]
        public void Withdrawal_through_the_view_is_debited()
        {
            var r = ViewReconciliation.Apply(Wood(500), 1000, 500, 470, 0);

            Assert.Equal(Wood(470), r.Result);
            Assert.Equal(30, r.Debited);
            Assert.Equal(0, r.Credited);
            Assert.Equal(0, r.Unbacked);
        }

        [Fact]
        public void No_change_when_view_matches_baseline()
        {
            var r = ViewReconciliation.Apply(Wood(500), 1000, 500, 500, 0);

            Assert.Equal(Wood(500), r.Result);
            Assert.False(r.AmountChanged);
            Assert.Equal(0, r.SpillDrawerItem);
            Assert.Equal(0, r.SpillForeign);
            Assert.Equal(0, r.Unbacked);
        }

        [Fact]
        public void Deposit_past_capacity_credits_what_fits_and_spills_the_rest()
        {
            var r = ViewReconciliation.Apply(Wood(990), 1000, 990, 1010, 0);

            Assert.Equal(Wood(1000), r.Result);
            Assert.Equal(10, r.Credited);
            Assert.Equal(10, r.SpillDrawerItem);
        }

        [Fact]
        public void Deposit_into_a_full_drawer_spills_everything()
        {
            var r = ViewReconciliation.Apply(Wood(1000), 1000, 1000, 1005, 0);

            Assert.Equal(Wood(1000), r.Result);
            Assert.Equal(0, r.Credited);
            Assert.Equal(5, r.SpillDrawerItem);
        }

        [Fact]
        public void Foreign_items_are_reported_for_spilling_and_do_not_change_stock()
        {
            var r = ViewReconciliation.Apply(Wood(500), 1000, 500, 500, 3);

            Assert.Equal(Wood(500), r.Result);
            Assert.Equal(3, r.SpillForeign);
            Assert.False(r.AmountChanged);
        }

        [Fact]
        public void Withdrawal_is_measured_against_baseline_not_current_amount()
        {
            // View built at 500; a player then took 100 by hand (Amount 400,
            // not yet rebuilt); a mod took 20 through the view (480).
            var r = ViewReconciliation.Apply(Wood(400), 1000, 500, 480, 0);

            Assert.Equal(Wood(380), r.Result);
            Assert.Equal(20, r.Debited);
        }

        [Fact]
        public void Deposit_is_measured_against_baseline_not_current_amount()
        {
            var r = ViewReconciliation.Apply(Wood(450), 1000, 500, 510, 0);

            Assert.Equal(Wood(460), r.Result);
            Assert.Equal(10, r.Credited);
        }

        [Fact]
        public void Removing_more_than_the_drawer_holds_debits_what_exists_and_reports_the_rest()
        {
            var r = ViewReconciliation.Apply(Wood(10), 1000, 500, 450, 0);

            Assert.Equal(Wood(0), r.Result);
            Assert.Equal(10, r.Debited);
            Assert.Equal(40, r.Unbacked);
        }

        [Fact]
        public void Unassigned_drawer_with_an_empty_view_is_unchanged()
        {
            var empty = new DrawerSnapshot("", 0);
            var r = ViewReconciliation.Apply(empty, 1000, 0, 0, 0);

            Assert.Equal(empty, r.Result);
            Assert.False(r.AmountChanged);
        }

        [Fact]
        public void A_freshly_rebuilt_view_reconciles_to_no_change()
        {
            var after = Wood(520);
            int baseline = ViewLayout.Compute(true, after.Amount, 1000, 50).Sum();

            Assert.Equal(520, baseline);

            var r = ViewReconciliation.Apply(after, 1000, baseline, baseline, 0);
            Assert.Equal(after, r.Result);
            Assert.False(r.AmountChanged);
        }

        [Fact]
        public void Negative_inputs_are_treated_as_zero()
        {
            var r = ViewReconciliation.Apply(Wood(100), 1000, -5, -5, -3);

            Assert.Equal(Wood(100), r.Result);
            Assert.False(r.AmountChanged);
            Assert.Equal(0, r.SpillForeign);
        }
    }
}
