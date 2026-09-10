# Approved drawer proportions

Settled 2026-09-09 via Drawer Forge (interactive 3D preview).
Geometry is generated procedurally in C# from chamfered boxes — no
asset bundle, no modelled mesh, vanilla Valheim materials applied.

```csharp
static class DrawerSpec
{
    public const float Width          = 1.000f;
    public const float Height         = 1.000f;
    public const float Depth          = 1.000f;
    public const float FrameThickness = 0.050f;
    public const float RecessDepth    = 0.030f;
    public const float Bevel          = 0.0060f;
    public const float HandleWidth    = 0.400f;
    public const float HandleSection  = 0.065f;
    public const float HandleProud    = 0.035f;
    public const float LabelSize      = 0.691f;
    public const HandleStyle Handle   = HandleStyle.Bar;
}
```

Notes:
- 1m cube tiles flush in a wall; matches makail's original footprint.
- Bevel is small but non-zero — sharp 90 deg edges read as CG.
- LabelSize is derived (opening minus handle clearance), not free —
  regenerate it if Width/Height/FrameThickness/HandleSection change.

Preview tool: https://claude.ai/code/artifact/2760da81-255c-47b7-83e6-c4498ae02526
