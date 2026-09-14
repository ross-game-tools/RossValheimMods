# Approved drawer proportions

Settled 2026-09-09 via Drawer Forge (interactive 3D preview).
Geometry is generated procedurally in C# from chamfered boxes — no
asset bundle, no modelled mesh, vanilla Valheim materials applied.

The overall cube size below (0.66m) is a later correction, not part of
the original interactive session: the 1m figure first approved here
turned out to be wrong, and was replaced once measured directly from
makail/KG's shipped `kg_itemdrawers` asset bundle (extracted with
UnityPy) rather than assumed. Every drawer variant's `Cube` transform
in that bundle has `scale = (0.66, 0.66, 0.66)` on a unit-AABB mesh,
and its snap points sit at ±0.33 on each axis — exactly half of 0.66 —
confirming the real footprint. Every other absolute-metre figure below
is scaled by the same 0.66 factor from the values approved in the
original 1m-cube preview session, so the proportions that session
approved are preserved rather than becoming relatively chunkier on the
smaller drawer.

```csharp
static class DrawerSpec
{
    public const float Width          = 0.660f;
    public const float Height         = 0.660f;
    public const float Depth          = 0.660f;
    public const float FrameThickness = 0.0330f;
    public const float RecessDepth    = 0.0198f;
    public const float Bevel          = 0.00396f;
    public const float HandleWidth    = 0.2640f;
    public const float HandleSection  = 0.0429f;
    public const float HandleProud    = 0.0231f;
    public const float LabelSize      = 0.456f;
    public const HandleStyle Handle   = HandleStyle.Bar;
}
```

For reference, the original 1m-cube figures approved in the interactive
session (before the measured 0.66 correction above superseded them):

```csharp
Width = Height = Depth = 1.000f;
FrameThickness = 0.050f;
RecessDepth    = 0.030f;
Bevel          = 0.0060f;
HandleWidth    = 0.400f;
HandleSection  = 0.065f;
HandleProud    = 0.035f;
LabelSize      = 0.691f;
```

Notes:
- 0.66m cube tiles flush in a wall; matches the measured footprint of
  makail/KG's original drawers, superseding the earlier (wrong) 1m
  assumption below.
- Bevel is small but non-zero — sharp 90 deg edges read as CG.
- LabelSize is derived (opening minus handle clearance), not free —
  regenerate it if Width/Height/FrameThickness/HandleSection change.
  0.456 = 0.691 × 0.66 to three decimals, confirming the scale-down was
  applied consistently rather than re-tuned by eye.
- The `BoxCollider` in `DrawerPieces.BuildPrefab` and the icon atlas
  material's triplanar texture scale both reference the drawer's own
  size independently of `DrawerProportions`; the collider was updated
  to 0.66m alongside this change, while the triplanar scale was left as
  a texture-density judgement call rather than a geometric proportion
  that has to track the resize.

Preview tool: https://claude.ai/code/artifact/2760da81-255c-47b7-83e6-c4498ae02526

# Container view (1.0)

What other mods see through `Container.GetInventory()` on a drawer. The
drawer's `Prefab` + `Amount` ZDO fields remain the only source of truth;
the view is a translation of them. Design:
`docs/superpowers/specs/2026-09-14-container-compat-design.md`.

## Layout (`ItemDrawers.Core.ViewLayout`)

For item max stack size *M*, capacity *C*, amount *A*:

- Unassigned, or *A* = 0: 0 slots (a 0×1 grid), so nothing can be added.
- Otherwise up to 8 slots, all the drawer's item, grid exactly the slot
  count (8 → 4×2, fewer → n×1), so a stocked view has no empty cell and
  vanilla `AddItem` refuses any other item.
- Slot 1 holds the rest and may exceed *M*; slots 2..n hold ≥1, raised
  evenly (larger values first), never above *M*, until the deposit room
  vanilla sees — Σ(*M* − count) over every slot below *M*, slot 1 included
  — is ≤ *C* − *A*. When no layout of n ≥ 2 slots meets that (very large
  *M* such as coins, or tiny capacities), fewer slots are used; one slot of
  *A* is the last resort.

| *A* (M=50, C=1000) | Slots |
|---|---|
| 0 | – |
| 3 | 1, 1, 1 |
| 500 | 493, 1 ×7 |
| 900 | 650, 36 ×5, 35 ×2 |
| 1000 | 650, 50 ×7 |

View items carry the drawer item's prefab, `Game.m_worldLevel`, and
`m_cheated = false`.

## Persistence

| ZDO key | Content |
|---|---|
| `ViewSlots` | ZPackage: `int` format version (1), `int` slot count (0–8), each slot's `int` count, `int` foreign-entry count, then (`string` prefab, `int` count) per foreign item |
| `ViewBaseline` | `int`: total of the drawer's item the view held when the owner last published it |

Absent on pre-1.0 drawers; the owner publishes them on first tick.

## Flow

1. A mod changes the view → `m_onChanged` marks it dirty.
2. `DrawerManager.LateUpdate` writes dirty views to `ViewSlots` from
   whichever peer changed them (`Container.Save` does the same
   immediately).
3. On the owner, `DrawerManager.Update` sees the ZDO revision change and,
   if the `ViewSlots` total differs from `ViewBaseline` (or holds foreign
   items, or the baseline no longer matches `Amount`), runs
   `DrawerComponent.ReconcileView`: `ViewReconciliation.Apply` credits
   (`DrawerState.Deposit`, excess spilled at the drawer) or debits
   (`DrawerState.WithdrawExact`), foreign items are spilled, then the view
   is republished.
4. Every write of `Amount` by the drawer's own code (`WriteOwned`) also
   reconciles and republishes.

## Oversized-stack guard (`InventoryStackGuard`)

Prefixes on `Inventory.AddItem(ItemData)`, `AddItem(ItemData, Vector2i)`
and the private `AddItem(ItemData, int, int, int, bool)`. An incoming
amount above max stack size is split: partial stacks topped up, the rest
placed as max-size stacks (the 4-argument overload places its first stack
in its target slot first), anything left stays on the source item and the
call returns false. `Changed()` fires once. Everything else runs vanilla.
