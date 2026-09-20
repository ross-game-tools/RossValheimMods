# RossPortals

Pick a portal's destination from a list, and actually find the one you want.

![The RossPortals portal panel: a searchable, foldered list of destinations](https://raw.githubusercontent.com/ross-game-tools/RossValheimMods/main/RossPortals/docs/images/portal.png)

Once you have more than a handful of portals, vanilla tag-pairing — and even a
plain destination dropdown — turns finding the right one into a scavenger hunt.
RossPortals gives every portal a configuration panel with:

- **A search box.** Type any part of a name to filter the list instantly.
- **Folders.** Put a separator in a portal's name and it becomes a folder:
  name portals `Mines/Copper`, `Mines/Iron`, `Base/Home` and they group under
  collapsible **Mines** and **Base** headers. The separator is `/` by default
  and configurable. No extra menus — you organise purely by how you name things.
- **Sorting.** Sort by **Name** (which keeps the folders), or by **Nearest** or
  **Recent** — those two show a flat list of every portal, so you can jump
  straight to the closest one, or the one you just came from.
- **No tag pairing.** Portals don't need matching tags or to point at each
  other. Choose a destination and go.

## Switching from XPortal

RossPortals is a drop-in replacement for XPortal and **imports your existing
portal network** the first time it loads a save: every destination you set in
XPortal is carried over automatically. Just disable XPortal and enable
RossPortals — your portal names and connections come with you.

The two mods can't run at the same time (they both take over the portal
interface), so remove or disable XPortal before playing.

## Multiplayer

Everyone on a server needs the mod, at the same minor version. The server keeps
the authoritative portal list and syncs it to every player.

## Usage

Walk up to a portal and press your Use key. Give it a name (use `/` to file it
in a folder), search and pick a destination, and press OK. That's it.
