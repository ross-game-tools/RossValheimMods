using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RossPortals.Core.Tests
{
    public class PortalListViewTests
    {
        private const char Sep = '/';
        private static readonly Vec3 Origin = new Vec3(0, 0, 0);

        private static PortalEntry P(string id, string name, float dist = 0f, string target = null)
            // Distance encoded on the X axis so tests can dial it in directly.
            => new PortalEntry(id, name, new Vec3(dist, 0, 0), target);

        private static IReadOnlyList<DisplayRow> Build(
            IEnumerable<PortalEntry> portals,
            string query = "",
            SortMode sort = SortMode.Name,
            Vec3 player = default,
            IReadOnlyList<string> recent = null,
            IReadOnlyCollection<string> collapsed = null)
            => PortalListView.Build(portals, Sep, query, sort, player, recent, collapsed);

        [Fact]
        public void Ungrouped_portals_are_flat_and_alphabetical()
        {
            var rows = Build(new[] { P("1", "Charlie"), P("2", "alpha"), P("3", "Bravo") });

            Assert.All(rows, r => Assert.Equal(RowKind.Portal, r.Kind));
            Assert.Equal(new[] { "alpha", "Bravo", "Charlie" }, rows.Select(r => r.Label).ToArray());
            Assert.All(rows, r => Assert.Equal(0, r.Depth));
        }

        [Fact]
        public void A_name_prefix_becomes_a_collapsible_folder()
        {
            var rows = Build(new[] { P("1", "Mines/Copper"), P("2", "Mines/Iron"), P("3", "Home") });

            // Folder header first (with its descendant count), its two portals
            // indented, then the ungrouped portal at the root.
            Assert.Equal(RowKind.Group, rows[0].Kind);
            Assert.Equal("Mines", rows[0].Label);
            Assert.Equal("Mines", rows[0].GroupPath);
            Assert.Equal(2, rows[0].PortalCount);
            Assert.Equal(0, rows[0].Depth);

            Assert.Equal(new[] { "Copper", "Iron" }, rows.Skip(1).Take(2).Select(r => r.Label).ToArray());
            Assert.All(rows.Skip(1).Take(2), r => Assert.Equal(1, r.Depth));

            Assert.Equal(RowKind.Portal, rows[3].Kind);
            Assert.Equal("Home", rows[3].Label);
            Assert.Equal(0, rows[3].Depth);
        }

        [Fact]
        public void Folders_sort_before_loose_portals_at_the_same_level()
        {
            // "Aaa" (loose) would sort before folder "Mines" alphabetically,
            // but folders are deliberately grouped ahead of loose portals.
            var rows = Build(new[] { P("1", "Zeta/One"), P("2", "Aaa") });

            Assert.Equal(RowKind.Group, rows[0].Kind);
            Assert.Equal("Zeta", rows[0].Label);
            Assert.Equal(RowKind.Portal, rows.Last().Kind);
            Assert.Equal("Aaa", rows.Last().Label);
        }

        [Fact]
        public void Collapsing_a_folder_hides_its_contents_but_keeps_the_header()
        {
            var portals = new[] { P("1", "Mines/Copper"), P("2", "Mines/Iron"), P("3", "Home") };
            var rows = Build(portals, collapsed: new HashSet<string> { "Mines" });

            Assert.Equal(RowKind.Group, rows[0].Kind);
            Assert.True(rows[0].Collapsed);
            Assert.Equal(2, rows[0].PortalCount); // still reports what's inside
            // No depth-1 rows survive the collapse.
            Assert.DoesNotContain(rows, r => r.Depth == 1);
            Assert.Contains(rows, r => r.Kind == RowKind.Portal && r.Label == "Home");
        }

        [Fact]
        public void Search_filters_to_matching_portals_and_prunes_empty_folders()
        {
            var portals = new[] { P("1", "Mines/Copper"), P("2", "Mines/Iron"), P("3", "Home") };
            var rows = Build(portals, query: "iron");

            Assert.Equal(RowKind.Group, rows[0].Kind);
            Assert.Equal("Mines", rows[0].Label);
            Assert.Equal(1, rows[0].PortalCount); // only the match counts
            Assert.Single(rows, r => r.Kind == RowKind.Portal);
            Assert.Equal("Iron", rows.Single(r => r.Kind == RowKind.Portal).Label);
            Assert.DoesNotContain(rows, r => r.Label == "Home");
        }

        [Fact]
        public void Searching_a_folder_name_surfaces_every_portal_beneath_it()
        {
            // Leaf names ("Copper", "Iron") don't contain "mines"; the match is
            // on the whole path, so the folder query still finds them.
            var portals = new[] { P("1", "Mines/Copper"), P("2", "Mines/Iron"), P("3", "Home") };
            var rows = Build(portals, query: "mines");

            Assert.Equal(2, rows.Count(r => r.Kind == RowKind.Portal));
            Assert.DoesNotContain(rows, r => r.Label == "Home");
        }

        [Fact]
        public void Nearest_sort_is_a_flat_distance_ordered_list_with_no_folders()
        {
            var portals = new[]
            {
                P("far", "Base/Far", dist: 300f),
                P("near", "Base/Near", dist: 10f),
                P("mid", "Base/Mid", dist: 100f),
            };
            var rows = Build(portals, sort: SortMode.Nearest, player: Origin);

            // No folder headers in Nearest view; every row is a portal at depth 0.
            Assert.DoesNotContain(rows, r => r.Kind == RowKind.Group);
            Assert.All(rows, r => Assert.Equal(0, r.Depth));
            // Full names (folder prefix kept) ordered nearest-first, distance stamped.
            Assert.Equal(new[] { "Base/Near", "Base/Mid", "Base/Far" }, rows.Select(r => r.Label).ToArray());
            Assert.Equal(10f, rows[0].Distance, 3);
            Assert.Equal(300f, rows[2].Distance, 3);
        }

        [Fact]
        public void Recent_sort_puts_recently_used_first_and_unused_last()
        {
            var portals = new[] { P("a", "A"), P("b", "B"), P("c", "C") };
            // Used order: C then A. B never used.
            var rows = Build(portals, sort: SortMode.Recent, recent: new[] { "c", "a" })
                .Select(r => r.Label).ToArray();

            Assert.Equal(new[] { "C", "A", "B" }, rows);
        }

        [Fact]
        public void Folder_names_fold_case_insensitively_into_one_folder()
        {
            var rows = Build(new[] { P("1", "mines/a"), P("2", "Mines/b") });

            var groups = rows.Where(r => r.Kind == RowKind.Group).ToList();
            Assert.Single(groups);
            Assert.Equal(2, groups[0].PortalCount);
        }

        [Fact]
        public void Nested_folders_indent_by_depth_and_count_all_descendants()
        {
            var portals = new[]
            {
                P("1", "Mines/Copper/North"),
                P("2", "Mines/Copper/South"),
                P("3", "Mines/Iron"),
            };
            var rows = Build(portals);

            var mines = rows.Single(r => r.GroupPath == "Mines");
            var copper = rows.Single(r => r.GroupPath == "Mines/Copper");
            Assert.Equal(0, mines.Depth);
            Assert.Equal(3, mines.PortalCount);
            Assert.Equal(1, copper.Depth);
            Assert.Equal(2, copper.PortalCount);
            Assert.Contains(rows, r => r.Kind == RowKind.Portal && r.Label == "North" && r.Depth == 2);
        }

        [Fact]
        public void Untagged_portal_is_a_root_leaf_with_an_empty_label()
        {
            var rows = Build(new[] { P("1", ""), P("2", "Home") });

            var untagged = rows.Single(r => r.PortalId == "1");
            Assert.Equal(RowKind.Portal, untagged.Kind);
            Assert.Equal(0, untagged.Depth);
            Assert.Equal("", untagged.Label);
        }
    }
}
