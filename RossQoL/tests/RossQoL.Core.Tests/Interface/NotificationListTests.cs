using System.Linq;
using RossQoL.Core.Interface;
using Xunit;

namespace RossQoL.Core.Tests.Interface
{
    public class NotificationListTests
    {
        private static NotificationList NewList() => new NotificationList();

        [Fact]
        public void A_first_arrival_becomes_a_line()
        {
            var list = NewList();

            list.Add("Wood", "Wood", NotificationStyle.Count, 12f, now: 0f);

            Assert.Equal(new[] { "Wood x12" }, list.Entries.Select(e => e.Text));
        }

        [Fact]
        public void A_single_item_says_no_count_at_all()
        {
            var list = NewList();

            list.Add("Flint", "Flint", NotificationStyle.Count, 1f, now: 0f);

            Assert.Equal("Flint", list.Entries[0].Text);
        }

        [Fact]
        public void A_repeat_grows_its_own_line_rather_than_adding_one()
        {
            var list = NewList();
            list.Add("Wood", "Wood", NotificationStyle.Count, 12f, now: 0f);

            list.Add("Wood", "Wood", NotificationStyle.Count, 8f, now: 0.5f);

            Assert.Equal(1, list.Count);
            Assert.Equal("Wood x20", list.Entries[0].Text);
        }

        [Fact]
        public void A_message_in_between_does_not_break_the_merge()
        {
            // The vanilla bug this list exists to fix: vanilla compares the
            // next message only against the one currently displayed, so
            // anything arriving between two Wood pickups splits them.
            var list = NewList();
            list.Add("Wood", "Wood", NotificationStyle.Count, 12f, now: 0f);
            list.Add("Woodcutting", "Woodcutting", NotificationStyle.Percent, 6f, now: 0.2f);

            list.Add("Wood", "Wood", NotificationStyle.Count, 8f, now: 0.4f);

            Assert.Equal(2, list.Count);
            Assert.Equal("Wood x20", list.Entries[0].Text);
            Assert.Equal("Woodcutting +6%", list.Entries[1].Text);
        }

        [Fact]
        public void An_updated_line_stays_where_it_is()
        {
            var list = NewList();
            list.Add("Wood", "Wood", NotificationStyle.Count, 1f, now: 0f);
            list.Add("Stone", "Stone", NotificationStyle.Count, 1f, now: 0.1f);

            list.Add("Wood", "Wood", NotificationStyle.Count, 1f, now: 0.2f);

            Assert.Equal(new[] { "Wood", "Stone" }, list.Entries.Select(e => e.Label));
        }

        [Fact]
        public void A_repeat_restarts_only_its_own_dwell()
        {
            var list = NewList();
            list.Add("Wood", "Wood", NotificationStyle.Count, 1f, now: 0f);
            list.Add("Stone", "Stone", NotificationStyle.Count, 1f, now: 0f);

            list.Add("Wood", "Wood", NotificationStyle.Count, 1f, now: 3f);

            Assert.Equal(1f, list.AlphaOf(list.Find("Wood"), 3f));
            Assert.True(list.AlphaOf(list.Find("Stone"), 3f) < 1f);
        }

        [Fact]
        public void A_skill_gain_accumulates_as_a_percentage()
        {
            var list = NewList();
            list.Add("Woodcutting", "Woodcutting", NotificationStyle.Percent, 6f, now: 0f);

            list.Add("Woodcutting", "Woodcutting", NotificationStyle.Percent, 5f, now: 0.5f);

            Assert.Equal("Woodcutting +11%", list.Entries[0].Text);
        }

        [Fact]
        public void A_gain_too_small_to_round_up_still_says_one_percent()
        {
            var list = NewList();

            list.Add("Sneak", "Sneak", NotificationStyle.Percent, 0.2f, now: 0f);

            Assert.Equal("Sneak +1%", list.Entries[0].Text);
        }

        [Fact]
        public void A_line_changing_style_starts_its_total_again()
        {
            // A skill that levels up has no meaningful percentage left over.
            var list = NewList();
            list.Add("Woodcutting", "Woodcutting", NotificationStyle.Percent, 87f, now: 0f);

            list.Add("Woodcutting", "Woodcutting: 12", NotificationStyle.Plain, 0f, now: 1f);

            Assert.Equal(1, list.Count);
            Assert.Equal("Woodcutting: 12", list.Entries[0].Text);
        }

        [Fact]
        public void The_list_never_grows_past_its_cap_and_the_oldest_goes()
        {
            var list = new NotificationList(capacity: 5);
            for (int i = 0; i < 5; i++)
                list.Add("item" + i, "Item" + i, NotificationStyle.Count, 1f, now: i * 0.1f);

            list.Add("item5", "Item5", NotificationStyle.Count, 1f, now: 0.6f);

            Assert.Equal(5, list.Count);
            Assert.Equal(
                new[] { "Item1", "Item2", "Item3", "Item4", "Item5" },
                list.Entries.Select(e => e.Label));
        }

        [Fact]
        public void An_update_while_full_evicts_nothing()
        {
            var list = new NotificationList(capacity: 5);
            for (int i = 0; i < 5; i++)
                list.Add("item" + i, "Item" + i, NotificationStyle.Count, 1f, now: 0f);

            list.Add("item0", "Item0", NotificationStyle.Count, 1f, now: 0.5f);

            Assert.Equal(5, list.Count);
            Assert.Equal("Item0 x2", list.Entries[0].Text);
        }

        [Fact]
        public void A_line_is_solid_for_its_hold_then_fades_to_nothing()
        {
            var list = new NotificationList(capacity: 5, holdSeconds: 1f, fadeSeconds: 4f);
            var entry = list.Add("Wood", "Wood", NotificationStyle.Count, 1f, now: 0f);

            Assert.Equal(1f, list.AlphaOf(entry, 0f));
            Assert.Equal(1f, list.AlphaOf(entry, 1f));
            Assert.Equal(0.5f, list.AlphaOf(entry, 3f), 3);
            Assert.Equal(0f, list.AlphaOf(entry, 5f));
            Assert.Equal(0f, list.AlphaOf(entry, 60f));
        }

        [Fact]
        public void Pruning_drops_faded_lines_and_the_ones_below_move_up()
        {
            var list = new NotificationList(capacity: 5, holdSeconds: 1f, fadeSeconds: 4f);
            list.Add("Wood", "Wood", NotificationStyle.Count, 1f, now: 0f);
            list.Add("Stone", "Stone", NotificationStyle.Count, 1f, now: 3f);

            list.Prune(now: 5f);

            Assert.Equal(new[] { "Stone" }, list.Entries.Select(e => e.Label));
        }

        [Fact]
        public void Pruning_keeps_a_line_that_is_still_fading()
        {
            var list = new NotificationList(capacity: 5, holdSeconds: 1f, fadeSeconds: 4f);
            list.Add("Wood", "Wood", NotificationStyle.Count, 1f, now: 0f);

            list.Prune(now: 4.9f);

            Assert.Equal(1, list.Count);
        }

        [Fact]
        public void A_line_can_be_taken_off_early()
        {
            var list = NewList();
            list.Add("Woodcutting", "Woodcutting", NotificationStyle.Percent, 40f, now: 0f);

            Assert.True(list.Remove("Woodcutting"));
            Assert.False(list.Remove("Woodcutting"));
            Assert.Equal(0, list.Count);
        }

        [Fact]
        public void Clearing_empties_the_list()
        {
            var list = NewList();
            list.Add("Wood", "Wood", NotificationStyle.Count, 1f, now: 0f);

            list.Clear();

            Assert.Equal(0, list.Count);
        }

        [Fact]
        public void A_plain_line_is_just_its_label()
        {
            var list = NewList();

            list.Add("boss", "The forest is moving...", NotificationStyle.Plain, 0f, now: 0f);

            Assert.Equal("The forest is moving...", list.Entries[0].Text);
        }

        [Fact]
        public void A_forty_swing_session_is_one_growing_line()
        {
            var list = NewList();
            for (int i = 0; i < 40; i++)
                list.Add("Woodcutting", "Woodcutting", NotificationStyle.Percent, 0.5f, now: i * 0.1f);

            Assert.Equal(1, list.Count);
            Assert.Equal("Woodcutting +20%", list.Entries[0].Text);
        }
    }
}
