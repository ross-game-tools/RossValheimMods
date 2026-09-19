using System;
using RossQoL.Core.Death;
using TMPro;
using UnityEngine;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Drives the grave marker every frame: projects the selected grave onto
    /// the screen and either sits over it or slides to the screen edge and
    /// points, refreshes its distance text a few times a second, and reads
    /// its own two key bindings directly. Drawing and the keys are all it
    /// does: noticing that a grave has been looted belongs to the category,
    /// in <see cref="GraveCleanupWatcher"/>, because a grave must stop being
    /// remembered whether or not this marker is switched on.
    ///
    /// Reading the keys here rather than from a Harmony patch on ZInput is
    /// deliberate: ZInput's small methods are inlined by Mono, so a patch on
    /// them is silently bypassed. Update always runs, so this is the one
    /// place that reliably sees every key press.
    /// </summary>
    internal sealed class GraveMarkerDisplay : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;

        private TMP_Text _glyph;
        private TMP_Text _distance;
        private TMP_Text _hint;
        private float _nextRefresh;

        /// <summary>Time.unscaledTime the clear key started being held; negative while not held.</summary>
        private float _clearHoldStart = -1f;

        /// <summary>
        /// The selected grave, reparsed from the stored string only once
        /// every RefreshSeconds -- <see cref="DeathState.Selected"/> walks
        /// and reparses the whole stored grave list on every call, which is
        /// too expensive to do every frame for a marker that may not even
        /// be visible.
        /// </summary>
        private GraveRecord? _cachedGrave;

        private void Awake()
        {
            // Transform's overloaded `==` is what actually detects a
            // destroyed Unity object; `?.` on a UnityEngine.Object bypasses
            // it, which is exactly the pattern DeathState's Data property
            // forbids for the same reason.
            var glyphTransform = transform.Find("Glyph");
            _glyph = glyphTransform != null ? glyphTransform.GetComponent<TMP_Text>() : null;

            var distanceTransform = transform.Find("Distance");
            _distance = distanceTransform != null ? distanceTransform.GetComponent<TMP_Text>() : null;

            var hintTransform = transform.Find("Hint");
            _hint = hintTransform != null ? hintTransform.GetComponent<TMP_Text>() : null;
        }

        private void Update()
        {
            // Nothing here may throw past Update -- an exception escaping a
            // MonoBehaviour callback would stop this component ticking for
            // the rest of the session, silently freezing the marker in place.
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Death: grave marker update failed: {ex}");
            }
        }

        private void Tick()
        {
            if (_glyph == null || _distance == null || _hint == null) return;

            bool active = GraveMarkerFeature.Instance?.IsActive == true && Player.m_localPlayer != null;
            if (!active)
            {
                _cachedGrave = null;
                SetVisible(false);
                return;
            }

            bool dueForRefresh = Time.unscaledTime >= _nextRefresh;
            if (dueForRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
                var graves = DeathState.Graves();
                _cachedGrave = DeathState.Selected;
                _hint.text = BuildHintText(graves.Count);
            }

            var grave = _cachedGrave;
            if (grave == null) { SetVisible(false); return; }

            var camera = Utils.GetMainCamera();
            if (camera == null) { SetVisible(false); return; }

            var target = new Vector3(grave.Value.X, grave.Value.Y, grave.Value.Z);
            float distance = Vector3.Distance(Player.m_localPlayer.transform.position, target);

            HandleKeys(grave.Value);

            if (distance <= DeathConfig.HideDistance.Value) { SetVisible(false); return; }

            var screen = camera.WorldToScreenPointScaled(target);
            var placement = ScreenMarker.Place(
                screen.x, screen.y, screen.z, Screen.width, Screen.height, DeathConfig.EdgeMargin.Value);
            if (!placement.Visible) { SetVisible(false); return; }

            SetVisible(true);
            transform.position = new Vector3(placement.X, placement.Y, 0f);

            // 0 is straight up, increasing clockwise; unrotated is correct
            // for the on-screen case too, since AngleDegrees is always 0 then.
            _glyph.rectTransform.rotation = Quaternion.Euler(0f, 0f, -placement.AngleDegrees);
            _glyph.text = placement.OnScreen ? "▼" : "▲";

            if (dueForRefresh) _distance.text = $"{Mathf.RoundToInt(distance)}m";
        }

        /// <summary>
        /// Disabling the two TMP_Text components rather than the GameObject
        /// keeps Update running, so the marker comes straight back once the
        /// feature is switched on again or a grave is (re)selected -- the
        /// same reason ClockDisplay does it.
        /// </summary>
        private void SetVisible(bool visible)
        {
            if (_glyph.enabled != visible) _glyph.enabled = visible;
            if (_distance.enabled != visible) _distance.enabled = visible;
            if (_hint.enabled != visible) _hint.enabled = visible;
        }

        /// <summary>
        /// Builds the key-hint line from the configured <see cref="KeyboardShortcut"/>
        /// values, not a hardcoded key name, so a rebind is reflected. The
        /// dismiss hint says HOLD, since <see cref="DeathConfig.ClearGraveKey"/>
        /// is a hold, not a tap, and is hidden along with the whole marker
        /// if that key has been unbound. The cycle hint is added only when
        /// <see cref="DeathConfig.CycleGraveKey"/> is actually bound -- its
        /// default is None, and an unbound key must never be advertised --
        /// and only when there is more than one grave to cycle between.
        /// </summary>
        private static string BuildHintText(int graveCount)
        {
            var clearKey = DeathConfig.ClearGraveKey?.Value;
            if (clearKey == null || clearKey.Value.MainKey == KeyCode.None) return string.Empty;

            string hint = $"Hold {clearKey.Value} to dismiss";

            var cycleKey = DeathConfig.CycleGraveKey?.Value;
            if (cycleKey != null && cycleKey.Value.MainKey != KeyCode.None && graveCount > 1)
                hint += $"  ·  {cycleKey.Value} to cycle";

            return hint;
        }

        private void HandleKeys(GraveRecord grave)
        {
            // Ignore both keys while the player is typing, so Delete in
            // chat or the console does not also clear a grave.
            if (HasInputFocus()) { _clearHoldStart = -1f; return; }

            var cycleKey = DeathConfig.CycleGraveKey?.Value;
            if (cycleKey != null && cycleKey.Value.MainKey != KeyCode.None
                && ZInput.GetKeyDown(cycleKey.Value.MainKey))
            {
                DeathState.SelectNext();
            }

            var clearKey = DeathConfig.ClearGraveKey?.Value;
            if (clearKey == null || clearKey.Value.MainKey == KeyCode.None
                || !ZInput.GetKey(clearKey.Value.MainKey))
            {
                _clearHoldStart = -1f;
                return;
            }

            if (_clearHoldStart < 0f) _clearHoldStart = Time.unscaledTime;
            float holdSeconds = DeathConfig.ClearGraveHoldSeconds?.Value ?? 1.5f;
            if (Time.unscaledTime - _clearHoldStart >= holdSeconds)
            {
                DeathState.Forget(grave);
                _clearHoldStart = -1f;
            }
        }

        /// <summary>
        /// System.Console and Valheim's own (global-namespace) Console
        /// would otherwise collide under `using System;`; qualifying with
        /// `global::` picks the game's.
        /// </summary>
        private static bool HasInputFocus() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || global::Console.IsVisible();
    }
}
