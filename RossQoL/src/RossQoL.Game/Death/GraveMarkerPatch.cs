using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Creates the grave marker once the HUD exists: two clones of the
    /// minimap's own small biome-name text (the same donor
    /// <see cref="ClockMinimapPatch"/> uses), parented under
    /// <c>EnemyHud.m_hudRoot</c> so vanilla's own hide/show
    /// (<c>Hud.IsUserHidden()</c>) applies for free.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "Awake")]
    internal static class GraveMarkerPatch
    {
        private const string ObjectName = "RossQoL_GraveMarker";
        private const float OutlineWidth = 0.18f;
        private const float FaceDilate = 0.1f;
        private const float GlyphFontScale = 2.2f;

        /// <summary>
        /// The key-hint line reads as a whisper under the distance, not a
        /// second HUD element competing with it: smaller than the donor
        /// text and noticeably dimmer than even the distance line.
        /// </summary>
        private const float HintFontScale = 0.75f;

        private static readonly Color32 OutlineColor = new Color32(0, 0, 0, 150);
        private static readonly Color GlyphColor = new Color(1f, 0.92f, 0.55f, 0.95f);
        private static readonly Color DistanceColor = new Color(0.93f, 0.89f, 0.80f, 0.85f);
        private static readonly Color HintColor = new Color(0.93f, 0.89f, 0.80f, 0.45f);

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Hud), "Awake", GraveMarkerFeature.FeatureName);

        private static void Postfix(Hud __instance)
        {
            // An exception escaping Hud.Awake breaks the HUD entirely.
            try
            {
                if (TryCreateMarker()) return;

                // EnemyHud is not ready yet. Unity gives no Awake ordering
                // guarantee between Hud and EnemyHud, so this is a real
                // race, not a theoretical one -- retry from a bounded
                // component rather than giving up for the whole session.
                if (__instance != null) __instance.gameObject.AddComponent<GraveMarkerReadyRetry>();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Death: could not add the grave marker: {ex}");
            }
        }

        /// <summary>
        /// Tries once to create the marker. Returns true when the outcome
        /// is final -- the marker was added, or a specific missing part
        /// means it never will be this session -- and false only when
        /// EnemyHud itself has simply not Awoken yet, the one case worth
        /// retrying.
        /// </summary>
        internal static bool TryCreateMarker()
        {
            var enemyHud = EnemyHud.instance;
            if (enemyHud == null) return false; // not ready yet; the caller retries

            var hudRoot = enemyHud.m_hudRoot;
            if (!hudRoot)
            {
                RossQoLPlugin.Log.LogWarning(
                    "Death: EnemyHud.m_hudRoot is missing; grave marker will not be added this session.");
                return true;
            }

            // Minimap.Start runs before Hud.Awake in practice, and by the
            // time EnemyHud is ready this postfix (or its retry) is
            // certainly past that point too -- so a still-missing minimap
            // here means this world genuinely has no map, not a race.
            var minimap = Minimap.instance;
            if (minimap == null)
            {
                RossQoLPlugin.Log.LogInfo("Death: this world has no minimap; grave marker will not be shown.");
                return true;
            }

            var template = minimap.m_biomeNameSmall;
            if (!template)
            {
                RossQoLPlugin.Log.LogWarning(
                    "Death: Minimap.m_biomeNameSmall is missing; grave marker will not be added this session.");
                return true;
            }

            BuildMarker(hudRoot.transform, template);
            return true;
        }

        private static void BuildMarker(Transform parent, TMP_Text template)
        {
            if (parent.Find(ObjectName)) return;

            var root = new GameObject(ObjectName, typeof(RectTransform));
            try
            {
                root.transform.SetParent(parent, false);
                var rootRect = (RectTransform)root.transform;
                rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
                rootRect.pivot = new Vector2(0.5f, 0.5f);
                rootRect.sizeDelta = Vector2.zero;
                rootRect.localScale = Vector3.one;

                Vector2 donorSize = ((RectTransform)template.transform).rect.size;

                var glyph = CloneDonorText(template, root.transform, "Glyph", donorSize);
                glyph.alignment = TextAlignmentOptions.Center;
                glyph.fontSize *= GlyphFontScale;
                glyph.color = GlyphColor;
                ((RectTransform)glyph.transform).anchoredPosition = Vector2.zero;

                var distance = CloneDonorText(template, root.transform, "Distance", donorSize);
                distance.alignment = TextAlignmentOptions.Top;
                distance.color = DistanceColor;
                float gap = Mathf.Max(donorSize.y, glyph.fontSize);
                ((RectTransform)distance.transform).anchoredPosition = new Vector2(0f, -gap);

                var hint = CloneDonorText(template, root.transform, "Hint", donorSize);
                hint.alignment = TextAlignmentOptions.Top;
                hint.fontSize *= HintFontScale;
                hint.color = HintColor;
                ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, -(gap + donorSize.y));

                root.SetActive(true);
                root.AddComponent<GraveMarkerDisplay>();
                RossQoLPlugin.Log.LogInfo("Death: grave marker added.");
            }
            catch
            {
                Object.Destroy(root);
                throw;
            }
        }

        /// <summary>
        /// Clones the donor's GameObject and gives it the clock's own
        /// treatment: the Animator is stripped by type name, since this
        /// project does not reference UnityEngine.AnimationModule, every
        /// Localize is removed so the text is never overwritten by the
        /// localization system, and the outline/dilate combination keeps
        /// the face readable over any background.
        /// </summary>
        private static TMP_Text CloneDonorText(TMP_Text template, Transform parent, string name, Vector2 size)
        {
            var clone = Object.Instantiate(template.gameObject, parent, false);
            clone.name = name;

            foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true))
                if (behaviour.GetType().Name == "Animator")
                    Object.DestroyImmediate(behaviour);
            foreach (var localize in clone.GetComponentsInChildren<Localize>(true))
                Object.DestroyImmediate(localize);

            var text = clone.GetComponent<TMP_Text>();
            text.text = string.Empty;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            // Setting the outline gives this text its own material instance,
            // so the biome name it was cloned from keeps its look.
            text.outlineWidth = OutlineWidth;
            text.outlineColor = OutlineColor;
            text.fontStyle |= FontStyles.Bold;
            text.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, FaceDilate);

            // Normalise anchors, pivot and size explicitly rather than
            // trusting whatever the donor had: a stretch-anchored donor
            // collapses to zero size under this zero-sizeDelta parent, and
            // an inherited off-centre pivot would make the rotated glyph
            // swing in an arc instead of spinning in place.
            var rect = (RectTransform)clone.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.anchoredPosition = Vector2.zero;

            clone.SetActive(true);
            return text;
        }
    }

    /// <summary>
    /// Keeps retrying <see cref="GraveMarkerPatch.TryCreateMarker"/> for a
    /// bounded time when EnemyHud has not Awoken yet, rather than the
    /// marker simply never existing for the rest of the session. Attached
    /// to the Hud's own GameObject, which outlives it either way.
    /// </summary>
    internal sealed class GraveMarkerReadyRetry : MonoBehaviour
    {
        private const float RetryInterval = 0.5f;
        private const float MaxWaitSeconds = 10f;

        private float _elapsed;
        private float _nextAttempt;

        private void Update()
        {
            try
            {
                if (Time.unscaledTime < _nextAttempt) return;
                _nextAttempt = Time.unscaledTime + RetryInterval;
                _elapsed += RetryInterval;

                if (GraveMarkerPatch.TryCreateMarker())
                {
                    Destroy(this);
                    return;
                }

                if (_elapsed >= MaxWaitSeconds)
                {
                    RossQoLPlugin.Log.LogWarning(
                        "Death: EnemyHud never became ready; grave marker will not appear this session.");
                    Destroy(this);
                }
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Death: grave marker retry failed: {ex}");
                Destroy(this);
            }
        }
    }
}
