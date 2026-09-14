namespace ItemDrawers.Core
{
    /// <summary>What the owner does with one observed change to a drawer's container view.</summary>
    public readonly struct ViewReconcileOutcome
    {
        /// <summary>The drawer's state after applying the change.</summary>
        public readonly DrawerSnapshot Result;

        /// <summary>Items added through the view that the drawer accepted.</summary>
        public readonly int Credited;

        /// <summary>Items removed through the view that the drawer gave up.</summary>
        public readonly int Debited;

        /// <summary>Items of the drawer's own item added through the view that it could not accept; spill them at the drawer.</summary>
        public readonly int SpillDrawerItem;

        /// <summary>Count of items in the view that are not the drawer's item; spill them at the drawer.</summary>
        public readonly int SpillForeign;

        /// <summary>
        /// Items removed through the view beyond what the drawer still held.
        /// Only possible when another peer changed the drawer at the same
        /// time; nothing can be recovered, so the caller only logs it.
        /// </summary>
        public readonly int Unbacked;

        public ViewReconcileOutcome(DrawerSnapshot result, int credited, int debited, int spillDrawerItem, int spillForeign, int unbacked)
        {
            Result = result;
            Credited = credited;
            Debited = debited;
            SpillDrawerItem = spillDrawerItem;
            SpillForeign = spillForeign;
            Unbacked = unbacked;
        }

        public bool AmountChanged => Credited > 0 || Debited > 0;
    }

    /// <summary>
    /// Pure arithmetic for folding a change made through a drawer's
    /// container view into its real stock. The change is always measured
    /// as view total minus baseline -- the total the view held when the
    /// owner last rebuilt it -- never against the drawer's current Amount,
    /// which may have moved for unrelated reasons. All stock rules come
    /// from DrawerState.
    /// </summary>
    public static class ViewReconciliation
    {
        public static ViewReconcileOutcome Apply(DrawerSnapshot current, int capacity, int baseline, int viewItemTotal, int foreignCount)
        {
            int foreign = foreignCount < 0 ? 0 : foreignCount;
            long delta = (long)(viewItemTotal < 0 ? 0 : viewItemTotal) - (baseline < 0 ? 0 : baseline);

            if (delta > 0)
            {
                int added = delta > int.MaxValue ? int.MaxValue : (int)delta;
                var deposit = DrawerState.Deposit(current, capacity, current.ItemName, added);
                int credited = deposit.Accepted ? deposit.MovedToDrawer : 0;
                return new ViewReconcileOutcome(
                    deposit.Accepted ? deposit.Result : current,
                    credited, 0, DrawerState.RefundShortfall(added, credited), foreign, 0);
            }

            if (delta < 0)
            {
                int removed = -delta > int.MaxValue ? int.MaxValue : (int)-delta;
                var withdrawal = DrawerState.WithdrawExact(current, removed);
                int debited = withdrawal.MovedToPlayer;
                return new ViewReconcileOutcome(withdrawal.Result, 0, debited, 0, foreign, removed - debited);
            }

            return new ViewReconcileOutcome(current, 0, 0, 0, foreign, 0);
        }
    }
}
