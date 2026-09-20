using System;
using System.Collections.Generic;
using System.Linq;

namespace RossPortals.Core
{
    /// <summary>
    /// The whole "make a big pile of portals findable" feature, as one pure
    /// function. Given the known portals plus the player's current view state
    /// (search text, sort, which folders are collapsed, where the player is),
    /// it returns the exact rows the panel should draw, already filtered,
    /// grouped into folders, sorted and flattened.
    ///
    /// Everything interesting is here so it can be tested without the game:
    /// the panel is left with nothing but "draw these rows, indented".
    /// </summary>
    public static class PortalListView
    {
        /// <param name="portals">Every known portal to consider. The caller
        /// excludes the portal being configured (a portal can't target
        /// itself) before passing them in.</param>
        /// <param name="separator">Folder separator inside names.</param>
        /// <param name="query">Search text; empty shows everything. Matched
        /// case-insensitively against the whole raw name, so typing a folder
        /// name ("mines") surfaces every portal beneath it even when the leaf
        /// doesn't contain the text.</param>
        /// <param name="sort">Ordering for portals within a folder.</param>
        /// <param name="playerPosition">Used for <see cref="SortMode.Nearest"/>
        /// and to stamp each portal row with a distance.</param>
        /// <param name="recentIds">Portal ids most-recently-used first; used
        /// for <see cref="SortMode.Recent"/>. May be null.</param>
        /// <param name="collapsedGroups">Full paths of folders the player has
        /// collapsed. A collapsed folder still emits its header row but none of
        /// its contents. May be null.</param>
        public static IReadOnlyList<DisplayRow> Build(
            IEnumerable<PortalEntry> portals,
            char separator,
            string query,
            SortMode sort,
            Vec3 playerPosition,
            IReadOnlyList<string> recentIds = null,
            IReadOnlyCollection<string> collapsedGroups = null)
        {
            if (portals == null) throw new ArgumentNullException(nameof(portals));

            var collapsed = new HashSet<string>(collapsedGroups ?? Array.Empty<string>(), StringComparer.Ordinal);
            var recent = recentIds ?? Array.Empty<string>();
            var recentIndex = BuildRecentIndex(recent);

            var trimmedQuery = query?.Trim();
            bool filtering = !string.IsNullOrEmpty(trimmedQuery);

            var filtered = new List<PortalEntry>();
            foreach (var portal in portals)
            {
                if (portal.Id == null) continue;
                if (filtering && portal.Name.IndexOf(trimmedQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                filtered.Add(portal);
            }

            var rows = new List<DisplayRow>();

            // Folders only make sense when the list is alphabetical. Distance and
            // recency are global orderings a folder can't represent, so those
            // views are a single flat list of every portal, each labelled with
            // its full name so it stays identifiable without its folder header.
            if (sort != SortMode.Name)
            {
                foreach (var portal in SortLeaves(filtered, separator, sort, playerPosition, recentIndex))
                    rows.Add(DisplayRow.Portal(0, FlatLabel(portal), portal.Id, portal.Position.DistanceTo(playerPosition)));
                return rows;
            }

            var root = new Node(null, null);
            foreach (var portal in filtered) Insert(root, portal, separator);
            Emit(root, depth: 0, rows, separator, sort, playerPosition, recentIndex, collapsed);
            return rows;
        }

        private static Dictionary<string, int> BuildRecentIndex(IReadOnlyList<string> recent)
        {
            // First occurrence wins, so a duplicate id later in the history
            // can't push a portal further down than its most-recent use.
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < recent.Count; i++)
                if (recent[i] != null && !index.ContainsKey(recent[i]))
                    index[recent[i]] = i;
            return index;
        }

        // In a flat (non-alphabetical) view a portal shows its whole name, folder
        // prefix and all, since there's no folder header to give it context.
        private static string FlatLabel(PortalEntry portal) => portal.Name ?? string.Empty;

        private static void Insert(Node root, PortalEntry portal, char separator)
        {
            var groupPath = PortalName.GroupPath(portal.Name, separator);

            var node = root;
            foreach (var segment in groupPath)
                node = node.Child(segment, separator);

            node.Leaves.Add(portal);
        }

        private static void Emit(Node node, int depth, List<DisplayRow> rows, char separator,
            SortMode sort, Vec3 player, Dictionary<string, int> recentIndex, HashSet<string> collapsed)
        {
            // Folders first, alphabetical, then this folder's own portals.
            // Folders-before-portals keeps a folder's header visually attached
            // to its contents instead of stranded among loose portals.
            foreach (var child in node.Children.Values.OrderBy(c => c.Segment, StringComparer.OrdinalIgnoreCase))
            {
                bool isCollapsed = collapsed.Contains(child.Path);
                rows.Add(DisplayRow.Group(depth, child.Segment, child.Path, isCollapsed, child.CountLeaves()));

                if (!isCollapsed)
                    Emit(child, depth + 1, rows, separator, sort, player, recentIndex, collapsed);
            }

            foreach (var portal in SortLeaves(node.Leaves, separator, sort, player, recentIndex))
            {
                rows.Add(DisplayRow.Portal(
                    depth,
                    PortalName.Leaf(portal.Name, separator),
                    portal.Id,
                    portal.Position.DistanceTo(player)));
            }
        }

        private static IEnumerable<PortalEntry> SortLeaves(List<PortalEntry> leaves, char separator,
            SortMode sort, Vec3 player, Dictionary<string, int> recentIndex)
        {
            // Leaf label + Id as the final tiebreak everywhere, so ordering is
            // total and deterministic — two portals that tie on the primary key
            // (same distance, both unnamed, neither used) never swap around
            // between rebuilds.
            string Label(PortalEntry p) => PortalName.Leaf(p.Name, separator);

            switch (sort)
            {
                case SortMode.Nearest:
                    return leaves
                        .OrderBy(p => p.Position.DistanceTo(player))
                        .ThenBy(Label, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(p => p.Id, StringComparer.Ordinal);

                case SortMode.Recent:
                    return leaves
                        .OrderBy(p => recentIndex.TryGetValue(p.Id, out var i) ? i : int.MaxValue)
                        .ThenBy(Label, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(p => p.Id, StringComparer.Ordinal);

                case SortMode.Name:
                default:
                    return leaves
                        .OrderBy(Label, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(p => p.Id, StringComparer.Ordinal);
            }
        }

        /// <summary>Mutable folder tree, built once per <see cref="Build"/> call.</summary>
        private sealed class Node
        {
            public string Segment { get; }
            public string Path { get; }
            public Dictionary<string, Node> Children { get; } = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            public List<PortalEntry> Leaves { get; } = new List<PortalEntry>();

            public Node(string segment, string path)
            {
                Segment = segment;
                Path = path;
            }

            public Node Child(string segment, char separator)
            {
                if (!Children.TryGetValue(segment, out var child))
                {
                    // Case-insensitive keying folds "Mines" and "mines" into one
                    // folder; the first spelling seen becomes the header label.
                    var path = Path == null ? segment : Path + separator + segment;
                    child = new Node(segment, path);
                    Children[segment] = child;
                }
                return child;
            }

            public int CountLeaves()
            {
                int count = Leaves.Count;
                foreach (var child in Children.Values) count += child.CountLeaves();
                return count;
            }
        }
    }
}
