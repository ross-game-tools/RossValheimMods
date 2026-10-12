# How items leave one inventory for another (read at 1.0.17)

Every vanilla path that takes an `ItemDrop.ItemData` out of a container
and puts it somewhere else, from the decompiled `assembly_valheim.dll`.
Use this when an item in a container carries state that must not follow
it out (ItemDrawers' single-stack view, issue #17).

## Into another inventory: always one of three `AddItem` overloads

| Caller | Path |
|---|---|
| Drag and drop (`InventoryGrid.DropItem`) | `Inventory.MoveItemToThis(from, item, amount, x, y)` → private `AddItem(item, amount, x, y, bool)`, then `from.RemoveItem(item)` |
| Swap branch of `InventoryGrid.DropItem` | `fromInventory.MoveItemToThis(m_inventory, itemAt, ...)` then the dragged item, both as above |
| Shift/ctrl-click (`InventoryGui.OnSelectedItem`) | `MoveItemToThis(from, item)` → `AddItem(item)` |
| Take All (`InventoryGui.OnTakeAll`, `Container.RPC_TakeAllResponse`) | `Inventory.MoveAll(from)` → `AddItem(item, item.m_stack, x, y)` for the same cell, else `AddItem(item)` |
| Pickup from the ground (`Humanoid.Pickup`) | `AddItem(component.m_itemData)` |

`AddItem(ItemData)` merges through `FindFreeStackItem(name, quality,
worldLevel)`, which compares `m_shared.m_name` and
`item.m_stack < item.m_shared.m_maxStackSize` of the item **already in the
inventory**. Whatever is left is added as the incoming object itself
(`m_inventory.Add(item)`), keeping its `m_shared`. The private positional
overload `Clone()`s the item when moving part of a stack; a clone shares
`m_shared` (`ItemData.Clone` is `MemberwiseClone`).

Inventory matching never compares `m_shared` by reference; every match is
on `m_shared.m_name`. The one reference lookup is `ObjectDB.m_itemByData`
(`Dictionary<SharedData, GameObject>`, filled from the prefabs' own
`SharedData`), behind `ObjectDB.GetItemPrefab(SharedData)` and
`TryGetItemPrefab(SharedData)`. Every inventory item already carries its
instance's copy (`item-instances.md`), so that lookup misses for them in
vanilla too; its callers (`Player.AddTrophy`, the eat-hand visual) fall back
to `m_dropPrefab` or only lose a visual.

`CanAddItem(ItemData, int)` sizes empty slots by the **incoming** item's
`m_shared.m_maxStackSize`:
`FindFreeStackSpace(name, worldLevel) + emptySlots * item.m_shared.m_maxStackSize >= stack`.

## Onto the ground: `ItemDrop.DropItem`

`Humanoid.DropItem` (drag outside the grid, the drop button) calls
`ItemDrop.DropItem(item, amount, ...)`, which instantiates `m_dropPrefab`
and then **replaces** the new instance's `m_itemData` with `item.Clone()`.
The ground item therefore carries the source item's `m_shared`, not one
from its own `Awake`. `Container.DropAllItems` does the same per item; a
drawer never runs it (its `Container.Awake` body is skipped).

## Persistence heals `m_shared`

Neither `Inventory.Save` nor `ItemDrop.Save` writes `SharedData`. On load,
an item is rebuilt from its prefab name, so a stray `m_shared` lasts only
until the next load. A stack above the real max size is worse than
stray: `Inventory.Load` rebuilds it with
`m_stack = Mathf.Min(stack, m_shared.m_maxStackSize)`, so everything over
the max is silently lost.
