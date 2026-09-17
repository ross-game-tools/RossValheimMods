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
| Use item from hotbar | Assign that item to an empty drawer. Once a drawer has an item, the hotbar no longer touches it |
| `E` (Interact) | Take one stack |
| `Ctrl+E` | Take one item |
| `Ctrl+E` on an empty (zero-count) drawer | Unassign it, so it can hold something else |
| `Shift+E` | Deposit every matching item in your inventory |

`Ctrl` and `Shift` are this mod's own bindings, not Valheim's. Change them
under `[Controls]` in the config to any key you like, or set one to `None`
to switch that action off. The hover text follows whatever you choose.

## Nearby items

A drawer pulls in matching items dropped near it, so emptying a full
inventory onto the floor of a storage room files everything away by
itself. Only items a drawer is already assigned to are taken — a drawer
never claims something it was not holding, and a full one takes nothing.

On by default, within 40 metres. Both the range and the switch are in
the config under `[Pickup]`, and are server-synced.

## Compatibility

Drawers are `Container`s. Other mods see a drawer as a small chest
holding its item, and use it through the ordinary inventory API with no
drawer-specific support. This version is designed for:

- **Taking from drawers** — crafting from nearby containers, OttoFuel,
  NoVikingLeftBehind: the drawer's whole count is available.
- **Storing into drawers** — AzuAutoStore, Quick Stack Store: only the
  drawer's own item is accepted, up to its capacity. Empty and
  unassigned drawers accept nothing, so they are never chosen as a
  destination.
- **Take-all mods** receive normal-sized stacks.

Items moved this way are applied to the drawer's count within a frame by
the player whose mod moved them, who takes charge of the drawer the same
way those mods take charge of a chest.

OttoFuel and NoVikingLeftBehind were verified in game against 0.9.x.
The 1.0 container support above has not yet been tested in game with
these mods.

Every player and the server must run the same ItemDrawers version.

### Known limitations

- Mods on two players' machines changing the same drawer in the same
  instant can race — whichever change lands last wins, the same exposure
  a vanilla chest has when two players use it at once.
- A withdrawal made through the view in the exact frame a client
  disconnects may not reach the server.

## Dependencies

- BepInEx 5.4.2350
- Jotunn 2.30.0 or newer

## Thanks

To **makail** and **KG**, whose drawer mods are the inspiration for this
one. RossItemDrawers is its own mod with its own code, and does not read
or convert drawers built with theirs.
