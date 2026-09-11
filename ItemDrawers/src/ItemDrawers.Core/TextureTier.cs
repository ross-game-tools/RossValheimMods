namespace ItemDrawers.Core
{
    /// <summary>
    /// Core's own notion of "which material to paint", independent of
    /// ItemDrawers.Game's DrawerTier. Kept separate so Core never references
    /// a Game type -- the Game layer maps its DrawerTier onto this enum at
    /// the one call site that needs both.
    /// </summary>
    public enum TextureTier
    {
        Wood,
        Stone,
        BlackMarble
    }
}
