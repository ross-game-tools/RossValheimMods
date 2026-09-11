using System.Collections.Generic;

namespace ItemDrawers.Core
{
    public readonly struct IconSize
    {
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;

        public IconSize(string name, int width, int height)
        {
            Name = name;
            Width = width;
            Height = height;
        }
    }

    public readonly struct AtlasRect
    {
        public readonly int X, Y, Width, Height;

        public AtlasRect(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }
    }

    public sealed class AtlasLayout
    {
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyDictionary<string, AtlasRect> Rects { get; }

        public AtlasLayout(int width, int height, IReadOnlyDictionary<string, AtlasRect> rects)
        {
            Width = width;
            Height = height;
            Rects = rects;
        }

        /// <summary>
        /// Retrieve normalized UV coordinates for an icon in the atlas.
        ///
        /// This is a pure rectangle allocator: Y=0 is simply "the first shelf packed,"
        /// with no inherent top/bottom orientation. The returned V coordinates use the
        /// exact same Y axis as the packed AtlasRect (v = rect.Y / Height, uninverted) —
        /// they are defined purely in terms of this class's own rectangles, not any
        /// external "screen space" convention.
        ///
        /// Correctness therefore depends on using the *same* Y axis to blit pixels into
        /// the atlas texture as you use here to read them back. Concretely, with Unity's
        /// Texture2D.SetPixels32(x, y, ...) — whose (0,0) is bottom-left — if you blit each
        /// icon at (rect.X, rect.Y) directly, no V flip is needed: the UV returned here
        /// already addresses the same location you wrote to. Only flip V if your blit step
        /// itself reinterprets rect.Y relative to a different origin (e.g. you deliberately
        /// treat rect.Y as "rows from the top" when writing). Do not flip by default.
        /// </summary>
        /// <returns>true if the icon was found; false otherwise.</returns>
        public bool TryGetUv(string name, out float u0, out float v0, out float u1, out float v1)
        {
            u0 = v0 = u1 = v1 = 0f;
            if (name == null || !Rects.TryGetValue(name, out var r)) return false;

            u0 = (float)r.X / Width;
            v0 = (float)r.Y / Height;
            u1 = (float)(r.X + r.Width) / Width;
            v1 = (float)(r.Y + r.Height) / Height;
            return true;
        }
    }
}
