using System.Runtime.CompilerServices;
using HarmonyLib;

namespace ItemDrawers.Game
{
    /// <summary>
    /// The drawer-owned SharedData copies a drawer view's stack carries, and
    /// the healing that keeps them inside drawer views.
    ///
    /// A view shows its stock as one stack whose max stack size is the
    /// drawer's capacity (ViewLayout). That size lives on SharedData, which
    /// every ItemData of a kind normally shares, so each view gets its own
    /// copy. ItemData.Clone is a MemberwiseClone and every vanilla transfer
    /// moves or clones the ItemData (docs/valheim-api/inventory-transfers.md),
    /// so the copy follows anything taken out of the view: a player could
    /// hold a stack that grows to 2000, and Inventory.Load would cut it back
    /// to the real max on the next load, losing the rest.
    ///
    /// So: InventoryStackGuard splits by the REAL max (RealMaxStack) on the
    /// way in, every non-view inventory swaps the copy back for the real
    /// SharedData in Inventory.Changed, which every vanilla mutation calls,
    /// and a dropped item is swapped back in ItemDrop.DropItem. Neither
    /// Inventory.Save nor ItemDrop.Save writes SharedData, so anything that
    /// still slips through heals on the next load -- only an oversized count
    /// would not, and the guard is what prevents that.
    ///
    /// Inventory matching is by m_shared.m_name, never by reference. The one
    /// reference lookup, ObjectDB.GetItemPrefab(SharedData), already misses
    /// every inventory item -- each carries its instance's own copy
    /// (docs/valheim-api/item-instances.md) -- so a view's copy changes
    /// nothing there either.
    ///
    /// Vanilla's positional AddItem is the one add that calls Changed only
    /// with a local player; InventoryStackGuard heals after it directly, so
    /// a dedicated server is covered too. Only a mod writing straight into
    /// an inventory's item list, skipping AddItem and Changed, gets past all
    /// of this -- the same exposure such a mod has always had.
    /// </summary>
    internal static class ViewSharedData
    {
        // Copy -> the real SharedData it was made from. Weak, so a copy
        // dropped by its view (item changed, drawer unloaded) is collected
        // once nothing references it.
        private static readonly ConditionalWeakTable<ItemDrop.ItemData.SharedData, ItemDrop.ItemData.SharedData> RealOf =
            new ConditionalWeakTable<ItemDrop.ItemData.SharedData, ItemDrop.ItemData.SharedData>();

        // View inventory -> the view, for the copy it owns.
        private static readonly ConditionalWeakTable<Inventory, DrawerView> Views = new ConditionalWeakTable<Inventory, DrawerView>();

        private static readonly System.Reflection.MethodInfo MemberwiseCloneMethod =
            AccessTools.Method(typeof(object), "MemberwiseClone");

        internal static ItemDrop.ItemData.SharedData CopyOf(ItemDrop.ItemData.SharedData real)
        {
            var copy = (ItemDrop.ItemData.SharedData)MemberwiseCloneMethod.Invoke(real, null);
            RealOf.Add(copy, real);
            return copy;
        }

        internal static void RegisterView(Inventory inventory, DrawerView view) => Views.Add(inventory, view);

        internal static bool IsView(Inventory inventory) => inventory != null && Views.TryGetValue(inventory, out _);

        /// <summary>The SharedData the item really has: its own, unless it carries a view's copy.</summary>
        internal static ItemDrop.ItemData.SharedData Real(ItemDrop.ItemData.SharedData shared) =>
            shared != null && RealOf.TryGetValue(shared, out var real) ? real : shared;

        internal static int RealMaxStack(ItemDrop.ItemData item) => Real(item.m_shared).m_maxStackSize;

        internal static void Heal(ItemDrop.ItemData item)
        {
            if (item?.m_shared != null && RealOf.TryGetValue(item.m_shared, out var real))
                item.m_shared = real;
        }

        /// <summary>
        /// Swaps any view copy in an inventory back for the real SharedData,
        /// except, in a view, that view's own copy. An item dragged from one
        /// drawer into another's empty slot would otherwise keep the first
        /// drawer's capacity as its limit until the second one rebuilds.
        /// </summary>
        internal static void Heal(Inventory inventory)
        {
            if (inventory == null) return;
            var own = Views.TryGetValue(inventory, out var view) ? view.OwnShared : null;
            var items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                if (own != null && ReferenceEquals(items[i]?.m_shared, own)) continue;
                Heal(items[i]);
            }
        }

        /// <summary>
        /// Prefix, so weight and every m_onChanged listener already see the
        /// real SharedData. Changed(bool, bool) is called at the end of every
        /// vanilla add, move and remove.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) })]
        private static class InventoryChangedPatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

            private static void Prefix(Inventory __instance) => Heal(__instance);
        }

        /// <summary>
        /// CanAddItem sizes empty slots by the INCOMING item's max stack
        /// size; for an item carrying a view's copy that is the drawer's
        /// capacity, so it would promise room the guard then refuses to use.
        /// A mod that checks CanAddItem, removes the item from the drawer and
        /// then adds it would lose the part that did not fit.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), new[] { typeof(ItemDrop.ItemData), typeof(int) })]
        private static class CanAddItemPatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Inventory), nameof(Inventory.CanAddItem), new[] { typeof(ItemDrop.ItemData), typeof(int) });

            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
            {
                if (item?.m_shared == null || !RealOf.TryGetValue(item.m_shared, out var real)) return true;

                int wanted = stack <= 0 ? item.m_stack : stack;
                long emptySlots = __instance.GetWidth() * __instance.GetHeight() - __instance.NrOfItems();
                __result = __instance.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel)
                           + emptySlots * real.m_maxStackSize >= wanted;
                return false;
            }
        }

        /// <summary>
        /// ItemDrop.DropItem instantiates the item's prefab and then replaces
        /// the new instance's m_itemData with item.Clone(), so a stack dropped
        /// from a drawer's view lands with the view's copy.
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem))]
        private static class DropItemPatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(ItemDrop), nameof(ItemDrop.DropItem));

            private static void Postfix(ItemDrop __result)
            {
                if (__result != null) Heal(__result.m_itemData);
            }
        }
    }
}
