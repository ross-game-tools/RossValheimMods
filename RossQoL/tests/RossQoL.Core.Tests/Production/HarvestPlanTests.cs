using System.Linq;
using RossQoL.Core.Production;
using Xunit;

namespace RossQoL.Core.Tests.Production
{
    public class HarvestPlanTests
    {
        private static DestinationCandidate C(int index, bool holds, int room, float dist) =>
            new DestinationCandidate(index, holds, room, dist);

        [Fact]
        public void Holding_the_item_beats_distance_and_full_ones_are_dropped()
        {
            var ranked = HarvestPlan.Rank(new[]
            {
                C(0, false, 10, 1f),
                C(1, true, 10, 100f),
                C(2, true, 0, 0f),
                C(3, false, 10, 0.5f),
            });
            Assert.Equal(new[] { 1, 3, 0 }, ranked.Select(r => r.Index).ToArray());
        }

        [Fact]
        public void Ties_keep_index_order()
        {
            var ranked = HarvestPlan.Rank(new[] { C(5, false, 1, 2f), C(4, false, 1, 2f) });
            Assert.Equal(new[] { 4, 5 }, ranked.Select(r => r.Index).ToArray());
        }

        [Fact]
        public void Whole_output_goes_to_the_first_destination_that_fits()
        {
            var plan = HarvestPlan.Plan(4, 1, new[] { C(7, true, 50, 0f), C(8, false, 50, 0f) }, false);
            Assert.Single(plan);
            Assert.Equal(7, plan[0].Index);
            Assert.Equal(4, plan[0].Amount);
        }

        [Fact]
        public void Output_splits_across_destinations_in_order()
        {
            var plan = HarvestPlan.Plan(10, 1, new[] { C(1, true, 3, 0f), C(2, false, 50, 0f) }, false);
            Assert.Equal(new[] { (1, 3), (2, 7) }, plan.Select(p => (p.Index, p.Amount)).ToArray());
        }

        [Fact]
        public void Remainder_that_fits_nowhere_is_left_out()
        {
            var plan = HarvestPlan.Plan(10, 1, new[] { C(1, true, 4, 0f) }, false);
            Assert.Equal(4, plan.Sum(p => p.Amount));
        }

        [Fact]
        public void Placements_are_whole_units()
        {
            var plan = HarvestPlan.Plan(6, 2, new[] { C(1, true, 3, 0f), C(2, false, 3, 0f) }, false);
            Assert.Equal(new[] { (1, 2), (2, 2) }, plan.Select(p => (p.Index, p.Amount)).ToArray());
        }

        [Fact]
        public void Whole_only_batches_move_entirely_or_not_at_all()
        {
            Assert.Empty(HarvestPlan.Plan(6, 1, new[] { C(1, true, 5, 0f) }, true));
            var plan = HarvestPlan.Plan(6, 1, new[] { C(1, true, 4, 0f), C(2, false, 2, 0f) }, true);
            Assert.Equal(6, plan.Sum(p => p.Amount));
        }

        [Fact]
        public void A_batch_as_one_unit_goes_whole_into_one_container()
        {
            // The fermenter plans amount == unitSize with wholeOnly.
            var plan = HarvestPlan.Plan(6, 6, new[] { C(1, true, 5, 0f), C(2, false, 6, 1f) }, true);
            Assert.Equal(new[] { (2, 6) }, plan.Select(p => (p.Index, p.Amount)).ToArray());
        }

        [Fact]
        public void A_batch_as_one_unit_is_never_split_across_containers()
        {
            // 4 + 4 would hold 6 items, but neither holds the whole batch.
            Assert.Empty(HarvestPlan.Plan(6, 6, new[] { C(1, true, 4, 0f), C(2, false, 4, 1f) }, true));
        }

        [Fact]
        public void Nothing_to_move_or_nowhere_to_put_it_is_empty()
        {
            Assert.Empty(HarvestPlan.Plan(0, 1, new[] { C(1, true, 5, 0f) }, false));
            Assert.Empty(HarvestPlan.Plan(3, 1, new DestinationCandidate[0], false));
        }

        [Fact]
        public void Plan_ignores_candidates_with_no_room()
        {
            var plan = HarvestPlan.Plan(5, 1, new[] { C(1, true, 0, 0f), C(2, false, 5, 1f) }, false);
            Assert.Equal(new[] { (2, 5) }, plan.Select(p => (p.Index, p.Amount)).ToArray());

            Assert.Empty(HarvestPlan.Plan(5, 1, new[] { C(1, true, 0, 0f), C(2, false, 0, 1f) }, true));
        }
    }
}
