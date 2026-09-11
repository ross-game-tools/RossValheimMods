namespace ItemDrawers.Core
{
    /// <summary>
    /// Generates strictly increasing request ids from a caller-supplied
    /// seed. Exists as its own tiny, pure type specifically so the seeding
    /// choice can be unit-tested in isolation from Unity/networking: a
    /// generator reset to the SAME seed every time (e.g. a counter that
    /// always restarts at 0 or 1 on a fresh instance) reissues ids that
    /// collide with anything still cached under an owner's
    /// HandledRequestCache for the same sender, replaying a stale answer to
    /// a genuinely new request -- see DrawerManager, which seeds its one
    /// generator from a wall-clock value ONCE per client session (not per
    /// drawer, and not per DrawerComponent instance, which can be
    /// destroyed and recreated many times in one session as a drawer moves
    /// in and out of range) so ids stay unique for the life of the session
    /// regardless of how many times any individual drawer's component is
    /// rebuilt.
    /// </summary>
    public sealed class RequestIdGenerator
    {
        private long _next;

        public RequestIdGenerator(long seed)
        {
            _next = seed;
        }

        public long Next() => ++_next;
    }
}
