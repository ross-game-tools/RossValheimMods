using System;
using RossQoL.Core.Production;
using Xunit;

namespace RossQoL.Core.Tests.Production
{
    public class FermenterReadinessTests
    {
        private static long Secs(double s) => (long)(s * TimeSpan.TicksPerSecond);

        [Fact]
        public void Empty_barrel_is_never_ready()
        {
            Assert.False(FermenterReadiness.IsReady(0, Secs(1), Secs(100000), 2400f));
        }

        [Fact]
        public void Unset_start_time_is_never_ready()
        {
            Assert.False(FermenterReadiness.IsReady(123, 0, Secs(100000), 2400f));
        }

        [Fact]
        public void Ready_only_strictly_after_duration()
        {
            Assert.False(FermenterReadiness.IsReady(123, Secs(1000), Secs(3400), 2400f));
            Assert.True(FermenterReadiness.IsReady(123, Secs(1000), Secs(3401), 2400f));
        }

        [Fact]
        public void Clock_before_start_is_not_ready()
        {
            Assert.False(FermenterReadiness.IsReady(123, Secs(5000), Secs(1000), 2400f));
        }
    }
}
