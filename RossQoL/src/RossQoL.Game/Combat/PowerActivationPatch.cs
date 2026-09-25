using System;
using System.Reflection;
using HarmonyLib;
using RossQoL.Core.Combat;
using RossQoL.Game.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// Every frame, fire the second power from its own key and tick its own
    /// cooldown. Slot 1 stays vanilla's: the game's own key still fires the
    /// selected power, and the second power is never fired when it happens to be
    /// the one vanilla currently holds -- so the two keys never fire the same
    /// power, and it is never double-fired.
    ///
    /// A power is applied exactly as <c>Player.ActivateGuardianPower</c> does:
    /// resolve the StatusEffect from the ObjectDB, add it through the player's
    /// SEMan, grant the same guardian-power adrenaline, and start the cooldown
    /// from the effect's own <c>m_cooldown</c>. Vanilla's own
    /// <c>m_guardianPower</c>/<c>m_guardianPowerCooldown</c> are never written.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class PowerActivationPatch
    {
        // Player.m_adrenalineGuardianPower is private; read it once by
        // reflection. If a game version drops it, vanilla's default stands in.
        private const float DefaultAdrenaline = 10f;

        private static readonly FieldInfo AdrenalineField =
            AccessTools.Field(typeof(Player), "m_adrenalineGuardianPower");

        private static bool _logged;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), "Update", MultiplePowersFeature.FeatureName);

        private static void Postfix(Player __instance)
        {
            if (MultiplePowersFeature.Instance?.IsActive != true) return;
            if (__instance != Player.m_localPlayer) return;

            try
            {
                SecondPower slot = PlayerPowers.ForLocalPlayer();
                slot.Tick(Time.deltaTime);

                if (!slot.HasPower) return;

                KeyCode key = MultiplePowersConfig.SecondPowerKey?.Value ?? KeyCode.H;
                if (key == KeyCode.None || !ZInput.GetKeyDown(key)) return;
                if (!TakesPowerInput(__instance)) return;

                // Never fire the second power while it is also the vanilla-held
                // one: vanilla's own key already fires that, and the HUD hides
                // it too.
                if (slot.Power == __instance.GetGuardianPowerName()) return;
                if (!slot.IsReady) return;

                Activate(__instance, slot);
            }
            catch (Exception ex)
            {
                if (_logged) return;
                _logged = true;
                RossQoLPlugin.Log.LogError(
                    $"MultiplePowers: could not fire the second power (repeats of this error are not logged): {ex}");
            }
        }

        /// <summary>
        /// The same gate vanilla puts on its own guardian-power key, plus one.
        /// Player.Update reads "GP" only inside <c>if (TakeInput())</c> and
        /// only when <c>!Hud.InRadial() &amp;&amp; !Hud.IsPieceSelectionVisible()</c>;
        /// TakeInput is false while chat, the console, a text prompt (sign,
        /// rename), the inventory, the menu, the map and the build search box
        /// have the keyboard. Reading the raw key without that fired the second
        /// power every time H was typed into chat.
        ///
        /// TakeInput knows only vanilla's own text boxes, so a focused input
        /// field anywhere -- another mod's search box, a portal-name field --
        /// also holds the key off: whatever has the caret owns the keystroke.
        /// </summary>
        private static bool TakesPowerInput(Player player)
        {
            if (!player.TakeInput()) return false;
            if (Hud.InRadial() || Hud.IsPieceSelectionVisible()) return false;

            var selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null) return true;

            var tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return false;

            var legacy = selected.GetComponent<InputField>();
            return legacy == null || !legacy.isFocused;
        }

        private static void Activate(Player player, SecondPower slot)
        {
            if (ObjectDB.instance == null) return;

            StatusEffect se = ObjectDB.instance.GetStatusEffect(slot.Power.GetStableHashCode());
            if (se == null) return;

            player.GetSEMan().AddStatusEffect(se.NameHash(), true, 0, 0f);
            player.AddAdrenaline(AdrenalineFor(player));
            slot.StartCooldown(se.m_cooldown);
            PlayerPowers.Persist(player, slot);
        }

        private static float AdrenalineFor(Player player)
        {
            if (AdrenalineField != null && AdrenalineField.GetValue(player) is float v) return v;
            return DefaultAdrenaline;
        }
    }
}
