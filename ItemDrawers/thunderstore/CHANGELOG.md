# Changelog

Beta: functional and in use, but not yet widely tested.

## 0.9.7 — beta

- Drawers now show their contents to every player, the same way a chest
  does, instead of only to whoever the game currently considers their
  owner. This is the real fix for crafting and fuel-pulling in
  multiplayer; 0.9.4 through 0.9.6 worked around the symptom and still
  left two players at the same wall fighting over it.

## 0.9.6 — beta

- Fixed crafting from drawers in multiplayer. Drawers near you are now
  claimed shortly after you arrive, which is what mods that check
  ownership before reading a container need. A drawer another player is
  standing next to stays theirs until they move away.

## 0.9.5 — beta

- Fixed drawers being private to whoever placed them. Other players
  could not craft from them, and OttoFuel skipped them entirely. Drawers
  are shared storage and are now public, which also applies to drawers
  you have already built.

## 0.9.4 — beta

- Fixed area pickup being unreliable in multiplayer. Drawers now absorb
  nearby items regardless of which player dropped them or who last
  touched the drawer; previously it only worked when one player happened
  to have a claim on both, so mob drops and drawers in a just-loaded area
  were often ignored.
- Fixed container-aware mods seeing drawers as empty in multiplayer.
  OttoFuel would refuse to pull fuel, and would keep feeding a kiln past
  its coal cutoff because the coal it counts was in drawers it could not
  see.

## 0.9.3 — beta

- Added a screenshot to the description.

## 0.9.2 — beta

- Rewrote the README and changelog for players rather than developers.

## 0.9.1 — beta

- Renamed the Thunderstore package to RossItemDrawers. The plugin ID and
  prefab names are unchanged, so existing drawers and configs carry over
  untouched.
- Corrected the compatibility notes, which still described the OttoFuel
  and NoVikingLeftBehind support that shipped in 0.9.0 as planned.

## 0.9.0 — beta

First public build.

- Three tiers, built at a Workbench from the Hammer's Furniture tab:
  Wood (10 Fine Wood, holds 1,000), Stone (5 Fine Wood + 10 Stone, holds
  2,000), Black Marble (5 Fine Wood + 10 Black Marble, holds 10,000).
  Capacities are configurable per tier.
- Controls: assign from the hotbar, `E` takes one stack, `Ctrl+E` takes
  one item (or clears the drawer's item type once it reads zero),
  `Shift+E` deposits every matching item in your inventory.
- Works with container-aware mods. Verified in game with **OttoFuel**,
  which pulls fuel from drawers, and **NoVikingLeftBehind**, which
  crafts from them.
- Each tier is coloured to match the material it is built from, with a
  metal handle and a dark label plate so the front reads at a glance and
  the count stays legible in direct sunlight.
- Drawers snap to each other and are built to tile into large walls
  without costing frames.
