using System;
using System.Linq;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class ViewLayoutTests
    {
        private static int[] Repeat(int value, int count) => Enumerable.Repeat(value, count).ToArray();
        private static int[] Concat(params int[][] parts) => parts.SelectMany(p => p).ToArray();

        [Fact]
        public void Unassigned_drawer_has_no_slots()
        {
            Assert.Empty(ViewLayout.Compute(assigned: false, amount: 500, capacity: 1000, maxStackSize: 50));
        }

        // An assigned drawer with no stock must still expose one empty slot,
        // so a mod depositing through the Container/Inventory API (e.g. a
        // sap extractor's auto-harvest) finds somewhere to place the FIRST
        // item. With no slot at all -- Array.Empty, a 0x0 inventory -- vanilla
        // AddItem/CanAddItem/FindEmptySlot refuse, which is why an empty
        // drawer would not accept sap while one already holding some (a real
        // stack to merge into) did. Room equals the max stack size, matching
        // the room a one-item drawer exposes (M - 1); the reconcile clamps to
        // capacity and folds the deposit in, republishing the full layout.
        [Fact]
        public void Empty_assigned_drawer_exposes_one_empty_slot()
        {
            var slots = ViewLayout.Compute(assigned: true, amount: 0, capacity: 1000, maxStackSize: 50);
            Assert.Equal(new[] { 0 }, slots);
            Assert.Equal(50, ViewLayout.Room(slots, 50));
        }

        // ---------- the spec's table, M = 50, C = 1000 ----------

        [Fact]
        public void Three_items_give_three_single_item_slots()
        {
            Assert.Equal(new[] { 1, 1, 1 }, ViewLayout.Compute(true, 3, 1000, 50));
        }

        [Fact]
        public void Five_hundred_of_a_thousand_keeps_seven_single_item_slots()
        {
            var slots = ViewLayout.Compute(true, 500, 1000, 50);
            Assert.Equal(Concat(new[] { 493 }, Repeat(1, 7)), slots);
            Assert.Equal(343, ViewLayout.Room(slots, 50));
        }

        [Fact]
        public void Nine_hundred_of_a_thousand_raises_the_single_item_slots_evenly()
        {
            var slots = ViewLayout.Compute(true, 900, 1000, 50);
            Assert.Equal(new[] { 650, 36, 36, 36, 36, 36, 35, 35 }, slots);
            Assert.Equal(100, ViewLayout.Room(slots, 50));
        }

        [Fact]
        public void Full_drawer_offers_no_room()
        {
            var slots = ViewLayout.Compute(true, 1000, 1000, 50);
            Assert.Equal(Concat(new[] { 650 }, Repeat(50, 7)), slots);
            Assert.Equal(0, ViewLayout.Room(slots, 50));
        }

        // ---------- other shapes ----------

        [Fact]
        public void Fewer_items_than_slots_gives_one_slot_per_item()
        {
            Assert.Equal(Repeat(1, 5), ViewLayout.Compute(true, 5, 1000, 50));
        }

        [Fact]
        public void Eight_items_fill_all_eight_slots_with_one_each()
        {
            Assert.Equal(Repeat(1, 8), ViewLayout.Compute(true, 8, 1000, 50));
        }

        [Fact]
        public void Room_is_capped_at_remaining_capacity()
        {
            var slots = ViewLayout.Compute(true, 400, 410, 50);
            Assert.Equal(new[] { 60, 49, 49, 49, 49, 48, 48, 48 }, slots);
            Assert.Equal(10, ViewLayout.Room(slots, 50));
        }

        [Fact]
        public void Tiny_max_stack_full_drawer()
        {
            Assert.Equal(Concat(new[] { 986 }, Repeat(2, 7)), ViewLayout.Compute(true, 1000, 1000, 2));
        }

        [Fact]
        public void Tiny_max_stack_one_short_of_full()
        {
            var slots = ViewLayout.Compute(true, 999, 1000, 2);
            Assert.Equal(new[] { 986, 2, 2, 2, 2, 2, 2, 1 }, slots);
            Assert.Equal(1, ViewLayout.Room(slots, 2));
        }

        [Fact]
        public void Tiny_max_stack_few_items()
        {
            Assert.Equal(Repeat(1, 5), ViewLayout.Compute(true, 5, 1000, 2));
        }

        [Fact]
        public void Huge_max_stack_uses_fewer_slots_rather_than_offer_room_past_capacity()
        {
            // Coins: M = 999. Any second slot would expose slot 1's own free
            // space (it is below M), so the only layout within C - A is one slot.
            var slots = ViewLayout.Compute(true, 500, 1000, 999);
            Assert.Equal(new[] { 500 }, slots);
            Assert.True(ViewLayout.Room(slots, 999) <= 500);
        }

        [Fact]
        public void Capacity_below_stock_offers_no_room()
        {
            var slots = ViewLayout.Compute(true, 1200, 1000, 50);
            Assert.Equal(Concat(new[] { 850 }, Repeat(50, 7)), slots);
            Assert.Equal(0, ViewLayout.Room(slots, 50));
        }

        [Fact]
        public void Uses_fewer_slots_when_all_eight_would_offer_too_much_room()
        {
            var slots = ViewLayout.Compute(true, 370, 390, 50);
            Assert.Equal(new[] { 90, 47, 47, 47, 47, 46, 46 }, slots);
            Assert.Equal(20, ViewLayout.Room(slots, 50));
        }

        [Fact]
        public void Capacity_below_one_stack_falls_back_to_a_single_slot()
        {
            Assert.Equal(new[] { 10 }, ViewLayout.Compute(true, 10, 30, 50));
        }

        [Fact]
        public void Non_positive_max_stack_is_treated_as_one()
        {
            var slots = ViewLayout.Compute(true, 20, 1000, 0);
            Assert.Equal(Concat(new[] { 13 }, Repeat(1, 7)), slots);
            Assert.Equal(0, ViewLayout.Room(slots, 0));
        }

        [Fact]
        public void Room_counts_every_slot_below_max_stack_including_slot_one()
        {
            Assert.Equal(147, ViewLayout.Room(new[] { 1, 1, 1 }, 50));
        }

        [Fact]
        public void Invariants_hold_across_a_grid_of_inputs()
        {
            int[] amounts = { 1, 2, 7, 8, 9, 50, 51, 349, 350, 351, 499, 500, 901, 999, 1000, 1200 };
            int[] capacities = { 30, 50, 400, 1000, 10000 };
            int[] maxStacks = { 1, 2, 20, 50, 100, 999 };

            foreach (int a in amounts)
            foreach (int c in capacities)
            foreach (int m in maxStacks)
            {
                string at = $"A={a} C={c} M={m}";
                var slots = ViewLayout.Compute(true, a, c, m);

                Assert.True(slots.Length >= 1 && slots.Length <= Math.Min(8, a), $"{at}: slot count {slots.Length}");
                Assert.True(slots.Sum() == a, $"{at}: sum {slots.Sum()}");
                Assert.True(slots.All(s => s >= 1), $"{at}: a slot below 1");

                for (int i = 1; i < slots.Length; i++)
                {
                    Assert.True(slots[i] <= m, $"{at}: slot {i + 1} = {slots[i]} above M");
                    if (i > 1)
                    {
                        Assert.True(slots[i] <= slots[i - 1], $"{at}: single-item slots not non-increasing");
                        Assert.True(slots[1] - slots[i] <= 1, $"{at}: single-item slots not even");
                    }
                }

                if (c >= m)
                    Assert.True(ViewLayout.Room(slots, m) <= Math.Max(0, c - a),
                        $"{at}: room {ViewLayout.Room(slots, m)} exceeds {Math.Max(0, c - a)}");
            }
        }
    }
}
