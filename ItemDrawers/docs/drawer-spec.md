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
