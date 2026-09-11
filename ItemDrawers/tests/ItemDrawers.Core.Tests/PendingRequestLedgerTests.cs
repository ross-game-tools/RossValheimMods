using System.Collections.Generic;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class PendingRequestLedgerTests
    {
        [Fact]
        public void Add_then_TryComplete_returns_the_payload_and_removes_it()
        {
            var ledger = new PendingRequestLedger<string>(timeoutSeconds: 5f, maxAttempts: 3);
            ledger.Add(1, "payload-A", now: 0f);

            Assert.True(ledger.TryComplete(1, out var payload));
            Assert.Equal("payload-A", payload);
            Assert.Equal(0, ledger.Count);
        }

        [Fact]
        public void A_second_TryComplete_for_the_same_id_is_ignored()
        {
            // This is the requester-side half of the anti-duplication
            // guarantee: a reply that arrives twice (or a duplicate/late
            // reply after the first was already banked) must not be
            // banked a second time.
            var ledger = new PendingRequestLedger<int>(5f, 3);
            ledger.Add(1, 100, now: 0f);

            Assert.True(ledger.TryComplete(1, out _));
            Assert.False(ledger.TryComplete(1, out var second));
            Assert.Equal(0, second);
        }

        [Fact]
        public void TryComplete_for_an_id_never_added_is_ignored()
        {
            var ledger = new PendingRequestLedger<int>(5f, 3);
            Assert.False(ledger.TryComplete(999, out _));
        }

        [Fact]
        public void Tick_before_the_deadline_does_nothing()
        {
            var ledger = new PendingRequestLedger<int>(timeoutSeconds: 5f, maxAttempts: 3);
            ledger.Add(1, 100, now: 0f);

            bool retried = false, gaveUp = false;
            ledger.Tick(now: 4.9f, retry: (_, __) => retried = true, giveUp: (_, __) => gaveUp = true);

            Assert.False(retried);
            Assert.False(gaveUp);
            Assert.Equal(1, ledger.Count);
        }

        [Fact]
        public void Tick_past_the_deadline_retries_before_max_attempts_is_reached()
        {
            var ledger = new PendingRequestLedger<int>(timeoutSeconds: 5f, maxAttempts: 3);
            ledger.Add(1, 100, now: 0f);

            int retries = 0;
            ledger.Tick(now: 5f, retry: (id, payload) => { retries++; Assert.Equal(1, id); Assert.Equal(100, payload); },
                giveUp: (_, __) => Assert.Fail("should not give up on the first retry"));

            Assert.Equal(1, retries);
            Assert.Equal(1, ledger.Count);  // still pending, waiting on the retried attempt
        }

        [Fact]
        public void Add_then_timeout_three_times_gives_up_and_removes_the_entry()
        {
            var ledger = new PendingRequestLedger<int>(timeoutSeconds: 5f, maxAttempts: 3);
            ledger.Add(1, 100, now: 0f);

            float now = 0f;
            int retries = 0, giveUps = 0;
            int? givenUpPayload = null;

            // Attempt 1 (from Add) expires at t=5 -> retry (attempt 2).
            now = 5f;
            ledger.Tick(now, (_, __) => retries++, (_, __) => giveUps++);
            // Attempt 2 expires at t=10 -> retry (attempt 3).
            now = 10f;
            ledger.Tick(now, (_, __) => retries++, (_, __) => giveUps++);
            // Attempt 3 (the last allowed) expires at t=15 -> give up.
            now = 15f;
            ledger.Tick(now, (_, __) => retries++, (id, payload) => { giveUps++; givenUpPayload = payload; });

            Assert.Equal(2, retries);
            Assert.Equal(1, giveUps);
            Assert.Equal(100, givenUpPayload);
            Assert.Equal(0, ledger.Count);
        }

        [Fact]
        public void Remove_takes_an_entry_out_unconditionally_for_a_reason_unrelated_to_a_reply()
        {
            // Models a component being destroyed mid-request: the entry
            // must be resolvable (spilled, in the real caller) immediately,
            // not left to wait out its deadline.
            var ledger = new PendingRequestLedger<int>(5f, 3);
            ledger.Add(1, 100, now: 0f);

            Assert.True(ledger.Remove(1, out var payload));
            Assert.Equal(100, payload);
            Assert.Equal(0, ledger.Count);
            Assert.False(ledger.Remove(1, out _));
        }

        [Fact]
        public void All_enumerates_every_still_pending_entry_for_a_last_resort_drain()
        {
            var ledger = new PendingRequestLedger<int>(5f, 3);
            ledger.Add(1, 100, now: 0f);
            ledger.Add(2, 200, now: 0f);
            ledger.TryComplete(1, out _);

            var remaining = new List<int>();
            foreach (var kv in ledger.All()) remaining.Add(kv.Value);

            Assert.Single(remaining);
            Assert.Equal(200, remaining[0]);
        }

        [Fact]
        public void Multiple_independent_ids_do_not_interfere_with_each_others_deadlines()
        {
            var ledger = new PendingRequestLedger<int>(timeoutSeconds: 5f, maxAttempts: 3);
            ledger.Add(1, 1, now: 0f);
            ledger.Add(2, 2, now: 3f);   // added later, deadline is later too

            var retried = new List<long>();
            ledger.Tick(now: 5f, retry: (id, __) => retried.Add(id), giveUp: (_, __) => { });

            Assert.Equal(new List<long> { 1 }, retried);  // id 2's deadline (8f) hasn't arrived yet
        }
    }
}
