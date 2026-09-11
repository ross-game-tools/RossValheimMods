using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class HandledRequestCacheTests
    {
        [Fact]
        public void TryGet_before_any_Record_returns_false()
        {
            var cache = new HandledRequestCache<int>(lifetimeSeconds: 60f);
            Assert.False(cache.TryGet(sender: 1, id: 1, out _));
        }

        [Fact]
        public void Record_then_TryGet_within_lifetime_replays_the_recorded_result()
        {
            var cache = new HandledRequestCache<int>(60f);
            cache.Record(sender: 1, id: 1, result: 42, now: 0f);

            Assert.True(cache.TryGet(1, 1, out var result));
            Assert.Equal(42, result);
        }

        [Fact]
        public void Different_senders_with_the_same_request_id_are_independent()
        {
            var cache = new HandledRequestCache<int>(60f);
            cache.Record(sender: 1, id: 1, result: 10, now: 0f);
            cache.Record(sender: 2, id: 1, result: 20, now: 0f);

            Assert.True(cache.TryGet(1, 1, out var a));
            Assert.Equal(10, a);
            Assert.True(cache.TryGet(2, 1, out var b));
            Assert.Equal(20, b);
        }

        [Fact]
        public void Prune_evicts_entries_older_than_the_configured_lifetime()
        {
            var cache = new HandledRequestCache<int>(lifetimeSeconds: 60f);
            cache.Record(sender: 1, id: 1, result: 42, now: 0f);

            cache.Prune(now: 59f);
            Assert.True(cache.TryGet(1, 1, out _));   // not yet old enough

            cache.Prune(now: 61f);
            Assert.False(cache.TryGet(1, 1, out _));  // evicted
        }

        [Fact]
        public void Prune_leaves_fresher_entries_alone_while_evicting_stale_ones()
        {
            var cache = new HandledRequestCache<int>(60f);
            cache.Record(sender: 1, id: 1, result: 1, now: 0f);
            cache.Record(sender: 1, id: 2, result: 2, now: 50f);

            cache.Prune(now: 61f);

            Assert.False(cache.TryGet(1, 1, out _));  // recorded at t=0, now 61s old
            Assert.True(cache.TryGet(1, 2, out var v));  // recorded at t=50, only 11s old
            Assert.Equal(2, v);
        }

        [Fact]
        public void Re_recording_the_same_key_replaces_the_stored_result_and_its_age()
        {
            var cache = new HandledRequestCache<int>(60f);
            cache.Record(sender: 1, id: 1, result: 1, now: 0f);
            cache.Record(sender: 1, id: 1, result: 2, now: 30f);

            Assert.True(cache.TryGet(1, 1, out var v));
            Assert.Equal(2, v);

            // Its age is measured from the SECOND recording, not the first.
            cache.Prune(now: 61f);   // 31s old relative to the second Record -- not stale yet
            Assert.True(cache.TryGet(1, 1, out _));
        }

        /// <summary>
        /// This is C3 in one test: a request id is not, by itself, a safe
        /// key -- it is only safe paired with a sender AND made unique
        /// upstream by the caller (see RequestIdGeneratorTests). If a
        /// generator ever reissues an id a sender has already used (the
        /// exact bug fixed by seeding DrawerManager's one generator from a
        /// wall-clock value instead of letting a per-component counter
        /// restart at 0 on every rebuild), this cache has NO way to tell
        /// that "request 1" the second time is a brand new, unrelated
        /// request rather than a retry of the first one -- it will
        /// confidently replay the FIRST request's answer to the SECOND,
        /// entirely different, request. Fixing that collision is
        /// RequestIdGenerator's job, not this cache's -- this test exists
        /// to make that boundary explicit rather than leave it implicit.
        /// </summary>
        [Fact]
        public void A_reused_id_from_the_same_sender_is_indistinguishable_from_a_genuine_retry()
        {
            var cache = new HandledRequestCache<int>(60f);

            // "Request 1": withdraw request, sender 7, granted 80.
            cache.Record(sender: 7, id: 1, result: 80, now: 0f);

            // Some time later, id generation collided (the bug this cache
            // cannot detect) and a completely unrelated "request 1" -- a
            // deposit, say, that should have been granted 5 -- arrives
            // from the same sender before the first entry is pruned.
            Assert.True(cache.TryGet(sender: 7, id: 1, out var replayed));
            Assert.Equal(80, replayed);  // wrong for the second request -- and this cache has no way to know that
        }
    }
}
