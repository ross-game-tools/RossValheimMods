using System;
using System.Collections.Generic;
using RossQoL.Core.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RossQoL.Game.Interface
{
    /// <summary>
    /// Draws the stacking top-left notification list.
    ///
    /// Vanilla has exactly one message text and one message icon and shows
    /// queued messages through them one after another, so a second message
    /// wipes the first off the screen. This clones that pair into five rows
    /// -- so the styling is vanilla's own, whatever a font or UI mod has done
    /// to it -- and drives them from <see cref="NotificationList"/>, which
    /// decides what each line says.
    ///
    /// Each row is an icon with its text beside it rather than above it, so a
    /// row is exactly one line of the game's own font tall and five of them
    /// take a fifth of the screen they otherwise would.
    ///
    /// Time is read unscaled throughout, so a line keeps fading while the
    /// game is paused, exactly as vanilla's <c>CrossFadeAlpha</c> does with
    /// <c>ignoreTimeScale: true</c>.
    /// </summary>
    internal sealed class NotificationHud : MonoBehaviour
    {
        private const string ContainerName = "RossQoL_Notifications";
        private const string RowName = "RossQoL_NotificationRow";
        private const string TextName = "Text";
        private const string IconName = "Icon";

        /// <summary>Breathing room between one row and the next.</summary>
        private const float RowGap = 2f;

        /// <summary>Between the icon and the start of the text beside it.</summary>
        private const float IconTextGap = 4f;

        /// <summary>
        /// How wide a row is allowed to be if vanilla's own message area
        /// measures narrower than this -- wide enough for any item name the
        /// game has, so the ellipsis is a safety net rather than a habit.
        /// </summary>
        private const float MinimumRowWidth = 260f;

        /// <summary>Only used if TMP cannot measure a line of its own font.</summary>
        private const float FallbackLineHeightScale = 1.2f;

        private static readonly Vector3[] CornerBuffer = new Vector3[4];

        private static NotificationHud _instance;

        /// <summary>
        /// True only when there is a list on screen to draw into. Nothing may
        /// suppress vanilla's own messages while this is false, or the player
        /// would simply stop being told anything.
        /// </summary>
        internal static bool Ready => _instance != null && _instance._rows != null;

        private readonly NotificationList _list = new NotificationList();
        private readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();
        private readonly List<string> _stale = new List<string>();

        private Row[] _rows;

        private sealed class Row
        {
            public RectTransform Root;
            public TMP_Text Text;
            public Image Icon;
            public Color IconColor;
        }

        /// <summary>
        /// Builds the list under the message HUD, once. Returns false when
        /// vanilla's own message text or icon is not there to clone -- in
        /// which case nothing is suppressed and vanilla keeps its messages.
        /// </summary>
        internal static bool Create(MessageHud hud)
        {
            if (hud == null) return false;
            if (_instance != null && _instance.gameObject == hud.gameObject) return true;

            var textDonor = hud.m_messageText;
            var iconDonor = hud.m_messageIcon;
            if (!textDonor || !iconDonor)
            {
                RossQoLPlugin.Log.LogWarning(
                    "Notifications: MessageHud has no message text or icon to copy, so vanilla's own "
                    + "top-left messages are left exactly as they are.");
                return false;
            }

            var hudComponent = hud.gameObject.AddComponent<NotificationHud>();
            if (!hudComponent.Build(textDonor, iconDonor))
            {
                Object.Destroy(hudComponent);
                return false;
            }

            _instance = hudComponent;
            RossQoLPlugin.Log.LogInfo("Notifications: top-left notification list added.");
            return true;
        }

        /// <summary>
        /// Puts an arrival on the list: a repeat of a key already there
        /// updates that line in place, anything else starts a new one.
        /// </summary>
        internal static void Push(string key, string label, NotificationStyle style, float amount, Sprite icon)
        {
            if (!Ready || key == null) return;

            float now = Time.unscaledTime;
            _instance._list.Prune(now);
            _instance._list.Add(key, label, style, amount, now);
            _instance._icons[key] = icon;
        }

        /// <summary>
        /// Puts a skill gain on the list: what the game added and the share of
        /// the next level it works out to, both growing on the skill's own
        /// line while the player keeps earning.
        /// </summary>
        internal static void PushProgress(string key, string label, float gain, float percent, Sprite icon)
        {
            if (!Ready || key == null) return;

            float now = Time.unscaledTime;
            _instance._list.Prune(now);
            _instance._list.AddProgress(key, label, gain, percent, now);
            _instance._icons[key] = icon;
        }

        /// <summary>Takes a line off early, e.g. a skill's progress once it has levelled.</summary>
        internal static void Drop(string key)
        {
            if (!Ready || key == null) return;

            _instance._list.Remove(key);
            _instance._icons.Remove(key);
        }

        /// <summary>
        /// Lays the list out: one container of its own, holding a row per
        /// line, each row an icon with its text beside it. The container is
        /// dropped exactly where vanilla's message sits -- measured from the
        /// donors' own corners rather than assumed -- and every anchor, pivot
        /// and size below it is set explicitly, because a clone inherits
        /// whatever the donor had and a stretched donor collapses to nothing
        /// under a differently shaped parent.
        /// </summary>
        private bool Build(TMP_Text textDonor, Image iconDonor)
        {
            var textRect = textDonor.rectTransform;
            var iconRect = iconDonor.rectTransform;
            if (!textRect || !iconRect || !textRect.parent)
            {
                RossQoLPlugin.Log.LogWarning(
                    "Notifications: vanilla's message text and icon have no parent to sit under, so "
                    + "vanilla's own top-left messages are left exactly as they are.");
                return false;
            }

            // Anything under a layout group has its position overwritten every
            // frame, so the list hangs off the nearest ancestor that is not
            // driven by one. The LayoutElement below is the belt to that
            // braces: it tells any group we did not spot to leave us alone.
            var host = FreeParent(textRect.parent);
            if (!host)
            {
                RossQoLPlugin.Log.LogWarning(
                    "Notifications: found nowhere free of a layout group to put the list, so vanilla's "
                    + "own top-left messages are left exactly as they are.");
                return false;
            }

            Vector3 min, max;
            if (!WorldBounds(textRect, iconRect, out min, out max))
            {
                RossQoLPlugin.Log.LogWarning(
                    "Notifications: vanilla's message area has no size to measure, so vanilla's own "
                    + "top-left messages are left exactly as they are.");
                return false;
            }

            var topLeft = new Vector3(min.x, max.y, (min.z + max.z) * 0.5f);
            float width = Mathf.Max(LocalWidth(host, min.x, max.x, max.y, topLeft.z), MinimumRowWidth);

            var container = new GameObject(ContainerName, typeof(RectTransform));
            try
            {
                var containerRect = (RectTransform)container.transform;
                containerRect.SetParent(host, false);
                containerRect.anchorMin = containerRect.anchorMax = new Vector2(0f, 1f);
                containerRect.pivot = new Vector2(0f, 1f);
                containerRect.sizeDelta = new Vector2(width, 0f);
                containerRect.localScale = Vector3.one;
                containerRect.position = topLeft;
                container.AddComponent<LayoutElement>().ignoreLayout = true;

                var rows = new Row[_list.Capacity];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = BuildRow(textDonor, iconDonor, containerRect, i);

                // The real height of one line of this font at this size,
                // measured by TMP itself rather than guessed from fontSize.
                // "Ag" spans an ascender and a descender, so nothing a row can
                // hold is taller than what this reports.
                float lineHeight = rows[0].Text.GetPreferredValues("Ag").y;
                if (lineHeight <= 0f) lineHeight = rows[0].Text.fontSize * FallbackLineHeightScale;

                for (int i = 0; i < rows.Length; i++) Place(rows[i], width, lineHeight, i);

                _rows = rows;
                return true;
            }
            catch
            {
                Object.Destroy(container);
                throw;
            }
        }

        /// <summary>
        /// One row: an empty rect holding a cloned icon and a cloned text.
        /// Both clones get the treatment every cloned Valheim element in this
        /// mod gets -- the Animator stripped by type name, since the project
        /// does not reference UnityEngine.AnimationModule, and every Localize
        /// removed so nothing overwrites what we write. The canvas renderer is
        /// forced back to full alpha because MessageHud.Start faded the donors
        /// to nothing moments ago and a clone inherits that.
        /// </summary>
        private static Row BuildRow(TMP_Text textDonor, Image iconDonor, RectTransform parent, int index)
        {
            var row = new GameObject(RowName + index, typeof(RectTransform));
            row.transform.SetParent(parent, false);

            var iconClone = Object.Instantiate(iconDonor.gameObject, row.transform, false);
            iconClone.name = IconName;
            Strip(iconClone);
            var icon = iconClone.GetComponent<Image>();
            icon.canvasRenderer.SetAlpha(1f);
            // A square icon scaled to the line: the row stays one line tall
            // however the sprite itself is shaped.
            icon.preserveAspect = true;
            icon.enabled = false;
            iconClone.SetActive(true);

            var textClone = Object.Instantiate(textDonor.gameObject, row.transform, false);
            textClone.name = TextName;
            Strip(textClone);
            var text = textClone.GetComponent<TMP_Text>();
            text.text = string.Empty;
            text.canvasRenderer.SetAlpha(1f);
            text.textWrappingMode = TextWrappingModes.NoWrap;

            // A name longer than the row is cut short with an ellipsis rather
            // than wrapping under the icon or running off the screen edge. No
            // item Valheim ships comes close to the row width, so this is a
            // safety net for a modded name, not everyday behaviour.
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Left;
            text.enabled = false;
            textClone.SetActive(true);

            return new Row { Root = (RectTransform)row.transform, Text = text, Icon = icon, IconColor = icon.color };
        }

        /// <summary>
        /// Puts a row, its icon and its text where they belong: rows stacked
        /// downward a measured line apart, the icon at the left edge, the text
        /// filling what is left of the row beside it, the two centred against
        /// each other so the row reads as one line.
        /// </summary>
        private static void Place(Row row, float width, float lineHeight, int index)
        {
            var rowRect = row.Root;
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.sizeDelta = new Vector2(width, lineHeight);
            rowRect.anchoredPosition = new Vector2(0f, -(lineHeight + RowGap) * index);
            rowRect.localScale = Vector3.one;

            var iconRect = row.Icon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.sizeDelta = new Vector2(lineHeight, lineHeight);
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.localScale = Vector3.one;

            var textRect = row.Text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0f, 0.5f);
            textRect.offsetMin = new Vector2(lineHeight + IconTextGap, 0f);
            textRect.offsetMax = Vector2.zero;
            textRect.localScale = Vector3.one;
        }

        /// <summary>
        /// The nearest transform from here upward that no layout group is
        /// driving, so an anchored position set on a child of it stays put.
        /// </summary>
        private static Transform FreeParent(Transform start)
        {
            for (var t = start; t != null; t = t.parent)
                if (t.GetComponent<LayoutGroup>() == null)
                    return t;

            return null;
        }

        /// <summary>
        /// The world-space box the two donors occupy between them: where
        /// vanilla's top-left message actually is, measured rather than
        /// inferred from a hierarchy this mod cannot see into.
        /// </summary>
        private static bool WorldBounds(RectTransform a, RectTransform b, out Vector3 min, out Vector3 max)
        {
            min = Vector3.zero;
            max = Vector3.zero;
            bool any = false;

            for (int r = 0; r < 2; r++)
            {
                var rect = r == 0 ? a : b;
                if (!rect) continue;

                rect.GetWorldCorners(CornerBuffer);
                for (int i = 0; i < CornerBuffer.Length; i++)
                {
                    if (!any)
                    {
                        min = max = CornerBuffer[i];
                        any = true;
                        continue;
                    }

                    min = Vector3.Min(min, CornerBuffer[i]);
                    max = Vector3.Max(max, CornerBuffer[i]);
                }
            }

            return any && max.x > min.x;
        }

        /// <summary>
        /// That box's width expressed in the host's own units, which is what
        /// a sizeDelta is measured in -- world units would be wrong at any
        /// canvas scale but one.
        /// </summary>
        private static float LocalWidth(Transform host, float minX, float maxX, float y, float z)
        {
            Vector3 left = host.InverseTransformPoint(new Vector3(minX, y, z));
            Vector3 right = host.InverseTransformPoint(new Vector3(maxX, y, z));
            return Mathf.Abs(right.x - left.x);
        }

        private static void Strip(GameObject clone)
        {
            // Matched by name: Animator lives in UnityEngine.AnimationModule,
            // which this project does not otherwise need to reference.
            foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true))
                if (behaviour.GetType().Name == "Animator")
                    Object.DestroyImmediate(behaviour);
            foreach (var localize in clone.GetComponentsInChildren<Localize>(true))
                Object.DestroyImmediate(localize);
        }

        private void Update()
        {
            // An exception here would run every frame and take the HUD with it.
            try
            {
                Draw();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Notifications: could not draw the list: {ex}");
                enabled = false;
            }
        }

        private void Draw()
        {
            if (_rows == null) return;

            bool active = NotificationsFeature.Instance != null && NotificationsFeature.Instance.IsActive;
            if (!active)
            {
                // Switched off mid-session: vanilla's messages are no longer
                // suppressed, so anything still held here would sit on top of
                // them until it faded.
                _list.Clear();
                _icons.Clear();
                HideAll();
                return;
            }

            // Whatever hides the rest of the HUD hides this too.
            if (Hud.IsUserHidden())
            {
                HideAll();
                return;
            }

            float now = Time.unscaledTime;
            _list.Prune(now);
            ForgetIconsOfGoneLines();

            var entries = _list.Entries;
            for (int i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                if (i >= entries.Count)
                {
                    Hide(row);
                    continue;
                }

                var entry = entries[i];
                float alpha = _list.AlphaOf(entry, now);

                // Disabling the component rather than the GameObject keeps the
                // row in place and ready for the next line to land on it.
                if (!row.Text.enabled) row.Text.enabled = true;
                row.Text.text = entry.Text;
                row.Text.alpha = alpha;

                Sprite sprite;
                _icons.TryGetValue(entry.Key, out sprite);
                if (sprite != null)
                {
                    if (!row.Icon.enabled) row.Icon.enabled = true;
                    row.Icon.sprite = sprite;
                    var c = row.IconColor;
                    row.Icon.color = new Color(c.r, c.g, c.b, c.a * alpha);
                }
                else if (row.Icon.enabled)
                {
                    row.Icon.enabled = false;
                }
            }
        }

        private void ForgetIconsOfGoneLines()
        {
            if (_icons.Count == _list.Count) return;

            _stale.Clear();
            foreach (var key in _icons.Keys)
                if (_list.Find(key) == null)
                    _stale.Add(key);

            for (int i = 0; i < _stale.Count; i++) _icons.Remove(_stale[i]);
            _stale.Clear();
        }

        private void HideAll()
        {
            for (int i = 0; i < _rows.Length; i++) Hide(_rows[i]);
        }

        private static void Hide(Row row)
        {
            if (row.Text.enabled) row.Text.enabled = false;
            if (row.Icon.enabled) row.Icon.enabled = false;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
