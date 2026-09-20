# RossPortals

A portal-interface mod for Valheim: choose a portal's destination from a
searchable, foldered, sortable list, with no vanilla tag-pairing. It is a
from-scratch replacement for XPortal that imports an existing XPortal save's
destinations on first load.

For how to work in this repo — building, the Core/Game split, the Valheim API
notes, deploying and releasing — see the root `AGENTS.md`. This file covers what
the mod is.

## What it does

- Replaces the portal hover text and the "set tag" dialog with a configuration
  panel: a name field, a live search box, a sort selector, and a scrollable list
  of every portal grouped into folders.
- Groups portals by name prefix. A separator in the tag (default `/`) is read as
  a folder path: `Mines/Copper` is portal *Copper* in folder *Mines*. Folders
  are collapsible and nest.
- Removes tag pairing. A portal's destination is chosen explicitly and stored in
  its ZDO; the vanilla portal *connection* is written alongside it, so vanilla
  `TeleportWorld.Teleport` does the actual jump unchanged.
- Server-authoritative: the server scans the world's portals and syncs the list
  to every client; clients ask the server to change a portal.

## Structure

The mod follows the repo's Core/Game split (enforced by `ArchitectureTests`):

- **`src/RossPortals.Core`** — engine-free. The list model: filtering, folder
  grouping, sorting and the collapse/flatten that turns a portal set into the
  exact rows the panel draws (`PortalListView`). All unit-tested.
- **`src/RossPortals.Game`** — the adapter. Jotunn registration, the ZDO
  destination format and its XPortal import, the server-authoritative RPC sync,
  the Harmony patches, and the panel UI. See `docs/valheim-api/portals.md` (repo
  root) for the verified Valheim internals it patches.

## Importing from XPortal

RossPortals uses its own ZDO keys but reads XPortal's on the first load pass for
any portal that doesn't have ours yet, then writes its own — a one-way,
self-retiring import. Because portal ZDOIDs are reassigned each session, the
same load pass also remaps stored destinations to current ids and rebuilds the
live connections (replacing vanilla's tag-based pairing).
