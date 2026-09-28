using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>Thin wrapper over ObjectDB so the rest of the code needn't repeat null checks.</summary>
    internal static class ItemFacts
    {
        public static GameObject Prefab(string itemName) =>
            string.IsNullOrEmpty(itemName) || ObjectDB.instance == null
                ? null
                : ObjectDB.instance.GetItemPrefab(itemName);

        public static ItemDrop Drop(string itemName)
        {
            var prefab = Prefab(itemName);
            return prefab == null ? null : prefab.GetComponent<ItemDrop>();
        }

        public static bool IsStorable(string itemName)
        {
            var drop = Drop(itemName);
            return drop != null && drop.m_itemData.m_shared.m_maxStackSize > 1;
        }

        public static int MaxStackSize(string itemName)
        {
            var drop = Drop(itemName);
            return drop == null ? 1 : drop.m_itemData.m_shared.m_maxStackSize;
        }

        public static string LocalizedName(string itemName)
        {
            var drop = Drop(itemName);
            return drop == null
                ? itemName
                : Localization.instance.Localize(drop.m_itemData.m_shared.m_name);
        }

        public static Sprite Icon(string itemName)
        {
            var drop = Drop(itemName);
            return drop == null ? null : SafeIcon(drop.m_itemData);
        }

        /// <summary>
        /// An item's icon, without trusting <c>ItemData.GetIcon()</c> not to
        /// throw.
        ///
        /// GetIcon decompiles to a bare <c>return m_shared.m_icons[m_variant];</c>
        /// with no bounds check at all, and Valheim ships items whose
        /// m_variant is outside their own m_icons array -- confirmed on
        /// 1.0.12 for draugr_arrow, GoblinSpear and GoblinSpearDeepNorth
        /// (github.com/ross-game-tools/RossValheimMods/issues/3). The last
        /// of those is a new 1.0 item, so this is a live data shape rather
        /// than a historical quirk, and the set can be expected to change
        /// again between versions.
        ///
        /// Two distinct shapes end up here, and they deserve different
        /// answers:
        ///
        /// An item with icons but an out-of-range variant still has a
        /// perfectly good icon at index 0, so it gets that rather than
        /// being dropped -- the only thing wrong with it is the index.
        ///
        /// An item with an EMPTY icon array has nothing to show and returns
        /// null. That is the likely shape of the three items in issue #3:
        /// a base prefab in ObjectDB has m_variant 0 (variants are assigned
        /// to instances, not prefabs), and with variant 0 the only way
        /// GetIcon can throw is an empty array. All three are mob-only
        /// items that never reach a player inventory, so having no icon is
        /// correct for them and no drawer can ever hold one. They are
        /// skipped either way; what changes is that it is no longer an
        /// exception and a warning.
        /// </summary>
        public static Sprite SafeIcon(ItemDrop.ItemData item) => SafeIcon(item, out _);

        /// <summary>
        /// As <see cref="SafeIcon(ItemDrop.ItemData)"/>, reporting whether the
        /// fallback was needed. The atlas build uses this to name the affected
        /// items once per boot, so the condition stays visible instead of
        /// being silently papered over -- the item set is game-version
        /// dependent and worth knowing about when it changes.
        /// </summary>
        public static Sprite SafeIcon(ItemDrop.ItemData item, out bool variantOutOfRange)
        {
            variantOutOfRange = false;

            var icons = item?.m_shared?.m_icons;
            if (icons == null || icons.Length == 0) return null;

            int variant = item.m_variant;
            if ((uint)variant < (uint)icons.Length) return icons[variant];

            variantOutOfRange = true;
            return icons[0];
        }

        /// <summary>
        /// The identity to persist for an inventory item: the prefab name,
        /// never the localized m_shared.m_name. Null when it cannot be
        /// determined (no m_dropPrefab) -- callers must refuse the deposit
        /// in that case rather than fall back to a name that a drawer's ZDO
        /// can never look back up (I4).
        /// </summary>
        public static string PrefabNameOf(ItemDrop.ItemData item) =>
            item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;

        /// <summary>
        /// Gives items to a player, spilling to the ground only what the
        /// inventory itself reported it could not take. Never silently
        /// deletes: a drawer that eats your iron is worse than one that
        /// drops it at your feet.
        ///
        /// The amount to spill comes from AddItem's OWN return value, never
        /// from re-counting player.GetInventory() afterwards. That is the
        /// whole fix for issue #12: mods that extend player storage
        /// (AdventureBackpacks, AzuExtendedPlayerInventory, shudnal's
        /// ExtraSlots) patch Inventory.AddItem and route the item into a worn
        /// backpack or a dedicated slot -- somewhere player.GetInventory()
        /// .m_inventory never lists it. The previous version measured only
        /// that main list before and after the call, saw no increase, and
        /// spilled a second copy on the ground even though the item HAD been
        /// accepted -- duplicating every withdrawal made with such a mod
        /// installed.
        ///
        /// Vanilla Inventory.AddItem(ItemData) (buildid 25253764,
        /// assembly_valheim.dll) already reports exactly what is needed: it
        /// merges into FindFreeStackItem stacks, drops any remainder into a
        /// FindEmptySlot, and
        ///
        ///   if (gridPos.x >= 0) { m_inventory.Add(item); }   // remainder placed
        ///   else                { flag = false; }            // no room for it
        ///   return flag;
        ///
        /// so it returns true when the whole stack landed and false with the
        /// unplaced count left on item.m_stack when it did not. A storage-
        /// extending mod's patch on that same method is the one authority on
        /// whether the item was accepted, wherever it chose to put it, so
        /// trusting the return is correct regardless of destination and needs
        /// no knowledge of any specific mod. The ItemData is built here rather
        /// than via AddItem(GameObject, int) -- which builds one internally
        /// and discards it -- purely so the false-case remainder is readable;
        /// the field setup mirrors that overload exactly, so merge matching
        /// (keyed on m_shared.m_name, m_quality and m_worldLevel) is identical.
        ///
        /// The spill amount itself goes through DrawerState.RefundShortfall,
        /// the same clamped "removed minus accepted" boundary the deposit-
        /// refund path uses, so the give side is covered by the same Core
        /// arithmetic tests and can never spill a negative or an over-count.
        /// </summary>
        public static void GiveToPlayer(Player player, string itemName, int amount)
        {
            var drop = Drop(itemName);
            if (drop == null || amount <= 0) return;

            var prefab = drop.gameObject;
            int stackSize = drop.m_itemData.m_shared.m_maxStackSize;
            var inventory = player.GetInventory();
            var spillPos = player.transform.position + player.transform.forward + Vector3.up;

            foreach (var chunk in ChunkSplitter.Chunks(amount, stackSize))
            {
                // global::Game: inside a *.Game namespace the bare name Game
                // binds to the namespace, not the game type.
                var item = drop.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = chunk;                 // chunk <= stackSize already
                item.m_worldLevel = (byte)global::Game.m_worldLevel;

                int accepted = inventory.AddItem(item) ? chunk : chunk - item.m_stack;
                int shortfall = DrawerState.RefundShortfall(chunk, accepted);
                if (shortfall > 0) SpillOneStack(prefab, spillPos, shortfall);
            }
        }

        /// <summary>
        /// Drops a whole amount on the ground at a fixed position, split
        /// into stack-sized chunks. Used when a drawer is destroyed: its
        /// contents must land somewhere rather than vanish with the ZDO.
        /// </summary>
        public static void SpillAtPosition(Vector3 position, string itemName, int amount)
        {
            var prefab = Prefab(itemName);
            if (prefab == null || amount <= 0) return;

            int stackSize = MaxStackSize(itemName);
            foreach (var chunk in ChunkSplitter.Chunks(amount, stackSize))
                SpillOneStack(prefab, position, chunk);
        }

        private static void SpillOneStack(GameObject prefab, Vector3 position, int stack)
        {
            var dropped = Object.Instantiate(prefab, position, Quaternion.identity);
            var drop = dropped.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.m_itemData.m_stack = stack;
                drop.Save();
            }
        }
    }
}
