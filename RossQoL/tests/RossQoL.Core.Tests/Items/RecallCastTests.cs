using RossQoL.Core.Items;
using Xunit;

namespace RossQoL.Core.Tests.Items
{
    public class RecallCastTests
    {
        [Fact]
        public void Zero_cast_time_is_always_complete()
        {
            Assert.True(RecallCast.IsComplete(startedAt: 10f, now: 10f, castSeconds: 0f));
        }

        [Fact]
        public void Negative_cast_time_is_treated_as_instant()
        {
            Assert.True(RecallCast.IsComplete(startedAt: 10f, now: 10f, castSeconds: -1f));
        }

        [Fact]
        public void Not_yet_complete_before_the_cast_time_elapses()
        {
            Assert.False(RecallCast.IsComplete(startedAt: 10f, now: 10.2f, castSeconds: 0.5f));
        }

        [Fact]
        public void Complete_exactly_at_the_cast_time_boundary()
        {
            Assert.True(RecallCast.IsComplete(startedAt: 10f, now: 10.5f, castSeconds: 0.5f));
        }

        [Fact]
        public void Complete_well_after_the_cast_time()
        {
            Assert.True(RecallCast.IsComplete(startedAt: 10f, now: 20f, castSeconds: 0.5f));
        }
    }
}
