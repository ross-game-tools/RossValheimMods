# ItemDrawers

A drawer that holds a large quantity of a single item and displays that
item and its count on its front face. Built from the Hammer, furniture tab.

Valheim 1.0 broke every existing drawer mod and none are maintained, so
this is a clean-room rewrite rather than a revival. It keeps the control
scheme players already know from makail's original.

**Design:** [docs/superpowers/specs/2026-09-09-item-drawers-design.md](docs/superpowers/specs/2026-09-09-item-drawers-design.md)
**Proportions:** [docs/drawer-spec.md](docs/drawer-spec.md)

## What makes it different

- **Built for walls.** The normal way to use drawers is a hundred of them
  in a wall. No per-drawer Canvas, no per-drawer tick, one shared mesh and
  one icon atlas, so a 10x10 wall costs a handful of draw calls. This is a
  measured acceptance criterion, not an aspiration.
- **Legible to automation mods.** Drawers present themselves as
  containers, so OttoFuel can pull fuel and ore out of them and
  NoVikingLeftBehind can craft from them, without either mod knowing this
  one exists.
- **No shipped assets.** The drawer mesh is generated procedurally from
  chamfered boxes and wears vanilla Valheim materials, so it inherits the
  game's wear, wet and snow shading and there is nothing encumbered to
  publish.

## Controls

| Input | Effect |
|---|---|
| Use item from hotbar | Assign that item to an empty drawer |
| Interact | Take one stack |
| Alt + Interact | Take one item |
| Alt + Interact at zero | Clear the drawer's item type |
| Shift + Interact | Deposit every matching item in your inventory |

## Status

Design approved. Not yet implemented.
