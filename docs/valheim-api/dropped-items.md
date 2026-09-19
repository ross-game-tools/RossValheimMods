# Dropped items: the auto-destroy clock and the base exemption

What this covers: how vanilla decides a dropped item (`ItemDrop`) has
become litter and destroys it -- the one-hour clock, and the permanent
exemption for anything sitting inside a player base, which is why
items dropped at home never disappear at all.

Produced on 2026-09-19 by decompiling `assembly_valheim.dll` with
ilspycmd 8.2 (`8.2.0.7535-95108c96`), and confirmed against the live
assembly via reflection (`ItemDrop`'s members, accessibility, and
signatures). Game version from the assembly: `Version.CurrentVersion =
new GameVersion(1, 0, 15)`. `ItemDrop.cs` was read in full (1830
lines).

## The schedule: `SlowUpdate`, owner-only, every 10 seconds

```csharp
private void Awake()
{
    ...
    m_nview = GetComponent<ZNetView>();
    if ((bool)m_nview && m_nview.IsValid())
    {
        ...
        InvokeRepeating("SlowUpdate", UnityEngine.Random.Range(1f, 2f), 10f);
    }
    ...
}

private void SlowUpdate()
{
    if (m_nview.IsValid() && m_nview.IsOwner())
    {
        TerrainCheck();
        if (m_autoDestroy)
        {
            TimedDestruction();
        }
        if (s_instances.Count > 200)
        {
            AutoStackItems();
        }
    }
}
```

`SlowUpdate` is gated by `m_nview.IsValid() && m_nview.IsOwner()` --
`TimedDestruction()` only ever runs on whichever client currently owns
the item's ZDO, never on every peer. A patch that adds to this check
must run the same way: on the owner, or it silently does nothing for
items owned elsewhere.

`m_autoDestroy` (`public bool m_autoDestroy = true;`) is the one
existing off-switch -- some prefabs (quest items, for instance) set it
false and are permanently exempt from all of the below, base or no
base. A patch here should leave that exemption alone.

## The check itself

```csharp
private const double c_AutoDestroyTimeout = 3600.0;

private double GetTimeSinceSpawned()
{
    DateTime dateTime = new DateTime(m_nview.GetZDO().GetLong(ZDOVars.s_spawnTime, 0L));
    return (ZNet.instance.GetTime() - dateTime).TotalSeconds;
}

private void TimedDestruction()
{
    if (!(GetTimeSinceSpawned() < 3600.0) && !IsInsideBase() && !Player.IsPlayerInRange(base.transform.position, 25f) && !InTar() && !IsPiece())
    {
        m_nview.Destroy();
    }
}
```

`c_AutoDestroyTimeout` is a **private `const double`, inlined at its
one call site** -- the `3600.0` literal in `TimedDestruction` above is
that constant after inlining, not a coincidence; there is no live field
to read it back from at runtime, and no way for a patch to reference it
by name. `GetTimeSinceSpawned()` reads `ZDOVars.s_spawnTime`, a ZDO
long written once, the first time the item's owner sees it in `Awake()`
(`if (m_nview.IsOwner() && new DateTime(...GetLong(ZDOVars.s_spawnTime, 0L)).Ticks == 0L) { m_nview.GetZDO().Set(ZDOVars.s_spawnTime, ZNet.instance.GetTime().Ticks); }`)
-- the clock starts at spawn and is never reset by anything in this
file.

Because C#'s `&&` short-circuits left to right, **the moment
`IsInsideBase()` returns true, none of `Player.IsPlayerInRange`,
`InTar()`, or `IsPiece()` are evaluated at all** -- not "always false",
literally never called. An item inside a base is exempt outright,
regardless of age, and that exemption never expires. This is the
mechanism behind the complaint that litter dropped at home lasts
forever: it is not that the exemption's clock is too generous, there is
no clock running for it whatsoever.

## `IsInsideBase()`

```csharp
private bool IsInsideBase()
{
    if (base.transform.position.y > 28f && (bool)EffectArea.IsPointInsideArea(base.transform.position, EffectArea.Type.PlayerBase))
    {
        return true;
    }
    return false;
}
```

Two conditions, both required: above `y > 28f` (world-space altitude,
not relative to terrain), and inside an `EffectArea` of type
`PlayerBase` -- the same marker a workbench or ward radius projects
(see `eitr-refinery.md` for another `EffectArea.Type` in use on a
different prefab). `IsInsideBase()` is **private** and a two-statement
method; Mono inlines methods this small, so a Harmony patch targeting
it directly would silently stop taking effect at any call site the
JIT decided to inline instead of calling through the patched method --
the same family of trap this codebase has already hit once, patching a
tiny `ZInput.GetButton*` method that got inlined out from under the
patch. Add a case instead of patching this method.

## The exemptions that must survive any fix

Read in full so a patch can reproduce them exactly rather than
re-deriving them:

```csharp
public bool InTar()
{
    if (m_body == null)
    {
        return false;
    }
    if (m_floating != null)
    {
        return m_floating.IsInTar();
    }
    Vector3 worldCenterOfMass = m_body.worldCenterOfMass;
    float liquidLevel = Floating.GetLiquidLevel(worldCenterOfMass, 1f, LiquidType.Tar);
    return worldCenterOfMass.y < liquidLevel;
}

public bool IsPiece()
{
    if (!m_body && (bool)m_piece)
    {
        return m_wnt;
    }
    return false;
}
```

`InTar()` and `IsPiece()` are both **public**, ordinary multi-statement
methods -- no inlining risk, callable directly by name. `IsPiece()` is
"has no rigidbody but does have a `Piece` and a `WearNTear`" -- an item
that has been placed as building debris (a dropped item that also
carries piece/structural components) rather than a stack lying on the
ground; vanilla exempts it from `TimedDestruction` unconditionally,
base or not.

`Player.IsPlayerInRange(Vector3 point, float range)` is public and
static (three overloads exist; this is the two-argument one vanilla
calls here, `range = 25f`).

## Member accessibility summary (reflection-confirmed against the live assembly)

| Member | Accessibility |
|---|---|
| `ItemDrop.TimedDestruction()` | private |
| `ItemDrop.SlowUpdate()` | private |
| `ItemDrop.GetTimeSinceSpawned()` | private |
| `ItemDrop.IsInsideBase()` | private |
| `ItemDrop.InTar()` | public |
| `ItemDrop.IsPiece()` | public |
| `ItemDrop.m_autoDestroy` | public field |
| `ItemDrop.m_nview` | private field |
| `ItemDrop.c_AutoDestroyTimeout` | private const (inlined, not readable at runtime) |

A patch that needs `GetTimeSinceSpawned`, `IsInsideBase`, or `m_nview`
reaches them by reflection (`AccessTools.Method`/`AccessTools.Field`),
same as this codebase's existing `AutoRepairPatch` does for
`InventoryGui.CanRepair`. `InTar`, `IsPiece`, and
`Player.IsPlayerInRange` are called directly.

## Recommended hook point

A **postfix on `TimedDestruction`**. Vanilla's method is a complete,
self-contained check with one destructive side effect
(`m_nview.Destroy()`) and no return value -- there is nothing to
intercept before it runs, only a case to add after it returns:

- If vanilla's own call already destroyed the item (every condition
  held outside a base), the postfix finds `m_nview` invalid and has
  nothing to do.
- If vanilla left the item alone because `IsInsideBase()` was the
  short-circuit, the postfix independently evaluates the same
  age/player/tar/piece conditions vanilla would have, and destroys the
  item itself if they all hold.

This reproduces every vanilla exemption (age, the 25 m player-presence
check, tar, building debris) and only changes whether the base
exemption applies -- it cannot fire for an item vanilla would have kept
for any other reason, since it reuses vanilla's own conditions rather
than a re-derived approximation of them.

## Things I could not verify from the assemblies

1. Real, live values for anything not in this file's excerpts --
   `EffectArea.IsPointInsideArea`'s own body (not decompiled this
   session; treated as a black box returning whether the point falls
   inside a registered `PlayerBase`-type `EffectArea`, consistent with
   `eitr-refinery.md`'s findings on the same type) and exactly which
   prefabs project a `PlayerBase` `EffectArea` (workbench, ward --
   asset data, not in the DLL).
2. Whether any other exemption exists elsewhere in the file besides
   the four in `TimedDestruction` and the `m_autoDestroy` gate in
   `SlowUpdate` -- `ItemDrop.cs` was read in full for this session and
   none were found, but a future version could add one without
   changing any of the members named here.
