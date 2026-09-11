# Changelog

Beta: single-player and single-client dedicated-server play are
tested; simultaneous multi-client use is not yet verified.

## 0.9.0 — beta

First public build, released for testing. A clean-room rewrite for
Valheim 1.0 — not a fork of
makail's or KG's drawer mods, and drawers from those mods are not
converted.

- Each tier is coloured to match the material it is built from,
  with a brightened metal handle and a dark label plate behind the
  icon and count so the front face reads at a glance and the count
  stays legible in direct sunlight.
- Three tiers, built at a Workbench from the Hammer's Furniture tab:
  Wood (10 Fine Wood, holds 1,000), Stone (5 Fine Wood + 10 Stone,
  holds 2,000), Black Marble (5 Fine Wood + 10 Black Marble, holds
  10,000). Capacities are configurable per tier.
- Controls: assign from the hotbar, `E` takes one stack, `Ctrl+E`
  takes one item (or clears the drawer's item type once it reads
  zero), `Shift+E` deposits every matching item in your inventory.
  This is not the same scheme as makail's original — see the package
  README for why Alt+Interact could never work on Valheim 1.0.
- No shipped art: geometry is a procedurally generated chamfered box
  and textures are generated to tile seamlessly, using Valheim's own
  `Custom/Piece` shader so drawers light and shade like any
  other building piece.
- Built for walls: one shared mesh and one manager component tick
  every drawer, rather than a per-drawer Update.
