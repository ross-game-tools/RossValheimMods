using RossQoL.Core.Production;
using Xunit;

namespace RossQoL.Core.Tests.Production
{
    public class HarvestMathTests
    {
        [Fact]
        public void Room_counts_partial_stacks_and_empty_slots()
        {
            Assert.Equal(107, HarvestMath.Room(7, 2, 50));
        }

        [Fact]
        public void Room_is_zero_with_no_space()
        {
            Assert.Equal(0, HarvestMath.Room(0, 0, 50));
        }

        [Fact]
        public void Room_treats_bad_inputs_as_none()
        {
            Assert.Equal(0, HarvestMath.Room(-5, -1, 0));
            Assert.Equal(3, HarvestMath.Room(0, 3, 0));
        }

        [Fact]
        public void Room_saturates_instead_of_overflowing()
        {
            Assert.Equal(int.MaxValue, HarvestMath.Room(int.MaxValue, int.MaxValue, 1000));
        }

        [Fact]
        public void Units_taken_round_up()
        {
            Assert.Equal(2, HarvestMath.UnitsTaken(3, 2));
            Assert.Equal(2, HarvestMath.UnitsTaken(4, 2));
            Assert.Equal(1, HarvestMath.UnitsTaken(1, 1));
            Assert.Equal(1, HarvestMath.UnitsTaken(1, 0));
        }

        [Fact]
        public void Nothing_placed_takes_and_owes_nothing()
        {
            Assert.Equal(0, HarvestMath.UnitsTaken(0, 3));
            Assert.Equal(0, HarvestMath.Shortfall(0, 3));
        }

        [Fact]
        public void Shortfall_is_what_rounding_up_owes()
        {
            Assert.Equal(1, HarvestMath.Shortfall(3, 2));
            Assert.Equal(0, HarvestMath.Shortfall(4, 2));
            Assert.Equal(2, HarvestMath.Shortfall(4, 6));
        }

        [Fact]
        public void First_attempt_is_due()
        {
            Assert.True(HarvestMath.IsDue(null, 5, 10));
        }

        [Fact]
        public void Due_only_once_the_interval_has_passed()
        {
            Assert.False(HarvestMath.IsDue(0, 9.9, 10));
            Assert.True(HarvestMath.IsDue(0, 10, 10));
        }

        [Fact]
        public void Units_taken_are_not_capped_at_the_producer_callers_clamp()
        {
            // 3 levels × 2 = 6 items at most; 9 placed would be 5 units.
            // UnitsTaken does not know the level count; Harvester clamps
            // with Math.Min(levels, ...).
            Assert.Equal(5, HarvestMath.UnitsTaken(9, 2));
            Assert.Equal(3, System.Math.Min(3, HarvestMath.UnitsTaken(9, 2)));
        }

        [Fact]
        public void Time_running_backwards_is_due()
        {
            Assert.True(HarvestMath.IsDue(500, 3, 10));
            Assert.True(HarvestMath.IsDue(10, 9.999, 10));
        }

        [Fact]
        public void Negative_interval_is_always_due()
        {
            Assert.True(HarvestMath.IsDue(5, 5, -1));
        }
    }
}
