using Jotunn.Managers;
using TMPro;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Resolves a TMP_FontAsset to use for drawer count labels without
    /// shipping one. TextMeshPro's own fallback ("LiberationSans SDF") does
    /// not exist in Valheim's asset set, so a TextMeshPro created without an
    /// explicit font logs "Font Asset was not found" and renders no text at
    /// all.
    /// </summary>
    internal static class DrawerFont
    {
        private static TMP_FontAsset _cached;
        private static bool _loggedFailure;

        /// <summary>
        /// Null if no TMP font could be found anywhere -- logs an error
        /// (once) naming the problem rather than letting count labels
        /// silently render nothing with no indication why.
        /// </summary>
        public static TMP_FontAsset Shared
        {
            get
            {
                if (_cached != null) return _cached;

                // Preferred: Jotunn's own bundled TMP font, a supported API
                // rather than a memory scrape. Verified by decompiling the
                // deployed Jotunn 2.30.1 (Jotunn.Managers.GUIManager
                // declares `TMP_FontAsset TMP_AveriaSansLibre`, populated
                // from Jotunn's own "jotunn" asset bundle inside
                // GUIManager.InitializeAssets() -- not scraped from
                // Valheim's own UI at all, so its availability doesn't
                // depend on Valheim having created any TMP_Text yet).
                // AveriaSerif/AveriaSerifBold also exist on GUIManager but
                // are plain UnityEngine.Font, not usable as a TMP_FontAsset.
                var jotunnFont = GUIManager.Instance != null ? GUIManager.Instance.TMP_AveriaSansLibre : null;
                if (jotunnFont != null)
                {
                    _cached = jotunnFont;
                    return _cached;
                }

                // Fall back: a font an existing TMP_Text is already
                // rendering with -- the surest evidence it actually works
                // in this build, as opposed to some loaded-but-unused asset.
                foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (text != null && text.font != null)
                    {
                        _cached = text.font;
                        return _cached;
                    }
                }

                // Fall back further: any TMP font asset resident in memory
                // at all.
                var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts.Length > 0)
                {
                    _cached = fonts[0];
                    return _cached;
                }

                // Last resort: TMP's own global default. Guarded: its
                // getter lazily does Resources.Load<TMP_Settings>("TMP
                // Settings") and dereferences the result unconditionally --
                // confirmed by decompiling Unity.TextMeshPro.dll -- so it
                // NREs outright if Valheim ships no "TMP Settings" resource
                // asset, which is plausible for a non-editor build.
                try
                {
                    var defaultFont = TMP_Settings.defaultFontAsset;
                    if (defaultFont != null)
                    {
                        _cached = defaultFont;
                        return _cached;
                    }
                }
                catch (System.Exception)
                {
                    // No "TMP Settings" resource asset in this build; fall through to the failure log below.
                }

                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    DrawerPlugin.Log.LogError(
                        "No TMP font asset found anywhere (checked Jotunn.GUIManager.TMP_AveriaSansLibre, "
                        + "every live TMP_Text, Resources.FindObjectsOfTypeAll<TMP_FontAsset>, and "
                        + "TMP_Settings.defaultFontAsset). Drawer count labels will not render any text "
                        + "until this resolves. This mod ships no font of its own by design.");
                }
                return null;
            }
        }
    }
}
