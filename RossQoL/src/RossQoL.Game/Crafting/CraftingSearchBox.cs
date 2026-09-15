using System;
using System.Collections.Generic;
using GUIFramework;
using RossQoL.Core.Crafting;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// The search box itself: created once per InventoryGui, placed directly
    /// above the recipe list, which is shortened to make room.
    ///
    /// A copy of the build menu's search box rather than a Jotunn-built
    /// field, so it matches vanilla's look and localized placeholder.
    /// </summary>
    internal static class CraftingSearchBox
    {
        private const string ObjectName = "RossQoL_RecipeSearch";
        private const float Gap = 4f;
        private const float FallbackHeight = 32f;
        private const float FocusWaitSeconds = 1f;

        private static GuiInputField s_field;
        /// <summary>Unscaled time by which a pending auto-focus must happen; 0 when none.</summary>
        private static float s_focusDeadline;
        private static InventoryGui s_owner;
        private static InventoryGui s_failedOwner;
        private static readonly List<Recipe> s_filtered = new List<Recipe>();

        public static string Term => s_field && s_owner ? s_field.text : string.Empty;

        /// <summary>Called for every pressed game button, so keep it cheap.</summary>
        public static bool IsTyping =>
            s_field && s_field.isFocused && InventoryGui.IsVisible()
            && CraftingSearchFeature.Instance?.IsActive == true;

        public static void OnShow(InventoryGui gui)
        {
            if (!EnsureCreated(gui)) return;
            SetVisible(true);

            var player = Player.m_localPlayer;
            if (CraftingSearchConfig.SearchAutoFocus?.Value == true
                && player && player.GetCurrentCraftingStation()
                && !ZInput.IsGamepadActive())
            {
                s_focusDeadline = Time.unscaledTime + FocusWaitSeconds;
            }
        }

        /// <summary>
        /// Focuses the box once it is active, if a Show asked for it. Called
        /// every frame the inventory updates; nothing to do almost always.
        /// </summary>
        public static void TryPendingFocus()
        {
            if (s_focusDeadline <= 0f) return;
            if (!s_field || Time.unscaledTime > s_focusDeadline)
            {
                s_focusDeadline = 0f;
                return;
            }
            if (!s_field.IsActive() || !s_field.IsInteractable()) return;

            s_focusDeadline = 0f;
            s_field.Select();
            s_field.ActivateInputField();
        }

        /// <summary>
        /// Hiding leaves the recipe list shortened; the gap is cosmetic and
        /// goes away on the next launch.
        /// </summary>
        public static void SetVisible(bool visible)
        {
            if (s_field && s_field.gameObject.activeSelf != visible) s_field.gameObject.SetActive(visible);
        }

        /// <summary>InventoryGui.Hide runs every frame in some states; do nothing when already clear.</summary>
        public static void OnHide()
        {
            s_focusDeadline = 0f;
            if (!s_field) return;

            if (s_field.isFocused) s_field.DeactivateInputField();
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == s_field.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            if (s_field.text.Length > 0) s_field.SetTextWithoutNotify(string.Empty);
        }

        /// <summary>
        /// The recipes whose displayed name or category words match, in their
        /// original order. Category words: the item type's English words, and
        /// for weapons, bows and tools the weapon skill's name in the game's
        /// language.
        /// </summary>
        public static List<Recipe> Filter(List<Recipe> recipes, string term)
        {
            s_filtered.Clear();
            foreach (var recipe in recipes)
            {
                if (!recipe || !recipe.m_item) continue;

                var shared = recipe.m_item.m_itemData.m_shared;
                string name = Localization.instance.Localize(shared.m_name);

                s_words.Clear();
                string itemType = shared.m_itemType.ToString();
                s_words.AddRange(RecipeCategories.WordsFor(itemType));
                if (RecipeCategories.HasSkillWord(itemType) && shared.m_skillType != Skills.SkillType.None)
                    s_words.Add(Localization.instance.Localize("$skill_" + shared.m_skillType.ToString().ToLower()));

                if (RecipeSearch.Matches(name, s_words, term)) s_filtered.Add(recipe);
            }
            return s_filtered;
        }

        private static readonly List<string> s_words = new List<string>();

        private static bool EnsureCreated(InventoryGui gui)
        {
            if (s_field && s_owner == gui) return true;
            if (s_failedOwner == gui) return false;

            s_field = null;
            s_owner = null;
            try
            {
                s_field = Create(gui);
                s_owner = gui;

                // Show sized the list before the box existed; size it again
                // with the shortened base height.
                gui.UpdateCraftingPanel();
                return true;
            }
            catch (Exception ex)
            {
                // Once per InventoryGui: this runs on every inventory open.
                s_failedOwner = gui;
                RossQoLPlugin.Log.LogError($"RecipeSearch: could not add the search box; recipes are unfiltered: {ex}");
                return false;
            }
        }

        private static GuiInputField Create(InventoryGui gui)
        {
            // No ?. on Unity objects: it skips Unity's destroyed-object check.
            var buildUi = Hud.instance ? Hud.instance.m_buildUi : null;
            var template = buildUi ? buildUi.m_searchField : null;
            if (!template) throw new InvalidOperationException("build menu search box not found");

            var list = gui.m_recipeListRoot;
            var scroll = list.GetComponentInParent<ScrollRect>(true);
            var frame = (scroll ? scroll.transform : list.parent) as RectTransform;
            if (!frame || !frame.parent) throw new InvalidOperationException("recipe list frame not found");

            float height = ((RectTransform)template.transform).rect.height;
            height = height > 0f ? Mathf.Clamp(height, 24f, 40f) : FallbackHeight;
            float shift = height + Gap;

            var clone = Object.Instantiate(template.gameObject, frame.parent, false);
            try
            {
                clone.name = ObjectName;
                clone.SetActive(true);

                // The strip the list's top edge is about to give up. Both
                // vertical anchors sit on the list's TOP anchor: offsetMin is
                // measured from anchorMin, so copying a vertically stretched
                // list's anchors would put the box's bottom near the list's
                // bottom and cover it.
                var rect = (RectTransform)clone.transform;
                rect.localScale = Vector3.one;
                rect.anchorMin = new Vector2(frame.anchorMin.x, frame.anchorMax.y);
                rect.anchorMax = frame.anchorMax;
                rect.pivot = new Vector2(frame.pivot.x, 1f);
                rect.offsetMin = new Vector2(frame.offsetMin.x, frame.offsetMax.y - height);
                rect.offsetMax = frame.offsetMax;
                ShortenTop(frame, shift);

                // The scrollbar may sit beside the list rather than inside it.
                var bar = gui.m_recipeListScroll ? gui.m_recipeListScroll.transform as RectTransform : null;
                if (bar && bar.parent == frame.parent) ShortenTop(bar, shift);

                // Awake measured the list at full height; without this the list
                // scrolls into empty space below the last recipe.
                gui.m_recipeListBaseSize = Mathf.Max(0f, gui.m_recipeListBaseSize - shift);

                var field = clone.GetComponent<GuiInputField>();
                field.onValueChanged = new TMP_InputField.OnChangeEvent();
                field.onValueChanged.AddListener(_ => OnTextChanged(gui));
                field.navigation = new Navigation { mode = Navigation.Mode.None };
                field.SetTextWithoutNotify(string.Empty);

                RossQoLPlugin.Log.LogInfo(
                    $"RecipeSearch: search box added above '{frame.name}' (height {height:0}); "
                    + $"scrollbar {(bar && bar.parent == frame.parent ? "shortened" : "left alone")}.");
                return field;
            }
            catch
            {
                Object.Destroy(clone);
                throw;
            }
        }

        private static void ShortenTop(RectTransform rect, float by) =>
            rect.offsetMax = new Vector2(rect.offsetMax.x, rect.offsetMax.y - by);

        private static void OnTextChanged(InventoryGui gui)
        {
            if (!gui || !InventoryGui.IsVisible() || !Player.m_localPlayer) return;

            try
            {
                gui.UpdateCraftingPanel();
                if (gui.m_recipeListScroll) gui.m_recipeListScroll.value = 1f;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"RecipeSearch: refreshing the recipe list failed: {ex}");
            }
        }
    }
}
