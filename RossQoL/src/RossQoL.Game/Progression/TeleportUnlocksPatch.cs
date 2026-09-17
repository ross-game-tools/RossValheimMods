using System;
using HarmonyLib;
using RossQoL.Core.Progression;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// Inventory.IsTeleportable answers "may this go through a portal" for a
    /// whole inventory: nothing above tool tier 1000 ever, everything when
    /// TeleportAll is set, otherwise every item's own teleportable flag.
    ///
    /// The prefix answers the same question with one exception -- an item
    /// whose biome's boss is dead counts as teleportable -- and skips
    /// vanilla. Replacing the answer rather than editing item data means
    /// nothing is written into saved items: switch the feature off and the
    /// ore is refused again, with no trace left behind.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.IsTeleportable))]
    internal static class TeleportUnlocksPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Inventory), nameof(Inventory.IsTeleportable), TeleportUnlocksFeature.FeatureName);

        private static bool Prefix(Inventory __instance, bool allowAllItems, ref bool __result)
        {
            if (TeleportUnlocksFeature.Instance?.IsActive != true) return true;

            try
            {
                // As vanilla: a tool above tier 1000 pins you in place whatever
                // else is true.
                foreach (var item in __instance.m_inventory)
                {
                    if (item?.m_shared == null) continue;
                    if (item.m_shared.m_toolTier >= 1000)
                    {
                        __result = false;
                        return false;
                    }
                }

                if (allowAllItems || ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll))
                {
                    __result = true;
                    return false;
                }

                foreach (var item in __instance.m_inventory)
                {
                    if (item?.m_shared == null) continue;
                    if (item.m_shared.m_teleportable) continue;
                    if (IsUnlocked(item)) continue;

                    __result = false;
                    return false;
                }

                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"TeleportUnlocks: could not check an inventory, leaving vanilla's answer: {ex}");
                return true;
            }
        }

        /// <summary>
        /// The item's prefab name is what the unlock table is keyed on: the
        /// shared name is a localisation token, and a dropped prefab may be
        /// missing on an item built in code.
        /// </summary>
        private static bool IsUnlocked(ItemDrop.ItemData item)
        {
            string prefab = item.m_dropPrefab ? item.m_dropPrefab.name : null;
            if (string.IsNullOrEmpty(prefab)) return false;

            return TeleportUnlocks.IsUnlocked(prefab, HasKey);
        }

        private static bool HasKey(string key) =>
            ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
    }
}
