namespace ItemDrawers.Core
{
    public readonly struct DrawerOutcome
    {
        public readonly bool Accepted;
        public readonly DrawerSnapshot Result;
        public readonly int MovedToDrawer;
        public readonly int MovedToPlayer;
        public readonly string Rejection;

        private DrawerOutcome(bool accepted, DrawerSnapshot result, int toDrawer, int toPlayer, string rejection)
        {
            Accepted = accepted;
            Result = result;
            MovedToDrawer = toDrawer;
            MovedToPlayer = toPlayer;
            Rejection = rejection;
        }

        public static DrawerOutcome Deposited(DrawerSnapshot result, int moved) =>
            new DrawerOutcome(true, result, moved, 0, null);

        public static DrawerOutcome Withdrew(DrawerSnapshot result, int moved) =>
            new DrawerOutcome(true, result, 0, moved, null);

        public static DrawerOutcome Changed(DrawerSnapshot result) =>
            new DrawerOutcome(true, result, 0, 0, null);

        public static DrawerOutcome Refused(DrawerSnapshot unchanged, string why) =>
            new DrawerOutcome(false, unchanged, 0, 0, why);
    }
}
