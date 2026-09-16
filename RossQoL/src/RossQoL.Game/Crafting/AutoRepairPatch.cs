using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// Repairs on Show, the moment the station's panel opens.
    ///
    /// Vanilla's RepairOneItem does one item per press, each with its own
    /// centre-screen message and sound, so pressing it in a loop would mend
    /// the gear but shout about it six times. The repair here is the same
    /// work -- full durability, the same crafting skill for each item -- with
    /// one sound and one message at the end.
    ///
    /// Which items qualify is decided by vanilla's own CanRepair, called by
    /// reflection rather than reimplemented, so the station-type and
    /// station-level rules stay exactly the game's and cannot drift from it.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class AutoRepairPatch
    {
        private static MethodInfo s_canRepair;
        private static readonly List<ItemDrop.ItemData> Worn = new List<ItemDrop.ItemData>();

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(InventoryGui), nameof(InventoryGui.Show), AutoRepairFeature.FeatureName)
            & ValheimCompat.RequireMethod(typeof(InventoryGui), "CanRepair", AutoRepairFeature.FeatureName);

        private static void Postfix(InventoryGui __instance)
        {
            if (AutoRepairFeature.Instance?.IsActive != true) return;

            // An exception escaping here would break opening the inventory.
            try
            {
                Repair(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"AutoRepair: repairing on open failed, the repair button still works: {ex}");
            }
        }

        private static void Repair(InventoryGui gui)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            // Only at a station, and only one that repairs at all. The plain
            // inventory and a chest open as they always did.
            var station = player.GetCurrentCraftingStation();
            if (station == null || !station.m_canRepair) return;
            if (!station.CheckUsable(player, showMessage: false)) return;

            var inventory = player.GetInventory();
            if (inventory == null) return;

            Worn.Clear();
            inventory.GetWornItems(Worn);

            int repaired = 0;
            string lastName = null;
            foreach (var item in Worn)
            {
                if (!CanRepair(gui, item)) continue;

                // As vanilla's RepairOneItem: the skill gain is the share of
                // the item's durability that was missing.
                player.RaiseSkill(Skills.SkillType.Crafting, 1f - item.m_durability / item.GetMaxDurability());
                item.m_durability = item.GetMaxDurability();
                lastName = item.m_shared.m_name;
                repaired++;
            }
            Worn.Clear();

            if (repaired == 0) return;

            station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
            player.Message(MessageHud.MessageType.Center, repaired == 1
                ? Localization.instance.Localize("$msg_repaired", lastName)
                : $"{Localization.instance.Localize("$msg_repaired", string.Empty).Trim()} x{repaired}");
        }

        /// <summary>Vanilla's own check: the item's recipe against this station's type and level.</summary>
        private static bool CanRepair(InventoryGui gui, ItemDrop.ItemData item)
        {
            s_canRepair = s_canRepair ?? AccessTools.Method(typeof(InventoryGui), "CanRepair");
            if (s_canRepair == null) throw new InvalidOperationException("InventoryGui.CanRepair not found");

            return (bool)s_canRepair.Invoke(gui, new object[] { item });
        }
    }
}
