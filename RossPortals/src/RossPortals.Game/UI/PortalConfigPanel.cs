using System.Collections.Generic;
using Jotunn.Managers;
using RossPortals.Core;
using RossPortals.Game.Portals;
using UnityEngine;
using UnityEngine.UI;

namespace RossPortals.Game.UI
{
    /// <summary>
    /// The configuration panel shown when a player interacts with a portal. It
    /// replaces XPortal's single flat dropdown with a searchable, foldered,
    /// sortable list — the point of the whole mod. All the list logic lives in
    /// <see cref="PortalListView"/> (Core, unit-tested); this class is only the
    /// Unity plumbing: build the widgets once, and on every change rebuild the
    /// scroll contents from the rows Core hands back.
    ///
    /// Not a MonoBehaviour — it owns a GameObject tree under Jotunn's custom GUI
    /// root and is driven by the plugin's Update and by registry sync events.
    /// </summary>
    internal sealed class PortalConfigPanel
    {
        public static PortalConfigPanel Instance { get; private set; }

        // Panel geometry. Deliberately generous; a late-game world has a lot of
        // portals and the list wants room. Easy to tune in-game.
        private const float PanelWidth = 640f;
        private const float PanelHeight = 600f;
        private const float RowHeight = 30f;

        private GameObject _panel;
        private InputField _nameField;
        private InputField _searchField;
        private Text _destinationText;
        private Toggle _defaultToggle;
        private Toggle _showMapToggle;
        private RectTransform _content;
        private Font _font;
        private readonly Dictionary<SortMode, Text> _sortLabels = new Dictionary<SortMode, Text>();

        private bool _built;

        // Per-open state.
        private PortalRecord _portal;
        private string _selectedKey;      // Core id of the chosen destination, or null for none
        private SortMode _sort = SortMode.Name;
        private readonly HashSet<string> _collapsed = new HashSet<string>();

        public PortalConfigPanel() => Instance = this;

        public bool IsOpen => _panel != null && _panel.activeSelf;

        public void Open(PortalRecord portal)
        {
            if (Env.IsHeadless) return;
            if (!EnsureBuilt()) return;
            // Ask the server for the full, current portal list every time the
            // panel opens. The initial join-time sync can miss (or race), which
            // otherwise leaves the list showing only portals this client has
            // loaded nearby. Result arrives async and triggers OnRegistryChanged.
            PortalManager.RefreshList();

            _portal = portal;
            _selectedKey = portal.HasTarget ? PortalKey.Of(portal.Target) : null;
            _nameField.text = portal.Name ?? string.Empty;
            _searchField.text = string.Empty;
            _defaultToggle.isOn = portal.IsDefault;
            _showMapToggle.isOn = portal.ShowOnMap;

            _panel.SetActive(true);
            GUIManager.BlockInput(true);
            Rebuild();

            // Focus only lands a frame after the field is enabled.
            Scheduler.Instance?.NextFrame(() => _searchField.ActivateInputField());
        }

        public void Close()
        {
            if (_panel != null) _panel.SetActive(false);
            GUIManager.BlockInput(false);
            _portal = null;
        }

        public void HandleInput()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        /// <summary>A sync changed the list while we're open — re-fetch our
        /// portal (its record may have been replaced) and redraw.</summary>
        public void OnRegistryChanged()
        {
            if (!IsOpen || _portal == null) return;
            _portal = PortalRegistry.Instance.GetById(_portal.Id) ?? _portal;
            Rebuild();
        }

        public void Dispose()
        {
            if (_panel != null) Object.Destroy(_panel);
            _panel = null;
            _built = false;
            if (Instance == this) Instance = null;
        }

        // --- Build (once) ---

        private bool EnsureBuilt()
        {
            if (_built) return true;
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
            {
                RossPortalsPlugin.Log.LogWarning("GUI not ready; cannot open the portal panel yet.");
                return false;
            }

            var font = GUIManager.Instance.AveriaSerifBold;
            _font = font;
            var parent = GUIManager.CustomGUIFront.transform;
            var mid = new Vector2(0.5f, 0.5f);
            var top = new Vector2(0.5f, 1f);

            _panel = GUIManager.Instance.CreateWoodpanel(parent, mid, mid, Vector2.zero, PanelWidth, PanelHeight, false);
            _panel.SetActive(false);

            GUIManager.Instance.CreateText("Configure Portal", _panel.transform, top, top,
                new Vector2(0f, -28f), font, 24, Color.white, true, Color.black, 600f, 34f, false);

            _nameField = GUIManager.Instance
                .CreateInputField(_panel.transform, top, top, new Vector2(0f, -74f),
                    InputField.ContentType.Standard, "Portal name", 18, 580f, 36f)
                .GetComponent<InputField>();

            _searchField = GUIManager.Instance
                .CreateInputField(_panel.transform, top, top, new Vector2(0f, -118f),
                    InputField.ContentType.Standard, "Search portals...", 18, 580f, 34f)
                .GetComponent<InputField>();
            _searchField.onValueChanged.AddListener(_ => Rebuild());

            CreateSortButton(SortMode.Name, "Name", -160f, font);
            CreateSortButton(SortMode.Nearest, "Nearest", 0f, font);
            CreateSortButton(SortMode.Recent, "Recent", 160f, font);

            _destinationText = GUIManager.Instance.CreateText(string.Empty, _panel.transform, top, top,
                new Vector2(-110f, -198f), font, 18, Color.white, true, Color.black, 360f, 30f, false)
                .GetComponent<Text>();
            _destinationText.alignment = TextAnchor.MiddleLeft;

            var clear = GUIManager.Instance.CreateButton("Clear", _panel.transform, top, top,
                new Vector2(210f, -198f), 110f, 30f);
            clear.GetComponent<Button>().onClick.AddListener(() => { _selectedKey = null; Rebuild(); });

            var scroll = GUIManager.Instance.CreateScrollView(
                _panel.transform, false, true, 12f, 6f, HandleColors(),
                new Color(0f, 0f, 0f, 0.6f), 590f, 300f);
            ((RectTransform)scroll.transform).anchoredPosition = new Vector2(0f, -85f);
            var scrollRect = scroll.GetComponentInChildren<ScrollRect>();
            _content = scrollRect.content;
            // Default sensitivity (~35) crawls through a long portal list.
            scrollRect.scrollSensitivity = 400f;

            // CreateScrollView only turns on childControlWidth when it draws a
            // horizontal scrollbar, so otherwise rows keep their own (too small)
            // width and don't fill the list. Drive width from the layout, and
            // give the rows a little spacing and inset.
            var listLayout = _content.GetComponent<VerticalLayoutGroup>();
            listLayout.childControlWidth = true;
            listLayout.childForceExpandWidth = true;
            listLayout.spacing = 2f;
            listLayout.padding = new RectOffset(6, 6, 6, 6);

            var ok = GUIManager.Instance.CreateButton("OK", _panel.transform, top, top,
                new Vector2(225f, -566f), 130f, 40f);
            ok.GetComponent<Button>().onClick.AddListener(Submit);

            var cancel = GUIManager.Instance.CreateButton("Cancel", _panel.transform, top, top,
                new Vector2(81f, -566f), 130f, 40f);
            cancel.GetComponent<Button>().onClick.AddListener(Close);

            // Two per-portal flags, bottom-left, side by side. Default starts off
            // (only one portal is the default at a time); Show on map starts on.
            _defaultToggle = CreateCheckbox("Default", -278f, -566f, 70f);
            _showMapToggle = CreateCheckbox("Show on map", -150f, -566f, 130f);

            _built = true;
            return true;
        }

        private Toggle CreateCheckbox(string label, float x, float y, float labelWidth)
        {
            var top = new Vector2(0.5f, 1f);
            var go = GUIManager.Instance.CreateToggle(_panel.transform, 24f, 24f);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = top;
            rt.anchorMax = top;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);

            // The toggle template ships its own label; blank it and draw our own,
            // left-aligned just right of the box, so we control placement.
            foreach (var t in go.GetComponentsInChildren<Text>()) t.text = string.Empty;

            var text = GUIManager.Instance.CreateText(label, _panel.transform, top, top,
                new Vector2(x + 18f + labelWidth / 2f, y), _font, 16, Color.white, true, Color.black, labelWidth, 24f, false)
                .GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false; // never steal clicks from the next checkbox

            return go.GetComponent<Toggle>();
        }

        private void CreateSortButton(SortMode mode, string token, float x, Font font)
        {
            var button = GUIManager.Instance.CreateButton(token, _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, -156f), 150f, 30f);
            button.GetComponent<Button>().onClick.AddListener(() => { _sort = mode; Rebuild(); });
            _sortLabels[mode] = button.GetComponentInChildren<Text>();
        }

        private static ColorBlock HandleColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            colors.highlightedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            colors.colorMultiplier = 1f;
            return colors;
        }

        // --- Rebuild (on every change) ---

        private void Rebuild()
        {
            if (!_built || _portal == null) return;

            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(_content.GetChild(i).gameObject);

            _destinationText.text = "Destination: " + SelectedName();
            foreach (var pair in _sortLabels)
                pair.Value.color = pair.Key == _sort ? Color.yellow : Color.white;

            BuildRow("(no destination)", "", 0, isGroup: false, selected: _selectedKey == null,
                () => { _selectedKey = null; Rebuild(); });

            var player = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            var rows = PortalListView.Build(
                PortalRegistry.Instance.BuildEntries(_portal.Id),
                PortalConfig.Separator,
                _searchField.text,
                _sort,
                new Vec3(player.x, player.y, player.z),
                PortalManager.RecentKeys,
                _collapsed);

            foreach (var row in rows) AddRow(row);
        }

        private void AddRow(DisplayRow row)
        {
            if (row.Kind == RowKind.Group)
            {
                var arrow = row.Collapsed ? "\u25B6" : "\u25BC"; // ▶ / ▼
                var path = row.GroupPath;
                BuildRow($"{arrow}  {row.Label}", row.PortalCount.ToString(), row.Depth,
                    isGroup: true, selected: false,
                    () => { if (!_collapsed.Remove(path)) _collapsed.Add(path); Rebuild(); });
                return;
            }

            var name = string.IsNullOrEmpty(row.Label) ? "(no name)" : row.Label;
            var selected = row.PortalId == _selectedKey;
            var key = row.PortalId;
            BuildRow(name, FormatDistance(row.Distance), row.Depth,
                isGroup: false, selected: selected,
                () => { _selectedKey = key; Rebuild(); });
        }

        // A lightweight list row: a flat, full-width clickable strip with a
        // left-aligned label and a right-aligned trailer (a portal's distance,
        // or a folder's count). No wood-button chrome -- that skin is for dialog
        // actions, not a list of dozens of entries.
        private void BuildRow(string label, string trailer, int depth, bool isGroup, bool selected, UnityEngine.Events.UnityAction onClick)
        {
            var row = new GameObject("row", typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(_content, false);

            var bg = row.GetComponent<Image>();
            bg.color = Color.white; // the Button tints this per interaction state
            var button = row.GetComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = RowColors(isGroup, selected);
            button.onClick.AddListener(onClick);

            var element = row.AddComponent<LayoutElement>();
            element.minHeight = RowHeight;
            element.preferredHeight = RowHeight;
            element.flexibleWidth = 1f;

            var textColor = selected
                ? new Color(1f, 0.86f, 0.4f)
                : isGroup ? new Color(0.85f, 0.9f, 1f) : new Color(0.92f, 0.9f, 0.85f);
            AddLabel(row.transform, label, TextAnchor.MiddleLeft, textColor, 12f + depth * 16f, 60f);
            if (!string.IsNullOrEmpty(trailer))
                AddLabel(row.transform, trailer, TextAnchor.MiddleRight, new Color(0.65f, 0.65f, 0.62f), 8f, 10f);
        }

        private void AddLabel(Transform parent, string text, TextAnchor anchor, Color color, float leftPad, float rightPad)
        {
            var go = new GameObject("label", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(leftPad, 0f);
            rt.offsetMax = new Vector2(-rightPad, 0f);

            var label = go.GetComponent<Text>();
            label.font = _font;
            label.fontSize = 17;
            label.color = color;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow; // clipped by the scroll viewport mask
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.text = text;

            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
        }

        private static ColorBlock RowColors(bool isGroup, bool selected)
        {
            var normal = selected
                ? new Color(0.85f, 0.7f, 0.2f, 0.30f)   // gold wash on the chosen destination
                : isGroup
                    ? new Color(1f, 1f, 1f, 0.10f)        // faint header strip
                    : new Color(1f, 1f, 1f, 0.02f);       // almost invisible until hovered
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = normal;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.16f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.24f);
            colors.selectedColor = normal;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        // --- Submit ---

        private void Submit()
        {
            var target = ZDOID.None;
            if (_selectedKey != null)
            {
                var chosen = PortalRegistry.Instance.GetByKey(_selectedKey);
                if (chosen != null) target = chosen.Id;
            }

            PortalManager.SubmitPortalConfig(_portal, _nameField.text, target, _defaultToggle.isOn, _showMapToggle.isOn);
            Close();
        }

        private string SelectedName()
        {
            if (_selectedKey == null) return "(none)";
            var chosen = PortalRegistry.Instance.GetByKey(_selectedKey);
            if (chosen == null) return "(none)";
            return string.IsNullOrEmpty(chosen.Name) ? "(no name)" : chosen.Name;
        }

        private static string FormatDistance(float metres)
        {
            if (metres >= 1000f) return $"{metres / 1000f:0.0} km";
            return $"{(int)metres} m";
        }

    }
}
