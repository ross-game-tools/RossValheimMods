using RossQoL.Core.Progression;
using Xunit;

namespace RossQoL.Core.Tests.Progression
{
    public class DungeonRespawnMathTests
    {
        [Fact]
        public void A_dungeon_is_due_once_the_full_span_has_passed()
        {
            Assert.False(DungeonRespawnMath.IsDue(lastVisitDay: 10, today: 33, respawnDays: 24));
            Assert.True(DungeonRespawnMath.IsDue(lastVisitDay: 10, today: 34, respawnDays: 24));
            Assert.True(DungeonRespawnMath.IsDue(lastVisitDay: 10, today: 200, respawnDays: 24));
        }

        [Fact]
        public void A_dungeon_with_no_stamp_is_never_due()
        {
            // The caller stamps it with today instead, so a world cleared
            // before this feature existed does not reset all at once.
            Assert.False(DungeonRespawnMath.IsDue(lastVisitDay: null, today: 500, respawnDays: 24));
        }

        [Fact]
        public void Visiting_today_pushes_it_back()
        {
            Assert.False(DungeonRespawnMath.IsDue(lastVisitDay: 100, today: 100, respawnDays: 24));
        }

        [Fact]
        public void A_span_of_zero_or_less_never_resets()
        {
            Assert.False(DungeonRespawnMath.IsDue(lastVisitDay: 1, today: 999, respawnDays: 0));
            Assert.False(DungeonRespawnMath.IsDue(lastVisitDay: 1, today: 999, respawnDays: -5));
        }

        [Fact]
        public void A_clock_that_moved_backwards_waits()
        {
            Assert.False(DungeonRespawnMath.IsDue(lastVisitDay: 300, today: 10, respawnDays: 24));
        }

        [Fact]
        public void Days_remaining_counts_down_to_zero_and_stops()
        {
            Assert.Equal(24, DungeonRespawnMath.DaysRemaining(lastVisitDay: 10, today: 10, respawnDays: 24));
            Assert.Equal(1, DungeonRespawnMath.DaysRemaining(lastVisitDay: 10, today: 33, respawnDays: 24));
            Assert.Equal(0, DungeonRespawnMath.DaysRemaining(lastVisitDay: 10, today: 34, respawnDays: 24));
            Assert.Equal(0, DungeonRespawnMath.DaysRemaining(lastVisitDay: 10, today: 900, respawnDays: 24));
        }

        [Fact]
        public void Days_remaining_survives_a_missing_stamp_and_a_backwards_clock()
        {
            Assert.Equal(24, DungeonRespawnMath.DaysRemaining(lastVisitDay: null, today: 50, respawnDays: 24));
            Assert.Equal(24, DungeonRespawnMath.DaysRemaining(lastVisitDay: 300, today: 10, respawnDays: 24));
            Assert.Equal(0, DungeonRespawnMath.DaysRemaining(lastVisitDay: 10, today: 20, respawnDays: 0));
        }
    }
}
