using System;
using HarmonyLib;
using RossQoL.Core.Combat;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// Using a guardian stone normally still sets vanilla's single power (slot
    /// 1), fired by the vanilla guardian-power key -- that path is left
    /// completely alone. Holding the assign modifier (Shift by default) while
    /// using a stone instead sends that stone's power to the player's SECOND
    /// slot, without disturbing slot 1.
    ///
    /// Prefix on <see cref="ItemStand.Interact"/>: the guardian branch of
    /// Interact schedules <c>DelayedPowerActivation</c> -&gt;
    /// <c>Player.SetGuardianPower</c>. When the modifier is held on a
    /// guardian-power stand, this handles the interaction itself (stores the
    /// second power) and returns false so vanilla never runs -- so slot 1 is
    /// untouched and no vanilla activation is scheduled. Any other case
    /// (weapon/armour stand, no modifier, another player) falls straight through
    /// to vanilla.
    ///
    /// The power's identity is its StatusEffect name (<c>m_guardianPower.name</c>,
    /// e.g. "GP_Eikthyr") -- the exact string SetGuardianPower stores and
    /// GetGuardianPowerName returns -- never the stand's GameObject name, which
    /// can be a bare "itemstand" (see docs/valheim-api/guardian-powers.md).
    /// </summary>
    [HarmonyPatch(typeof(ItemStand), nameof(ItemStand.Interact))]
    internal static class GuardianStonePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ItemStand), nameof(ItemStand.Interact), MultiplePowersFeature.FeatureName);

        private static bool Prefix(ItemStand __instance, Humanoid user, ref bool __result)
        {
            if (MultiplePowersFeature.Instance?.IsActive != true) return true;

            // A guardian-power stand is the only ItemStand that grants a power;
            // a weapon/armour stand has m_guardianPower == null and is left to
            // vanilla entirely.
            var power = __instance != null ? __instance.m_guardianPower : null;
            if (power == null || string.IsNullOrEmpty(power.name)) return true;

            if (!(user is Player player) || player != Player.m_localPlayer) return true;

            // Plain use assigns slot 1 the vanilla way; only the held modifier
            // diverts the stone's power into the second slot.
            KeyCode modifier = MultiplePowersConfig.AssignSecondPowerModifier?.Value ?? KeyCode.LeftShift;
            if (modifier == KeyCode.None || !ZInput.GetKey(modifier)) return true;

            try
            {
                SecondPower slot = PlayerPowers.ForLocalPlayer();
                slot.Set(power.name);
                PlayerPowers.Persist(player, slot);

                // Play the same burst vanilla plays when a stone grants a power
                // (ItemStand.Interact's guardian branch), so assigning the second
                // slot looks and sounds identical to setting the first.
                __instance.m_activatePowerEffects?.Create(__instance.transform.position, __instance.transform.rotation);
                __instance.m_activatePowerEffectsPlayer?.Create(player.transform.position, Quaternion.identity, player.transform);

                string label = Localization.instance.Localize(power.m_name);
                player.Message(MessageHud.MessageType.Center, label + " set as your second power");
                __result = true;   // the interaction was handled
                return false;      // skip vanilla: slot 1 and its cooldown are untouched
            }
            catch (Exception ex)
            {
                // Never strand the player: on any failure, let vanilla's normal
                // interaction run so the stone still does something.
                RossQoLPlugin.Log.LogError($"MultiplePowers: could not assign the second power at a stone: {ex}");
                return true;
            }
        }
    }
}
