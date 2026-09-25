using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RossQoL.Core.Combat;
using RossQoL.Game.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// Draws the second power's slot beside vanilla's single guardian-power
    /// icon. Vanilla's <see cref="Hud"/> owns exactly one slot -- the
    /// <c>m_gpRoot</c> rect holding <c>m_gpIcon</c> (sprite + tint),
    /// <c>m_gpName</c> and <c>m_gpCooldown</c> (a remaining-time string or
    /// <c>$hud_ready</c>; there is no radial fill in 1.0.15). This postfix
    /// clones that whole slot for the second power and, every frame, drives the
    /// clone's icon and cooldown. The cloned name is shown but scaled down to
    /// fit the slot width (see FitName), so a long power name shrinks rather
    /// than spilling into the vanilla slot beside it.
    ///
    /// The second power is shown only when it is set and is not the one vanilla
    /// currently holds -- the same power PowerActivationPatch fires from its key
    /// -- so the slot always matches what the key does. The clone is created
    /// lazily and reused across frames; when the second power changes at a stone
    /// the clone is reconciled (rebuilt for the new power), never per frame.
    ///
    /// The HUD is strictly best-effort: every UI lookup is null-guarded and the
    /// whole body is wrapped so a HUD fault can NEVER throw out of vanilla's
    /// update or stop powers from firing. If a required element is missing or a
    /// clone cannot be resolved, the row is torn down and disabled with a single
    /// logged line (headless fallback).
    /// </summary>
    [HarmonyPatch(typeof(Hud), "UpdateGuardianPower")]
    internal static class PowerHudPatch
    {
        // Vanilla tints the icon transparent-magenta while on cooldown (alpha
        // 0, so it reads as "hidden, watch the timer"); white when ready. Read
        // the real static so the clones match, with the source value as a
        // fallback if the field is ever renamed.
        private static readonly Color OnCooldownColor = ReadOnCooldownColor();

        // Horizontal spacing between slots when the vanilla rect's own width
        // cannot be measured (layout not yet resolved).
        private const float FallbackStep = 70f;
        private const float SlotGap = 6f;

        // One reused clone per power, keyed by power name. Reconciled in place.
        private static readonly Dictionary<string, Slot> Slots =
            new Dictionary<string, Slot>();

        // Scratch lists reused each frame so the postfix allocates nothing on
        // the steady-state path.
        private static readonly List<string> Desired = new List<string>();
        private static readonly List<string> Stale = new List<string>();

        private static Hud _hud;
        private static bool _disabled;
        private static bool _logged;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Hud), "UpdateGuardianPower", MultiplePowersFeature.FeatureName);

        private static void Postfix(Hud __instance, Player player)
        {
            try
            {
                // A new Hud instance (game reloaded) invalidates every clone we
                // hold; drop them and give the row a fresh chance.
                if (!ReferenceEquals(__instance, _hud))
                {
                    DestroyAll();
                    _hud = __instance;
                    _disabled = false;
                    _logged = false;
                }

                if (__instance == null) return;

                if (MultiplePowersFeature.Instance?.IsActive != true)
                {
                    DestroyAll();
                    return;
                }

                if (_disabled) return;
                if (player == null || player != Player.m_localPlayer) return;

                SecondPower second = PlayerPowers.ForLocalPlayer();
                if (second == null) return;

                // The second power is shown only when it is set and is not the
                // one vanilla currently holds, so the row never duplicates the
                // vanilla slot next to it.
                string vanilla = player.GetGuardianPowerName();
                Desired.Clear();
                if (second.HasPower && second.Power != vanilla)
                    Desired.Add(second.Power);

                Reconcile(__instance, second);
            }
            catch (Exception ex)
            {
                // A throw here would run every frame and could take vanilla's
                // HUD update down with it. Disable the row for good, log once.
                DisableRow($"MultiplePowers: HUD row failed and was disabled (powers still fire): {ex}");
            }
        }

        /// <summary>
        /// Bring the live clone set in line with <see cref="Desired"/>: drop
        /// clones for powers no longer held, create clones lazily for new ones,
        /// then position and refresh every kept clone.
        /// </summary>
        private static void Reconcile(Hud hud, SecondPower second)
        {
            // Any missing vanilla member is a structural fault: no template to
            // clone from and nothing to anchor against.
            if (hud.m_gpRoot == null || hud.m_gpIcon == null
                || hud.m_gpName == null || hud.m_gpCooldown == null)
            {
                DisableRow("MultiplePowers: the guardian-power HUD slot is missing, so the "
                    + "loadout row is disabled (powers still fire).");
                return;
            }

            // Drop clones whose power left the loadout.
            Stale.Clear();
            foreach (var kv in Slots)
                if (!Desired.Contains(kv.Key))
                    Stale.Add(kv.Key);
            for (int i = 0; i < Stale.Count; i++)
            {
                if (Slots.TryGetValue(Stale[i], out var gone) && gone?.Root != null)
                    Object.Destroy(gone.Root);
                Slots.Remove(Stale[i]);
            }

            var root = (RectTransform)hud.m_gpRoot;
            for (int i = 0; i < Desired.Count; i++)
            {
                string power = Desired[i];
                if (!Slots.TryGetValue(power, out var slot) || slot == null || slot.Root == null)
                {
                    slot = EnsureSlot(hud, root, power);
                    if (_disabled) return;          // structural fault already logged
                    if (slot == null) continue;      // transient (ObjectDB not ready) -- retry next frame
                    Slots[power] = slot;
                }

                Place(slot, root, i);
                UpdateSlot(slot, second);
            }
        }

        /// <summary>
        /// Clone the vanilla slot once for <paramref name="power"/>. Returns
        /// null (without disabling) when the power's StatusEffect cannot be
        /// resolved yet; sets <see cref="_disabled"/> and returns null when the
        /// clone's children cannot be located (a structural fault).
        /// </summary>
        private static Slot EnsureSlot(Hud hud, RectTransform root, string power)
        {
            StatusEffect se = ObjectDB.instance != null
                ? ObjectDB.instance.GetStatusEffect(power.GetStableHashCode())
                : null;
            if (se == null) return null;

            // Relative paths from the vanilla slot to each driven child, read
            // from the live prefab (the names are asset data, not in the DLL).
            string iconPath = RelativePath(root, hud.m_gpIcon.transform);
            string namePath = RelativePath(root, hud.m_gpName.transform);
            string cooldownPath = RelativePath(root, hud.m_gpCooldown.transform);
            if (iconPath == null || namePath == null || cooldownPath == null)
            {
                DisableRow("MultiplePowers: could not map the guardian-power slot's children, "
                    + "so the loadout row is disabled (powers still fire).");
                return null;
            }

            GameObject clone = Object.Instantiate(root.gameObject, root.parent, false);
            try
            {
                clone.name = "RossQoL_gp_" + power;
                Strip(clone);

                var icon = FindOn<Image>(clone.transform, iconPath);
                var name = FindOn<TMP_Text>(clone.transform, namePath);
                var cooldown = FindOn<TMP_Text>(clone.transform, cooldownPath);
                if (icon == null || name == null || cooldown == null)
                {
                    Object.Destroy(clone);
                    DisableRow("MultiplePowers: a cloned guardian-power slot was missing its "
                        + "icon/name/cooldown, so the loadout row is disabled (powers still fire).");
                    return null;
                }

                // Show the power's name, scaled to fit the slot width so a long
                // name shrinks rather than spilling into the vanilla slot beside
                // it (see FitName). The name is fixed per power, so it is set
                // once here; only the font fit waits for a resolved layout.
                var loc = Localization.instance;
                if (loc != null && se != null) name.text = loc.Localize(se.m_name);

                clone.SetActive(true);
                return new Slot
                {
                    Power = power, Root = clone, Icon = icon, Name = name,
                    Cooldown = cooldown, Effect = se, BaseFontSize = name.fontSize,
                };
            }
            catch
            {
                Object.Destroy(clone);
                throw;
            }
        }

        /// <summary>Place a clone one step to the right of the vanilla slot.</summary>
        private static void Place(Slot slot, RectTransform root, int index)
        {
            if (slot.Root == null) return;
            var rect = slot.Root.transform as RectTransform;
            if (rect == null) return;

            rect.anchorMin = root.anchorMin;
            rect.anchorMax = root.anchorMax;
            rect.pivot = root.pivot;
            rect.localScale = root.localScale;

            float width = root.rect.width;
            float step = (width > 1f ? width : FallbackStep) + SlotGap;
            rect.anchoredPosition = root.anchoredPosition + new Vector2(step * (index + 1), 0f);

            FitName(slot, width);
        }

        /// <summary>
        /// Scale the name down until it fits the slot's own width, so a long
        /// power name shrinks instead of spilling into the slot beside it. Runs
        /// once, when the layout has resolved a real width; the name and its
        /// base size do not change after.
        /// </summary>
        private static void FitName(Slot slot, float width)
        {
            if (slot.NameFitted || slot.Name == null || width <= 1f) return;

            slot.Name.fontSize = slot.BaseFontSize;
            slot.Name.ForceMeshUpdate();
            float pref = slot.Name.preferredWidth;
            if (pref > width && pref > 0f)
                slot.Name.fontSize = Mathf.Max(slot.BaseFontSize * (width / pref), slot.BaseFontSize * 0.4f);

            slot.NameFitted = true;
        }

        /// <summary>Drive a clone's icon and cooldown exactly as vanilla does its own.</summary>
        private static void UpdateSlot(Slot slot, SecondPower second)
        {
            float remaining = second.Remaining;
            bool ready = remaining <= 0f;
            var loc = Localization.instance;

            if (slot.Icon != null)
            {
                if (slot.Effect != null) slot.Icon.sprite = slot.Effect.m_icon;
                slot.Icon.color = ready ? Color.white : OnCooldownColor;
            }

            if (slot.Cooldown != null)
            {
                slot.Cooldown.text = ready
                    ? (loc != null ? loc.Localize("$hud_ready") : "Ready")
                    : StatusEffect.GetTimeString(remaining);
            }
        }

        /// <summary>Tear the whole row down and stop trying; log at most once.</summary>
        private static void DisableRow(string message)
        {
            _disabled = true;
            DestroyAll();
            if (_logged) return;
            _logged = true;
            RossQoLPlugin.Log.LogWarning(message);
        }

        private static void DestroyAll()
        {
            foreach (var kv in Slots)
                if (kv.Value?.Root != null)
                    Object.Destroy(kv.Value.Root);
            Slots.Clear();
        }

        /// <summary>
        /// The path from <paramref name="root"/> down to <paramref name="child"/>,
        /// as <c>Transform.Find</c> expects it. Empty string when the child is
        /// the root itself; null when the child is not under the root.
        /// </summary>
        private static string RelativePath(Transform root, Transform child)
        {
            if (root == null || child == null) return null;
            if (child == root) return string.Empty;

            var sb = new StringBuilder();
            var t = child;
            while (t != null && t != root)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, t.name);
                t = t.parent;
            }
            return t == root ? sb.ToString() : null;
        }

        private static T FindOn<T>(Transform cloneRoot, string relativePath) where T : Component
        {
            Transform target = string.IsNullOrEmpty(relativePath)
                ? cloneRoot
                : cloneRoot.Find(relativePath);
            return target != null ? target.GetComponent<T>() : null;
        }

        /// <summary>
        /// Every cloned Valheim element gets the same treatment: its Animator is
        /// stripped by type name (the project does not reference the animation
        /// module), every Localize removed so nothing overwrites what we write,
        /// and any UIInputHandler removed so clicking a clone cannot fire
        /// vanilla's single-power path.
        /// </summary>
        private static void Strip(GameObject clone)
        {
            foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true))
                if (behaviour.GetType().Name == "Animator")
                    Object.DestroyImmediate(behaviour);
            foreach (var localize in clone.GetComponentsInChildren<Localize>(true))
                Object.DestroyImmediate(localize);
            foreach (var handler in clone.GetComponentsInChildren<UIInputHandler>(true))
                Object.DestroyImmediate(handler);
        }

        private static Color ReadOnCooldownColor()
        {
            FieldInfo field = typeof(Hud).GetField(
                "s_colorRedBlueZeroAlpha", BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null && field.GetValue(null) is Color c) return c;
            return new Color(1f, 0f, 1f, 0f);
        }

        private sealed class Slot
        {
            public string Power;
            public GameObject Root;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text Cooldown;
            public StatusEffect Effect;
            public float BaseFontSize;
            public bool NameFitted;
        }
    }
}
