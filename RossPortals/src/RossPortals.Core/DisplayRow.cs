namespace RossPortals.Core
{
    public enum RowKind
    {
        /// <summary>A collapsible folder header derived from a name prefix.</summary>
        Group,

        /// <summary>A selectable portal.</summary>
        Portal,
    }

    /// <summary>
    /// One line the panel draws. <see cref="PortalListView"/> produces a flat,
    /// already-ordered list of these — the panel only has to render them top to
    /// bottom and indent by <see cref="Depth"/>. Keeping all the tree, filter,
    /// sort and collapse decisions on this side of the boundary is what makes
    /// the interesting behaviour unit-testable without the game running.
    /// </summary>
    public readonly struct DisplayRow
    {
        public RowKind Kind { get; }

        /// <summary>Indentation level; top level is 0.</summary>
        public int Depth { get; }

        /// <summary>Folder segment (Group) or portal leaf name (Portal). May be
        /// empty for an untagged portal — the Game layer substitutes a
        /// localized "(no name)" placeholder for display.</summary>
        public string Label { get; }

        // --- Group rows only ---

        /// <summary>Full path of this folder, separator-joined (e.g.
        /// <c>"Mines/Copper"</c>). Identifies the folder for collapse state;
        /// null on Portal rows.</summary>
        public string GroupPath { get; }

        public bool Collapsed { get; }

        /// <summary>How many portals sit anywhere beneath this folder (after
        /// filtering). Lets the header read "Mines (7)".</summary>
        public int PortalCount { get; }

        // --- Portal rows only ---

        /// <summary>The portal's opaque id; null on Group rows.</summary>
        public string PortalId { get; }

        /// <summary>Straight-line distance from the player, metres.</summary>
        public float Distance { get; }

        private DisplayRow(RowKind kind, int depth, string label, string groupPath,
            bool collapsed, int portalCount, string portalId, float distance)
        {
            Kind = kind;
            Depth = depth;
            Label = label;
            GroupPath = groupPath;
            Collapsed = collapsed;
            PortalCount = portalCount;
            PortalId = portalId;
            Distance = distance;
        }

        public static DisplayRow Group(int depth, string label, string path, bool collapsed, int portalCount)
            => new DisplayRow(RowKind.Group, depth, label, path, collapsed, portalCount, null, 0f);

        public static DisplayRow Portal(int depth, string label, string portalId, float distance)
            => new DisplayRow(RowKind.Portal, depth, label, null, false, 0, portalId, distance);
    }
}
