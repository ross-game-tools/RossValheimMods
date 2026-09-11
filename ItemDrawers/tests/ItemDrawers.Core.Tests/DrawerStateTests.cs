using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class DrawerStateTests
    {
        private static DrawerSnapshot Empty => new DrawerSnapshot("", 0);
        private static DrawerSnapshot Wood(int n) => new DrawerSnapshot("Wood", n);

        // ---------- assignment ----------

        [Fact]
        public void Depositing_into_an_unassigned_drawer_assigns_the_item()
        {
            var r = DrawerState.Deposit(Empty, capacity: 1000, itemName: "Wood", offered: 20);

            Assert.True(r.Accepted);
            Assert.Equal("Wood", r.Result.ItemName);
            Assert.Equal(20, r.Result.Amount);
            Assert.Equal(20, r.MovedToDrawer);
        }

        [Fact]
        public void Depositing_a_different_item_is_refused_with_a_reason()
        {
            var r = DrawerState.Deposit(Wood(50), capacity: 1000, itemName: "Stone", offered: 10);

            Assert.False(r.Accepted);
            Assert.Equal(0, r.MovedToDrawer);
            Assert.Equal(Wood(50), r.Result);          // unchanged
            Assert.False(string.IsNullOrEmpty(r.Rejection));
        }

        // ---------- capacity ----------

        [Fact]
        public void Deposit_clamps_to_capacity_and_reports_what_actually_moved()
        {
            var r = DrawerState.Deposit(Wood(995), capacity: 1000, itemName: "Wood", offered: 20);

            Assert.True(r.Accepted);
            Assert.Equal(1000, r.Result.Amount);
            Assert.Equal(5, r.MovedToDrawer);          // the player keeps the other 15
        }

        [Fact]
        public void Depositing_into_a_full_drawer_moves_nothing_and_is_refused()
        {
            var r = DrawerState.Deposit(Wood(1000), capacity: 1000, itemName: "Wood", offered: 20);

            Assert.False(r.Accepted);
            Assert.Equal(0, r.MovedToDrawer);
            Assert.Equal(1000, r.Result.Amount);
        }

        [Fact]
        public void Deposit_of_zero_or_negative_is_refused()
        {
            Assert.False(DrawerState.Deposit(Wood(10), 1000, "Wood", 0).Accepted);
            Assert.False(DrawerState.Deposit(Wood(10), 1000, "Wood", -5).Accepted);
        }

        [Fact]
        public void Deposit_cannot_overflow_when_capacity_is_enormous()
        {
            var r = DrawerState.Deposit(Wood(int.MaxValue - 10), capacity: int.MaxValue,
                                        itemName: "Wood", offered: 1000);

            Assert.True(r.Result.Amount > 0);          // never wraps negative
            Assert.Equal(int.MaxValue, r.Result.Amount);
            Assert.Equal(10, r.MovedToDrawer);
        }

        [Fact]
        public void Deposit_with_zero_or_negative_capacity_is_refused()
        {
            // Capacity 0
            var r0 = DrawerState.Deposit(Empty, capacity: 0, itemName: "Wood", offered: 10);
            Assert.False(r0.Accepted);
            Assert.Equal(0, r0.MovedToDrawer);
            Assert.Equal(Empty, r0.Result);

            var r0assigned = DrawerState.Deposit(Wood(5), capacity: 0, itemName: "Wood", offered: 10);
            Assert.False(r0assigned.Accepted);
            Assert.Equal(0, r0assigned.MovedToDrawer);
            Assert.Equal(Wood(5), r0assigned.Result);

            // Capacity int.MinValue (guards against wraparound)
            var rmin = DrawerState.Deposit(Wood(1), capacity: int.MinValue, itemName: "Wood", offered: 5);
            Assert.False(rmin.Accepted);
            Assert.Equal(0, rmin.MovedToDrawer);
            Assert.Equal(Wood(1), rmin.Result);
        }

        [Fact]
        public void Deposit_with_null_or_empty_item_name_is_refused()
        {
            // Null item name against unassigned
            var rnull = DrawerState.Deposit(Empty, capacity: 1000, itemName: null, offered: 10);
            Assert.False(rnull.Accepted);
            Assert.Equal(0, rnull.MovedToDrawer);
            Assert.Equal(Empty, rnull.Result);

            // Empty string item name against unassigned
            var rempty = DrawerState.Deposit(Empty, capacity: 1000, itemName: "", offered: 10);
            Assert.False(rempty.Accepted);
            Assert.Equal(0, rempty.MovedToDrawer);
            Assert.Equal(Empty, rempty.Result);

            // Null against assigned drawer
            var rnullassigned = DrawerState.Deposit(Wood(50), capacity: 1000, itemName: null, offered: 10);
            Assert.False(rnullassigned.Accepted);
            Assert.Equal(0, rnullassigned.MovedToDrawer);
            Assert.Equal(Wood(50), rnullassigned.Result);

            // Empty string against assigned drawer
            var remptyassigned = DrawerState.Deposit(Wood(50), capacity: 1000, itemName: "", offered: 10);
            Assert.False(remptyassigned.Accepted);
            Assert.Equal(0, remptyassigned.MovedToDrawer);
            Assert.Equal(Wood(50), remptyassigned.Result);
        }

        // ---------- withdraw ----------

        [Fact]
        public void Withdraw_stack_takes_a_full_stack_when_there_is_one()
        {
            var r = DrawerState.WithdrawStack(Wood(120), maxStackSize: 50);

            Assert.True(r.Accepted);
            Assert.Equal(50, r.MovedToPlayer);
            Assert.Equal(70, r.Result.Amount);
        }

        [Fact]
        public void Withdraw_stack_takes_the_remainder_when_less_than_a_stack()
        {
            var r = DrawerState.WithdrawStack(Wood(7), maxStackSize: 50);

            Assert.True(r.Accepted);
            Assert.Equal(7, r.MovedToPlayer);
            Assert.Equal(0, r.Result.Amount);
        }

        [Fact]
        public void A_drained_drawer_keeps_its_item_type_and_its_place_in_the_wall()
        {
            var r = DrawerState.WithdrawStack(Wood(7), maxStackSize: 50);

            Assert.Equal("Wood", r.Result.ItemName);
            Assert.True(r.Result.IsAssigned);
            Assert.True(r.Result.IsEmpty);
        }

        [Fact]
        public void Withdrawing_from_an_empty_drawer_is_refused_and_changes_nothing()
        {
            var r = DrawerState.WithdrawStack(Wood(0), maxStackSize: 50);

            Assert.False(r.Accepted);
            Assert.Equal(0, r.MovedToPlayer);
            Assert.Equal("Wood", r.Result.ItemName);
        }

        [Fact]
        public void Withdraw_stack_with_zero_or_negative_max_stack_size_falls_back_to_one()
        {
            // maxStackSize 0 should be treated as 1
            var r0 = DrawerState.WithdrawStack(Wood(50), maxStackSize: 0);
            Assert.True(r0.Accepted);
            Assert.Equal(1, r0.MovedToPlayer);
            Assert.Equal(49, r0.Result.Amount);

            // maxStackSize -1 should be treated as 1
            var rneg = DrawerState.WithdrawStack(Wood(50), maxStackSize: -1);
            Assert.True(rneg.Accepted);
            Assert.Equal(1, rneg.MovedToPlayer);
            Assert.Equal(49, rneg.Result.Amount);
        }

        [Fact]
        public void Withdraw_one_takes_exactly_one()
        {
            var r = DrawerState.WithdrawOne(Wood(3));

            Assert.True(r.Accepted);
            Assert.Equal(1, r.MovedToPlayer);
            Assert.Equal(2, r.Result.Amount);
        }

        [Fact]
        public void Withdraw_one_from_an_unassigned_drawer_is_refused()
        {
            Assert.False(DrawerState.WithdrawOne(Empty).Accepted);
        }

        // ---------- clear ----------

        [Fact]
        public void Clearing_an_empty_drawer_unassigns_it()
        {
            var r = DrawerState.Clear(Wood(0));

            Assert.True(r.Accepted);
            Assert.Equal("", r.Result.ItemName);
            Assert.False(r.Result.IsAssigned);
        }

        [Fact]
        public void Clearing_a_drawer_that_still_holds_items_is_refused()
        {
            // Guards against wiping a full drawer with a misclick.
            var r = DrawerState.Clear(Wood(1));

            Assert.False(r.Accepted);
            Assert.Equal("Wood", r.Result.ItemName);
            Assert.Equal(1, r.Result.Amount);
        }

        // ---------- external withdrawal (the Container bridge path) ----------

        [Fact]
        public void External_withdrawal_on_unassigned_drawer_moves_nothing_silently()
        {
            var r = DrawerState.WithdrawExact(Empty, requested: 10);

            Assert.True(r.Accepted);  // Silently accepts (doesn't refuse)
            Assert.Equal(0, r.MovedToPlayer);
            Assert.Equal("", r.Result.ItemName);
        }

        [Fact]
        public void External_withdrawal_taking_exactly_the_amount_present_leaves_empty_but_assigned()
        {
            var r = DrawerState.WithdrawExact(Wood(30), requested: 30);

            Assert.True(r.Accepted);
            Assert.Equal(30, r.MovedToPlayer);
            Assert.Equal(0, r.Result.Amount);
            Assert.Equal("Wood", r.Result.ItemName);  // Still assigned even though empty
            Assert.True(r.Result.IsAssigned);
            Assert.True(r.Result.IsEmpty);
        }

        [Fact]
        public void External_withdrawal_cannot_take_more_than_is_present()
        {
            var r = DrawerState.WithdrawExact(Wood(40), requested: 100);

            Assert.True(r.Accepted);  // Silently clamps, never refuses
            Assert.Equal(40, r.MovedToPlayer);
            Assert.Equal(0, r.Result.Amount);
        }

        [Fact]
        public void External_withdrawal_of_a_negative_amount_moves_nothing()
        {
            var r = DrawerState.WithdrawExact(Wood(40), requested: -3);

            Assert.True(r.Accepted);  // Silently accepts even with negative request
            Assert.Equal(0, r.MovedToPlayer);
            Assert.Equal(40, r.Result.Amount);
        }

        // ---------- RPC-to-owner grant arithmetic ----------
        // These exercise WithdrawExact/Deposit exactly the way the ZDO owner
        // uses them: applied sequentially, once per incoming request, always
        // against the CURRENT snapshot (never the requester's stale one).
        // Two clients racing the same drawer is modelled here as two calls
        // in a row, because that is precisely what the owner does with two
        // requests that arrive microseconds apart on its single thread --
        // there is no interleaving possible mid-grant, so "concurrent" from
        // the requesters' point of view is strictly sequential here.

        [Fact]
        public void Two_withdraw_requests_racing_the_same_drawer_never_over_grant()
        {
            // Two clients each see 100 and each ask for 80 -- the classic
            // duplication scenario from the design doc. The owner processes
            // them one at a time against its own live state, so the second
            // request is clamped to whatever is left, never to what the
            // second requester merely believed was present.
            var afterFirst = DrawerState.WithdrawExact(Wood(100), requested: 80);
            Assert.Equal(80, afterFirst.MovedToPlayer);
            Assert.Equal(20, afterFirst.Result.Amount);

            var afterSecond = DrawerState.WithdrawExact(afterFirst.Result, requested: 80);
            Assert.Equal(20, afterSecond.MovedToPlayer);  // clamped, not 80
            Assert.Equal(0, afterSecond.Result.Amount);

            Assert.Equal(100, afterFirst.MovedToPlayer + afterSecond.MovedToPlayer);
        }

        [Fact]
        public void Withdraw_request_against_a_drawer_that_emptied_underneath_it_grants_nothing()
        {
            // Requester read 100 and asked for 60, but by the time the
            // owner processes the request a prior grant has already
            // drained the drawer to zero.
            var drained = DrawerState.WithdrawExact(Wood(100), requested: 100).Result;
            Assert.True(drained.IsEmpty);

            var r = DrawerState.WithdrawExact(drained, requested: 60);
            Assert.Equal(0, r.MovedToPlayer);
            Assert.Equal(0, r.Result.Amount);
        }

        [Fact]
        public void Withdraw_request_under_the_true_amount_grants_exactly_what_was_asked()
        {
            var r = DrawerState.WithdrawExact(Wood(500), requested: 30);
            Assert.Equal(30, r.MovedToPlayer);
            Assert.Equal(470, r.Result.Amount);
        }

        [Fact]
        public void Two_deposit_requests_racing_the_same_drawer_never_exceed_capacity()
        {
            // Two clients each removed 700 from their own inventory (having
            // read 400/1000 free) and ask the owner to credit it. The owner
            // grants only what capacity allows for each request in turn; the
            // requester is responsible for refunding whatever the owner did
            // not accept (see DrawerComponent's RPC handlers).
            var current = Wood(400);
            var first = DrawerState.Deposit(current, capacity: 1000, itemName: "Wood", offered: 700);
            Assert.True(first.Accepted);
            Assert.Equal(600, first.MovedToDrawer);   // clamped to the room that existed
            Assert.Equal(1000, first.Result.Amount);

            var second = DrawerState.Deposit(first.Result, capacity: 1000, itemName: "Wood", offered: 700);
            Assert.False(second.Accepted);   // refused outright: no room left at all
            Assert.Equal(DrawerState.DrawerFull, second.Rejection);
            Assert.Equal(0, second.MovedToDrawer);
            Assert.Equal(1000, second.Result.Amount);

            // Nothing above 1000 total the drawer ever reports having accepted.
            Assert.Equal(600, first.MovedToDrawer + second.MovedToDrawer);
        }

        [Fact]
        public void Deposit_request_exceeding_capacity_reports_the_true_shortfall_for_a_refund()
        {
            var r = DrawerState.Deposit(Wood(950), capacity: 1000, itemName: "Wood", offered: 100);
            Assert.True(r.Accepted);
            Assert.Equal(50, r.MovedToDrawer);
            // The requester already removed 100 from the player; the
            // difference (100 - 50 == 50) is what the RPC reply tells it to
            // refund rather than silently swallow.
        }

        [Fact]
        public void Deposit_request_against_a_drawer_reassigned_to_a_different_item_is_refused()
        {
            // Between the requester reading the drawer and the owner
            // processing the request, another client's grant reassigned it
            // to a different item. The owner must refuse rather than mix
            // item types, so the requester knows to refund everything.
            var r = DrawerState.Deposit(new DrawerSnapshot("Stone", 10), capacity: 1000, itemName: "Wood", offered: 50);
            Assert.False(r.Accepted);
            Assert.Equal(0, r.MovedToDrawer);
            Assert.Equal(DrawerState.WrongItem, r.Rejection);
        }

        // ---------- refund arithmetic (the requester's half of a deposit grant) ----------

        [Fact]
        public void RefundShortfall_is_zero_when_everything_removed_was_accepted()
        {
            Assert.Equal(0, DrawerState.RefundShortfall(removed: 100, accepted: 100));
        }

        [Fact]
        public void RefundShortfall_is_the_difference_when_the_owner_accepted_less()
        {
            Assert.Equal(30, DrawerState.RefundShortfall(removed: 100, accepted: 70));
        }

        [Fact]
        public void RefundShortfall_is_everything_removed_when_the_owner_accepted_nothing()
        {
            Assert.Equal(100, DrawerState.RefundShortfall(removed: 100, accepted: 0));
        }

        [Fact]
        public void RefundShortfall_never_goes_negative_even_if_accepted_exceeds_removed()
        {
            // Should not happen in a correct protocol, but a stale replayed
            // answer (see HandledRequestCacheTests) could in principle
            // report "accepted" larger than what this specific caller
            // actually removed. Clamping to zero here means a downstream
            // `if (shortfall > 0)` guard is redundant defence, not the only
            // thing standing between this and a negative refund silently
            // being treated as "nothing to do" while items are still gone.
            Assert.Equal(0, DrawerState.RefundShortfall(removed: 50, accepted: 80));
        }
    }
}
