namespace RossQoL.Core.Death
{
    /// <summary>Which of a grave's two remembered points the marker should aim at.</summary>
    public enum GraveAimTarget
    {
        /// <summary>The tombstone itself.</summary>
        Grave,

        /// <summary>The surface door of the dungeon the tombstone is inside.</summary>
        Entrance,
    }

    /// <summary>
    /// Where to point for a grave that may be inside a dungeon.
    ///
    /// A dungeon interior is instantiated at its zone's centre plus 5000m on
    /// the y axis (`Location.Awake`; see docs/valheim-api/dungeons.md §1), so
    /// a tombstone in a crypt is recorded five kilometres above the surface.
    /// Aimed at straight from outside, the marker points at empty sky and
    /// reads a distance in kilometres for a body that may be one room away --
    /// and anything scaling on that distance, the corpse run buff included,
    /// sits pinned at full strength the whole time.
    ///
    /// So while the player is outside, the meaningful target is the dungeon's
    /// entrance: the place they actually have to walk to. Once they are
    /// inside, the grave itself is meaningful again and the entrance is
    /// behind them.
    ///
    /// A player inside a DIFFERENT interior than the grave's still gets the
    /// grave. Both points are then equally strange -- the entrance would be
    /// five kilometres below -- and the grave is at least the true answer to
    /// "where is my body", so it is the one worth showing.
    /// </summary>
    public static class GraveAim
    {
        /// <summary>
        /// The height above which a point is inside an interior. Vanilla's own
        /// test, verbatim: `Character.InInterior(Vector3 position) =>
        /// position.y > 3000f` (Character.cs:4372, game 1.0.15).
        /// </summary>
        public const float InteriorHeight = 3000f;

        /// <summary>True when a point at this height is inside a dungeon interior.</summary>
        public static bool InInterior(float y) => y > InteriorHeight;

        /// <summary>
        /// Picks the point to aim at, and to measure distance to.
        /// </summary>
        /// <param name="graveY">Height of the recorded tombstone.</param>
        /// <param name="hasEntrance">Whether an entrance was recorded alongside the grave.</param>
        /// <param name="playerY">Height of the player right now.</param>
        public static GraveAimTarget Choose(float graveY, bool hasEntrance, float playerY)
        {
            // No entrance recorded -- an old record, a surface death, or a
            // dungeon whose door could not be found. The grave is all there is.
            if (!hasEntrance) return GraveAimTarget.Grave;

            // An entrance recorded for a surface grave would be a nonsense; the
            // grave is the answer either way, so it costs nothing to say so.
            if (!InInterior(graveY)) return GraveAimTarget.Grave;

            return InInterior(playerY) ? GraveAimTarget.Grave : GraveAimTarget.Entrance;
        }
    }
}
