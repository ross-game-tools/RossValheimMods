namespace RossPortals.Game.Portals
{
    /// <summary>
    /// Bridges a live portal's <see cref="ZDOID"/> to the opaque string id the
    /// Core list model uses. Core must stay engine-free, so it can't hold a
    /// ZDOID; instead it carries the string this produces, and the registry
    /// maps it back. We never parse the string back into a ZDOID — the
    /// registry keeps the real ZDOID alongside — so the exact format is a
    /// private detail, only ever required to be stable and unique per portal.
    /// </summary>
    internal static class PortalKey
    {
        public static string Of(ZDOID id) => id.ToString();
    }
}
