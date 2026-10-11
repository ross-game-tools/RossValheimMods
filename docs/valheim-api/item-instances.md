# Item instances and SharedData (read at 1.0.17)

## Every item instance owns its own SharedData

`ItemDrop.ItemData.m_shared` is a `[Serializable]` plain class, so
`Object.Instantiate` of an item prefab deep-copies it. `ItemDrop.Awake`
only points it back at the prefab's copy in the editor:

```csharp
if (Application.isEditor)
{
    m_itemData.m_shared = itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared;
}
```

In a built game each instance keeps its own copy.

## How vanilla makes an inventory item

`Inventory.AddItem(string name, int stack, ...)` never clones the prefab's
`m_itemData`. Per stack it does:

```csharp
ZNetView.m_forceDisableInit = true;
GameObject gameObject = UnityEngine.Object.Instantiate(itemPrefab);
ZNetView.m_forceDisableInit = false;
ItemDrop component2 = gameObject.GetComponent<ItemDrop>();
int num2 = Mathf.Min(num, component2.m_itemData.m_shared.m_maxStackSize);
...
```

then adds that instance's `m_itemData` and destroys the object. Inventory
load goes through the same path, so every item a player holds came from an
awakened instance.

`ItemData.Clone()` is `MemberwiseClone` plus a new `m_customData`
dictionary: `m_shared` is shared by reference with the source.

## Why it matters

Mods that change item stats in an `ItemDrop.Awake` prefix change only
instances. Valheim Plus 0.10.2 does exactly this (`ItemDrop_Awake_Patch`:
`m_maxStackSize` multiplier, `m_weight` reduction, `m_teleportable`). ObjectDB
prefabs never run `Awake`, so `ObjectDB.GetItemPrefab(name)
.GetComponent<ItemDrop>().m_itemData` still has vanilla values. An item made
by cloning the prefab's data misses every such change (ItemDrawers issue #16:
50-stacks beside 300-stacks). Make items the way vanilla does, or clone from
an awakened instance.
