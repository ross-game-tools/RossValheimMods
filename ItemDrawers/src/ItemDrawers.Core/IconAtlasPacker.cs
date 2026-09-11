using System;
using System.Collections.Generic;
using System.Linq;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Shelf packer. Item icons are near-uniform squares, so shelves waste
    /// very little and the algorithm stays short enough to be obviously
    /// correct — which matters more here than packing density.
    /// </summary>
    public static class IconAtlasPacker
    {
        private const int Padding = 2;   // keeps bilinear filtering from bleeding between icons

        /// <summary>
        /// Pack icons into a texture atlas. The atlas dimension is a power of 2, starting
        /// at 64×64 and doubling until all icons fit or maxDimension is exceeded.
        ///
        /// Each icon is allocated (Width + Padding) × (Height + Padding) space in the atlas,
        /// so the effective maximum icon dimension is (maxDimension - Padding). Icons larger
        /// than this will be rejected with a clear error message before packing is attempted.
        ///
        /// If icons overflow maxDimension, an exception is thrown rather than silently
        /// truncating the layout, because a drawer showing the wrong item is worse than
        /// a loud failure at startup.
        /// </summary>
        /// <param name="icons">Collection of icons to pack. Must not be null.</param>
        /// <param name="maxDimension">Maximum atlas dimension (default 4096). The atlas
        /// will be (power of 2) × (power of 2), both ≤ maxDimension.</param>
        /// <returns>An AtlasLayout containing the packed rectangles and UV coordinates.</returns>
        /// <exception cref="ArgumentNullException">If icons is null.</exception>
        /// <exception cref="ArgumentException">If any icon has zero or negative width/height.</exception>
        /// <exception cref="ArgumentException">If any icon dimension exceeds (maxDimension - Padding).</exception>
        /// <exception cref="InvalidOperationException">If icons cannot fit into a maxDimension×maxDimension atlas.</exception>
        public static AtlasLayout Pack(IReadOnlyList<IconSize> icons, int maxDimension = 4096)
        {
            if (icons == null) throw new ArgumentNullException(nameof(icons));
            if (icons.Count == 0)
                return new AtlasLayout(1, 1, new Dictionary<string, AtlasRect>());

            // Validate all icons before attempting to pack
            int maxIconSize = maxDimension - Padding;
            foreach (var icon in icons)
            {
                if (icon.Width <= 0 || icon.Height <= 0)
                    throw new ArgumentException(
                        $"Icon '{icon.Name}' has invalid dimensions {icon.Width}×{icon.Height}. " +
                        "Icon width and height must be positive.");

                if (icon.Width > maxIconSize || icon.Height > maxIconSize)
                    throw new ArgumentException(
                        $"Icon '{icon.Name}' has dimensions {icon.Width}×{icon.Height}, " +
                        $"but maximum icon size for maxDimension={maxDimension} is {maxIconSize}×{maxIconSize} " +
                        $"(atlas dimension - {Padding} pixel padding).");
            }

            // Use stable sort (OrderByDescending is documented as stable)
            var sorted = icons.OrderByDescending(i => i.Height).ToList();

            for (int size = 64; size <= maxDimension; size *= 2)
            {
                if (TryPackInto(sorted, size, out var rects))
                    return new AtlasLayout(size, size, rects);
            }

            throw new InvalidOperationException(
                $"Cannot fit {icons.Count} icons into a {maxDimension}×{maxDimension} atlas.");
        }

        private static bool TryPackInto(List<IconSize> sorted, int size,
                                        out Dictionary<string, AtlasRect> rects)
        {
            rects = new Dictionary<string, AtlasRect>(sorted.Count);

            int shelfY = 0, shelfHeight = 0, cursorX = 0;

            foreach (var icon in sorted)
            {
                int w = icon.Width + Padding;
                int h = icon.Height + Padding;

                if (w > size || h > size) return false;

                if (cursorX + w > size)
                {
                    shelfY += shelfHeight;
                    shelfHeight = 0;
                    cursorX = 0;
                }

                if (shelfY + h > size) return false;

                rects[icon.Name] = new AtlasRect(cursorX, shelfY, icon.Width, icon.Height);
                cursorX += w;
                if (h > shelfHeight) shelfHeight = h;
            }

            return true;
        }
    }
}
