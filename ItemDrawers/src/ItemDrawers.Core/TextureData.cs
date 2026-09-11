namespace ItemDrawers.Core
{
    /// <summary>
    /// A generated texture pair as plain byte arrays. Deliberately not a
    /// UnityEngine.Texture2D: the adapter converts it, which keeps
    /// generation testable -- mirrors how MeshData relates to Mesh.
    /// </summary>
    public sealed class TextureData
    {
        public int Width { get; }
        public int Height { get; }

        /// <summary>RGBA32, row-major, row 0 first. Length == Width * Height * 4.</summary>
        public byte[] Albedo { get; }

        /// <summary>Tangent-space normal map, RGBA32 (alpha unused, 255). Length == Width * Height * 4.</summary>
        public byte[] Normal { get; }

        public TextureData(int width, int height, byte[] albedo, byte[] normal)
        {
            Width = width;
            Height = height;
            Albedo = albedo;
            Normal = normal;
        }
    }
}
