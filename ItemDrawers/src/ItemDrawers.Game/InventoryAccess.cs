using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Cached, name-based access to the Inventory internals the drawer view
    /// and the oversized-stack guard need. Resolved once; if any member is
    /// gone after a Valheim update, Available is false and every caller
    /// falls back to vanilla behaviour instead of throwing inside AddItem
    /// or GetInventory. ValheimCompat lists the same members so the startup
    /// log names what disappeared.
    /// </summary>
    internal static class InventoryAccess
    {
        internal static readonly bool Available;

        private static readonly AccessTools.FieldRef<Inventory, List<ItemDrop.ItemData>> ItemsRef;
        private static readonly AccessTools.FieldRef<Inventory, int> WidthRef;
        private static readonly AccessTools.FieldRef<Inventory, int> HeightRef;
        private static readonly Action<Inventory, bool, bool> ChangedCall;
        private static readonly FieldInfo TemporaryField;
        private static readonly Func<Inventory, bool, Vector2i> FindEmptySlotCall;
        private static readonly Func<Inventory, ItemDrop.ItemData, bool> TopFirstCall;

        static InventoryAccess()
        {
            try
            {
                ItemsRef = AccessTools.FieldRefAccess<Inventory, List<ItemDrop.ItemData>>("m_inventory");
                WidthRef = AccessTools.FieldRefAccess<Inventory, int>("m_width");
                HeightRef = AccessTools.FieldRefAccess<Inventory, int>("m_height");
                ChangedCall = AccessTools.MethodDelegate<Action<Inventory, bool, bool>>(
                    AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) }));
                TemporaryField = AccessTools.Field(typeof(Inventory), "m_temoraryInventory")
                                 ?? throw new MissingFieldException(nameof(Inventory), "m_temoraryInventory");
                FindEmptySlotCall = AccessTools.MethodDelegate<Func<Inventory, bool, Vector2i>>(
                    AccessTools.Method(typeof(Inventory), "FindEmptySlot", new[] { typeof(bool) }));
                TopFirstCall = AccessTools.MethodDelegate<Func<Inventory, ItemDrop.ItemData, bool>>(
                    AccessTools.Method(typeof(Inventory), "TopFirst", new[] { typeof(ItemDrop.ItemData) }));
                Available = true;
            }
            catch (Exception ex)
            {
                Available = false;
                DrawerPlugin.Log?.LogError(
                    "Inventory internals not found; drawers will be invisible to other mods. "
                    + $"See the compatibility check at startup. {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>The live backing list. Adding to it bypasses AddItem (and so the stack guard) and Changed().</summary>
        internal static List<ItemDrop.ItemData> Items(Inventory inventory) => ItemsRef(inventory);

        internal static ref int Width(Inventory inventory) => ref WidthRef(inventory);

        internal static ref int Height(Inventory inventory) => ref HeightRef(inventory);

        /// <summary>Inventory.Changed: recomputes weight and fires m_onChanged.</summary>
        internal static void Changed(Inventory inventory, bool success, bool cheatedStateChanged) =>
            ChangedCall(inventory, success, cheatedStateChanged);

        /// <summary>Trader-UI inventories; vanilla skips bounds and Changed for them. Reflection, so call it last.</summary>
        internal static bool IsTemporary(Inventory inventory) => (bool)TemporaryField.GetValue(inventory);

        /// <summary>Inventory.FindEmptySlot: (-1, -1) when there is none.</summary>
        internal static Vector2i FindEmptySlot(Inventory inventory, bool topFirst) => FindEmptySlotCall(inventory, topFirst);

        internal static bool TopFirst(Inventory inventory, ItemDrop.ItemData item) => TopFirstCall(inventory, item);
    }
}
