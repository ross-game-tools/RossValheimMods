using System;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    /// <summary>
    /// Scheduled since Ruling 23 in progress.md and never built until now:
    /// a randomised conservation harness proving
    /// <c>player.Δ + drawer.Δ + ground.Δ == 0</c> holds for
    /// <see cref="DrawerState"/>'s withdraw/deposit/refund arithmetic --
    /// the exact functions the RPC-to-owner protocol's owner-side handlers
    /// and requester-side refund logic are built on -- across long
    /// randomised operation sequences, including a PARTIALLY-FAILING FAKE
    /// standing in for a real inventory that sometimes can't remove or
    /// accept everything asked of it.
    ///
    /// This proves the CORE arithmetic conserves items under adversarial
    /// random load. It cannot and does not reach the Game-layer defects
    /// found in review (stale request ids, ownership theft, a ledger tied
    /// to a MonoBehaviour's lifetime) -- those live entirely outside
    /// ItemDrawers.Core, in code this project deliberately keeps free of
    /// engine references. What it does prove is that the delta/clamp/
    /// grant-before-give arithmetic those Game-layer handlers delegate to
    /// is, and remains, sound.
    /// </summary>
    public class ConservationHarnessTests
    {
        private const string Item = "Wood";
        private const int Capacity = 1000;

        /// <summary>
        /// Stands in for Inventory.RemoveItem: removes up to `requested`
        /// from the player, but a fraction of the time only partially
        /// succeeds (simulating another mod, or a race, having already
        /// taken some of the stack) -- never removes more than the player
        /// actually has, and never returns a negative amount.
        /// </summary>
        private static int FakeRemove(ref int player, int requested, Random rng)
        {
            if (requested <= 0) return 0;
            int available = Math.Min(requested, player);
            if (available <= 0) return 0;

            bool partialFailure = rng.NextDouble() < 0.2;
            int actual = partialFailure ? rng.Next(0, available + 1) : available;

            player -= actual;
            return actual;
        }

        /// <summary>
        /// Stands in for ItemFacts.GiveToPlayer: hands `amount` to the
        /// player, but a fraction of the time the player's inventory can't
        /// take all of it (simulating a full inventory), spilling the
        /// remainder to the ground instead. Every unit of `amount` lands
        /// SOMEWHERE -- this fake cannot destroy or invent items, which is
        /// exactly the property GiveToPlayer's own chunk-and-spill loop is
        /// designed to guarantee.
        /// </summary>
        private static void FakeGive(ref int player, ref int ground, int amount, Random rng)
        {
            if (amount <= 0) return;

            bool partialFailure = rng.NextDouble() < 0.2;
            int toPlayer = partialFailure ? rng.Next(0, amount + 1) : amount;
            int toGround = amount - toPlayer;

            player += toPlayer;
            ground += toGround;
        }

        private static void AssertConserved(int total, int drawer, int player, int ground, string where)
        {
            Assert.True(total == drawer + player + ground,
                $"Conservation violated at {where}: total={total} but drawer={drawer} + player={player} + ground={ground} = {drawer + player + ground}");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        public void Randomised_withdraw_and_deposit_sequences_always_conserve_the_total(int seed)
        {
            var rng = new Random(seed);

            int drawer = rng.Next(0, Capacity + 1);
            int player = rng.Next(0, 2000);
            int ground = 0;
            int total = drawer + player + ground;

            var snapshot = new DrawerSnapshot(Item, drawer);

            for (int step = 0; step < 200; step++)
            {
                bool doWithdraw = rng.Next(2) == 0;

                if (doWithdraw)
                {
                    // The "requester" asks for an amount that may be more
                    // or less than the drawer truly holds -- a stale local
                    // read is exactly what the owner must clamp against
                    // its own live state regardless of.
                    int requested = rng.Next(0, Capacity + 50);
                    var outcome = DrawerState.WithdrawExact(snapshot, requested);
                    snapshot = outcome.Result;
                    FakeGive(ref player, ref ground, outcome.MovedToPlayer, rng);
                }
                else
                {
                    int offered = rng.Next(0, 500);
                    int removed = FakeRemove(ref player, offered, rng);

                    var outcome = DrawerState.Deposit(snapshot, Capacity, Item, removed);
                    int accepted = outcome.Accepted ? outcome.MovedToDrawer : 0;
                    if (accepted > 0) snapshot = outcome.Result;

                    int shortfall = DrawerState.RefundShortfall(removed, accepted);
                    FakeGive(ref player, ref ground, shortfall, rng);
                }

                AssertConserved(total, snapshot.Amount, player, ground, $"seed={seed} step={step}");
            }

            AssertConserved(total, snapshot.Amount, player, ground, $"seed={seed} final");
        }

        /// <summary>
        /// The specific scenario the design doc and Ruling 22/24 exist to
        /// close: two requesters' worth of withdrawals applied back to back
        /// against the SAME starting snapshot (see DrawerStateTests'
        /// Two_withdraw_requests_racing_the_same_drawer_never_over_grant
        /// for the narrower, non-randomised version) -- run here through
        /// the same conservation assertion as the general harness, with
        /// both grants routed through the fallible FakeGive.
        /// </summary>
        [Fact]
        public void Two_racing_withdrawals_against_one_starting_amount_still_conserve()
        {
            var rng = new Random(42);
            int drawer = 100;
            int player = 0;
            int ground = 0;
            int total = drawer + player + ground;

            var snapshot = new DrawerSnapshot(Item, drawer);

            var first = DrawerState.WithdrawExact(snapshot, requested: 80);
            snapshot = first.Result;
            FakeGive(ref player, ref ground, first.MovedToPlayer, rng);

            var second = DrawerState.WithdrawExact(snapshot, requested: 80);
            snapshot = second.Result;
            FakeGive(ref player, ref ground, second.MovedToPlayer, rng);

            AssertConserved(total, snapshot.Amount, player, ground, "two racing withdrawals");
            Assert.Equal(100, first.MovedToPlayer + second.MovedToPlayer);  // nothing over-granted
        }
    }

    /// <summary>
    /// The first ConservationHarnessTests class models the protocol as
    /// synchronous and always-replying -- exactly the shape
    /// <see cref="DrawerState"/> itself has, and exactly why it could not
    /// have caught any of the four original Criticals or the three found
    /// in the following review round: every one of those lived in what
    /// happens BETWEEN a request being sent and a reply (or its absence)
    /// being handled, which a synchronous model has no room to represent.
    ///
    /// This class models that gap using <see cref="PendingRequestLedger{T}"/>
    /// (already pure, from ItemDrawers.Core, driven by an injected clock)
    /// as the actual requester-side state machine, with an explicit
    /// <c>inFlight</c> bucket added to the conservation equation: while a
    /// request is outstanding, the amount it concerns genuinely exists
    /// nowhere else (removed from the player, not yet credited anywhere),
    /// which mirrors the real protocol's ordering exactly. The invariant
    /// asserted throughout is
    /// <c>drawer + player + ground + inFlight == total</c> at every step,
    /// not just at the end.
    /// </summary>
    public class ProtocolConservationHarnessTests
    {
        private const string Item = "Wood";
        private const int Capacity = 1000;

        private static void AssertConserved(int total, int drawer, int player, int ground, int inFlight, string where)
        {
            int sum = drawer + player + ground + inFlight;
            Assert.True(total == sum,
                $"Conservation violated at {where}: total={total} but drawer={drawer} + player={player} + ground={ground} + inFlight={inFlight} = {sum}");
        }

        /// <summary>
        /// The single highest-value case review asked for: a deposit whose
        /// request is sent, but whose reply NEVER ARRIVES at all (the
        /// owner disconnected before receiving it, or before replying --
        /// either way, drop the reply entirely rather than modelling any
        /// particular cause). The player's items were already removed
        /// before the request was sent (see DrawerComponent.RequestDeposit's
        /// real ordering, mirrored here). After every retry the ledger
        /// allows is exhausted, the give-up path spills the amount that
        /// was in flight -- this is the ONE place in the whole protocol
        /// where a lost reply is recoverable rather than a pure loss (see
        /// the withdrawal case below for the asymmetric one that is NOT
        /// recoverable, by design, and is documented as an accepted
        /// residual rather than "fixed").
        /// </summary>
        [Fact]
        public void Deposit_with_no_reply_ever_arriving_conserves_via_the_give_up_spill()
        {
            var ledger = new PendingRequestLedger<int>(timeoutSeconds: 5f, maxAttempts: 3);

            int drawer = 400, player = 500, ground = 0, inFlight = 0;
            int total = drawer + player + ground + inFlight;

            int removed = 100;
            player -= removed;
            inFlight += removed;
            ledger.Add(id: 1, removed, now: 0f);
            AssertConserved(total, drawer, player, ground, inFlight, "after removal, request sent");

            // The owner never replies -- not even once. Every retry the
            // ledger issues goes unanswered (the real DrawerComponent
            // would keep re-sending to the SAME pinned peer; here we model
            // only the requester's own bookkeeping, which does not care
            // why no reply came back).
            float now = 0f;
            int giveUps = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                now += 5f;
                ledger.Tick(now,
                    retry: (_, __) => { /* resend -- no reply modelled, nothing changes here */ },
                    giveUp: (_, spilled) =>
                    {
                        giveUps++;
                        ground += spilled;
                        inFlight -= spilled;
                    });
                AssertConserved(total, drawer, player, ground, inFlight, $"after tick {attempt}");
            }

            Assert.Equal(1, giveUps);
            Assert.Equal(0, ledger.Count);
            Assert.Equal(0, inFlight);
            Assert.Equal(100, ground);   // the removed amount landed on the ground, not nowhere
            AssertConserved(total, drawer, player, ground, inFlight, "final");
        }

        /// <summary>
        /// The asymmetric case that is NOT recoverable, by design: a
        /// withdrawal whose owner-side debit genuinely applied (the drawer
        /// really did lose the amount) but whose grant reply never
        /// reaches the requester. Nothing was ever given to the player
        /// before a grant arrives (RequestWithdraw's whole point), so the
        /// requester's own give-up costs the PLAYER nothing -- but it also
        /// has no way to un-debit a drawer it isn't the owner of and has
        /// no channel back to. This is documented in this task's report as
        /// an accepted residual, not a defect: closing it would require
        /// either giving up the two-phase grant-before-give design (which
        /// re-opens the duplication this whole protocol exists to close)
        /// or a stronger consensus protocol than Valheim's networking
        /// provides. This test exists to make that residual concrete and
        /// keep it honest rather than implicit.
        /// </summary>
        [Fact]
        public void Withdraw_with_no_reply_ever_arriving_permanently_loses_the_debited_amount()
        {
            var ledger = new PendingRequestLedger<int>(timeoutSeconds: 5f, maxAttempts: 3);

            int drawer = 100, player = 0, ground = 0, inFlight = 0;
            int total = drawer + player + ground + inFlight;

            // The owner processes the request and genuinely debits its
            // ZDO -- exactly what RPC_RequestWithdraw does BEFORE sending
            // a reply. Modelled directly here since this test's subject is
            // what happens to that already-applied debit when the reply
            // that was meant to carry it to the player never arrives.
            var outcome = DrawerState.WithdrawExact(new DrawerSnapshot(Item, drawer), requested: 30);
            drawer = outcome.Result.Amount;
            inFlight += outcome.MovedToPlayer;   // "in flight" toward a player who will never receive it
            ledger.Add(id: 1, outcome.MovedToPlayer, now: 0f);

            float now = 0f;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                now += 5f;
                ledger.Tick(now, retry: (_, __) => { }, giveUp: (_, __) =>
                {
                    // Unlike the deposit case, there is nothing to spill:
                    // this requester never had the items and has no way to
                    // hand anything back to a drawer it does not own.
                });
            }

            Assert.Equal(0, ledger.Count);
            // Conservation is VIOLATED here -- deliberately demonstrated,
            // not asserted as correct. total no longer equals drawer +
            // player + ground once inFlight (still 30, forever unresolved)
            // is excluded from the accounting a real player or server
            // would ever observe.
            int observable = drawer + player + ground;
            Assert.NotEqual(total, observable);
            Assert.Equal(total - 30, observable);   // exactly the debited-but-never-granted amount
        }

        /// <summary>
        /// A duplicate/late grant reply for a deposit -- e.g. the owner's
        /// first reply was slow, so a value the requester already banked
        /// arrives a second time. TryComplete's single-completion
        /// guarantee (see PendingRequestLedgerTests) means only the FIRST
        /// reply is ever acted on; the second is inert. Conservation must
        /// hold regardless of how many times a reply for an already-
        /// completed id shows up.
        /// </summary>
        [Fact]
        public void Duplicate_deposit_grant_reply_is_banked_only_once_and_conserves()
        {
            var ledger = new PendingRequestLedger<(string ItemName, int Removed)>(5f, 3);

            int drawer = 400, player = 500, ground = 0, inFlight = 0;
            int total = drawer + player + ground + inFlight;

            int removed = 200;
            player -= removed;
            inFlight += removed;
            ledger.Add(id: 1, (Item, removed), now: 0f);

            // Owner applies the deposit once and (conceptually) sends two
            // replies carrying the same answer -- the duplicate is exactly
            // what a retry racing a merely-slow original reply looks like
            // from the requester's side.
            var outcome = DrawerState.Deposit(new DrawerSnapshot(Item, drawer), Capacity, Item, removed);
            int accepted = outcome.Accepted ? outcome.MovedToDrawer : 0;
            drawer = outcome.Accepted ? outcome.Result.Amount : drawer;

            void BankReply()
            {
                if (!ledger.TryComplete(1, out var pending)) return;   // second call: already completed, no-op
                inFlight -= pending.Removed;
                int shortfall = DrawerState.RefundShortfall(pending.Removed, accepted);
                player += shortfall;
                ground += 0;   // player is always reachable in this scenario -- no null-player spill case here
            }

            BankReply();
            AssertConserved(total, drawer, player, ground, inFlight, "after first (real) reply");

            BankReply();   // the duplicate
            AssertConserved(total, drawer, player, ground, inFlight, "after duplicate reply -- must be a no-op");
        }

        /// <summary>
        /// WriteOwned's compare-and-swap refuses (see its docstring for
        /// the split-ownership window this actually guards, and the one
        /// it does not) -- modelled here simply as the owner reporting
        /// accepted = 0 despite DrawerState.Deposit having computed a
        /// nonzero outcome, exactly what RPC_RequestDeposit does when
        /// <c>WriteOwned</c> returns false. The full removed amount must
        /// come back to the player, and the drawer must be untouched.
        /// </summary>
        [Fact]
        public void Deposit_refused_by_a_losing_compare_and_swap_refunds_in_full_and_conserves()
        {
            var ledger = new PendingRequestLedger<(string ItemName, int Removed)>(5f, 3);

            int drawer = 400, player = 500, ground = 0, inFlight = 0;
            int total = drawer + player + ground + inFlight;

            int removed = 150;
            player -= removed;
            inFlight += removed;
            ledger.Add(id: 1, (Item, removed), now: 0f);

            // WriteOwned lost the race -- accepted is 0 regardless of what
            // DrawerState.Deposit alone would have computed, and the
            // drawer is genuinely untouched (no partial write happened).
            const int accepted = 0;

            Assert.True(ledger.TryComplete(1, out var pending));
            inFlight -= pending.Removed;
            int shortfall = DrawerState.RefundShortfall(pending.Removed, accepted);
            player += shortfall;

            Assert.Equal(removed, shortfall);   // refunded in full
            Assert.Equal(400, drawer);          // untouched
            AssertConserved(total, drawer, player, ground, inFlight, "after a fully-refused deposit");
        }

        /// <summary>
        /// This is C3 made concrete inside the conservation invariant
        /// itself, not just at the cache layer (see
        /// HandledRequestCacheTests for that version). It deliberately
        /// constructs the bug a per-component request-id counter that
        /// resets on every rebuild would cause -- two DIFFERENT, unrelated
        /// deposits from the SAME sender happening to share request id 1
        /// -- and shows the conservation invariant breaks when the
        /// second, later deposit's reply is answered by replaying the
        /// FIRST deposit's cached result instead of being processed fresh.
        /// This is exactly why DrawerManager seeds ONE RequestIdGenerator
        /// per session rather than letting ids restart per component (see
        /// RequestIdGeneratorTests) -- with that fix in place, two
        /// different logical deposits can never share an id in the first
        /// place, so this specific failure is unreachable, not merely
        /// tolerated.
        /// </summary>
        [Fact]
        public void A_stale_replayed_answer_for_a_colliding_request_id_breaks_conservation()
        {
            var cache = new HandledRequestCache<int>(lifetimeSeconds: 60f);

            int drawer = 0, player = 1000, ground = 0;
            int total = drawer + player + ground;

            // First deposit: request id 1, sender 7, removes and accepts 50.
            int firstRemoved = 50;
            player -= firstRemoved;
            var firstOutcome = DrawerState.Deposit(new DrawerSnapshot(Item, drawer), Capacity, Item, firstRemoved);
            int firstAccepted = firstOutcome.Accepted ? firstOutcome.MovedToDrawer : 0;
            drawer = firstOutcome.Accepted ? firstOutcome.Result.Amount : drawer;
            cache.Record(sender: 7, id: 1, result: firstAccepted, now: 0f);
            int firstShortfall = DrawerState.RefundShortfall(firstRemoved, firstAccepted);
            player += firstShortfall;
            Assert.Equal(total, drawer + player + ground);   // conserved so far -- the bug hasn't fired yet

            // Second, UNRELATED deposit: same sender, request id ALSO 1
            // (the collision -- see RequestIdGeneratorTests for how a
            // broken per-component counter produces exactly this), a
            // completely different amount removed.
            int secondRemoved = 300;
            player -= secondRemoved;

            // The owner-side idempotency cache sees (sender=7, id=1)
            // already recorded and REPLAYS the first deposit's answer
            // (accepted=50) instead of ever running DrawerState.Deposit
            // against the second request's real removed=300 -- exactly
            // what HandledRequestCache is documented to do given a
            // collision it cannot detect.
            Assert.True(cache.TryGet(sender: 7, id: 1, out int replayedAccepted));
            int secondShortfall = DrawerState.RefundShortfall(secondRemoved, replayedAccepted);
            player += secondShortfall;
            // The drawer is NEVER credited for the second deposit's real
            // 300 -- the replay short-circuited before DrawerState.Deposit
            // ever ran for it.

            int observable = drawer + player + ground;
            Assert.NotEqual(total, observable);
            // The stale replay tells the requester "50 was accepted", so
            // it refunds only removed-50=250 back to the player and keeps
            // the other 50 as (believed) deposited -- but the drawer was
            // NEVER actually credited for this second, unrelated request
            // (the replay short-circuited before DrawerState.Deposit ever
            // ran for it). That 50 exists nowhere: not in the drawer, not
            // with the player, not on the ground.
            Assert.Equal(total - 50, observable);
        }
    }
}
