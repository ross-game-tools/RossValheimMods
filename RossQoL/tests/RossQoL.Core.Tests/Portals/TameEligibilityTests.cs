using RossQoL.Core.Portals;
using System.Collections.Generic;
using Xunit;

namespace RossQoL.Core.Tests.Portals
{
    public class TameEligibilityTests
    {
        private static readonly Vec3 Player = Vec3.Zero;
        private const float Radius = 20f;

        private static TameCandidate Eligible(float distance) =>
            new TameCandidate(new Vec3(distance, 0f, 0f), isTamed: true, isFollowingPlayer: true, isBusy: false);

        [Fact]
        public void A_tamed_follower_in_range_qualifies()
        {
            Assert.True(TameEligibility.Qualifies(Eligible(5f), Player, Radius));
        }

        [Fact]
        public void A_wild_creature_never_qualifies()
        {
            var wild = new TameCandidate(new Vec3(5f, 0f, 0f), isTamed: false, isFollowingPlayer: true, isBusy: false);
            Assert.False(TameEligibility.Qualifies(wild, Player, Radius));
        }

        [Fact]
        public void A_tame_that_is_not_following_me_never_qualifies()
        {
            // Covers both "following nobody" and "following another player":
            // the Game layer resolves the follow target and passes the answer
            // as a bool, so there is exactly one case here.
            var idle = new TameCandidate(new Vec3(5f, 0f, 0f), isTamed: true, isFollowingPlayer: false, isBusy: false);
            Assert.False(TameEligibility.Qualifies(idle, Player, Radius));
        }

        [Fact]
        public void A_busy_tame_never_qualifies()
        {
            // isBusy is ridden or otherwise attached. A half-attached creature
            // is exactly the state worth not moving.
            var ridden = new TameCandidate(new Vec3(5f, 0f, 0f), isTamed: true, isFollowingPlayer: true, isBusy: true);
            Assert.False(TameEligibility.Qualifies(ridden, Player, Radius));
        }

        [Fact]
        public void A_tame_beyond_the_radius_does_not_qualify()
        {
            Assert.False(TameEligibility.Qualifies(Eligible(Radius + 1f), Player, Radius));
        }

        [Fact]
        public void The_radius_is_inclusive_at_its_exact_edge()
        {
            // Pinned deliberately: a tame standing exactly at the configured
            // distance should come, so that setting the radius to 20 means
            // "within 20 metres" rather than "within 19.99".
            Assert.True(TameEligibility.Qualifies(Eligible(Radius), Player, Radius));
        }

        [Fact]
        public void Distance_is_measured_in_three_dimensions()
        {
            // A tame directly above or below -- on a roof, or under a floor --
            // is as far away as one across the ground, and a 2D test would
            // wrongly include something 30m up a cliff.
            var high = new TameCandidate(new Vec3(0f, Radius + 1f, 0f), isTamed: true, isFollowingPlayer: true, isBusy: false);
            Assert.False(TameEligibility.Qualifies(high, Player, Radius));
        }

        [Fact]
        public void SelectIndices_returns_the_positions_of_qualifying_candidates()
        {
            var candidates = new List<TameCandidate>
            {
                Eligible(1f),                                                                           // 0 yes
                new TameCandidate(new Vec3(2f, 0f, 0f), true, false, false),                            // 1 no
                Eligible(3f),                                                                           // 2 yes
                new TameCandidate(new Vec3(100f, 0f, 0f), true, true, false),                           // 3 no
            };

            Assert.Equal(new[] { 0, 2 }, TameEligibility.SelectIndices(candidates, Player, Radius));
        }

        [Fact]
        public void SelectIndices_returns_empty_rather_than_null_when_nothing_qualifies()
        {
            var candidates = new List<TameCandidate> { new TameCandidate(Vec3.Zero, false, false, false) };
            Assert.Empty(TameEligibility.SelectIndices(candidates, Player, Radius));
        }

        [Fact]
        public void A_summon_qualifies_even_though_it_does_not_report_itself_as_tamed()
        {
            // The bug this rule exists for: a raised skeleton follows its
            // summoner, but vanilla can drop the write that records it as
            // tamed, so IsTamed reads false forever. Requiring it excluded
            // every summon from portals. See TameCandidate.IsSummon.
            var summon = new TameCandidate(
                new Vec3(5f, 0f, 0f), isTamed: false, isFollowingPlayer: true, isBusy: false, isSummon: true);
            Assert.True(TameEligibility.Qualifies(summon, Player, Radius));
        }

        [Fact]
        public void A_summon_that_is_not_following_me_still_does_not_qualify()
        {
            // The exemption is from the tamed test only. A summon someone else
            // raised, or one told to stay, is not coming along.
            var summon = new TameCandidate(
                new Vec3(5f, 0f, 0f), isTamed: false, isFollowingPlayer: false, isBusy: false, isSummon: true);
            Assert.False(TameEligibility.Qualifies(summon, Player, Radius));
        }

        [Fact]
        public void A_summon_obeys_the_same_radius_and_busy_rules_as_any_tame()
        {
            // Summons deliberately share TamesFollow's radius rather than
            // getting settings of their own.
            var far = new TameCandidate(
                new Vec3(Radius + 1f, 0f, 0f), isTamed: false, isFollowingPlayer: true, isBusy: false, isSummon: true);
            Assert.False(TameEligibility.Qualifies(far, Player, Radius));

            var busy = new TameCandidate(
                new Vec3(5f, 0f, 0f), isTamed: false, isFollowingPlayer: true, isBusy: true, isSummon: true);
            Assert.False(TameEligibility.Qualifies(busy, Player, Radius));
        }

        [Fact]
        public void A_creature_that_is_neither_tamed_nor_a_summon_never_qualifies()
        {
            // Pins that the summon exemption did not quietly drop the tamed
            // test for everything else.
            var wild = new TameCandidate(
                new Vec3(5f, 0f, 0f), isTamed: false, isFollowingPlayer: true, isBusy: false, isSummon: false);
            Assert.False(TameEligibility.Qualifies(wild, Player, Radius));
        }

        [Fact]
        public void A_zero_or_negative_radius_selects_nothing()
        {
            // Guards a config edit of 0 or -1 from being read as "no limit".
            Assert.False(TameEligibility.Qualifies(Eligible(0f), Player, 0f));
            Assert.False(TameEligibility.Qualifies(Eligible(0f), Player, -5f));
        }
    }
}
