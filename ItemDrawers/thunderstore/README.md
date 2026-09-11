# ItemDrawers

A drawer that holds a large quantity of a single item and shows that item
and its count on its front face. Built from the Hammer, Furniture tab, at
a Workbench.

Valheim 1.0 broke every existing drawer mod and none of them are
maintained. This is a **clean-room rewrite**, not a fork or a port of
makail's original ItemDrawers or of KG's `kg_itemdrawers`: the code is new
and the mod ships no art of its own (see "No shipped assets" below). It
keeps the spirit of the original — one drawer, one item type, a big
number on the front — but the interaction scheme differs from makail's in
a way you should know about before you report it as a bug (see Controls).

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

## No shipped assets

This mod ships no meshes, textures, or icons of its own. Every drawer's
geometry is generated at runtime from chamfered boxes, and its textures
are generated procedurally and tile seamlessly — nothing is pulled from
another mod's asset bundle. Materials use Valheim's own `Custom/Piece`
shader (borrowed from vanilla building pieces, not modelled), so drawers
pick up the game's wear, wetness and snow shading like any other piece in
your build. There is nothing encumbered here to publish, and nothing
converted from an earlier mod's art.

## Compatibility

Drawers derive from Valheim's `Container` so that a `GetComponent<Container>()`
from another mod finds this type. As of 1.0.0 that is as far as the bridge
goes: this drawer does not run `Container`'s own setup, so a mod that calls
`GetInventory()` on it directly will not see its contents today. Treat
container-aware automation (fuel-feeders, auto-crafters, and similar) as
**not yet supported** rather than assumed compatible — a proper bridge for
that is planned but not shipped in this release.

Everything else about a drawer — placing it, assigning it, taking from it,
depositing into it, breaking it (its contents spill rather than vanish) —
works the same in single-player and on a dedicated server.

## Dependencies

- BepInEx 5.4.2350
- Jotunn 2.30.0

## Dedicated servers

Install it on the server as well as on every client. Drawers are custom
prefabs, and a peer that does not know a prefab discards the objects
using it: a server without this mod will strip every drawer from the
world the first time it loads those zones, and the items they held go
with them. Client and server should run the same version.

On the server:

1. Install **BepInExPack Valheim**. It contains the server start
   scripts as well as the loader.
2. Copy `Jotunn.dll`, `ItemDrawers.dll` and `ItemDrawers.Core.dll` into
   the server's `BepInEx/plugins/`.
3. Start the server with `start_server_bepinex.sh` (Linux) or
   `start_server_bepinex.bat` (Windows). The stock `start_server`
   script does not load BepInEx, so the server comes up vanilla and
   strips the drawers exactly as if the mod were not installed.

Check `BepInEx/LogOutput.log` for `ItemDrawers <version> loaded` before
letting anyone connect.

Capacity and pickup settings are marked admin-only, so Jotunn pushes the
**server's** values to every client and a client editing its own config
file changes nothing. Edit them in the server's
`BepInEx/config/com.rossdwest.itemdrawers.cfg`.

## Installing both DLLs

This mod ships as **two** assemblies: `ItemDrawers.dll` (the plugin) and
`ItemDrawers.Core.dll` (pure logic the plugin depends on). If you are
copying files by hand rather than using a mod manager, copy **both** —
`ItemDrawers.dll` alone loads with no error in the log but registers zero
drawers, because it throws `FileNotFoundException` looking for
`ItemDrawers.Core.dll` the first time it needs it. A Thunderstore-managed
install (r2modman, Thunderstore Mod Manager, or the mod manager's own
download) always installs the whole package, so this only matters for a
manual copy.
