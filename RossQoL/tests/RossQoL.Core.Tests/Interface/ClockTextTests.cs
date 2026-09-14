using RossQoL.Core.Interface;
using Xunit;

namespace RossQoL.Core.Tests.Interface
{
    public class ClockTextTests
    {
        [Theory]
        [InlineData(0f, "00:00")]
        [InlineData(0.25f, "06:00")]
        [InlineData(0.5f, "12:00")]
        [InlineData(0.75f, "18:00")]
        [InlineData(0.6041667f, "14:30")]
        public void Formats_24_hour_time_from_the_day_fraction(float fraction, string time)
        {
            Assert.Equal("Day 42  " + time, ClockText.Format("Day 42", fraction, use24Hour: true));
        }

        [Theory]
        [InlineData(0f, "12:00 AM")]
        [InlineData(0.25f, "6:00 AM")]
        [InlineData(0.5f, "12:00 PM")]
        [InlineData(0.6041667f, "2:30 PM")]
        [InlineData(0.999f, "11:58 PM")]
        public void Formats_12_hour_time(float fraction, string time)
        {
            Assert.Equal("Day 42  " + time, ClockText.Format("Day 42", fraction, use24Hour: false));
        }

        [Fact]
        public void Minutes_round_down_so_the_clock_never_shows_the_next_minute_early()
        {
            // 0.0006944 of a day is just under a minute.
            Assert.Equal("Day 1  00:00", ClockText.Format("Day 1", 0.0006f, use24Hour: true));
        }

        [Theory]
        [InlineData(1f, "00:00")]
        [InlineData(1.25f, "06:00")]
        [InlineData(-0.25f, "18:00")]
        public void Fractions_outside_a_day_wrap_around(float fraction, string time)
        {
            Assert.Equal("Day 3  " + time, ClockText.Format("Day 3", fraction, use24Hour: true));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void Unusable_fractions_show_midnight(float fraction)
        {
            Assert.Equal("Day 3  00:00", ClockText.Format("Day 3", fraction, use24Hour: true));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void A_missing_day_label_shows_just_the_time(string label)
        {
            Assert.Equal("12:00", ClockText.Format(label, 0.5f, use24Hour: true));
        }
    }
}
