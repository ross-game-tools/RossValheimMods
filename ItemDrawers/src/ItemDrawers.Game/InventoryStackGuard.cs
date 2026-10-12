using System;
using System.Collections.Generic;
using HarmonyLib;
using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Splits any item whose stack exceeds its max stack size as it enters
    /// a real inventory. A drawer's view shows slot 1 as one oversized
    /// stack so other mods can see the whole count; take-all and move mods
    /// copy that ItemData whole, and without this a player or chest would
    /// hold an oversized stack that Inventory.Load truncates on the next
    /// load. Generic on purpose: the item's origin does not matter.
    ///
    /// Each prefix returns true (vanilla runs untouched) unless the incoming
    /// amount is above the max stack size. When it takes over it tops up
    /// matching partial stacks, places the rest as max-size stacks, leaves
    /// whatever did not fit on the original item's m_stack, returns false
    /// through __result exactly as vanilla reports "did not fit" (so MoveAll
    /// and similar callers keep the remainder at the source), and fires
    /// Changed once, as vanilla does.
    ///
    /// When a guard returns false with a remainder, that remainder is still
    /// sitting on item.m_stack when any postfix on the same AddItem call
    /// runs -- postfixes see the reduced count, not the pre-split original.
    /// A Harmony transpiler targeting one of the three AddItem overloads is
    /// bypassed entirely for oversized items, since the prefix short-circuits
    /// the original method body. The positional AddItem(item, amount, x, y)
    /// guard (AddSplitToSlot) additionally tops up other matching partial
    /// stacks elsewhere in the inventory, beyond the single target cell
    /// vanilla inspects -- lossless, since anything not placed stays on
    /// item.m_stack exactly as the other two guards leave it.
    ///
    /// DropItemPatch guards a fourth entry point that is not itself an
    /// AddItem overload: InventoryGrid.DropItem's swap branch removes an
    /// item from its source inventory before re-adding it elsewhere, twice
    /// over (the dragged item, then the item it displaces). Either side of
    /// that swap can be the oversized one -- an oversized item dragged onto
    /// a normal stack, or a normal item dragged onto an oversized one -- and
    /// either way, if the guarded AddItem call it feeds into only places
    /// part of the stack, the remainder is already removed from its source
    /// with nowhere to go. See that patch's comment.
    /// </summary>
    internal static class InventoryStackGuard
    {
        private static bool IsOversized(Inventory inventory, ItemDrop.ItemData item, int moving) =>
            InventoryAccess.Available
            && inventory != null && item != null && item.m_shared != null
            && item.m_shared.m_maxStackSize > 1
            && moving > item.m_shared.m_maxStackSize
            && !InventoryAccess.IsTemporary(inventory);

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
        private static class AddItemPatch
        {
            private static bool Prepare() =>
                InventoryAccess.Available
                && ValheimCompat.RequireMethod(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) });

            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
            {
                if (!IsOversized(__instance, item, item == null ? 0 : item.m_stack)) return true;
                __result = AddSplit(__instance, item, null, markCheatedOnTopUp: true);
                return false;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) })]
        private static class AddItemAtPositionPatch
        {
            private static bool Prepare() =>
                InventoryAccess.Available
                && ValheimCompat.RequireMethod(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) });

            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, Vector2i pos, ref bool __result)
            {
                if (!IsOversized(__instance, item, item == null ? 0 : item.m_stack)) return true;
                __result = AddSplit(__instance, item, pos, markCheatedOnTopUp: false);
                return false;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) })]
        private static class AddItemToSlotPatch
        {
            private static bool Prepare() =>
                InventoryAccess.Available
                && ValheimCompat.RequireMethod(typeof(Inventory), nameof(Inventory.AddItem),
                    new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });

            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, int amount, int x, int y, ref bool __result)
            {
                if (item == null) return true;
                int moving = Math.Min(amount, item.m_stack);
                if (!IsOversized(__instance, item, moving)) return true;

                // Out of bounds: vanilla refuses. Occupied: vanilla only tops
                // up that slot to its max, which can never be oversized.
                if (x < 0 || y < 0 || x >= __instance.GetWidth() || y >= __instance.GetHeight()) return true;
                if (__instance.GetItemAt(x, y) != null) return true;

                __result = AddSplitToSlot(__instance, item, moving, new Vector2i(x, y));
                return false;
            }
        }

        /// <summary>
        /// InventoryGrid.DropItem's swap branch -- target cell holds a
        /// different item and the whole dragged stack is being dropped --
        /// does, in order:
        ///   1. fromInventory.RemoveItem(item) -- the dragged item is gone
        ///      from its source right away.
        ///   2. fromInventory.MoveItemToThis(m_inventory, itemAt, itemAt.m_stack, oldPos)
        ///      -- moves the displaced item back to the drag's old slot.
        ///   3. m_inventory.MoveItemToThis(fromInventory, item, amount, pos)
        ///      -- moves the dragged item into the now-vacated target cell.
        /// Both (2) and (3) go through our guarded positional AddItem
        /// (ItemData, int, int, int, bool). Two different items can be the
        /// oversized one here, and both lose items if the guard only places
        /// part of the stack: an oversized item dragged onto a normal target
        /// (step 3 places part of the drag, item was already removed from
        /// fromInventory in step 1); or a normal item dragged onto an
        /// oversized target (step 2 places only part of itemAt back into
        /// fromInventory at oldPos -- itemAt was never removed from
        /// m_inventory so its remainder lives on, but then step 3 finds pos
        /// still occupied by the rest of itemAt, AddItem returns false, and
        /// the dragged item -- already removed in step 1 -- is lost).
        /// Refuse the whole drop whenever either side of the swap is
        /// oversized: nothing moves, the drag stays on the source item,
        /// exactly as if the drop target were invalid. Whether fromInventory
        /// actually has room for the swap is not evaluated -- always
        /// refusing here is safe, just occasionally more conservative than
        /// vanilla would have been. Oversized drags onto an empty cell or a
        /// matching partial stack take the plain MoveItemToThis path below
        /// (no RemoveItem-before-add), which AddItemToSlotPatch already
        /// handles losslessly.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        private static class DropItemPatch
        {
            private static bool Prepare() =>
                InventoryAccess.Available
                && ValheimCompat.RequireMethod(typeof(InventoryGrid), nameof(InventoryGrid.DropItem));

            // Harmony003 misfires here: it flags reading pos.x/pos.y (a
            // value-type patch parameter's fields) as a no-effect mutation.
            // Nothing is ever assigned to pos or its fields below.
#pragma warning disable Harmony003
            private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, int amount, Vector2i pos, ref bool __result)
            {
                if (item == null || item.m_shared == null) return true;

                var destInventory = __instance.GetInventory();
                if (destInventory == null) return true;

                var itemAt = destInventory.GetItemAt(pos.x, pos.y);
                if (itemAt == null || itemAt == item) return true; // empty cell, or no-op: safe to let through

                bool wouldSwap = (itemAt.m_shared.m_name != item.m_shared.m_name
                        || (item.m_shared.m_maxQuality > 1 && itemAt.m_quality != item.m_quality)
                        || itemAt.m_shared.m_maxStackSize == 1)
                    && item.m_stack == amount;
                if (!wouldSwap) return true; // matching stack: AddItemToSlotPatch handles it losslessly

                bool draggedOversized = item.m_stack > item.m_shared.m_maxStackSize;
                bool targetOversized = itemAt.m_shared != null && itemAt.m_stack > itemAt.m_shared.m_maxStackSize;
                if (!draggedOversized && !targetOversized) return true; // neither side oversized: ordinary swap

                __result = false;
                return false;
            }
#pragma warning restore Harmony003
        }

        /// <summary>Guarded AddItem(ItemData) and AddItem(ItemData, Vector2i).</summary>
        internal static bool AddSplit(Inventory inventory, ItemDrop.ItemData item, Vector2i? target, bool markCheatedOnTopUp)
        {
            bool cheatedStateChanged = item.m_cheated && !Achievements.IsCheatedAtAll();
            var items = InventoryAccess.Items(inventory);
            var partials = FindPartialStacks(items, item, out var freeSpace);
            int incoming = item.m_stack;

            var plan = StackSplit.Plan(freeSpace, inventory.GetEmptySlots(), incoming, item.m_shared.m_maxStackSize, firstStackToTarget: false);
            int moved = ApplyTopUps(partials, plan.TopUps, item, markCheatedOnTopUp);

            bool topFirst = InventoryAccess.TopFirst(inventory, item);
            bool planComplete = plan.Remainder == 0;
            bool placedAll = true;
            for (int s = 0; s < plan.NewStacks.Length; s++)
            {
                Vector2i pos = s == 0 && target.HasValue && IsFreeCell(inventory, target.Value)
                    ? target.Value
                    : InventoryAccess.FindEmptySlot(inventory, topFirst);
                if (pos.x < 0) { placedAll = false; break; }

                // Vanilla puts the incoming ItemData itself into the
                // inventory; keep that for the final stack when everything
                // fits, so callers holding the reference see the same thing.
                bool useOriginal = planComplete && s == plan.NewStacks.Length - 1;
                var stack = useOriginal ? item : item.Clone();
                stack.m_stack = plan.NewStacks[s];
                stack.m_gridPos = pos;
                items.Add(stack);
                if (!useOriginal) moved += plan.NewStacks[s];
            }

            bool success = planComplete && placedAll;
            if (!success) item.m_stack = incoming - moved;

            InventoryAccess.Changed(inventory, success, cheatedStateChanged);
            return success;
        }

        /// <summary>Guarded private AddItem(ItemData, int amount, int x, int y, bool); (x, y) is in bounds and empty.</summary>
        internal static bool AddSplitToSlot(Inventory inventory, ItemDrop.ItemData item, int moving, Vector2i target)
        {
            var localPlayer = Player.m_localPlayer;
            bool cheatedStateChanged = item.m_cheated && !Achievements.IsCheatedAtAll()
                                       && localPlayer != null && localPlayer.GetInventory() == inventory;

            var items = InventoryAccess.Items(inventory);
            var partials = FindPartialStacks(items, item, out var freeSpace);

            var plan = StackSplit.Plan(freeSpace, inventory.GetEmptySlots(), moving, item.m_shared.m_maxStackSize, firstStackToTarget: true);
            int moved = ApplyTopUps(partials, plan.TopUps, item, markCheated: false);

            bool topFirst = InventoryAccess.TopFirst(inventory, item);
            for (int s = 0; s < plan.NewStacks.Length; s++)
            {
                Vector2i pos = s == 0 ? target : InventoryAccess.FindEmptySlot(inventory, topFirst);
                if (pos.x < 0) break;

                var stack = item.Clone();
                stack.m_stack = plan.NewStacks[s];
                stack.m_gridPos = pos;
                items.Add(stack);
                moved += plan.NewStacks[s];
            }

            item.m_stack -= moved;
            if (Player.m_localPlayerExists) InventoryAccess.Changed(inventory, true, cheatedStateChanged);
            return moved == moving;
        }

        /// <summary>Stacks vanilla's FindFreeStackItem would top up, in the same (list) order.</summary>
        private static List<ItemDrop.ItemData> FindPartialStacks(List<ItemDrop.ItemData> items, ItemDrop.ItemData incoming, out List<int> freeSpace)
        {
            var partials = new List<ItemDrop.ItemData>();
            freeSpace = new List<int>();
            foreach (var existing in items)
            {
                if (existing.m_shared.m_name != incoming.m_shared.m_name) continue;
                if (existing.m_quality != incoming.m_quality || existing.m_worldLevel != incoming.m_worldLevel) continue;
                if (existing.m_stack >= existing.m_shared.m_maxStackSize) continue;
                partials.Add(existing);
                freeSpace.Add(existing.m_shared.m_maxStackSize - existing.m_stack);
            }
            return partials;
        }

        private static int ApplyTopUps(List<ItemDrop.ItemData> partials, int[] topUps, ItemDrop.ItemData incoming, bool markCheated)
        {
            int total = 0;
            for (int i = 0; i < topUps.Length; i++)
            {
                if (topUps[i] <= 0) continue;
                partials[i].m_stack += topUps[i];
                if (markCheated && incoming.m_cheated && !PlayerProfile.s_bypassCheatChecks)
                    partials[i].m_cheated = true;
                total += topUps[i];
            }
            return total;
        }

        private static bool IsFreeCell(Inventory inventory, Vector2i pos) =>
            pos.x >= 0 && pos.y >= 0 && pos.x < inventory.GetWidth() && pos.y < inventory.GetHeight()
            && inventory.GetItemAt(pos.x, pos.y) == null;
    }
}
