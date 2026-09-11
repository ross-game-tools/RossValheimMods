using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class IconAtlasPackerTests
    {
        private static List<IconSize> Uniform(int count, int size = 64) =>
            Enumerable.Range(0, count).Select(i => new IconSize("item" + i, size, size)).ToList();

        private static bool Overlaps(AtlasRect a, AtlasRect b) =>
            a.X < b.X + b.Width && b.X < a.X + a.Width &&
            a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

        [Fact]
        public void Every_icon_gets_a_rect()
        {
            var layout = IconAtlasPacker.Pack(Uniform(500));
            Assert.Equal(500, layout.Rects.Count);
        }

        [Fact]
        public void No_two_icons_overlap()
        {
            var layout = IconAtlasPacker.Pack(Uniform(500));
            var rects = layout.Rects.Values.ToArray();

            for (int i = 0; i < rects.Length; i++)
                for (int j = i + 1; j < rects.Length; j++)
                    Assert.False(Overlaps(rects[i], rects[j]), $"rect {i} overlaps rect {j}");
        }

        [Fact]
        public void Everything_lands_inside_the_atlas()
        {
            var layout = IconAtlasPacker.Pack(Uniform(500));

            foreach (var r in layout.Rects.Values)
            {
                Assert.InRange(r.X, 0, layout.Width - r.Width);
                Assert.InRange(r.Y, 0, layout.Height - r.Height);
            }
        }

        [Fact]
        public void Mixed_sizes_pack_without_overlapping()
        {
            var icons = new List<IconSize>();
            var rng = new Random(1234);
            for (int i = 0; i < 200; i++)
                icons.Add(new IconSize("i" + i, 16 + rng.Next(64), 16 + rng.Next(64)));

            var layout = IconAtlasPacker.Pack(icons);
            var rects = layout.Rects.Values.ToArray();

            for (int i = 0; i < rects.Length; i++)
                for (int j = i + 1; j < rects.Length; j++)
                    Assert.False(Overlaps(rects[i], rects[j]));
        }

        [Fact]
        public void Uv_coordinates_are_normalised_and_ordered()
        {
            var layout = IconAtlasPacker.Pack(Uniform(64));

            Assert.True(layout.TryGetUv("item7", out float u0, out float v0, out float u1, out float v1));
            Assert.InRange(u0, 0f, 1f);
            Assert.InRange(v0, 0f, 1f);
            Assert.True(u1 > u0);
            Assert.True(v1 > v0);
        }

        [Fact]
        public void An_unknown_item_reports_no_uv_rather_than_throwing()
        {
            var layout = IconAtlasPacker.Pack(Uniform(4));
            Assert.False(layout.TryGetUv("nope", out _, out _, out _, out _));
        }

        [Fact]
        public void Atlas_dimensions_are_powers_of_two()
        {
            var layout = IconAtlasPacker.Pack(Uniform(300));

            Assert.Equal(0, layout.Width & (layout.Width - 1));
            Assert.Equal(0, layout.Height & (layout.Height - 1));
        }

        [Fact]
        public void Too_much_to_fit_throws_rather_than_silently_dropping_icons()
        {
            // A silently truncated atlas would show wrong icons on drawers,
            // which is worse than failing loudly at startup.
            Assert.Throws<InvalidOperationException>(() =>
                IconAtlasPacker.Pack(Uniform(10_000, 256), maxDimension: 1024));
        }

        [Fact]
        public void An_empty_input_produces_an_empty_layout()
        {
            var layout = IconAtlasPacker.Pack(new List<IconSize>());
            Assert.Empty(layout.Rects);
        }

        [Fact]
        public void Exact_uv_values_for_deterministic_input()
        {
            // Create a small deterministic layout where we can hand-compute UVs
            var icons = new List<IconSize>
            {
                new IconSize("a", 32, 32),
                new IconSize("b", 32, 32),
            };

            var layout = IconAtlasPacker.Pack(icons, maxDimension: 256);

            // Both icons fit in first shelf. With padding (2 pixels each):
            // icon "a" at (0, 0) with size 32×32, occupies 34×34 in atlas
            // icon "b" at (34, 0) with size 32×32, occupies 34×34 in atlas
            // 64×64 is too small (34 + 34 = 68 > 64 height after wrap), so 128×128

            Assert.True(layout.TryGetUv("a", out float u0_a, out float v0_a, out float u1_a, out float v1_a));
            Assert.Equal(0f, u0_a);
            Assert.Equal(0f, v0_a);
            Assert.Equal(32f / 128f, u1_a);
            Assert.Equal(32f / 128f, v1_a);

            Assert.True(layout.TryGetUv("b", out float u0_b, out float v0_b, out float u1_b, out float v1_b));
            Assert.Equal(34f / 128f, u0_b);  // 34 because icon "a" is 32 wide + 2 padding
            Assert.Equal(0f, v0_b);
            Assert.Equal((34f + 32f) / 128f, u1_b);
            Assert.Equal(32f / 128f, v1_b);
        }

        [Fact]
        public void Padding_creates_gap_between_adjacent_icons()
        {
            // Verify that padding actually creates gaps between icons
            var layout = IconAtlasPacker.Pack(Uniform(100, 64));
            var rects = layout.Rects.Values.ToArray();

            // Find two horizontally adjacent rects (same Y, consecutive X)
            bool foundHorizontalGap = false;
            for (int i = 0; i < rects.Length; i++)
            {
                for (int j = 0; j < rects.Length; j++)
                {
                    if (i == j) continue;
                    // Same shelf (Y coordinate)
                    if (rects[i].Y == rects[j].Y)
                    {
                        // i is to the left of j
                        if (rects[i].X < rects[j].X)
                        {
                            int gap = rects[j].X - (rects[i].X + rects[i].Width);
                            Assert.True(gap >= 2, $"Gap between horizontal neighbors should be at least 2, got {gap}");
                            foundHorizontalGap = true;
                        }
                    }
                }
            }
            Assert.True(foundHorizontalGap, "Should have found at least one pair of horizontally adjacent icons");

            // Find two vertically adjacent rects (different shelves)
            bool foundVerticalGap = false;
            var shelves = rects.GroupBy(r => r.Y).OrderBy(g => g.Key).ToList();
            for (int i = 0; i < shelves.Count - 1; i++)
            {
                var currentShelf = shelves[i].ToList();
                var nextShelf = shelves[i + 1].ToList();
                int currentMaxY = currentShelf.Max(r => r.Y + r.Height);
                int nextMinY = nextShelf.Min(r => r.Y);
                int gap = nextMinY - currentMaxY;
                Assert.True(gap >= 2, $"Gap between vertical neighbors should be at least 2, got {gap}");
                foundVerticalGap = true;
            }
            Assert.True(foundVerticalGap, "Should have at least two shelves to verify vertical gap");
        }

        [Fact]
        public void Icon_with_padded_size_exceeding_max_dimension_throws()
        {
            // An icon of size (maxDimension - 1) will have padded size (maxDimension + 1), exceeding maxDimension
            var icons = new List<IconSize>
            {
                new IconSize("toolarge", 1023, 64)
            };

            var ex = Assert.Throws<ArgumentException>(() =>
                IconAtlasPacker.Pack(icons, maxDimension: 1024));

            Assert.Contains("toolarge", ex.Message);
            Assert.Contains("1023", ex.Message);
            Assert.Contains("maximum", ex.Message.ToLower());
        }

        [Fact]
        public void Zero_width_or_height_icon_throws_with_clear_message()
        {
            // Zero width icon
            var icons1 = new List<IconSize>
            {
                new IconSize("badicon", 0, 64)
            };

            var ex1 = Assert.Throws<ArgumentException>(() =>
                IconAtlasPacker.Pack(icons1));
            Assert.Contains("badicon", ex1.Message);
            Assert.Contains("invalid", ex1.Message.ToLower());

            // Zero height icon
            var icons2 = new List<IconSize>
            {
                new IconSize("badicon2", 64, 0)
            };

            var ex2 = Assert.Throws<ArgumentException>(() =>
                IconAtlasPacker.Pack(icons2));
            Assert.Contains("badicon2", ex2.Message);
            Assert.Contains("invalid", ex2.Message.ToLower());
        }

        [Fact]
        public void Stable_sort_preserves_input_order_for_equal_heights()
        {
            // Create icons with identical heights but distinguishable names
            // This tests that the stable sort (OrderByDescending) preserves input order
            // for equal-height items, which is critical for determinism across CoreCLR and Mono.
            // Stable sort means icons of equal height stay in input order; unstable could permute them.
            var icons = new List<IconSize>
            {
                new IconSize("a", 16, 16),
                new IconSize("b", 16, 16),
                new IconSize("c", 16, 16),
                new IconSize("d", 16, 16),
                new IconSize("e", 16, 16),
            };

            var layout = IconAtlasPacker.Pack(icons);

            // All have same height, so should stay in input order after stable sort.
            // They'll be packed left-to-right in shelves. All with same Y are on the same shelf.
            var rects = new[] {
                layout.Rects["a"],
                layout.Rects["b"],
                layout.Rects["c"],
                layout.Rects["d"],
                layout.Rects["e"]
            };

            // Find positions: within each shelf (same Y), they should appear left-to-right
            // in input order because stable sort ensures equal-height items maintain order.
            var shelves = rects
                .Select((r, i) => new { index = i, name = new[] { "a", "b", "c", "d", "e" }[i], rect = r })
                .GroupBy(x => x.rect.Y)
                .OrderBy(g => g.Key)
                .ToList();

            // Verify each shelf's items appear in input order (ascending X)
            foreach (var shelf in shelves)
            {
                var shelfItems = shelf.OrderBy(x => x.rect.X).ToList();
                for (int i = 0; i < shelfItems.Count - 1; i++)
                {
                    Assert.True(shelfItems[i].rect.X < shelfItems[i + 1].rect.X,
                        $"Items on same shelf should be left-to-right: {shelfItems[i].name} at {shelfItems[i].rect.X} " +
                        $"should be left of {shelfItems[i + 1].name} at {shelfItems[i + 1].rect.X}");
                }

                // Verify they're in the order they were input (within the shelf)
                var orderedByInput = shelf.OrderBy(x => x.index).ToList();
                for (int i = 0; i < orderedByInput.Count - 1; i++)
                {
                    Assert.True(orderedByInput[i].rect.X <= orderedByInput[i + 1].rect.X,
                        $"Stable sort should preserve input order: {orderedByInput[i].name} should come before " +
                        $"{orderedByInput[i + 1].name}");
                }
            }
        }

        [Fact]
        public void Packing_twice_produces_identical_results()
        {
            var icons = new List<IconSize>();
            var rng = new Random(42);
            for (int i = 0; i < 100; i++)
                icons.Add(new IconSize("item" + i, 32 + rng.Next(32), 32 + rng.Next(32)));

            var layout1 = IconAtlasPacker.Pack(icons);
            var layout2 = IconAtlasPacker.Pack(icons);

            // Atlas dimensions should match
            Assert.Equal(layout1.Width, layout2.Width);
            Assert.Equal(layout1.Height, layout2.Height);

            // All rects should match exactly
            Assert.Equal(layout1.Rects.Count, layout2.Rects.Count);
            foreach (var name in layout1.Rects.Keys)
            {
                var r1 = layout1.Rects[name];
                var r2 = layout2.Rects[name];
                Assert.Equal(r1.X, r2.X);
                Assert.Equal(r1.Y, r2.Y);
                Assert.Equal(r1.Width, r2.Width);
                Assert.Equal(r1.Height, r2.Height);
            }

            // UV coordinates should match
            foreach (var name in layout1.Rects.Keys)
            {
                layout1.TryGetUv(name, out float u0_1, out float v0_1, out float u1_1, out float v1_1);
                layout2.TryGetUv(name, out float u0_2, out float v0_2, out float u1_2, out float v1_2);

                Assert.Equal(u0_1, u0_2);
                Assert.Equal(v0_1, v0_2);
                Assert.Equal(u1_1, u1_2);
                Assert.Equal(v1_1, v1_2);
            }
        }
    }
}
