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
        /// Gives items to a player, spilling to the ground whatever didn't
        /// actually land in the inventory. Never silently deletes: a drawer
        /// that eats your iron is worse than one that drops it at your feet.
        ///
        /// This does NOT ask CanAddItem to predict whether AddItem will
        /// succeed -- they are different functions with different rules and
        /// are not guaranteed to agree, in more ways than one. Matching
        /// AddItem's own m_worldLevel stamp on a CanAddItem probe (an
        /// earlier version of this method did exactly that) still leaves a
        /// second, independent asymmetry: CanAddItem counts headroom with
        /// FindFreeStackSpace, which ignores m_quality, while AddItem merges
        /// with FindFreeStackItem, which requires it. Two partial stacks of
        /// the same item at different quality, no empty slot: CanAddItem
        /// counts both and says yes; AddItem can only merge into the
        /// matching-quality stack, finds no second target, and fails --
        /// reopening the same merge-then-fail spill-the-whole-chunk hole a
        /// prediction-based check can never fully close.
        ///
        /// Instead this measures the real outcome: count this item's total
        /// in the inventory before calling AddItem, call it, count again,
        /// and spill only the shortfall between the chunk offered and what
        /// the count actually went up by. This is correct regardless of how
        /// AddItem's internal matching rules work or ever change, because it
        /// never has to replicate them.
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
                int before = CountMatching(inventory, itemName);
                inventory.AddItem(prefab, chunk);
                int added = CountMatching(inventory, itemName) - before;

                int shortfall = chunk - added;
                if (shortfall > 0) SpillOneStack(prefab, spillPos, shortfall);
            }
        }

        /// <summary>
        /// Total stack count of a prefab-identified item across an
        /// inventory. Not Inventory.CountItems(string, ...): that matches
        /// against m_shared.m_name (a localization token), the same trap
        /// RemoveItem(string, ...) has -- this instead uses PrefabNameOf,
        /// the one consistent identity rule, on every stack.
        ///
        /// This assumes prefab name and m_shared.m_name are in 1:1
        /// correspondence, which every vanilla item satisfies but nothing
        /// enforces. AddItem's own merge target (FindFreeStackItem) keys on
        /// m_shared.m_name, not prefab name; if two distinct stackable
        /// item prefabs ever shared one display-name token (realistically
        /// only via another mod adding such an item), AddItem could merge
        /// into a stack this count does not recognise as the same item,
        /// undercounting `added` in GiveToPlayer above and over-spilling
        /// the difference. Not reachable with any vanilla item today.
        /// </summary>
        private static int CountMatching(Inventory inventory, string itemName)
        {
            int total = 0;
            foreach (var item in inventory.GetAllItems())
                if (PrefabNameOf(item) == itemName) total += item.m_stack;
            return total;
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
