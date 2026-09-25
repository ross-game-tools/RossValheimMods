using RossQoL.Core.Items;
using Xunit;

namespace RossQoL.Core.Tests.Items
{
    public class SummonKindsTests
    {
        [Theory]
        [InlineData("Skeleton_Friendly", true)]        // Dead Raiser skeleton
        [InlineData("Skeleton_Friendly(Clone)", true)] // live instance carries (Clone)
        [InlineData("skeleton_friendly", true)]        // case-insensitive
        [InlineData("Bjorn_spiritcaller", true)]       // Spirit Caller summons
        [InlineData("Moose_spiritcaller(Clone)", true)]
        [InlineData("Wolf_spiritcaller", true)]
        [InlineData("Boar_spiritcaller", true)]
        [InlineData("Fox_SpiritCaller", true)]         // future spirit creature, case-insensitive suffix
        [InlineData("Skeleton", false)]                // a vanilla hostile skeleton
        [InlineData("Wolf", false)]                    // a real tamed wolf
        [InlineData("spiritcaller", false)]            // suffix must be preceded by a name
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsSummon_matches_our_summons_only(string name, bool expected)
            => Assert.Equal(expected, SummonKinds.IsSummon(name));

        [Theory]
        [InlineData("StaffSkeleton", true)]            // Dead Raiser
        [InlineData("StaffSpiritCaller", true)]        // Spirit Caller
        [InlineData("staffspiritcaller", true)]        // case-insensitive
        [InlineData("StaffFireball", false)]
        [InlineData("Trollstav", false)]               // another summon staff, left to vanilla
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsRecallStaff_matches_the_two_recall_staffs(string name, bool expected)
            => Assert.Equal(expected, SummonKinds.IsRecallStaff(name));
    }
}
