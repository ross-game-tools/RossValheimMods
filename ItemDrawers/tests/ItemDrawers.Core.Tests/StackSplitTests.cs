using System.Linq;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class StackSplitTests
    {
        [Fact]
        public void Tops_up_partial_stacks_before_using_empty_slots()
        {
            var p = StackSplit.Plan(new[] { 30, 10 }, emptySlots: 3, incoming: 120, maxStackSize: 50, firstStackToTarget: false);

            Assert.Equal(new[] { 30, 10 }, p.TopUps);
            Assert.Equal(new[] { 50, 30 }, p.NewStacks);
            Assert.Equal(0, p.Remainder);
            Assert.Equal(120, p.Placed);
        }

        [Fact]
        public void Places_full_stacks_then_a_smaller_last_stack()
        {
            var p = StackSplit.Plan(new int[0], 16, 620, 50, false);

            Assert.Equal(Enumerable.Repeat(50, 12).Concat(new[] { 20 }).ToArray(), p.NewStacks);
            Assert.Equal(0, p.Remainder);
        }

        [Fact]
        public void Partial_fit_leaves_the_correct_remainder()
        {
            var p = StackSplit.Plan(new[] { 5 }, 2, 200, 50, false);

            Assert.Equal(new[] { 5 }, p.TopUps);
            Assert.Equal(new[] { 50, 50 }, p.NewStacks);
            Assert.Equal(95, p.Remainder);
            Assert.Equal(105, p.Placed);
        }

        [Fact]
        public void No_room_at_all_leaves_everything()
        {
            var p = StackSplit.Plan(new int[0], 0, 120, 50, false);

            Assert.Empty(p.TopUps);
            Assert.Empty(p.NewStacks);
            Assert.Equal(120, p.Remainder);
        }

        [Fact]
        public void Positional_first_stack_goes_to_the_target_before_top_ups()
        {
            var p = StackSplit.Plan(new[] { 10 }, 1, 55, 50, firstStackToTarget: true);

            Assert.Equal(new[] { 50 }, p.NewStacks);
            Assert.Equal(new[] { 5 }, p.TopUps);
            Assert.Equal(0, p.Remainder);
        }

        [Fact]
        public void Without_a_target_top_ups_come_first()
        {
            var p = StackSplit.Plan(new[] { 10 }, 1, 55, 50, firstStackToTarget: false);

            Assert.Equal(new[] { 10 }, p.TopUps);
            Assert.Equal(new[] { 45 }, p.NewStacks);
            Assert.Equal(0, p.Remainder);
        }

        [Fact]
        public void Positional_with_no_empty_slot_only_tops_up()
        {
            var p = StackSplit.Plan(new[] { 10 }, 0, 60, 50, true);

            Assert.Equal(new[] { 10 }, p.TopUps);
            Assert.Empty(p.NewStacks);
            Assert.Equal(50, p.Remainder);
        }

        [Fact]
        public void Full_partial_stacks_are_skipped()
        {
            var p = StackSplit.Plan(new[] { 0, 20 }, 0, 30, 50, false);

            Assert.Equal(new[] { 0, 20 }, p.TopUps);
            Assert.Equal(10, p.Remainder);
        }

        [Fact]
        public void Non_positive_max_stack_is_treated_as_one()
        {
            var p = StackSplit.Plan(new int[0], 3, 5, 0, false);

            Assert.Equal(new[] { 1, 1, 1 }, p.NewStacks);
            Assert.Equal(2, p.Remainder);
        }

        [Fact]
        public void Placed_plus_remainder_always_equals_incoming_and_no_stack_exceeds_max()
        {
            int[][] partials = { new int[0], new[] { 1 }, new[] { 49, 25 }, new[] { 0, 0, 10 } };
            foreach (var partial in partials)
            foreach (int empty in new[] { 0, 1, 3, 8 })
            foreach (int incoming in new[] { 51, 100, 650, 1000 })
            foreach (bool target in new[] { false, true })
            {
                var p = StackSplit.Plan(partial, empty, incoming, 50, target);
                string at = $"partial=[{string.Join(",", partial)}] empty={empty} incoming={incoming} target={target}";

                Assert.True(p.Placed + p.Remainder == incoming, at);
                Assert.True(p.NewStacks.Length <= empty, at);
                Assert.True(p.NewStacks.All(s => s >= 1 && s <= 50), at);
                for (int i = 0; i < partial.Length; i++)
                    Assert.True(p.TopUps[i] >= 0 && p.TopUps[i] <= partial[i], at);
            }
        }
    }
}
