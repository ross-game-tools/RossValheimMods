using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class ViewLayoutTests
    {
        [Fact]
        public void Unassigned_drawer_has_no_slots()
        {
            Assert.Empty(ViewLayout.Compute(assigned: false, amount: 500));
        }

        // An assigned drawer with no stock must still expose one empty slot,
        // so a mod depositing through the Container/Inventory API (e.g. a
        // sap extractor's auto-harvest) finds somewhere to place the FIRST
        // item; a 0x0 inventory makes vanilla AddItem refuse it.
        [Fact]
        public void Empty_assigned_drawer_exposes_one_empty_slot()
        {
            Assert.Equal(new[] { 0 }, ViewLayout.Compute(assigned: true, amount: 0));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(58)]
        [InlineData(1000)]
        [InlineData(10000)]
        public void Stock_is_one_stack_of_the_whole_amount(int amount)
        {
            Assert.Equal(new[] { amount }, ViewLayout.Compute(assigned: true, amount: amount));
        }

        [Fact]
        public void Negative_amount_shows_an_empty_slot()
        {
            Assert.Equal(new[] { 0 }, ViewLayout.Compute(assigned: true, amount: -3));
        }

        // Vanilla AddItem merges into a stack while m_stack < m_maxStackSize,
        // so the room it finds is StackLimit - amount: exactly what is left.
        [Theory]
        [InlineData(1000, 0, 1000)]
        [InlineData(1000, 7, 993)]
        [InlineData(2000, 1999, 1)]
        [InlineData(1000, 1000, 0)]
        public void Room_is_the_remaining_capacity(int capacity, int amount, int room)
        {
            Assert.Equal(room, ViewLayout.StackLimit(capacity, amount) - amount);
        }

        // Capacity lowered in config below what a drawer already holds: no
        // room, and never a limit below the stack it describes.
        [Fact]
        public void Over_capacity_drawer_has_no_room()
        {
            Assert.Equal(1500, ViewLayout.StackLimit(capacity: 1000, amount: 1500));
        }

        [Fact]
        public void Limit_is_at_least_one()
        {
            Assert.Equal(1, ViewLayout.StackLimit(capacity: 0, amount: 0));
        }
    }
}
