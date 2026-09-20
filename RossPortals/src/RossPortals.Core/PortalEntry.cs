namespace RossPortals.Core
{
    /// <summary>
    /// One portal, reduced to the facts the list UI reasons about. The Game
    /// layer maps a live portal's ZDO (its ZDOID, tag string, world position
    /// and chosen destination) onto this; Core never sees an engine type.
    ///
    /// <para><see cref="Id"/> is an opaque, stable string. Core only ever
    /// compares and echoes it — the Game layer decides what it encodes (a
    /// ZDOID rendered to text). Two entries are "the same portal" iff their
    /// Ids are equal.</para>
    /// </summary>
    public readonly struct PortalEntry
    {
        public string Id { get; }

        /// <summary>Raw portal tag as the player typed it, e.g.
        /// <c>"Mines/Copper"</c>. May be empty for an untagged portal. The
        /// separator inside it is what drives grouping.</summary>
        public string Name { get; }

        public Vec3 Position { get; }

        /// <summary>Id of the portal this one currently sends you to, or null
        /// / empty when it has no destination set.</summary>
        public string TargetId { get; }

        public PortalEntry(string id, string name, Vec3 position, string targetId = null)
        {
            Id = id;
            Name = name ?? string.Empty;
            Position = position;
            TargetId = targetId;
        }

        public bool HasTarget => !string.IsNullOrEmpty(TargetId);
    }
}
