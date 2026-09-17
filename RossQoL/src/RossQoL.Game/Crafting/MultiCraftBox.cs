using System;
using GUIFramework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// A number box beside vanilla's Craft button. Whatever is typed in it is
    /// how many that button makes; at 1, which is where it starts, nothing
    /// about crafting changes.
    ///
    /// There is no second button. Vanilla already has a multi-craft path --
    /// m_touchMultiCrafting with m_multiCraftAmount, which its own touch UI
    /// uses -- and it drives the button's label, the requirement list, the
    /// affordability check and the craft itself. The box only sets those two
    /// fields, so every part of the panel agrees without anything being
    /// reimplemented or drawn over.
    ///
    /// Room for the box is made by shortening the Craft button, so nothing is
    /// added above it where the ingredient list lives.
    /// </summary>
    internal static class MultiCraftBox
    {
        private const string FieldName = "RossQoL_MultiCraftAmount";
        private const float Gap = 4f;
        private const float MaxFieldWidth = 54f;

        /// <summary>
        /// The box starts at one and holds at most a hundred. No setting: the
        /// box is the setting, it is in front of you, and it costs one
        /// keystroke to change.
        /// </summary>
        private const int MinAmount = 1;
        private const int MaxAmount = 100;

        private static GuiInputField s_field;
        private static InventoryGui s_owner;
        private static InventoryGui s_failedOwner;

        /// <summary>The Craft button we shortened, and by how much, so it can be put back.</summary>
        private static RectTransform s_craftRect;
        private static float s_shrunkBy;
        private static bool s_shrunk;

        /// <summary>True while the number box has the cursor, so game keys can be held off.</summary>
        public static bool IsTyping =>
            s_field && s_field.isFocused && InventoryGui.IsVisible()
            && MultiCraftFeature.Instance?.IsActive == true;

        /// <summary>The amount in the box, clamped to what the setting allows.</summary>
        public static int Amount
        {
            get
            {
                if (!s_field || !s_field.gameObject.activeSelf) return MinAmount;
                if (!int.TryParse(s_field.text, out int typed)) return MinAmount;

                return Mathf.Clamp(typed, MinAmount, MaxAmount);
            }
        }

        /// <summary>
        /// Whether a recipe may be made many at a time: it stacks, and it is
        /// not an upgrade, which vanilla never multi-crafts either.
        /// </summary>
        public static bool Allows(InventoryGui gui)
        {
            if (gui.m_selectedRecipe.ItemData != null) return false;

            var item = gui.m_selectedRecipe.Recipe?.m_item;
            return item && item.m_itemData.m_shared.m_maxStackSize > 1;
        }

        public static void OnHide()
        {
            if (!s_field) return;

            if (s_field.isFocused) s_field.DeactivateInputField();
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == s_field.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>
        /// Called from UpdateRecipe every frame the panel is open: creates the
        /// box on first use, then decides whether it belongs on screen.
        /// </summary>
        public static void Sync(InventoryGui gui)
        {
            if (MultiCraftFeature.Instance?.IsActive != true)
            {
                Show(false);
                return;
            }
            if (!EnsureCreated(gui)) return;

            // Vanilla hides the Craft button while a craft runs and when no
            // recipe is selected; the box goes with it.
            Show(gui.m_craftButton && gui.m_craftButton.gameObject.activeSelf && Allows(gui));
        }

        private static void Show(bool visible)
        {
            if (s_field && s_field.gameObject.activeSelf != visible)
            {
                if (!visible) OnHide();
                s_field.gameObject.SetActive(visible);
            }

            Shrink(visible);
        }

        /// <summary>Shortens the Craft button to make room, and puts it back when the box goes.</summary>
        private static void Shrink(bool on)
        {
            if (!s_craftRect || on == s_shrunk) return;

            float delta = on ? -s_shrunkBy : s_shrunkBy;
            s_craftRect.offsetMax = new Vector2(s_craftRect.offsetMax.x + delta, s_craftRect.offsetMax.y);
            s_shrunk = on;
        }

        private static bool EnsureCreated(InventoryGui gui)
        {
            if (s_field && s_owner == gui) return true;
            if (s_failedOwner == gui) return false;

            s_field = null;
            s_owner = null;
            s_craftRect = null;
            s_shrunk = false;
            try
            {
                Create(gui);
                s_owner = gui;
                return true;
            }
            catch (Exception ex)
            {
                // Once per InventoryGui: Sync runs every frame.
                s_failedOwner = gui;
                RossQoLPlugin.Log.LogError($"MultiCraft: could not add the amount box; crafting is unchanged: {ex}");
                return false;
            }
        }

        private static void Create(InventoryGui gui)
        {
            // No ?. on Unity objects: it skips Unity's destroyed-object check.
            var craftButton = gui.m_craftButton;
            if (!craftButton) throw new InvalidOperationException("craft button not found");

            var craftRect = (RectTransform)craftButton.transform;
            var parent = (RectTransform)craftRect.parent;
            if (!parent) throw new InvalidOperationException("craft button has no parent");

            // rect, not sizeDelta: sizeDelta is the difference from the anchor
            // rectangle, so for a stretched button it is not the width at all.
            float width = craftRect.rect.width;
            float height = craftRect.rect.height;
            if (width <= 0f || height <= 0f) throw new InvalidOperationException("craft button has no size");

            float fieldWidth = Mathf.Min(MaxFieldWidth, width * 0.3f);
            if (width - fieldWidth - Gap < 40f)
                throw new InvalidOperationException("craft button too narrow to share");

            var centre = parent.InverseTransformPoint(craftRect.TransformPoint(craftRect.rect.center));
            float right = centre.x + width / 2f;

            s_craftRect = craftRect;
            s_shrunkBy = fieldWidth + Gap;
            s_shrunk = false;

            // The box sits in the strip the button gives up, on the button's
            // own row: nothing is added above it, where the ingredients are.
            s_field = CreateField(parent,
                new Vector2(fieldWidth, height),
                new Vector2(right - fieldWidth / 2f, centre.y));

            RossQoLPlugin.Log.LogInfo("MultiCraft: amount box added beside the Craft button.");
        }

        private static GuiInputField CreateField(RectTransform parent, Vector2 size, Vector2 centre)
        {
            var buildUi = Hud.instance ? Hud.instance.m_buildUi : null;
            var template = buildUi ? buildUi.m_searchField : null;
            if (!template) throw new InvalidOperationException("build menu search box not found");

            var clone = Object.Instantiate(template.gameObject, parent, false);
            clone.name = FieldName;
            clone.SetActive(false);

            var rect = (RectTransform)clone.transform;
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.localPosition = new Vector3(centre.x, centre.y, 0f);

            var field = clone.GetComponent<GuiInputField>();
            field.onValueChanged = new TMP_InputField.OnChangeEvent();
            field.contentType = TMP_InputField.ContentType.IntegerNumber;
            field.characterLimit = MaxAmount.ToString().Length;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.SetTextWithoutNotify(MinAmount.ToString());

            var text = field.textComponent;
            if (text) text.alignment = TextAlignmentOptions.Center;

            return field;
        }
    }
}
