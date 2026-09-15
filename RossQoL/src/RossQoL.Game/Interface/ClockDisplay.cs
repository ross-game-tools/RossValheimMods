using System;
using HarmonyLib;
using RossQoL.Core.Interface;
using RossQoL.Game.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RossQoL.Game.Interface
{
    /// <summary>
    /// Adds the clock text under the small minimap once the minimap exists.
    /// A child of Minimap.m_smallRoot, so it hides whenever the minimap does:
    /// large map open, HUD hidden, or a world without a map.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), "Start")]
    internal static class ClockMinimapPatch
    {
        private const string ObjectName = "RossQoL_Clock";
        private const float Gap = 2f;
        private const float OutlineWidth = 0.18f;
        private const float FaceDilate = 0.1f;
        private static readonly Color32 OutlineColor = new Color32(0, 0, 0, 150);

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Minimap), "Start", ClockFeature.FeatureName);

        private static void Postfix(Minimap __instance)
        {
            // An exception escaping Minimap.Start would leave the minimap broken.
            try
            {
                AddClock(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Clock: could not add the clock: {ex}");
            }
        }

        private static void AddClock(Minimap minimap)
        {
            var root = minimap.m_smallRoot ? minimap.m_smallRoot.transform as RectTransform : null;
            var template = minimap.m_biomeNameSmall;
            if (!root || !template)
            {
                RossQoLPlugin.Log.LogWarning("Clock: minimap parts not found; clock not added.");
                return;
            }
            if (root.Find(ObjectName)) return;

            var clone = Object.Instantiate(template.gameObject, root, false);
            try
            {
                clone.name = ObjectName;

                // The biome name pulses when it changes; the clock must not.
                // Matched by name: Animator lives in UnityEngine.AnimationModule,
                // which this project does not otherwise need to reference.
                foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true))
                    if (behaviour.GetType().Name == "Animator")
                        Object.DestroyImmediate(behaviour);
                foreach (var localize in clone.GetComponentsInChildren<Localize>(true))
                    Object.DestroyImmediate(localize);

                var text = clone.GetComponent<TMP_Text>();
                text.text = string.Empty;
                text.alignment = TextAlignmentOptions.Top;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                // A soft, warm off-white at 70% opacity: readable, but quieter
                // than the biome name above it.
                text.color = new Color(0.93f, 0.89f, 0.80f, 0.7f);

                // A thin, semi-transparent dark outline so the text still
                // reads over snow, sand and bright sky under the minimap.
                // Setting the outline gives this text its own material
                // instance, so the biome name it was cloned from keeps its look.
                text.outlineWidth = OutlineWidth;
                text.outlineColor = OutlineColor;

                // A TMP outline grows inward as well as outward and thins the
                // letters; bold plus a slight dilate gives the face its weight back.
                text.fontStyle |= FontStyles.Bold;
                text.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, FaceDilate);

                // Centred directly below the minimap frame, as wide as the map.
                float height = Mathf.Max(((RectTransform)template.transform).rect.height, text.fontSize * 1.4f);
                var rect = (RectTransform)clone.transform;
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(root.rect.width, height);
                rect.anchoredPosition = new Vector2(0f, -Gap);
                rect.localScale = Vector3.one;

                clone.SetActive(true);
                clone.AddComponent<ClockDisplay>();
                RossQoLPlugin.Log.LogInfo("Clock: added under the minimap.");
            }
            catch
            {
                Object.Destroy(clone);
                throw;
            }
        }
    }

    internal sealed class ClockDisplay : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;

        private TMP_Text _text;
        private float _nextRefresh;

        private void Awake() => _text = GetComponent<TMP_Text>();

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            // Disabling the text rather than the object keeps Update running,
            // so switching the feature back on shows the clock again.
            var env = EnvMan.instance;
            bool show = ClockFeature.Instance?.IsActive == true && env && Player.m_localPlayer;
            if (_text.enabled != show) _text.enabled = show;
            if (!show) return;

            string day = Localization.instance.Localize("$msg_newday", env.GetCurrentDay().ToString());
            bool use24Hour = ClockConfig.Use24Hour?.Value ?? true;
            _text.text = ClockText.Format(day, env.GetDayFraction(), use24Hour);
        }
    }
}
