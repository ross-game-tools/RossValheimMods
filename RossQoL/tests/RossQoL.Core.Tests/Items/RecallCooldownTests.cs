using RossQoL.Core.Items;
using Xunit;

namespace RossQoL.Core.Tests.Items
{
    public class RecallCooldownTests
    {
        [Fact]
        public void Never_used_before_can_fire_immediately()
        {
            Assert.True(RecallCooldown.CanFire(lastUsed: null, now: 0f, cooldownSeconds: 8f));
        }

        [Fact]
        public void Blocked_before_the_cooldown_elapses()
        {
            Assert.False(RecallCooldown.CanFire(lastUsed: 10f, now: 15f, cooldownSeconds: 8f));
        }

        [Fact]
        public void Allowed_exactly_at_the_cooldown_boundary()
        {
            Assert.True(RecallCooldown.CanFire(lastUsed: 10f, now: 18f, cooldownSeconds: 8f));
        }

        [Fact]
        public void Allowed_well_after_the_cooldown()
        {
            Assert.True(RecallCooldown.CanFire(lastUsed: 10f, now: 100f, cooldownSeconds: 8f));
        }

        [Fact]
        public void Zero_cooldown_never_blocks()
        {
            Assert.True(RecallCooldown.CanFire(lastUsed: 10f, now: 10f, cooldownSeconds: 0f));
        }

        [Fact]
        public void Negative_cooldown_is_treated_as_off_rather_than_always_blocked()
        {
            Assert.True(RecallCooldown.CanFire(lastUsed: 10f, now: 10f, cooldownSeconds: -1f));
        }
    }
}
