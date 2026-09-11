# RossItemDrawers

A drawer that holds a large quantity of a single item and shows that item
and its count on its front face. Built from the Hammer, Furniture tab, at
a Workbench.

Valheim 1.0 broke every existing drawer mod and none of them are
maintained. This is a **clean-room rewrite**, not a fork or a port of
makail's original ItemDrawers or of KG's `kg_itemdrawers`: the code is new
and the mod ships no art of its own. It keeps the spirit of the original
— one drawer, one item type, a big number on the front — but the
interaction scheme differs from makail's in a way you should know about
before you report it as a bug (see Controls).

**Drawers from makail's or KG's mods are not converted by this mod.** If
you remove one of those mods from a world that used it, its drawers
become unknown pieces, the same as removing any other mod would; this
mod does not read or migrate their data.

## Tiers

| Tier | Capacity | Recipe (at Workbench) |
|---|---|---|
| Item Drawer (wood) | 1,000 | 10 Fine Wood |
| Stone Item Drawer | 2,000 | 5 Fine Wood + 10 Stone |
| Black Marble Item Drawer | 10,000 | 5 Fine Wood + 10 Black Marble |

Each drawer is a 0.66m cube — the same footprint as the drawers this mod
draws inspiration from, measured from their shipped asset bundle, so a
wall built to that spacing still lines up. Capacities above are the
defaults; a server admin can change them per tier in the config file.

## Controls

| Input | Effect |
|---|---|
| Use item from hotbar | Assign that item to an empty drawer, or deposit a stack into a matching one |
| `E` (Interact) | Take one stack |
| `Ctrl+E` | Take one item |
| `Ctrl+E` on an empty (zero-count) drawer | Clear the drawer's assigned item type |
| `Shift+E` | Deposit every matching item in your inventory |

**This is not makail's original control scheme, and that's deliberate, not
a bug.** The original mod documented "Alt+Interact" for take-one, but
Valheim 1.0 has no Alt key binding at all. What that scheme was actually
reading is Valheim's internal "AltPlace" action ("alternative placement"),
and on Valheim 1.0's default bindings that action shares a key with
**Run** (both are LeftShift) — so "Alt+Interact" and "Shift+Interact"
were never two different combinations to begin with. Take-one here is
bound to `Ctrl` instead (Valheim's "Crouch" action), a key that is
actually distinct from Run/Shift. If you rebind Crouch or Run in
Valheim's own settings, this mod follows the rebind.

## Compatibility

Drawers derive from Valheim's `Container`, so a `GetComponent<Container>()`
from another mod finds one, and `GetInventory()` returns a live view of the
drawer's contents: a single-slot inventory holding one oversized stack of
whatever the drawer is assigned. Container-aware mods can read from and
withdraw from drawers through the ordinary `Inventory` API, with no
knowledge of this mod.

Verified working in game:

- **OttoFuel** pulls fuel from drawers to feed fires, kilns and smelters.
- **NoVikingLeftBehind** crafts using items stored in drawers.

Both *withdraw*, not merely read, which is the harder half.

Everything else about a drawer — placing it, assigning it, taking from it,
depositing into it by hand, breaking it (its contents spill rather than
vanish) — works the same in single-player and on a dedicated server.

## Dependencies

- BepInEx 5.4.2350
- Jotunn 2.30.0
