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
    /// A number box and a second craft button, sitting just above vanilla's
    /// Craft button. Clicking it crafts that many of the selected recipe.
    ///
    /// The crafting itself is vanilla's: m_touchMultiCrafting is the flag the
    /// touch UI sets to make the next OnCraftPressed a multi-craft, and
    /// OnCraftPressed clears it again. Driving that instead of crafting by
    /// hand keeps the craft timer, the materials, the station effects, the
    /// skill gain and the "inventory full" check exactly as vanilla does them.
    ///
    /// Both widgets are copies of things already on screen -- the button from
    /// vanilla's own Craft button, the number box from the build menu's search
    /// field -- so they match the game's look without any assets of ours.
    /// </summary>
    internal static class MultiCraftBox
    {
        private const string ButtonName = "RossQoL_MultiCraftButton";
        private const string FieldName = "RossQoL_MultiCraftAmount";
        private const float Gap = 6f;
        private const float FieldWidth = 66f;

        private static Button s_button;
        private static TMP_Text s_label;
        private static GuiInputField s_field;
        private static InventoryGui s_owner;
        private static InventoryGui s_failedOwner;

        /// <summary>True while the number box has the cursor, so game keys can be held off.</summary>
        public static bool IsTyping =>
            s_field && s_field.isFocused && InventoryGui.IsVisible()
            && MultiCraftFeature.Instance?.IsActive == true;

        /// <summary>The amount in the box, clamped to what the setting allows.</summary>
        public static int Amount
        {
            get
            {
                int fallback = MultiCraftConfig.MultiCraftAmount?.Value ?? 10;
                if (!s_field || !int.TryParse(s_field.text, out int typed)) return fallback;
                return Mathf.Clamp(typed, MultiCraftConfig.MinAmount, MultiCraftConfig.MaxAmount);
            }
        }

        public static void OnHide()
        {
            if (!s_field) return;

            if (s_field.isFocused) s_field.DeactivateInputField();
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == s_field.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>
        /// Called from UpdateRecipe, every frame the panel is open: creates the
        /// widgets on first use and then decides whether they belong on screen.
        /// </summary>
        public static void Sync(InventoryGui gui)
        {
            if (MultiCraftFeature.Instance?.IsActive != true)
            {
                SetVisible(false);
                return;
            }
            if (!EnsureCreated(gui)) return;

            var recipe = gui.m_selectedRecipe.Recipe;
            bool upgrade = gui.m_selectedRecipe.ItemData != null;

            // Vanilla hides the Craft button while a craft is running or when
            // no recipe is selected; ours goes with it.
            bool craftable = recipe && !upgrade
                             && gui.m_craftButton && gui.m_craftButton.gameObject.activeSelf
                             && Stacks(recipe);
            SetVisible(craftable);
            if (!craftable) return;

            int amount = Amount;
            s_label.text = Localization.instance.Localize("$inventory_craftbutton") + " x " + amount;

            var player = Player.m_localPlayer;
            bool affordable = player != null
                              && (player.HaveRequirements(recipe, discover: false, 1, amount)
                                  || player.NoCostCheat()
                                  || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));

            // Vanilla's own button already answers "is the station usable, is
            // the recipe allowed"; ours adds only "can you afford this many".
            s_button.interactable = gui.m_craftButton.interactable && affordable;
        }

        private static bool Stacks(Recipe recipe)
        {
            var item = recipe.m_item;
            return item && item.m_itemData.m_shared.m_maxStackSize > 1;
        }

        private static void SetVisible(bool visible)
        {
            if (s_button && s_button.gameObject.activeSelf != visible) s_button.gameObject.SetActive(visible);
            if (s_field && s_field.gameObject.activeSelf != visible)
            {
                if (!visible) OnHide();
                s_field.gameObject.SetActive(visible);
            }
        }

        private static bool EnsureCreated(InventoryGui gui)
        {
            if (s_button && s_field && s_owner == gui) return true;
            if (s_failedOwner == gui) return false;

            s_button = null;
            s_field = null;
            s_owner = null;
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
                RossQoLPlugin.Log.LogError($"MultiCraft: could not add the craft box; crafting is unchanged: {ex}");
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
            // rectangle, so for a stretched button it is not the width at all
            // and sizing from it throws the row off screen.
            float width = craftRect.rect.width;
            float height = craftRect.rect.height;
            if (width <= 0f || height <= 0f) throw new InvalidOperationException("craft button has no size");

            // The row sits directly above the Craft button and never wider
            // than it, so whatever the panel's layout, it cannot overhang.
            float fieldWidth = Mathf.Min(FieldWidth, width * 0.4f);
            float buttonWidth = width - fieldWidth - Gap;
            if (buttonWidth <= 0f) throw new InvalidOperationException("craft button too narrow to share");

            var centre = Centre(parent, craftRect);
            float rowY = centre.y + height + Gap;

            var buttonObject = Object.Instantiate(craftButton.gameObject, parent, false);
            buttonObject.name = ButtonName;
            buttonObject.SetActive(false);

            Place((RectTransform)buttonObject.transform, craftRect.localScale,
                new Vector2(buttonWidth, height),
                new Vector2(centre.x + width / 2f - buttonWidth / 2f, rowY));

            s_button = buttonObject.GetComponent<Button>();
            s_button.onClick = new Button.ButtonClickedEvent();
            s_button.onClick.AddListener(() => OnPressed(gui));

            // A cloned button keeps vanilla's tooltip text ("craft this item"),
            // which would be wrong under ours; the label is set every Sync.
            var tooltip = buttonObject.GetComponent<UITooltip>();
            if (tooltip) tooltip.m_text = string.Empty;

            s_label = buttonObject.GetComponentInChildren<TMP_Text>();
            if (!s_label) throw new InvalidOperationException("craft button has no label");

            s_field = CreateField(parent,
                new Vector2(fieldWidth, height),
                new Vector2(centre.x - width / 2f + fieldWidth / 2f, rowY));
        }

        /// <summary>The craft button's centre, in its parent's own coordinates.</summary>
        private static Vector2 Centre(RectTransform parent, RectTransform craftRect) =>
            parent.InverseTransformPoint(craftRect.TransformPoint(craftRect.rect.center));

        /// <summary>
        /// Anchors a widget to one point rather than copying the button's
        /// anchors, so its size is the size asked for whether or not the
        /// button it sits above is stretched.
        /// </summary>
        private static void Place(RectTransform rect, Vector3 scale, Vector2 size, Vector2 centre)
        {
            rect.localScale = scale;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.localPosition = new Vector3(centre.x, centre.y, 0f);
        }

        private static GuiInputField CreateField(RectTransform parent, Vector2 size, Vector2 centre)
        {
            var buildUi = Hud.instance ? Hud.instance.m_buildUi : null;
            var template = buildUi ? buildUi.m_searchField : null;
            if (!template) throw new InvalidOperationException("build menu search box not found");

            var clone = Object.Instantiate(template.gameObject, parent, false);
            clone.name = FieldName;
            clone.SetActive(false);

            Place((RectTransform)clone.transform, Vector3.one, size, centre);

            var field = clone.GetComponent<GuiInputField>();
            field.onValueChanged = new TMP_InputField.OnChangeEvent();
            field.contentType = TMP_InputField.ContentType.IntegerNumber;
            field.characterLimit = MultiCraftConfig.MaxAmount.ToString().Length;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.SetTextWithoutNotify((MultiCraftConfig.MultiCraftAmount?.Value ?? 10).ToString());

            RossQoLPlugin.Log.LogInfo("MultiCraft: craft-many button added above the Craft button.");
            return field;
        }

        private static void OnPressed(InventoryGui gui)
        {
            try
            {
                if (!gui || MultiCraftFeature.Instance?.IsActive != true) return;

                int amount = Amount;
                if (amount < MultiCraftConfig.MinAmount) return;

                // Vanilla reads this for the craft, the materials and the skill.
                gui.m_multiCraftAmount = amount;

                // The flag vanilla's own touch UI sets; OnCraftPressed consumes
                // it, so it is never left on for the next ordinary craft.
                gui.m_touchMultiCrafting = true;
                MultiCraftPatches.InvokeCraftPressed(gui);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"MultiCraft: crafting many failed: {ex}");
            }
        }
    }
}
