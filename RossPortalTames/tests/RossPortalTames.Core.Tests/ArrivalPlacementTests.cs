using System;
using System.Linq;
using Xunit;

namespace RossPortalTames.Core.Tests
{
    public class ArrivalPlacementTests
    {
        private static readonly Vec3 Arrival = Vec3.Zero;
        private static readonly Vec3 North = new Vec3(0f, 0f, 1f);
        private const float Search = 6f;

        private static bool OpenWorld(Vec3 p) => true;

        [Fact]
        public void Returns_one_position_per_tame()
        {
            var placed = ArrivalPlacement.Compute(Arrival, North, 5, Search, OpenWorld);
            Assert.Equal(5, placed.Length);
        }

        [Fact]
        public void Returns_empty_for_no_tames()
        {
            Assert.Empty(ArrivalPlacement.Compute(Arrival, North, 0, Search, OpenWorld));
        }

        [Fact]
        public void Placements_are_within_the_search_distance()
        {
            var placed = ArrivalPlacement.Compute(Arrival, North, 8, Search, OpenWorld);
            foreach (var p in placed)
                Assert.True(Vec3.DistanceSquared(p, Arrival) <= Search * Search + 0.01f,
                    $"{p} is beyond the {Search}m search distance");
        }

        [Fact]
        public void Placements_do_not_collide_in_an_open_world()
        {
            var placed = ArrivalPlacement.Compute(Arrival, North, 8, Search, OpenWorld);
            Assert.Equal(placed.Length, placed.Distinct().Count());
        }

        [Fact]
        public void A_wall_pushes_every_placement_to_the_free_side()
        {
            // THE case this exists for. Portals are commonly built against a
            // wall, and a ring placement -- the obvious first design -- puts
            // half the pack inside the geometry. Here everything with X > 0 is
            // solid rock.
            bool IsFree(Vec3 p) => p.X <= 0f;

            var placed = ArrivalPlacement.Compute(Arrival, North, 6, Search, IsFree);

            foreach (var p in placed)
                Assert.True(IsFree(p) || p.Equals(Arrival),
                    $"{p} was placed inside the wall");
        }

        [Fact]
        public void Falls_back_to_the_arrival_point_when_the_world_is_entirely_blocked()
        {
            // The player's own arrival position is by definition somewhere a
            // body can stand, so it is the one safe answer when the search
            // finds nothing. Overlapping creatures separate themselves within
            // a second; a creature inside a wall does not.
            var placed = ArrivalPlacement.Compute(Arrival, North, 3, Search, _ => false);

            Assert.Equal(3, placed.Length);
            Assert.All(placed, p => Assert.Equal(Arrival, p));
        }

        [Fact]
        public void Prefers_the_direction_the_player_faces()
        {
            // Valheim puts the player in front of the destination portal, so
            // "where the player faces" is "away from the portal" -- the side
            // with room.
            var placed = ArrivalPlacement.Compute(Arrival, North, 1, Search, OpenWorld);
            Assert.True(placed[0].Z > 0f, $"expected a placement north of the player, got {placed[0]}");
        }

        [Fact]
        public void Is_deterministic()
        {
            // Two clients computing the same arrival must not disagree, and a
            // flaky test here would be untraceable.
            var a = ArrivalPlacement.Compute(Arrival, North, 5, Search, OpenWorld);
            var b = ArrivalPlacement.Compute(Arrival, North, 5, Search, OpenWorld);
            Assert.Equal(a, b);
        }

        [Fact]
        public void A_zero_facing_does_not_produce_invalid_positions()
        {
            // Guards against a player whose forward vector is degenerate;
            // NaN here would write a corrupt position into a ZDO.
            var placed = ArrivalPlacement.Compute(Arrival, Vec3.Zero, 4, Search, OpenWorld);

            Assert.Equal(4, placed.Length);
            foreach (var p in placed)
            {
                Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z), $"{p} contains NaN");
                Assert.False(float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z), $"{p} is infinite");
            }
        }

        [Fact]
        public void Null_predicate_is_treated_as_an_open_world()
        {
            var placed = ArrivalPlacement.Compute(Arrival, North, 2, Search, null);
            Assert.Equal(2, placed.Length);
        }
    }
}
