namespace RossPortals.Core
{
    /// <summary>
    /// How portals within a group are ordered. Groups (folders) themselves
    /// are always ordered alphabetically regardless of this — a folder has no
    /// single distance or "last used" time, and a stable folder order is
    /// easier to navigate than one that reshuffles as you move or teleport.
    /// </summary>
    public enum SortMode
    {
        /// <summary>Alphabetical by the portal's leaf name.</summary>
        Name,

        /// <summary>Closest to the player first.</summary>
        Nearest,

        /// <summary>Most recently teleported-to first; never-used last.</summary>
        Recent,
    }
}
