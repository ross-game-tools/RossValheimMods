using RossQoL.Core.Portals;
using Xunit;

namespace RossQoL.Core.Tests.Portals
{
    public class TameMoveOutcomeTests
    {
        [Fact]
        public void Everything_answered_means_the_tame_arrived()
        {
            Assert.Equal(
                TameMoveOutcome.Moved,
                TameMoveOutcomes.Classify(hasId: true, networkReady: true, zdoFound: true, ownershipHeld: true));
        }

        [Fact]
        public void A_blank_id_is_reported_as_such()
        {
            Assert.Equal(
                TameMoveOutcome.NoId,
                TameMoveOutcomes.Classify(hasId: false, networkReady: true, zdoFound: true, ownershipHeld: true));
        }

        [Fact]
        public void No_networking_is_distinguished_from_a_missing_creature()
        {
            Assert.Equal(
                TameMoveOutcome.NoZdoMan,
                TameMoveOutcomes.Classify(hasId: true, networkReady: false, zdoFound: false, ownershipHeld: false));
        }

        [Fact]
        public void A_destroyed_creature_reports_a_missing_zdo()
        {
            // The case this enum was written for: a summon whose ZDO was
            // destroyed while the player was in transit looks exactly like a
            // creature killed mid-hop, and both must be distinguishable from
            // an ownership problem.
            Assert.Equal(
                TameMoveOutcome.ZdoMissing,
                TameMoveOutcomes.Classify(hasId: true, networkReady: true, zdoFound: false, ownershipHeld: false));
        }

        [Fact]
        public void A_creature_that_exists_but_could_not_be_claimed_reports_ownership()
        {
            Assert.Equal(
                TameMoveOutcome.OwnershipRefused,
                TameMoveOutcomes.Classify(hasId: true, networkReady: true, zdoFound: true, ownershipHeld: false));
        }

        [Fact]
        public void An_earlier_failure_is_always_reported_ahead_of_a_later_one()
        {
            // Pinned deliberately: with every answer false, the report must be
            // "we never had an id", not "we could not claim it". A diagnosis
            // that names the last failing step instead of the first sends the
            // reader to the wrong part of the code.
            Assert.Equal(
                TameMoveOutcome.NoId,
                TameMoveOutcomes.Classify(hasId: false, networkReady: false, zdoFound: false, ownershipHeld: false));
        }

        [Theory]
        [InlineData(TameMoveOutcome.NoId)]
        [InlineData(TameMoveOutcome.NoZdoMan)]
        [InlineData(TameMoveOutcome.ZdoMissing)]
        [InlineData(TameMoveOutcome.OwnershipRefused)]
        public void Only_a_completed_move_counts_as_an_arrival(TameMoveOutcome failure)
        {
            Assert.False(TameMoveOutcomes.Arrived(failure));
            Assert.True(TameMoveOutcomes.Arrived(TameMoveOutcome.Moved));
        }
    }
}
