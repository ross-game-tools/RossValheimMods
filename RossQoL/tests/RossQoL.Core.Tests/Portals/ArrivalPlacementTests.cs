using RossQoL.Core.Portals;
using System;
using System.Linq;
using Xunit;

namespace RossQoL.Core.Tests.Portals
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
            // 8 tames all land in the first 1.5m ring (14 angle offsets and a
            // 1m minimum separation leave room for most of them there), so a
            // small count never approaches the 6m bound and a ring-termination
            // bug (e.g. an off-by-one in the `radius <= searchDistance + 0.001f`
            // loop condition) would pass unnoticed. 36 is the smallest count
            // that empirically forces a placement out to the full 6m ring
            // given RingStep=1.5 and the 14 angle offsets per ring.
            const int TameCount = 36;
            const float RingStep = 1.5f; // must match ArrivalPlacement's private RingStep
            var placed = ArrivalPlacement.Compute(Arrival, North, TameCount, Search, OpenWorld);

            Assert.True(Vec3.DistanceSquared(placed[placed.Length - 1], Arrival) >= (Search - RingStep) * (Search - RingStep),
                "test no longer reaches the outer ring -- increase TameCount");

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
        public void Prefers_the_direction_the_player_faces_when_facing_east()
        {
            // The North-only case above cannot distinguish real rotation from
            // a hardcoded (0,0,1) offset -- both would satisfy "Z > 0". Facing
            // a different direction and asserting on X instead of Z catches
            // that: a hardcoded offset would fail this, a correctly rotated
            // one would not.
            var east = new Vec3(1f, 0f, 0f);
            var placed = ArrivalPlacement.Compute(Arrival, east, 1, Search, OpenWorld);
            Assert.True(placed[0].X > 0f, $"expected a placement east of the player, got {placed[0]}");
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

        [Fact]
        public void A_search_distance_smaller_than_the_ring_step_still_searches()
        {
            // Tight portal huts are documented advice for lowering
            // SearchDistance below the ring step; that must still try at
            // least one ring instead of silently disabling the search.
            const float TinySearch = 0.5f;
            var placed = ArrivalPlacement.Compute(Arrival, North, 1, TinySearch, OpenWorld);

            Assert.NotEqual(Arrival, placed[0]);
            Assert.True(Vec3.DistanceSquared(placed[0], Arrival) <= TinySearch * TinySearch + 0.01f,
                $"{placed[0]} is beyond the {TinySearch}m search distance");
        }

        [Fact]
        public void A_NaN_facing_does_not_produce_invalid_positions()
        {
            // Positions are written directly into creature ZDOs, so a NaN
            // facing must not propagate into a NaN position.
            var nanFacing = new Vec3(float.NaN, 0f, float.NaN);
            var placed = ArrivalPlacement.Compute(Arrival, nanFacing, 4, Search, OpenWorld);

            Assert.Equal(4, placed.Length);
            foreach (var p in placed)
            {
                Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z), $"{p} contains NaN");
                Assert.False(float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z), $"{p} is infinite");
            }
        }
    }
}
