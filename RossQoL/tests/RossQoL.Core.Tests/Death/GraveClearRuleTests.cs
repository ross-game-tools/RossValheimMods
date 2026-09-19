using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class GraveClearRuleTests
    {
        [Fact]
        public void A_tombstone_standing_at_the_recorded_spot_is_a_sighting()
        {
            Assert.Equal(
                GraveVerdict.Observe,
                GraveClearRule.Decide(inRange: true, tombstoneStanding: true, seenThisSession: false, armed: true));
        }

        [Fact]
        public void A_sighting_is_taken_even_before_the_rule_is_armed()
        {
            // The arming window holds off deletions while a freshly joined
            // world streams in; it must not also throw away what was seen
            // during it, or a grave looted seconds after login is unclearable.
            Assert.Equal(
                GraveVerdict.Observe,
                GraveClearRule.Decide(inRange: true, tombstoneStanding: true, seenThisSession: false, armed: false));
        }

        [Fact]
        public void A_tombstone_that_stood_here_and_is_gone_was_looted()
        {
            Assert.Equal(
                GraveVerdict.Forget,
                GraveClearRule.Decide(inRange: true, tombstoneStanding: false, seenThisSession: true, armed: true));
        }

        [Fact]
        public void Nothing_is_forgotten_out_of_range_however_sure_we_are()
        {
            // The property the whole rule exists for: an unloaded zone has no
            // tombstone in it whether or not the grave is still full.
            Assert.Equal(
                GraveVerdict.Ignore,
                GraveClearRule.Decide(inRange: false, tombstoneStanding: false, seenThisSession: true, armed: true));
        }

        [Fact]
        public void Nothing_is_forgotten_while_the_world_is_still_streaming_in()
        {
            Assert.Equal(
                GraveVerdict.Ignore,
                GraveClearRule.Decide(inRange: true, tombstoneStanding: false, seenThisSession: true, armed: false));
        }

        [Fact]
        public void A_grave_never_seen_standing_this_session_is_kept()
        {
            // The scenario that broke in play testing, from the other side:
            // the grave was looted in an earlier session (or before anything
            // ever saw it), so nothing is there now and nothing ever saw it
            // there. Deleting on that evidence alone would also delete every
            // grave whose objects simply have not spawned yet.
            Assert.Equal(
                GraveVerdict.Ignore,
                GraveClearRule.Decide(inRange: true, tombstoneStanding: false, seenThisSession: false, armed: true));
        }

        [Fact]
        public void A_grave_looted_between_two_ticks_is_kept_until_something_sees_it_standing()
        {
            // Walked as a sequence: the player arrives and loots inside one
            // tick, so no pass ever finds the tombstone standing. The record
            // survives rather than being deleted on a guess -- the cost is a
            // marker the player clears by hand, not lost gear.
            var first = GraveClearRule.Decide(inRange: false, tombstoneStanding: false, seenThisSession: false, armed: true);
            var second = GraveClearRule.Decide(inRange: true, tombstoneStanding: false, seenThisSession: false, armed: true);

            Assert.Equal(GraveVerdict.Ignore, first);
            Assert.Equal(GraveVerdict.Ignore, second);
        }

        [Fact]
        public void A_grave_seen_standing_once_is_forgotten_on_the_pass_after_it_goes()
        {
            var seen = GraveClearRule.Decide(inRange: true, tombstoneStanding: true, seenThisSession: false, armed: true);
            var gone = GraveClearRule.Decide(inRange: true, tombstoneStanding: false, seenThisSession: true, armed: true);

            Assert.Equal(GraveVerdict.Observe, seen);
            Assert.Equal(GraveVerdict.Forget, gone);
        }

        [Fact]
        public void A_tombstone_within_the_match_radius_is_the_same_grave()
        {
            Assert.True(GraveClearRule.Matches(10f, 5f, -3f, 10f, 5f, -3f));
            Assert.True(GraveClearRule.Matches(10f, 5f, -3f, 12f, 3f, -4f));
        }

        [Fact]
        public void A_tombstone_further_off_than_the_match_radius_is_a_different_grave()
        {
            Assert.False(GraveClearRule.Matches(10f, 5f, -3f, 30f, 5f, -3f));
            Assert.False(GraveClearRule.Matches(10f, 5f, -3f, 10f, 25f, -3f));
        }
    }
}
