using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class CorpseRunTimeoutTests
    {
        [Fact]
        public void A_zero_limit_never_expires()
        {
            Assert.False(CorpseRunTimeout.Expired(startSeconds: 0f, nowSeconds: 1_000_000f, limitMinutes: 0f));
        }

        [Fact]
        public void A_negative_limit_is_treated_the_same_as_zero()
        {
            Assert.False(CorpseRunTimeout.Expired(startSeconds: 0f, nowSeconds: 1_000_000f, limitMinutes: -5f));
        }

        [Fact]
        public void Not_expired_before_the_limit_is_reached()
        {
            Assert.False(CorpseRunTimeout.Expired(startSeconds: 100f, nowSeconds: 100f + 599f, limitMinutes: 10f));
        }

        [Fact]
        public void Expired_exactly_on_the_boundary()
        {
            Assert.True(CorpseRunTimeout.Expired(startSeconds: 100f, nowSeconds: 100f + 600f, limitMinutes: 10f));
        }

        [Fact]
        public void Still_expired_well_past_the_limit()
        {
            Assert.True(CorpseRunTimeout.Expired(startSeconds: 0f, nowSeconds: 10_000f, limitMinutes: 10f));
        }

        [Fact]
        public void Not_expired_at_the_moment_it_starts()
        {
            Assert.False(CorpseRunTimeout.Expired(startSeconds: 50f, nowSeconds: 50f, limitMinutes: 10f));
        }
    }
}
