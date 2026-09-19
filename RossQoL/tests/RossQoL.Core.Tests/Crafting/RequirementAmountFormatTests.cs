using RossQoL.Core.Crafting;
using Xunit;

namespace RossQoL.Core.Tests.Crafting
{
    public class RequirementAmountFormatTests
    {
        [Fact]
        public void Single_digit_numbers_format_as_have_slash_need()
        {
            Assert.Equal("2/5", RequirementAmountFormat.Format(2, 5));
        }

        [Fact]
        public void A_three_digit_pair_keeps_every_digit_of_both_numbers()
        {
            // The bug this guards: a requirement row rendering both a
            // three-digit "have" and a three-digit "need" once lost the
            // final digit of "need" -- 150/400 read as 150/40. That was a
            // display-layer clipping problem, not a formatting one, but this
            // is the one place a regression in the string itself would show.
            Assert.Equal("150/400", RequirementAmountFormat.Format(150, 400));
        }

        [Fact]
        public void Zero_have_still_shows_the_full_need()
        {
            Assert.Equal("0/999", RequirementAmountFormat.Format(0, 999));
        }
    }
}
