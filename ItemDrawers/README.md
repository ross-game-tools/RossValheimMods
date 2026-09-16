# ItemDrawers

A drawer that holds a large quantity of a single item and displays that
item and its count on its front face. Built from the Hammer, furniture tab.

Valheim 1.0 broke every existing drawer mod and none are maintained, so
this is a clean-room rewrite, not a fork or a revival of makail's original
or of KG's `kg_itemdrawers` — no code or art from either is reused, and
drawers built with those mods are not converted by this one. The controls
below are inspired by makail's original but are not identical to it; see
"Controls" for why.

**Design:** [docs/superpowers/specs/2026-09-09-item-drawers-design.md](docs/superpowers/specs/2026-09-09-item-drawers-design.md)
**Proportions:** [docs/drawer-spec.md](docs/drawer-spec.md)

## Tiers

| Tier | Capacity (default, configurable) | Recipe (at Workbench) |
|---|---|---|
| Item Drawer (wood) | 1,000 | 10 Fine Wood |
| Stone Item Drawer | 2,000 | 5 Fine Wood + 10 Stone |
| Black Marble Item Drawer | 10,000 | 5 Fine Wood + 10 Black Marble |

Each drawer is a 0.66m cube, built from the Hammer's Furniture tab.

## What makes it different

- **Built for walls.** The normal way to use drawers is a hundred of them
  in a wall. No per-drawer Canvas, no per-drawer tick — one shared mesh and
  one manager component ticking every drawer instead.
- **No shipped assets.** The drawer mesh is generated procedurally from
  chamfered boxes and its textures are generated to tile seamlessly.
  Materials reuse Valheim's own `Custom/Piece` shader (not modelled or
  copied art), so drawers inherit the game's wear, wetness and snow
  shading like any other building piece.
- **Container-aware automation.** `DrawerComponent` derives from
  `Container`; `GetInventory()` returns the drawer's container view (see
  `docs/drawer-spec.md`, "Container view"), so mods can read, take from
  and store into drawers through the ordinary inventory API.

## Controls

| Input | Effect |
|---|---|
| Use item from hotbar | Assign that item to an empty drawer |
| Interact | Take one stack |
| Ctrl + Interact | Take one item |
| Ctrl + Interact at zero | Clear the drawer's item type |
| Shift + Interact | Deposit every matching item in your inventory |

Take-one is bound to Ctrl, not Alt as in makail's original scheme: Valheim
1.0 has no Alt key binding at all. What looked like "Alt+Interact" in the
original mod was always reading Valheim's "AltPlace" input action, which
means "alternative placement" and is bound to Shift by default — the same
key as "deposit all" above, which never actually gave the original mod a
third distinct combination either. The modifiers are this mod's own
bindings, read directly and configurable under `[Controls]`, so LeftAlt
now means the Alt key if you want it to. Keyboard only: a raw key carries
no gamepad binding, so controller players keep plain Interact and lose the
two modifiers.

## Building

Two MSBuild properties point the build at machine-specific paths — neither
is committed with a real value:

- `VALHEIM_INSTALL` — your Valheim install folder (contains `valheim.exe` and
  `valheim_Data\Managed\`). Game and Unity assemblies are referenced from
  here via `HintPath`, never as NuGet packages, and never committed.
- `JOTUNN_INSTALL` — the folder holding your installed `Jotunn.dll` (e.g. an
  r2modman profile's `BepInEx\plugins\ValheimModding-Jotunn`). This is a
  `HintPath` reference rather than a `PackageReference` because JotunnLib
  2.30.0 on nuget.org ships `lib/net462` only, with no netstandard asset — a
  netstandard2.1 project restores it with zero usable references and no
  warning to explain why.

Set both either as environment variables, or by copying
`Directory.Build.local.props.sample` to `Directory.Build.local.props`
(gitignored) in the `ItemDrawers/` folder and editing the paths there. Then:

```bash
cd ItemDrawers
dotnet build -c Release
```

## Packaging

`./package.sh` builds Release and assembles the Thunderstore zip under
`thunderstore/build/`. It fails loudly if either `ItemDrawers.dll` or
`ItemDrawers.Core.dll` is missing from the build output — see
`thunderstore/README.md` for why shipping only one is a real, previously
hit bug (`FileNotFoundException` at registration, with no drawers and no
obvious error).

## Status

Implemented and buildable. Thunderstore packaging (this README, the
package manifest/README/changelog, `LICENSE`, `package.sh`) is in place.
Outstanding before a Thunderstore release: `thunderstore/icon.png`
(must be exactly 256x256, not something a text-only pass can produce),
and a human should do a real in-game pass to confirm behavior — nothing
here has been verified by actually launching Valheim.
