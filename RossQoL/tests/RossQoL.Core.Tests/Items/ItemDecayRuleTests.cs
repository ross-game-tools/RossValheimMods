using RossQoL.Core.Items;
using Xunit;

namespace RossQoL.Core.Tests.Items
{
    public class ItemDecayRuleTests
    {
        private const double Old = ItemDecayRule.AutoDestroySeconds;
        private const double Young = ItemDecayRule.AutoDestroySeconds - 1.0;

        [Fact]
        public void Old_unattended_item_with_the_exemption_lifted_is_destroyed()
        {
            Assert.True(ItemDecayRule.ShouldDestroy(
                Old, playerNearby: false, inTar: false, isDebris: false, ignoreBaseExemption: true));
        }

        [Fact]
        public void With_the_exemption_left_alone_nothing_is_ever_destroyed()
        {
            Assert.False(ItemDecayRule.ShouldDestroy(
                Old, playerNearby: false, inTar: false, isDebris: false, ignoreBaseExemption: false));
        }

        [Fact]
        public void An_item_younger_than_one_hour_survives()
        {
            Assert.False(ItemDecayRule.ShouldDestroy(
                Young, playerNearby: false, inTar: false, isDebris: false, ignoreBaseExemption: true));
        }

        [Fact]
        public void A_nearby_player_protects_the_item()
        {
            Assert.False(ItemDecayRule.ShouldDestroy(
                Old, playerNearby: true, inTar: false, isDebris: false, ignoreBaseExemption: true));
        }

        [Fact]
        public void Tar_protects_the_item()
        {
            Assert.False(ItemDecayRule.ShouldDestroy(
                Old, playerNearby: false, inTar: true, isDebris: false, ignoreBaseExemption: true));
        }

        [Fact]
        public void Building_debris_is_never_destroyed_by_this_rule()
        {
            Assert.False(ItemDecayRule.ShouldDestroy(
                Old, playerNearby: false, inTar: false, isDebris: true, ignoreBaseExemption: true));
        }

        [Fact]
        public void The_age_boundary_is_inclusive_of_exactly_one_hour()
        {
            Assert.True(ItemDecayRule.ShouldDestroy(
                ItemDecayRule.AutoDestroySeconds, playerNearby: false, inTar: false, isDebris: false,
                ignoreBaseExemption: true));
        }
    }
}
