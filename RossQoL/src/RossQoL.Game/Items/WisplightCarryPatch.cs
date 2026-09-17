using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Grants the wisplight's own equip status effect while one is in the
    /// inventory and not worn, which is the whole of what equipping it looks
    /// like: the wisp circling you, its light, and the demister that pushes
    /// the mist back. The item has no model to hang on you -- its prefab
    /// carries no attach children at all -- so the effect is the item.
    ///
    /// The effect is added straight to the status effect manager rather than
    /// through the equipment list, so vanilla's own sweep leaves it alone:
    /// that loop removes only the effects it added itself, tracked in
    /// m_equipmentStatusEffects. It is taken back when the wisplight leaves
    /// the inventory, when it is equipped for real, or when the feature is
    /// switched off.
    ///
    /// Checked once a second rather than every frame: this rides Player.Update
    /// and the answer only changes when something enters or leaves your pack.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class WisplightCarryPatch
    {
        /// <summary>The wisplight's prefab name. Players call it a wisplight; the files call it Demister.</summary>
        private const string Wisplight = "Demister";

        private const float CheckInterval = 1f;

        private static float _nextCheck;
        private static StatusEffect _granted;
        private static Player _grantedTo;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), "Update", WisplightCarryFeature.FeatureName);

        private static void Postfix(Player __instance)
        {
            // An exception escaping here would break the player's update.
            try
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                if (Time.time < _nextCheck) return;
                _nextCheck = Time.time + CheckInterval;

                // Worn: vanilla grants the same effect through its equipment
                // sweep, so this hands ownership over and keeps its hands off.
                // Removing here would take vanilla's grant away with it, which
                // is exactly what made equipping the wisplight kill the wisp.
                if (Worn(__instance))
                {
                    Forget();
                    return;
                }

                var carried = Carried(__instance);
                if (carried != null) Grant(__instance, carried);
                else Revoke(__instance);
            }
            catch (Exception ex)
            {
                Revoke(__instance);
                RossQoLPlugin.Log.LogError($"WisplightCarry: leaving the wisplight to vanilla: {ex}");
            }
        }

        /// <summary>The wisplight is in the utility slot, so vanilla owns its effect.</summary>
        private static bool Worn(Player player)
        {
            var worn = player.m_utilityItem;
            return worn != null && IsWisplight(worn);
        }

        /// <summary>The carried wisplight, or null when there is nothing to do.</summary>
        private static ItemDrop.ItemData Carried(Player player)
        {
            if (WisplightCarryFeature.Instance?.IsActive != true) return null;

            var inventory = player.GetInventory();
            if (inventory == null) return null;

            foreach (var item in inventory.GetAllItems())
                if (IsWisplight(item)) return item;

            return null;
        }

        private static bool IsWisplight(ItemDrop.ItemData item) =>
            item?.m_dropPrefab != null
            && string.Equals(item.m_dropPrefab.name, Wisplight, StringComparison.Ordinal);

        private static void Grant(Player player, ItemDrop.ItemData carried)
        {
            var effect = carried?.m_shared?.m_equipStatusEffect;
            var seman = player.m_seman;
            if (effect == null || seman == null) return;

            // Already on: leave it be. Re-adding replays the effect's own
            // start -- the equip flourish -- which on a one-second check looks
            // like the animation stuck in a loop.
            if (seman.HaveStatusEffect(effect.NameHash()))
            {
                _granted = effect;
                _grantedTo = player;
                return;
            }

            seman.AddStatusEffect(effect, resetTime: false, 0, 0f, -1);
            _granted = effect;
            _grantedTo = player;
        }

        /// <summary>Stops tracking the effect without touching it: someone else owns it now.</summary>
        private static void Forget()
        {
            _granted = null;
            _grantedTo = null;
        }

        private static void Revoke(Player player)
        {
            if (_granted == null) return;

            var seman = (player != null ? player : _grantedTo)?.m_seman;
            if (seman != null) seman.RemoveStatusEffect(_granted.NameHash());

            Forget();
        }
    }
}
