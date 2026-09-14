using System;
using RossQoL.Core.Framework;
using Xunit;

namespace RossQoL.Core.Tests.Framework
{
    public class ConfigReloadScheduleTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Written0 = T0.AddHours(-1);

        private static ConfigReloadSchedule NewSchedule() => new ConfigReloadSchedule(Written0);

        private static DateTime At(double seconds) => T0.AddSeconds(seconds);

        [Fact]
        public void Nothing_to_do_while_the_file_is_unchanged()
        {
            var s = NewSchedule();
            s.ObserveWriteTime(Written0, At(0));
            Assert.False(s.TakeDueReload(At(10)));
        }

        [Fact]
        public void A_changed_write_time_reloads_after_the_debounce()
        {
            var s = NewSchedule();
            s.ObserveWriteTime(At(0), At(0));

            Assert.False(s.TakeDueReload(At(0.4)));
            Assert.True(s.TakeDueReload(At(0.5)));
        }

        [Fact]
        public void A_due_reload_is_taken_once()
        {
            var s = NewSchedule();
            s.ObserveWriteTime(At(0), At(0));

            Assert.True(s.TakeDueReload(At(1)));
            Assert.False(s.TakeDueReload(At(2)));
        }

        [Fact]
        public void Each_further_change_restarts_the_debounce()
        {
            // Editors and sed -i touch the file more than once per save.
            var s = NewSchedule();
            s.ObserveWriteTime(At(0), At(0));
            s.ObserveWriteTime(At(0.3), At(0.3));

            Assert.False(s.TakeDueReload(At(0.6)));
            Assert.True(s.TakeDueReload(At(0.8)));
        }

        [Fact]
        public void A_watcher_event_schedules_a_reload_without_a_write_time()
        {
            var s = NewSchedule();
            s.NotifyChanged(At(0));
            Assert.True(s.TakeDueReload(At(0.5)));
        }

        [Fact]
        public void An_edit_saved_just_after_a_reload_still_reloads()
        {
            // Fixing a typo a moment after the first save must not be lost.
            var s = NewSchedule();
            s.ObserveWriteTime(At(0), At(0));
            Assert.True(s.TakeDueReload(At(0.5)));
            s.Reloaded(At(0.5), At(0.5));

            s.ObserveWriteTime(At(0.7), At(0.7));
            Assert.False(s.TakeDueReload(At(1.0)));
            // AddSeconds(1.2) truncates a tick short of 0.7 + 0.5, so check past it.
            Assert.True(s.TakeDueReload(At(1.3)));
        }

        [Fact]
        public void A_failed_reload_is_retried_after_a_delay()
        {
            var s = NewSchedule();
            s.ObserveWriteTime(At(0), At(0));
            Assert.True(s.TakeDueReload(At(0.5)));
            s.ReloadFailed(At(0.5));

            Assert.False(s.TakeDueReload(At(1.0)));
            Assert.True(s.TakeDueReload(At(2.5)));
        }

        [Fact]
        public void A_new_edit_during_the_retry_delay_waits_only_the_debounce()
        {
            var s = NewSchedule();
            s.ObserveWriteTime(At(0), At(0));
            Assert.True(s.TakeDueReload(At(0.5)));
            s.ReloadFailed(At(0.5));

            s.ObserveWriteTime(At(0.8), At(0.8));
            Assert.True(s.TakeDueReload(At(1.3)));
        }

        [Fact]
        public void The_write_time_a_reload_leaves_behind_is_not_a_change()
        {
            var s = NewSchedule();
            s.ObserveWriteTime(At(0), At(0));
            Assert.True(s.TakeDueReload(At(0.5)));
            s.Reloaded(At(0.5), At(0.6));

            s.ObserveWriteTime(At(0.6), At(3));
            Assert.False(s.TakeDueReload(At(4)));
        }

        [Fact]
        public void An_unreadable_write_time_is_ignored()
        {
            var s = NewSchedule();
            s.ObserveWriteTime(DateTime.MinValue, At(0));
            Assert.False(s.TakeDueReload(At(1)));
        }

        [Fact]
        public void Polls_on_an_interval()
        {
            var s = NewSchedule();
            Assert.True(s.TakePollDue(At(0)));
            Assert.False(s.TakePollDue(At(0.4)));
            Assert.True(s.TakePollDue(At(0.5)));
        }
    }
}
