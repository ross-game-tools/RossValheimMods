# Performance: the 100-drawer wall

This document is the measurement method, not the measurement. The commands
it relies on (`rid_wall`, `rid_stats`) exist in
`ItemDrawers/src/ItemDrawers.Game/DebugCommands.cs`; nobody has run them in
a live game yet, because this was written from a worktree that cannot
launch Valheim. A human with a running game must fill in every numbered
blank below before this mod can be called done.

## Why this exists

The whole design -- one ZDO per drawer, a shared icon atlas material, a
single manager tick instead of a `MonoBehaviour.Update` per drawer, and an
auto-pickup pass that iterates dropped items instead of drawers -- is in
service of one claim: **a wall of a hundred drawers should cost nothing
measurable next to an empty room.** That claim is falsifiable and this is
how to falsify or confirm it.

## What to record, and at what settings

Record all of the following for both the baseline and the wall measurement,
so the two numbers are actually comparable:

- **Machine**: CPU, GPU, RAM.
- **Valheim version** and **BepInEx/Jotunn versions** in the profile.
- **Resolution** and **display mode** (fullscreen/windowed/borderless).
- **Graphics quality preset** and whether V-Sync is on (turn V-Sync off for
  this test -- it hides real frame-time differences below the sync
  interval).
- **Frame-time measurement method**: Valheim's own `F2`/dev console frame
  counter if using one, or an external overlay (RivaTuner Statistics
  Server, Steam's own FPS overlay, etc). Whichever is used, use the *same*
  one for both measurements. Note whether the number recorded is an
  instantaneous reading or an average over some window, and prefer frame
  *time* (ms) over FPS if the tool offers both -- frame time is linear and
  differences are easier to read.
- **Mod profile contents**: at minimum BepInEx + Jotunn + ItemDrawers for
  the baseline pass/fail; a second pass with OttoFuel and
  NoVikingLeftBehind also active, per Step 3 below, since those are the
  mods this drawer is designed to stay legible to.

## Step 1: Baseline

In a quiet, flat, already-explored area (so terrain/vegetation streaming
isn't itself a variable), with no drawers built anywhere nearby:

1. Enable the developer console (`devcommands`).
2. Stand still, facing a fixed, unremarkable direction.
3. Record frame time for ~10-15 seconds and note a representative number
   (an average, or a tight range if it isn't perfectly stable).

Write the number here:

```
## Baseline
Machine:
Valheim version:
Resolution / display mode:
Graphics preset / V-Sync:
Measurement method:
Frame time (ms):
```

## Step 2: Build and measure the wall

From the same spot, facing the same direction:

```
rid_wall 10 10
rid_stats
```

`rid_stats` reports drawers loaded, how many are assigned an item, total
items held, and whether the icon atlas has been built
(`DrawerIconAtlas.IsBuilt`) -- confirm it says `True` before trusting the
frame numbers below, since an unbuilt atlas means the drawers are drawing
without their shared material and any measurement would be meaningless.

Record, each as its own line:

1. **Frame time with the wall filling the screen** (walk up close enough
   that all 100 drawers are in view).
2. **Frame time with the wall entirely behind you** (turn around; confirms
   Unity's own frustum culling is doing its job and the wall costs nothing
   once off-screen).
3. **Draw calls with the wall visible.** Use a frame debugger (RenderDoc,
   Unity's own if reachable, or an overlay that reports draw calls) if one
   is available. If none is available, write "not measurable with
   available tools" -- do not guess a number.
4. **Frame time with the wall visible while OttoFuel and
   NoVikingLeftBehind are both active and doing real work**: stand at a
   workbench beside a smelter with ore in it, both mods scanning nearby
   containers, while the wall is in view.

```
## Wall measurement (rid_wall 10 10)
rid_stats output:
  drawers loaded :
  assigned       :
  items held     :
  atlas built    :

Frame time, wall on screen (ms):
Frame time, wall behind camera (ms):
Draw calls, wall on screen:
Frame time, wall on screen + OttoFuel/NVLB active (ms):
```

## Step 3: Judge against the criterion

The Global Constraint this mod was designed to satisfy:

> Frame cost within noise of the baseline, and single-digit draw calls.

"Within noise" means: the wall's on-screen frame time should not be
distinguishable from ordinary frame-to-frame variance in the baseline
measurement -- not "a little higher," not "acceptable," indistinguishable.
If the baseline itself swings by 1-2ms from moment to moment, the wall
number needs to land inside that same band.

Write the verdict:

```
## Verdict
PASS / FAIL
Reasoning (one or two lines, referencing the numbers above):
```

If it passes, this document is done -- record the numbers and stop.

## Step 4: If it fails, diagnose in this order

Do not adjust the target. Each of these is a specific, known way the
one-tick, one-material design gets undone by an innocent-looking change
elsewhere in the codebase:

1. **Shared material.** Two drawers holding different items must still
   share one material. Check that every `DrawerRenderer` in the scene
   references `DrawerIconAtlas.SharedMaterial` and that nothing has started
   instancing a per-drawer material (Unity silently clones a material the
   first time code touches `Renderer.material` instead of
   `Renderer.sharedMaterial` -- that one-character difference is exactly
   the kind of regression this check exists to catch).
2. **`SyncSomeFaces` doing real work every frame.** `DrawerRenderer.Show()`
   must return immediately when nothing changed. If it's rebuilding mesh or
   texture data on every call regardless of whether the drawer's contents
   changed, that's a hundred rebuilds a frame, not zero.
3. **`RunAutoPickup` costing something on an empty floor.** With nothing
   dropped anywhere near the wall, the pickup pass in
   `DrawerManager.RunAutoPickup` should find zero entries in
   `ItemDrop.s_instances` within `PickupScanRange` and do essentially
   nothing. If disabling `AutoPickupEnabled` in the config measurably
   changes the wall's frame time, the pass itself is the problem, not
   drawer count.
4. **`GetInventory` called per frame.** The `ContainerBridge` mirror-refresh
   guard (`RefreshMirror`, comparing against the last-mirrored
   `DrawerSnapshot`) must make repeat calls free. If some other mod (or our
   own code) is polling `Container.GetInventory()` every frame per drawer,
   that reintroduces exactly the kind of per-drawer work this design
   avoids.

Fix the regression, not the criterion, and re-measure from Step 2.

## Notes for whoever runs this

- `rid_wall <cols> <rows> [wood|stone|blackmarble]` and `rid_stats` are
  both registered `isCheat: true`, so `devcommands` (or an equivalent) must
  be active for them to run.
- `rid_wall` pre-fills each drawer with a semi-random amount (250-750) of
  one of six sample items (`Wood`, `Stone`, `Coal`, `Iron`, `Resin`,
  `Flint`), cycling through them across the grid, so the wall exercises the
  icon atlas with a realistic mix of items rather than one repeated icon.
- The wall is built with `Object.Instantiate` directly against the drawer
  prefab (not through the normal placement/build flow), floating in front
  of the player. This is fine for a performance measurement -- it is real
  `DrawerComponent`/`ZNetView`/`DrawerRenderer` instances doing real work --
  but do not use it to test placement, snapping, or build-cost behavior;
  those are exercised by actually building drawers through the hammer menu.
- Running `rid_wall` twice does not clear the previous wall; each call adds
  another `cols x rows` grid. Reload the world (or the WearNTear "destroy
  in a radius" console tooling, if available) to clear one out before a
  second measurement pass.
