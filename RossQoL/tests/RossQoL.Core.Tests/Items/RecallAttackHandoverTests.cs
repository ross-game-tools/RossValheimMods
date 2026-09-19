using RossQoL.Core.Items;
using Xunit;

namespace RossQoL.Core.Tests.Items
{
    public class RecallAttackHandoverTests
    {
        [Fact]
        public void Nothing_held_plays_the_cast_straight_away()
        {
            Assert.Equal(
                RecallAttackAction.PlayNow,
                RecallAttackHandover.Decide(hasCurrentAttack: false, currentAttackDone: false));
        }

        [Fact]
        public void Nothing_held_ignores_the_done_flag()
        {
            Assert.Equal(
                RecallAttackAction.PlayNow,
                RecallAttackHandover.Decide(hasCurrentAttack: false, currentAttackDone: true));
        }

        [Fact]
        public void A_finished_attack_is_retired_before_the_cast_plays()
        {
            Assert.Equal(
                RecallAttackAction.RetireThenPlay,
                RecallAttackHandover.Decide(hasCurrentAttack: true, currentAttackDone: true));
        }

        [Fact]
        public void An_attack_still_running_refuses_the_recall()
        {
            Assert.Equal(
                RecallAttackAction.Refuse,
                RecallAttackHandover.Decide(hasCurrentAttack: true, currentAttackDone: false));
        }
    }
}
