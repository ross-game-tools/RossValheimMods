using RossQoL.Core.Portals;
using Xunit;

namespace RossQoL.Core.Tests.Portals
{
    public class PlacementFootingTests
    {
        private const float Tolerance = PlacementFooting.SameFloorToleranceMetres;
        private const float UnderTerrainMargin = PlacementFooting.UnderTerrainMarginMetres;

        [Fact]
        public void Floor_at_the_players_own_height_is_standing_room()
        {
            Assert.True(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5030f, playerHeight: 5030f, toleranceMetres: Tolerance));
        }

        [Fact]
        public void A_step_up_or_down_within_tolerance_is_standing_room()
        {
            Assert.True(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5031f, playerHeight: 5030f, toleranceMetres: Tolerance));
            Assert.True(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5029f, playerHeight: 5030f, toleranceMetres: Tolerance));
        }

        [Fact]
        public void Exactly_at_the_tolerance_is_still_standing_room()
        {
            Assert.True(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5030f + Tolerance, playerHeight: 5030f, toleranceMetres: Tolerance));
        }

        [Fact]
        public void The_top_of_a_wall_is_not_standing_room()
        {
            // A probe started two metres above the player reports a wall taller
            // than that at roughly its own start height.
            Assert.False(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5032f, playerHeight: 5030f, toleranceMetres: Tolerance));
        }

        [Fact]
        public void A_drop_below_the_player_is_not_standing_room()
        {
            Assert.False(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5022f, playerHeight: 5030f, toleranceMetres: Tolerance));
        }

        [Fact]
        public void No_floor_found_is_never_standing_room()
        {
            // The fail-safe: an unvalidated spot must lose to the player's own
            // position, which is known-good ground.
            Assert.False(PlacementFooting.IsStandingRoom(
                floorFound: false, floorHeight: 5030f, playerHeight: 5030f, toleranceMetres: Tolerance));
        }

        [Fact]
        public void A_zero_tolerance_still_accepts_an_exact_match()
        {
            Assert.True(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5030f, playerHeight: 5030f, toleranceMetres: 0f));
        }

        [Fact]
        public void A_negative_tolerance_is_treated_as_zero_rather_than_rejecting_everything()
        {
            Assert.True(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5030f, playerHeight: 5030f, toleranceMetres: -5f));
            Assert.False(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5030.5f, playerHeight: 5030f, toleranceMetres: -5f));
        }

        [Fact]
        public void A_non_finite_height_is_never_standing_room()
        {
            Assert.False(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: float.NaN, playerHeight: 5030f, toleranceMetres: Tolerance));
            Assert.False(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: float.PositiveInfinity, playerHeight: 5030f, toleranceMetres: Tolerance));
            Assert.False(PlacementFooting.IsStandingRoom(
                floorFound: true, floorHeight: 5030f, playerHeight: float.NaN, toleranceMetres: Tolerance));
        }

        [Fact]
        public void A_height_well_below_the_terrain_surface_is_under_the_world()
        {
            Assert.True(PlacementFooting.IsUnderTerrain(
                height: 20f, terrainFound: true, terrainHeight: 34f, marginMetres: UnderTerrainMargin));
        }

        [Fact]
        public void A_height_at_or_just_under_the_terrain_surface_is_not_under_the_world()
        {
            Assert.False(PlacementFooting.IsUnderTerrain(
                height: 34f, terrainFound: true, terrainHeight: 34f, marginMetres: UnderTerrainMargin));

            // Vanilla's own boundary: y < groundHeight - 1 and no closer.
            Assert.False(PlacementFooting.IsUnderTerrain(
                height: 33f, terrainFound: true, terrainHeight: 34f, marginMetres: UnderTerrainMargin));
        }

        [Fact]
        public void A_height_above_the_terrain_is_never_under_the_world()
        {
            // An instanced dungeon interior, five kilometres over its own zone.
            Assert.False(PlacementFooting.IsUnderTerrain(
                height: 5030f, terrainFound: true, terrainHeight: 34f, marginMetres: UnderTerrainMargin));
        }

        [Fact]
        public void No_terrain_sample_means_nothing_to_be_under()
        {
            Assert.False(PlacementFooting.IsUnderTerrain(
                height: 20f, terrainFound: false, terrainHeight: 0f, marginMetres: UnderTerrainMargin));
            Assert.False(PlacementFooting.IsUnderTerrain(
                height: float.NaN, terrainFound: true, terrainHeight: 34f, marginMetres: UnderTerrainMargin));
        }

        [Fact]
        public void Level_ground_beside_the_player_is_acceptable()
        {
            Assert.True(Acceptable(
                floorFound: true, floorHeight: 34.2f, playerHeight: 34f,
                terrainFound: true, terrainHeight: 34.2f, playerIsUnderTerrain: false));
        }

        [Fact]
        public void A_hillside_within_tolerance_is_acceptable()
        {
            // The ordinary outdoor case the search must keep: a tame following
            // you across a hill lands on the hillside, not at your exact height.
            Assert.True(Acceptable(
                floorFound: true, floorHeight: 35.3f, playerHeight: 34f,
                terrainFound: true, terrainHeight: 35.3f, playerIsUnderTerrain: false));
        }

        [Fact]
        public void A_spot_buried_under_the_hillside_is_refused()
        {
            // Same floor height as the player -- the outdoor block check says
            // nothing, because its ray mask excludes terrain -- but the terrain
            // at that x/z is ten metres overhead, so vanilla would lift a
            // creature standing there up to the surface within five seconds.
            Assert.False(Acceptable(
                floorFound: true, floorHeight: 34f, playerHeight: 34f,
                terrainFound: true, terrainHeight: 44f, playerIsUnderTerrain: false));
        }

        [Fact]
        public void Underground_beside_an_underground_player_is_acceptable()
        {
            // A cave, or a dungeon built into the world rather than instanced:
            // the player is under the terrain sample too, so it is meaningless
            // and the floor test alone decides.
            Assert.True(Acceptable(
                floorFound: true, floorHeight: 12f, playerHeight: 12f,
                terrainFound: true, terrainHeight: 44f, playerIsUnderTerrain: true));
        }

        [Fact]
        public void A_different_floor_is_refused_even_where_terrain_is_meaningless()
        {
            Assert.False(Acceptable(
                floorFound: true, floorHeight: 8f, playerHeight: 12f,
                terrainFound: true, terrainHeight: 44f, playerIsUnderTerrain: true));
        }

        [Fact]
        public void Inside_an_instanced_interior_the_terrain_half_never_fires()
        {
            Assert.True(Acceptable(
                floorFound: true, floorHeight: 5030f, playerHeight: 5030f,
                terrainFound: true, terrainHeight: 34f, playerIsUnderTerrain: false));
        }

        [Fact]
        public void An_unvalidated_spot_is_refused_wherever_the_player_is()
        {
            Assert.False(Acceptable(
                floorFound: false, floorHeight: 0f, playerHeight: 34f,
                terrainFound: true, terrainHeight: 34f, playerIsUnderTerrain: false));
            Assert.False(Acceptable(
                floorFound: false, floorHeight: 0f, playerHeight: 12f,
                terrainFound: false, terrainHeight: 0f, playerIsUnderTerrain: true));
        }

        private static bool Acceptable(
            bool floorFound, float floorHeight, float playerHeight,
            bool terrainFound, float terrainHeight, bool playerIsUnderTerrain) =>
            PlacementFooting.IsAcceptable(
                floorFound, floorHeight, playerHeight, Tolerance,
                terrainFound, terrainHeight, playerIsUnderTerrain, UnderTerrainMargin);
    }
}
