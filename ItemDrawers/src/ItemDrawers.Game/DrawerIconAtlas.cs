using System.Collections.Generic;
using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// One texture holding every storable item's icon. This is what lets a
    /// hundred drawer labels share a single material and batch into one
    /// draw call — the old mods gave each drawer its own Canvas instead,
    /// which is where their frame cost went.
    /// </summary>
    public static class DrawerIconAtlas
    {
        private static AtlasLayout _layout;
        private static Texture2D _texture;

        public static Material SharedMaterial { get; private set; }
        public static bool IsBuilt => _layout != null && SharedMaterial != null;

        // Guards the outer Build() failure log: TryGetUv calls Build() on
        // every miss while the atlas isn't built, so an exception that
        // keeps recurring for some reason this file doesn't otherwise guard
        // against (the known one, a single bad item's GetIcon() throwing,
        // is now handled per-item below) must not spam once per drawer face
        // per frame -- the same failure mode DrawerRenderer.Show() had.
        private static bool _buildFailureLogged;

        /// <summary>
        /// Builds the atlas, or leaves it unbuilt on any failure -- see
        /// BuildInternal for the actual work. Wrapped here so a thrown
        /// exception (the known case -- ItemDrop.ItemData.GetIcon() being a
        /// bare, unchecked m_shared.m_icons[m_variant] index against items
        /// whose m_variant is out of range -- no longer throws at all, since
        /// the build reads icons through ItemFacts.SafeIcon; this outer guard
        /// remains for some other, not-yet-seen failure mode) degrades to
        /// "atlas stays unbuilt, logged once" rather than repeating on every
        /// retry from TryGetUv.
        /// </summary>
        public static void Build()
        {
            if (IsBuilt) return;

            try
            {
                BuildInternal();
            }
            catch (System.Exception ex)
            {
                _layout = null;
                SharedMaterial = null;

                if (_buildFailureLogged) return;
                _buildFailureLogged = true;
                DrawerPlugin.Log.LogError(
                    "Icon atlas build threw; leaving it unbuilt. Will keep retrying quietly on "
                    + $"later calls, but will not log this again: {ex}");
            }
        }

        private static void BuildInternal()
        {
            if (ObjectDB.instance == null)
            {
                DrawerPlugin.Log.LogWarning("ObjectDB not ready; icon atlas deferred.");
                return;
            }

            var sizes = new List<IconSize>();
            var sprites = new Dictionary<string, Sprite>();
            var variantFallbacks = new List<string>();

            foreach (var prefab in ObjectDB.instance.m_items)
            {
                var drop = prefab == null ? null : prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;
                if (drop.m_itemData.m_shared.m_maxStackSize <= 1) continue;

                // ItemFacts.SafeIcon rather than ItemData.GetIcon(), which
                // decompiles to a bare `return m_shared.m_icons[m_variant];`
                // with no bounds check and throws for any item whose
                // m_variant points past its own m_icons array. Valheim 1.0.12
                // ships three such items -- draugr_arrow, GoblinSpear and
                // GoblinSpearDeepNorth (issue #3) -- and since the last is a
                // new 1.0 item, the set should be expected to change again.
                //
                // This used to catch the exception and skip the item, which
                // cost those items their icon on every drawer face and logged
                // a warning per item per boot. SafeIcon falls back to icon 0
                // instead: an item in that state still has a usable icon, so
                // there is nothing to warn about and nothing to skip.
                //
                // The try/catch stays as a net for a DIFFERENT failure --
                // SafeIcon dereferences m_shared, and a malformed mod item
                // could still surprise us -- but it is no longer the expected
                // path, so anything reaching it is worth a warning.
                Sprite icon;
                try
                {
                    icon = ItemFacts.SafeIcon(drop.m_itemData, out bool variantOutOfRange);
                    if (variantOutOfRange) variantFallbacks.Add(prefab.name);
                }
                catch (System.Exception ex)
                {
                    DrawerPlugin.Log.LogWarning(
                        $"Skipping '{prefab.name}' in the icon atlas: reading its icon threw "
                        + $"({ex.GetType().Name}: {ex.Message}).");
                    continue;
                }

                if (icon == null || icon.texture == null) continue;
                if (sprites.ContainsKey(prefab.name)) continue;

                sprites[prefab.name] = icon;
                // textureRect, not rect -- see TryReadSprite's docstring.
                // The two agree for a sprite that owns its own texture, but
                // Valheim's item icons are packed into shared sheets, and
                // once packed, `rect` can still report the sprite's
                // pre-packing rect while `texture` has become the packed
                // sheet. Sizing the atlas allocation from a different rect
                // than the one actually read from would silently misalign
                // the two even if each were internally consistent.
                sizes.Add(new IconSize(prefab.name,
                    (int)icon.textureRect.width, (int)icon.textureRect.height));
            }

            // ObjectDB.instance being non-null is not the same as ObjectDB
            // having its items yet: mods (and Jotunn's own custom items)
            // register into ObjectDB.m_items well after ObjectDB.instance
            // first exists, and calling Build() before that population
            // finishes silently packed a 0-icon, 1x1 atlas that never
            // retried -- every drawer face then failed TryGetUv forever,
            // with only a quiet "0 icons" info line to explain why. Treat
            // zero storable items found as a real failure: log it loudly,
            // and -- critically -- leave _layout/SharedMaterial unset so
            // IsBuilt stays false and TryGetUv's own retry (below) gets
            // another chance once more items exist.
            if (sizes.Count == 0)
            {
                DrawerPlugin.Log.LogError(
                    "Icon atlas build found 0 storable items in ObjectDB.m_items -- called too early, "
                    + "before item registration finished. Not marking the atlas built; a later call to "
                    + "Build() (see DrawerIconAtlas.TryGetUv's retry) will try again.");
                return;
            }

            _layout = IconAtlasPacker.Pack(sizes);

            _texture = new Texture2D(_layout.Width, _layout.Height, TextureFormat.RGBA32, mipChain: true)
            {
                name = "rid_icon_atlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var clear = new Color32[_layout.Width * _layout.Height];
            _texture.SetPixels32(clear);

            // AtlasLayout.TryGetUv is a pure rectangle allocator: it returns
            // v = rect.Y / Height, uninverted -- it has no opinion about
            // "top" or "bottom". Two independent facts settle how the pixel
            // blit below must line up with that: Texture2D.SetPixels32(x, y, ...)
            // addresses pixel rows with y=0 at the BOTTOM of the texture, and
            // Unity's UV space also has v=0 at the bottom. Both conventions
            // already agree with the packer's raw Y, so each icon is blitted
            // at exactly (rect.X, rect.Y) with no flip. Flipping here would
            // silently mirror every icon vertically relative to the UVs
            // DrawerRenderer reads later.
            foreach (var pair in _layout.Rects)
            {
                if (!sprites.TryGetValue(pair.Key, out var sprite)) continue;
                if (!TryReadSprite(sprite, out var pixels, out int w, out int h)) continue;

                _texture.SetPixels32(pair.Value.X, pair.Value.Y, w, h, pixels);
            }

            _texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

            var shader = ResolveIconShader(out string shaderSource);
            if (shader == null)
            {
                DrawerPlugin.Log.LogError(
                    "No usable shader found for the drawer icon atlas material "
                    + $"(tried {string.Join(", ", ShaderCandidates)}). "
                    + "Drawer icons will not render until this is fixed.");

                // Leave no half-built state behind: without a shader there is
                // no SharedMaterial, so IsBuilt stays false and a later retry
                // (there isn't one today, but Build() is written to tolerate
                // one) must not find a stale _texture/_layout to leak or reuse.
                Object.Destroy(_texture);
                _texture = null;
                _layout = null;
                return;
            }

            SharedMaterial = new Material(shader)
            {
                name = "rid_icon_material",
                mainTexture = _texture
            };

            // Draw AFTER every opaque surface, the faceplate included, and
            // this is a correctness fix rather than a tidiness one.
            //
            // The icon sits a few millimetres in front of the plate, and at
            // distance the depth buffer stops resolving a gap that small --
            // ordinary non-linear depth precision, which is why the artifact
            // only ever showed from far away. What made it FLICKER rather
            // than simply pick a winner is draw order: Unity sorts opaque
            // renderers front-to-back, so the icon drew first and the plate
            // was then tested against it, and ZTest LEqual lets the plate
            // WIN a tie -- overwriting the icon. Temporal AA's sub-pixel
            // jitter pushed that comparison either side of the tie every
            // frame, which is the pulse, and it is also why the pulse did
            // not stop when the camera did.
            //
            // Queue 2450 is still opaque-side (Geometry ends at 2500) but
            // after everything in the default 2000 bucket, so the icon now
            // draws LAST of the pair. The same LEqual tie that used to hand
            // the pixel to the plate now hands it to the icon, every frame,
            // whatever the jitter does -- the comparison stops being a coin
            // flip instead of merely being made less likely to come up.
            //
            // Geometry genuinely in front of the drawer still occludes the
            // icon: this changes draw ORDER, not the depth test.
            SharedMaterial.renderQueue = 2450;

            // Single Info line: the best available signal that the atlas is
            // healthy (icon count, dimensions, and which shader it actually
            // resolved). A one-shot per-item rect/AtlasRect/UV mapping dump
            // lived here while a real packed-sprite offset bug was being
            // diagnosed (see TryReadSprite's docstring for the bug itself,
            // now fixed) -- removed now that it's confirmed fixed.
            DrawerPlugin.Log.LogInfo(
                $"Icon atlas built: {_layout.Rects.Count} icons in {_layout.Width}x{_layout.Height}, "
                + $"material shader = {shaderSource}");

            // One line, only when it applies. These items would previously
            // have thrown out of GetIcon and been skipped entirely; naming
            // them keeps a game-version-dependent quirk visible without the
            // per-item warning spam that prompted issue #3.
            if (variantFallbacks.Count > 0)
            {
                DrawerPlugin.Log.LogInfo(
                    $"{variantFallbacks.Count} item(s) have an icon variant outside their own icon array "
                    + $"and use their first icon: {string.Join(", ", variantFallbacks)}.");
            }
        }

        /// <summary>
        /// Valheim 1.0 runs Unity 6000.0.75f1 on its own render pipeline, not
        /// the built-in pipeline "Unlit/Transparent" was written for --
        /// confirmed live: Shader.Find("Unlit/Transparent") returns null on
        /// this build, so the fallback chain below is what's actually in
        /// play, not a theoretical safety net.
        ///
        /// "Sprites/Default" is tried next, ahead of "UI/Default": it is the
        /// shader every SpriteRenderer in the game already renders with, and
        /// a SpriteRenderer is a 3D-scene object drawn by the same kind of
        /// renderer this atlas's quad uses (MeshRenderer), just as unlit and
        /// alpha-blended as this material needs to be. "UI/Default" is
        /// written for CanvasRenderer inside a Canvas's screen-space or
        /// world-space-Canvas context; whether it renders correctly on a
        /// plain MeshRenderer with no Canvas at all (this atlas's actual
        /// use) was unverified here and is not something to trust by
        /// default just because Valheim's own HUD uses it for a different
        /// rendering path. Both are shipped for certain: Valheim references
        /// UnityEngine.UI.dll and draws its whole HUD through it (so
        /// "UI/Default" existing is guaranteed, or the game's own UI would
        /// already be broken), and ships sprite-based effects/UI elements
        /// throughout the world (so "Sprites/Default" existing is equally
        /// certain). Only Shader.Find is used here (no Canvas/UIModule API),
        /// so no extra assembly reference is needed beyond what the project
        /// already has.
        /// </summary>
        private static readonly string[] ShaderCandidates = { "Unlit/Transparent", "Sprites/Default", "UI/Default" };

        /// <summary>
        /// Tries each of ShaderCandidates, in order, via Shader.Find, logging
        /// a warning for every miss. Built from the same array the
        /// total-failure LogError in Build() reports, so the two cannot
        /// drift apart the way the message and the tried shaders once did.
        /// </summary>
        private static Shader ResolveIconShader(out string source)
        {
            foreach (var name in ShaderCandidates)
            {
                var shader = Shader.Find(name);
                if (shader != null)
                {
                    source = $"Shader.Find(\"{name}\")";
                    return shader;
                }

                DrawerPlugin.Log.LogWarning($"Shader.Find(\"{name}\") returned null; trying next candidate.");
            }

            source = null;
            return null;
        }

        /// <summary>
        /// Item icons usually live on a non-readable atlas texture, so they
        /// are blitted through a temporary RenderTexture rather than read
        /// directly, which would throw.
        ///
        /// Reads from `sprite.textureRect`, not `sprite.rect`. Both report a
        /// pixel rect "of the sprite," but they answer different questions:
        /// `rect` is the sprite's rect as originally imported, while
        /// `textureRect` is where it actually sits within `sprite.texture`
        /// right now. The two are identical for a sprite that owns its own
        /// texture -- which is why this went unnoticed -- but Valheim packs
        /// its item icons into shared sheets, and once a sprite is packed,
        /// `sprite.texture` becomes that shared sheet while `rect` can still
        /// report the sprite's pre-packing position. Reading `rect.x/y` out
        /// of the packed sheet samples whatever happens to sit at that
        /// pre-packing offset instead -- exactly the reported symptom (the
        /// wrong icon's corner, offset and too small: a genuinely different,
        /// smaller region of a neighbouring icon).
        /// </summary>
        private static bool TryReadSprite(Sprite sprite, out Color32[] pixels, out int width, out int height)
        {
            pixels = null;
            Rect texRect = sprite.textureRect;
            width = Mathf.RoundToInt(texRect.width);
            height = Mathf.RoundToInt(texRect.height);
            if (width <= 0 || height <= 0) return false;

            // This was RenderTextureReadWrite.Linear, and that was the root
            // cause of the reported dark icons, not a lighting/shader
            // problem: Valheim runs Linear color space, so sampling
            // `sprite.texture` (imported sRGB, like any normal colour
            // texture) during the Blit below already gives the shader
            // linear-space values -- that conversion happens on the way IN
            // regardless of this RT's own setting. Forcing the destination
            // RT to `.Linear` told the GPU to store those already-linear
            // values back out with NO re-encoding to sRGB, i.e. as raw
            // linear bytes. ReadPixels then copies those bytes verbatim
            // into `readable` below, which -- like every other colour
            // texture in this codebase (see DrawerMaterialTextures'
            // "albedo keeps sRGB" comment) -- is an sRGB-flagged Texture2D:
            // anything that samples it (this atlas's own material,
            // Sprites/Default) decodes its bytes AGAIN as if they were
            // sRGB-encoded. Linear values run through a second sRGB decode
            // come out darker (the sRGB decode curve satisfies decode(x) <
            // x on (0,1)), which is exactly "icons read dark" with no
            // change needed to lighting or the shader at all.
            //
            // `.Default` matches the RT's sRGB read/write behaviour to the
            // project's actual colour space -- the same behaviour a normal
            // sRGB Texture2D or the screen framebuffer already gets -- so
            // the Blit's output byte encoding matches what `readable` and
            // the final atlas texture (both plain sRGB Texture2Ds) expect,
            // and there is exactly one sRGB decode between "as imported"
            // and "as sampled by the drawer material", the same as for
            // every other sprite the game renders.
            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            try
            {
                Graphics.Blit(sprite.texture, rt);
                RenderTexture.active = rt;

                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(texRect.x, texRect.y, width, height), 0, 0);
                readable.Apply();
                pixels = readable.GetPixels32();
                Object.Destroy(readable);
                return true;
            }
            catch (System.Exception e)
            {
                DrawerPlugin.Log.LogWarning($"Could not read icon for atlas: {e.Message}");
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Retries Build() if the atlas isn't built yet, rather than just
        /// reporting failure -- this is the "a drawer face first needs it"
        /// retry path: whatever caused Build() to bail earlier (ObjectDB not
        /// ready, or zero items found because it ran too early) may well
        /// have resolved by the time a face is actually being shown. Build()
        /// itself is a no-op once IsBuilt is true, so calling it here on
        /// every miss is cheap once the atlas is actually built and only
        /// does real work while it still isn't.
        /// </summary>
        public static bool TryGetUv(string item, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.one;

            if (!IsBuilt) Build();
            if (_layout == null) return false;
            if (!_layout.TryGetUv(item, out float u0, out float v0, out float u1, out float v1)) return false;

            min = new Vector2(u0, v0);
            max = new Vector2(u1, v1);
            return true;
        }
    }
}
