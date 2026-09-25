using RossQoL.Core.Combat;
using Xunit;

namespace RossQoL.Core.Tests.Combat
{
    public class SecondPowerTests
    {
        [Fact]
        public void Set_stores_the_power_and_reports_change()
        {
            var s = new SecondPower();
            Assert.False(s.HasPower);
            Assert.True(s.Set("GP_Eikthyr"));
            Assert.True(s.HasPower);
            Assert.Equal("GP_Eikthyr", s.Power);
            Assert.False(s.Set("GP_Eikthyr"));          // same power -> no change
            Assert.True(s.Set("GP_Bonemass"));          // replaced
            Assert.Equal("GP_Bonemass", s.Power);
        }

        [Fact]
        public void Set_a_new_power_makes_it_ready_again()
        {
            var s = new SecondPower();
            s.Set("GP_Eikthyr");
            s.StartCooldown(30f);
            Assert.False(s.IsReady);
            s.Set("GP_Bonemass");                        // reassigning resets the cooldown
            Assert.True(s.IsReady);
            Assert.Equal(0f, s.Remaining, 3);
        }

        [Fact]
        public void Clear_empties_the_slot()
        {
            var s = new SecondPower();
            Assert.False(s.Clear());                     // nothing to clear
            s.Set("GP_Moder");
            s.StartCooldown(10f);
            Assert.True(s.Clear());
            Assert.False(s.HasPower);
            Assert.True(s.IsReady);
        }

        [Fact]
        public void Cooldown_starts_ticks_down_and_reports_ready()
        {
            var s = new SecondPower();
            s.Set("GP_Yagluth");
            Assert.True(s.IsReady);
            s.StartCooldown(10f);
            Assert.False(s.IsReady);
            Assert.Equal(10f, s.Remaining, 3);
            s.Tick(4f);
            Assert.Equal(6f, s.Remaining, 3);
            s.Tick(100f);                                // clamps at 0
            Assert.Equal(0f, s.Remaining, 3);
            Assert.True(s.IsReady);
        }

        [Fact]
        public void Round_trips_power_and_cooldown_through_its_string_form()
        {
            var s = new SecondPower();
            s.Set("GP_Eikthyr");
            s.StartCooldown(12.5f);
            var back = SecondPower.Parse(s.Serialize());
            Assert.Equal("GP_Eikthyr", back.Power);
            Assert.Equal(12.5f, back.Remaining, 3);
        }

        [Fact]
        public void A_ready_power_serialises_without_a_cooldown()
        {
            var s = new SecondPower();
            s.Set("GP_TheElder");
            Assert.Equal("GP_TheElder", s.Serialize());  // no "|" when ready
            var back = SecondPower.Parse("GP_TheElder");
            Assert.Equal("GP_TheElder", back.Power);
            Assert.True(back.IsReady);
        }

        [Fact]
        public void Empty_slot_serialises_empty_and_parses_back_empty()
        {
            Assert.Equal("", new SecondPower().Serialize());
            Assert.False(SecondPower.Parse(null).HasPower);
            Assert.False(SecondPower.Parse("").HasPower);
            Assert.False(SecondPower.Parse("   ").HasPower);
        }

        [Fact]
        public void Parse_drops_a_name_the_validator_rejects()
        {
            Assert.False(SecondPower.Parse("gone|5", n => n == "keep").HasPower);
            Assert.Equal("keep", SecondPower.Parse("keep|5", n => n == "keep").Power);
        }
    }
}
