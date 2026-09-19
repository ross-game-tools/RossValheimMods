using System;

namespace RossQoL.Core.Portals
{
    /// <summary>
    /// Whether a candidate spot is somewhere a creature can actually be put
    /// down next to the player.
    ///
    /// The rule, in one line: <b>a creature is never placed at a height the
    /// player is not at.</b> The player is standing somewhere valid by
    /// definition, so a candidate whose floor is well above or well below them
    /// is wrong -- a rooftop, a pit, the next turn of a staircase, the
    /// overworld five kilometres under a dungeon -- whatever the game happens
    /// to call the place they are standing in.
    ///
    /// This started as an interior-only rule, because the Game layer's outdoor
    /// question ("is anything blocking this point") reaches two kilometres
    /// above a point and eight below it, and inside a dungeon instanced five
    /// thousand metres up that column contains the interior's own ceiling AND
    /// the whole overworld beneath it. But the outdoor question is no better
    /// at telling a good spot from a bad one anywhere else the terrain sample
    /// lies: its ray mask excludes terrain entirely, so a point buried inside
    /// a hillside, or hanging over a forty-metre drop, passes it happily. The
    /// rule is therefore applied everywhere now, and nothing depends on
    /// correctly recognising an "interior" first.
    ///
    /// Stated here as pure rules so the tolerances are testable without a
    /// running game.
    /// </summary>
    public static class PlacementFooting
    {
        /// <summary>
        /// How far the floor under a candidate may differ from the player's
        /// own height and still count as the same floor.
        ///
        /// Big enough for a step, a ramp or a bit of rubble; small enough that
        /// the top of a wall (which a downward probe started just above head
        /// height reports at roughly that probe's own height) and any pit or
        /// drop is rejected. A Valheim building storey is two metres, so this
        /// also rejects the floor below and the roof above. It bounds how far
        /// the height correction that follows can move a creature: an accepted
        /// candidate is never snapped further than this from the player.
        /// </summary>
        public const float SameFloorToleranceMetres = 1.5f;

        /// <summary>
        /// How far below the terrain surface a position has to be before
        /// vanilla treats it as under the world.
        ///
        /// Verbatim from <c>Character.UnderWorldCheck</c> (Character.cs:883,
        /// 1.0.15), which runs every five seconds for every creature:
        ///
        /// <code>
        /// float groundHeight = ZoneSystem.instance.GetGroundHeight(base.transform.position);
        /// if (base.transform.position.y &lt; groundHeight - 1f)
        /// {
        ///     Vector3 position = base.transform.position;
        ///     position.y = groundHeight + 0.5f;
        ///     base.transform.position = position;
        ///     m_body.position = position;
        ///     m_body.linearVelocity = Vector3.zero;
        /// }
        /// </code>
        ///
        /// So a creature put down under the surface does not stay there: the
        /// game lifts it to the surface at that x/z within five seconds. For
        /// a creature placed beside a player part-way down an entrance shaft
        /// that surface is the ground outside, which is exactly the "it ends
        /// up outside the entrance" report this rule exists to fix.
        /// </summary>
        public const float UnderTerrainMarginMetres = 1f;

        /// <summary>
        /// Whether a candidate spot may be used.
        ///
        /// Two independent reasons to refuse, both of which come down to
        /// "the player is not there":
        /// <list type="number">
        /// <item>the floor under it is not the floor the player is standing
        /// on (<see cref="IsStandingRoom"/>);</item>
        /// <item>vanilla would immediately relocate a creature standing there
        /// to the terrain surface, when it would not do that to the player
        /// (<see cref="IsUnderTerrain"/>).</item>
        /// </list>
        /// </summary>
        /// <param name="playerIsUnderTerrain">
        /// Whether the player's OWN position is under the terrain surface by
        /// the same rule. When it is, the terrain sample says nothing useful
        /// about anywhere nearby either -- the player is inside an instanced
        /// interior, or genuinely below ground -- so that half of the test is
        /// skipped rather than rejecting every candidate and piling every
        /// creature on the player.
        /// </param>
        public static bool IsAcceptable(
            bool floorFound, float floorHeight, float playerHeight, float toleranceMetres,
            bool terrainFound, float terrainHeight, bool playerIsUnderTerrain,
            float underTerrainMarginMetres)
        {
            if (!IsStandingRoom(floorFound, floorHeight, playerHeight, toleranceMetres)) return false;

            if (playerIsUnderTerrain) return true;

            return !IsUnderTerrain(floorHeight, terrainFound, terrainHeight, underTerrainMarginMetres);
        }

        /// <param name="floorFound">
        /// Whether a downward probe found any solid under the candidate at
        /// all. False means "could not be validated", which is deliberately
        /// treated as unusable rather than as open: the caller's fallback is
        /// the player's own position, a spot a body is already standing on,
        /// and that is always the safer of the two.
        /// </param>
        /// <param name="floorHeight">The height of that solid. Ignored when <paramref name="floorFound"/> is false.</param>
        /// <param name="playerHeight">The height the player is standing at.</param>
        /// <param name="toleranceMetres">
        /// How far apart the two may be. Zero or negative means "must match
        /// exactly", which no real floor ever will -- a config typo should
        /// degrade to "everyone lands on me", not to creatures placed in
        /// mid-air.
        /// </param>
        public static bool IsStandingRoom(
            bool floorFound, float floorHeight, float playerHeight, float toleranceMetres)
        {
            if (!floorFound) return false;
            if (!IsFinite(floorHeight) || !IsFinite(playerHeight)) return false;

            return Math.Abs(floorHeight - playerHeight) <= Math.Max(0f, toleranceMetres);
        }

        /// <summary>
        /// Vanilla's own under-the-world rule, asked about a height rather
        /// than about a live character. A terrain sample that failed means
        /// there is no terrain loaded at that x/z to be under, so nothing is
        /// refused on its account.
        /// </summary>
        public static bool IsUnderTerrain(
            float height, bool terrainFound, float terrainHeight, float marginMetres)
        {
            if (!terrainFound) return false;
            if (!IsFinite(height) || !IsFinite(terrainHeight)) return false;

            return height < terrainHeight - Math.Max(0f, marginMetres);
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
