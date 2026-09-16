using System;
using RossQoL.Core.Interface;
using Xunit;

namespace RossQoL.Core.Tests.Interface
{
    public class ProductionTimerTests
    {
        [Theory]
        [InlineData(0, 1200, 1200)]
        [InlineData(200, 1200, 1000)]
        [InlineData(1200, 1200, 0)]
        [InlineData(1500, 1200, 0)]
        public void Next_unit_counts_down_the_current_level(double product, double secPerUnit, double expected)
        {
            Assert.Equal(expected, ProductionTimer.SecondsToNextUnit(product, secPerUnit));
        }

        [Fact]
        public void Next_unit_is_zero_when_the_rate_is_unusable()
        {
            Assert.Equal(0d, ProductionTimer.SecondsToNextUnit(10d, 0d));
        }

        [Theory]
        [InlineData(0, 1200, 0, 4, 4800)]
        [InlineData(200, 1200, 0, 4, 4600)]
        [InlineData(0, 1200, 3, 4, 1200)]
        public void Full_counts_every_missing_level(double product, double secPerUnit, int level, int max, double expected)
        {
            Assert.Equal(expected, ProductionTimer.SecondsToFull(product, secPerUnit, level, max));
        }

        [Theory]
        [InlineData(4, 4)]
        [InlineData(5, 4)]
        public void Full_is_zero_once_the_cap_is_reached(int level, int max)
        {
            Assert.Equal(0d, ProductionTimer.SecondsToFull(0d, 1200d, level, max));
        }

        [Fact]
        public void Fermenter_counts_down_from_its_start_time()
        {
            long start = TimeSpan.TicksPerSecond * 1000;
            long now = start + TimeSpan.TicksPerSecond * 400;

            Assert.Equal(2000d, ProductionTimer.SecondsUntilReady(start, now, 2400d));
        }

        [Fact]
        public void Fermenter_is_negative_when_empty_and_zero_when_done()
        {
            Assert.True(ProductionTimer.SecondsUntilReady(0L, TimeSpan.TicksPerSecond, 2400d) < 0d);

            long start = TimeSpan.TicksPerSecond;
            long now = start + TimeSpan.TicksPerSecond * 3000;
            Assert.Equal(0d, ProductionTimer.SecondsUntilReady(start, now, 2400d));
        }

        [Theory]
        [InlineData(0, "0s")]
        [InlineData(1, "1s")]
        [InlineData(45, "45s")]
        [InlineData(59, "59s")]
        [InlineData(59.2, "1m 00s")]
        [InlineData(252, "4m 12s")]
        [InlineData(3600, "1h 00m")]
        [InlineData(3780, "1h 03m")]
        public void Format_reads_as_a_short_countdown(double seconds, string expected)
        {
            Assert.Equal(expected, ProductionTimer.Format(seconds));
        }
    }
}
