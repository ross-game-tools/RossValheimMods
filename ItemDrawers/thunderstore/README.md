# RossItemDrawers

A drawer that holds a large quantity of a single item and shows that item
and its count on its front face. Built from the Hammer, Furniture tab, at
a Workbench.

![A 3x3 wall of drawers, one of each tier, each showing its item and count](https://raw.githubusercontent.com/ross-game-tools/RossValheimMods/main/ItemDrawers/docs/images/drawers-wall.png)

## Tiers

| Tier | Capacity | Recipe (at Workbench) |
|---|---|---|
| Item Drawer (wood) | 1,000 | 10 Fine Wood |
| Stone Item Drawer | 2,000 | 5 Fine Wood + 10 Stone |
| Black Marble Item Drawer | 10,000 | 5 Fine Wood + 10 Black Marble |

Capacities and build costs are the defaults; a server admin can change
both per tier in the config file.

Drawers are 0.66m cubes and snap to each other, so they tile into a wall.

## Controls

| Input | Effect |
|---|---|
| Use item from hotbar | Assign that item to an empty drawer, or deposit a stack into a matching one |
| `E` (Interact) | Take one stack |
| `Ctrl+E` | Take one item |
| `Ctrl+E` on an empty (zero-count) drawer | Clear the drawer's assigned item type |
| `Shift+E` | Deposit every matching item in your inventory |

`Ctrl` and `Shift` here are Valheim's Crouch and Run actions, so rebinding
those in Valheim's settings rebinds these too.

## Compatibility

Drawers are `Container`s, so container-aware mods can read from and
withdraw from them through the ordinary inventory API.

Verified in game with **OttoFuel**, which pulls fuel from drawers to feed
fires, kilns and smelters, and **NoVikingLeftBehind**, which crafts using
items stored in them.

## Dependencies

- BepInEx 5.4.2350
- Jotunn 2.30.0

## Thanks

To **makail** and **KG**, whose drawer mods are the inspiration for this
one. RossItemDrawers is its own mod with its own code, and does not read
or convert drawers built with theirs.
