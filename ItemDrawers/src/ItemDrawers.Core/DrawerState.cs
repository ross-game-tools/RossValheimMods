namespace ItemDrawers.Core
{
    /// <summary>
    /// Every rule governing what a drawer will accept and release. Pure: no
    /// game, no network, no side effects. The caller is responsible for
    /// having claimed ZDO ownership before applying an outcome.
    /// </summary>
    public static class DrawerState
    {
        public const string WrongItem = "This drawer holds a different item";
        public const string DrawerFull = "This drawer is full";
        public const string DrawerEmpty = "This drawer has been drained";
        public const string NotAssigned = "This drawer has not been assigned";
        public const string BadCapacity = "This drawer has an invalid capacity";
        public const string NotEmptyYet = "Empty the drawer before clearing it";
        public const string NothingOffered = "Nothing to deposit";

        public static DrawerOutcome Deposit(DrawerSnapshot current, int capacity, string itemName, int offered)
        {
            if (offered <= 0) return DrawerOutcome.Refused(current, NothingOffered);
            if (string.IsNullOrEmpty(itemName)) return DrawerOutcome.Refused(current, NothingOffered);
            if (capacity <= 0) return DrawerOutcome.Refused(current, BadCapacity);

            if (current.IsAssigned && current.ItemName != itemName)
                return DrawerOutcome.Refused(current, WrongItem);

            int room = capacity - current.Amount;
            if (room <= 0) return DrawerOutcome.Refused(current, DrawerFull);

            int moved = offered < room ? offered : room;
            return DrawerOutcome.Deposited(new DrawerSnapshot(itemName, current.Amount + moved), moved);
        }

        public static DrawerOutcome WithdrawStack(DrawerSnapshot current, int maxStackSize)
        {
            if (!current.IsAssigned) return DrawerOutcome.Refused(current, NotAssigned);
            if (current.IsEmpty) return DrawerOutcome.Refused(current, DrawerEmpty);

            // Treat nonsensical stack sizes as 1 — deliberate fallback for external callers.
            int stack = maxStackSize < 1 ? 1 : maxStackSize;
            int moved = current.Amount < stack ? current.Amount : stack;
            return DrawerOutcome.Withdrew(new DrawerSnapshot(current.ItemName, current.Amount - moved), moved);
        }

        public static DrawerOutcome WithdrawOne(DrawerSnapshot current)
        {
            if (!current.IsAssigned) return DrawerOutcome.Refused(current, NotAssigned);
            if (current.IsEmpty) return DrawerOutcome.Refused(current, DrawerEmpty);

            return DrawerOutcome.Withdrew(new DrawerSnapshot(current.ItemName, current.Amount - 1), 1);
        }

        /// <summary>
        /// Withdrawal on behalf of another mod through the Container bridge.
        /// Silently clamps rather than refusing, because the caller has
        /// already committed to taking what it can get.
        /// </summary>
        public static DrawerOutcome WithdrawExact(DrawerSnapshot current, int requested)
        {
            if (requested <= 0 || !current.IsAssigned || current.IsEmpty)
                return DrawerOutcome.Withdrew(current, 0);

            int moved = requested < current.Amount ? requested : current.Amount;
            return DrawerOutcome.Withdrew(new DrawerSnapshot(current.ItemName, current.Amount - moved), moved);
        }

        public static DrawerOutcome Clear(DrawerSnapshot current)
        {
            if (!current.IsEmpty) return DrawerOutcome.Refused(current, NotEmptyYet);
            return DrawerOutcome.Changed(new DrawerSnapshot("", 0));
        }

        /// <summary>
        /// How much of a deposit request the owner did NOT accept, and
        /// therefore must be handed back to whoever it was removed from --
        /// the RPC-to-owner deposit protocol's entire duplication-vs-loss
        /// boundary lives in this one subtraction. Clamped at zero rather
        /// than allowed to go negative: a caller passing an `accepted`
        /// larger than what it actually removed (e.g. replaying a stale
        /// cached answer against a fresh, smaller removal -- exactly the
        /// hazard HandledRequestCache's docstring describes) must never
        /// compute a "negative shortfall" that a naive `if (shortfall > 0)`
        /// guard downstream would already suppress into silent, unrefunded
        /// item loss; clamping here makes that impossible by construction
        /// rather than relying on every call site to remember the guard.
        /// </summary>
        public static int RefundShortfall(int removed, int accepted)
        {
            int shortfall = removed - accepted;
            return shortfall > 0 ? shortfall : 0;
        }
    }
}
