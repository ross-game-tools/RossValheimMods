using RossQoL.Core.Death;
using Xunit;

namespace RossQoL.Core.Tests.Death
{
    public class GraveAimTests
    {
        // A crypt floor: its own zone centre, 5000m up (Location.Awake).
        private const float InsideY = 5032f;
        private const float SurfaceY = 31f;

        [Fact]
        public void A_surface_grave_is_always_aimed_at_directly()
        {
            Assert.Equal(GraveAimTarget.Grave, GraveAim.Choose(SurfaceY, hasEntrance: false, playerY: SurfaceY));
        }

        [Fact]
        public void A_dungeon_grave_aims_at_its_entrance_from_outside()
        {
            Assert.Equal(GraveAimTarget.Entrance, GraveAim.Choose(InsideY, hasEntrance: true, playerY: SurfaceY));
        }

        [Fact]
        public void A_dungeon_grave_aims_at_itself_once_the_player_is_inside()
        {
            Assert.Equal(GraveAimTarget.Grave, GraveAim.Choose(InsideY, hasEntrance: true, playerY: InsideY - 4f));
        }

        [Fact]
        public void A_dungeon_grave_with_no_recorded_entrance_falls_back_to_the_grave()
        {
            // An old record, or a dungeon whose door could not be found: five
            // kilometres of sky is still better than showing nothing at all.
            Assert.Equal(GraveAimTarget.Grave, GraveAim.Choose(InsideY, hasEntrance: false, playerY: SurfaceY));
        }

        [Fact]
        public void A_surface_grave_that_somehow_carries_an_entrance_still_aims_at_itself()
        {
            Assert.Equal(GraveAimTarget.Grave, GraveAim.Choose(SurfaceY, hasEntrance: true, playerY: SurfaceY));
        }

        [Theory]
        [InlineData(2999f, false)]
        [InlineData(3000f, false)]
        [InlineData(3000.5f, true)]
        public void Interior_is_vanillas_own_test(float y, bool inside)
        {
            // Character.InInterior(Vector3 position) => position.y > 3000f
            Assert.Equal(inside, GraveAim.InInterior(y));
        }

        [Fact]
        public void An_underwater_grave_is_never_mistaken_for_a_dungeon()
        {
            // Below sea level is still a long way under the interior floor.
            Assert.Equal(GraveAimTarget.Grave, GraveAim.Choose(-12f, hasEntrance: true, playerY: 2f));
        }
    }
}
