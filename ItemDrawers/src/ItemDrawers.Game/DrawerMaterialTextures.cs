using System.Collections.Generic;
using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Wraps ItemDrawers.Core's generated albedo/normal byte arrays into
    /// UnityEngine.Texture2D, one pair per DrawerTier, generated once and
    /// shared by every drawer of that tier -- mirrors how DrawerPieces
    /// converts the shared MeshData into a single Unity Mesh.
    /// </summary>
    public static class DrawerMaterialTextures
    {
        // 512x512 is plenty for a texture that tiles across many 1m faces
        // via triplanar projection rather than being unwrapped onto one
        // object's UVs.
        private const int Size = 512;

        // Fixed per tier, not derived from anything player- or
        // world-specific: the same texture must be produced on every launch
        // so the drawer's appearance never changes underneath a save.
        private const int WoodSeed = 1;
        private const int StoneSeed = 2;
        private const int MarbleSeed = 3;

        private static readonly Dictionary<DrawerTier, (Texture2D albedo, Texture2D normal)> _cache =
            new Dictionary<DrawerTier, (Texture2D, Texture2D)>();

        // Raw generator output, kept so brightened variants can be derived
        // from the same pixels. Re-reading them off the Texture2D is not an
        // option: BuildTexture uploads with makeNoLongerReadable: true, which
        // frees the CPU-side copy.
        private static readonly Dictionary<DrawerTier, byte[]> _albedoBytes =
            new Dictionary<DrawerTier, byte[]>();

        private static readonly Dictionary<(DrawerTier, int), Texture2D> _brightCache =
            new Dictionary<(DrawerTier, int), Texture2D>();

        public static (Texture2D albedo, Texture2D normal) Get(DrawerTier tier)
        {
            if (_cache.TryGetValue(tier, out var existing)) return existing;

            var core = DrawerTextureGenerator.Generate(ToTextureTier(tier), Size, Size, SeedFor(tier));
            _albedoBytes[tier] = core.Albedo;
            var built = (albedo: BuildTexture(core.Albedo, "albedo", tier, linear: false),
                         normal: BuildTexture(core.Normal, "normal", tier, linear: true));

            _cache[tier] = built;
            return built;
        }

        /// <summary>
        /// The tier's albedo, scaled to average <paramref name="target"/>
        /// lightness, for surfaces that must stand clearly apart from the
        /// drawer body. Scales in either direction: the faceplate is DARKER
        /// than the wood and stone bodies and lighter than the marble one,
        /// from this one call.
        ///
        /// Done in PIXELS, not with the material's _Color tint, and that is
        /// the whole point of this method. _Color on Custom/Piece is an LDR
        /// colour property clamped to 1, so the obvious approach -- set
        /// _Color to (3.1, 3.1, 3.1) and let the shader's multiply do the
        /// work -- silently becomes a multiply by 1.0 and changes nothing at
        /// all. That failure is invisible in code review and looked, in
        /// game, exactly like "the effect is too subtle": the Black Marble
        /// handle came out the same colour as the drawer face because it
        /// WAS the same colour. Multiplying the bytes has no such ceiling.
        ///
        /// The factor is derived from the tier's own mid lightness rather
        /// than pinned per tier, so a future re-hue does not quietly leave
        /// these surfaces at the wrong brightness.
        ///
        /// Saturates at 255 per channel rather than wrapping. That does cost
        /// some of the grain in the brightest pixels at large factors, which
        /// is accepted: an evenly lit metal bar is the goal, and preserving
        /// contrast in a surface whose job is to be conspicuously lighter
        /// than everything around it is not worth the complexity of a
        /// proper tone-mapped lift.
        /// </summary>
        public static Texture2D GetAtLightness(DrawerTier tier, float target)
        {
            // Ensures _albedoBytes is populated; Get is cached, so this is
            // free after the first call per tier.
            Get(tier);

            int key = Mathf.RoundToInt(target * 1000f);
            if (_brightCache.TryGetValue((tier, key), out var cached)) return cached;

            float mid = DrawerTextureGenerator.MidLightness(ToTextureTier(tier));
            float factor = mid > 0.0001f ? target / mid : 1f;

            byte[] src = _albedoBytes[tier];
            var lifted = new byte[src.Length];
            for (int i = 0; i < src.Length; i += 4)
            {
                lifted[i + 0] = Saturate(src[i + 0] * factor);
                lifted[i + 1] = Saturate(src[i + 1] * factor);
                lifted[i + 2] = Saturate(src[i + 2] * factor);
                lifted[i + 3] = src[i + 3];
            }

            var tex = BuildTexture(lifted, $"albedo_bright{key}", tier, linear: false);
            _brightCache[(tier, key)] = tex;
            return tex;
        }

        private static byte Saturate(float v) => (byte)(v >= 255f ? 255 : v <= 0f ? 0 : v);

        private static Texture2D _metal;

        /// <summary>
        /// A flat, untextured albedo for the handle.
        ///
        /// The handle wore the drawer's own wood/stone/marble grain,
        /// brightened. That is wrong for the thing it depicts -- metal is
        /// smooth, and grain at that brightness reads as washed-out stone
        /// rather than as a metal bar -- and it is also what was fighting
        /// the colour: a brightened marble albedo carries marble's blotches
        /// and veins, so the "metal" was never one consistent tone.
        ///
        /// 4x4 rather than 1x1 purely so the texture has room for a mip
        /// chain; every texel is identical, so triplanar projection samples
        /// the same value wherever it lands and the handle shades purely
        /// from its normal and lighting.
        ///
        /// Slightly cool and well above any body tone, so the bar separates
        /// from all three tiers without needing a per-tier value.
        /// </summary>
        public static Texture2D Metal()
        {
            if (_metal != null) return _metal;

            const int n = 4;
            var rgba = new byte[n * n * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i + 0] = 197;
                rgba[i + 1] = 205;
                rgba[i + 2] = 214;
                rgba[i + 3] = 255;
            }

            _metal = new Texture2D(n, n, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "rid_handle_metal",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            _metal.SetPixelData(rgba, 0);
            _metal.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return _metal;
        }

        private static TextureTier ToTextureTier(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return TextureTier.Wood;
                case DrawerTier.Stone: return TextureTier.Stone;
                case DrawerTier.BlackMarble: return TextureTier.BlackMarble;
                default: return TextureTier.Wood;
            }
        }

        private static int SeedFor(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return WoodSeed;
                case DrawerTier.Stone: return StoneSeed;
                case DrawerTier.BlackMarble: return MarbleSeed;
                default: return WoodSeed;
            }
        }

        /// <summary>
        /// Repeat wrap mode matters as much as the generator's own periodicity:
        /// Custom/Piece's triplanar projection samples this texture well outside
        /// [0, 1] UV (it is indexed by world position, not UV0), so Clamp would
        /// smear the edge pixel across most of a wall. The generator guarantees
        /// the content is seamless; Repeat is what lets the GPU actually tile it.
        /// Normal maps are marked linear (no sRGB conversion) as is standard for
        /// tangent-space normals; albedo keeps sRGB so lighting matches vanilla art.
        /// </summary>
        private static Texture2D BuildTexture(byte[] rgba, string kind, DrawerTier tier, bool linear)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: true, linear: linear)
            {
                name = $"rid_drawer_{tier}_{kind}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixelData(rgba, 0);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }
    }
}
