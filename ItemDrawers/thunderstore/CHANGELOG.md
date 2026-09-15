# Changelog

## 1.0.2

- Fixed drawers becoming slow and unresponsive when more than one player
  is nearby, with withdrawals frequently refused. Two clients could take
  a drawer from each other indefinitely, so neither ever finished writing
  and every attempt to take items out was turned away.

## 1.0.1

- Hotbar keys no longer interact with a drawer that already has an item.
  Selecting a matching item near one used to deposit the stack you were
  holding without asking, and a mismatched item printed a message and
  swallowed the keypress. The hotbar is now only for assigning an empty
  drawer; Shift+E still stores everything matching that you carry.

## 1.0.0

- Drawers are designed to work with mods that store items into
  containers, such as AzuAutoStore and Quick Stack Store. Deposits of a
  drawer's item are added to its count. Previously those mods saw the
  deposit succeed and the items were lost, and Quick Stack Store's
  quick-stack failed for every container once a drawer was nearby.
- When another mod changes a drawer, the player running that mod takes
  charge of the drawer, the same way those mods take charge of a chest.
- Every player and the server must run the same ItemDrawers version
  (1.0.x); mismatched versions are refused at connect.
- Other mods see a drawer as a small chest: its full count to take from,
  and room for more of the same item up to its capacity. Empty and
  unassigned drawers accept nothing from other mods.
- A take-all mod emptying a drawer now hands over normal stacks instead
  of one oversized stack.
- Dragging an oversized stack onto a mismatched item in your own
  inventory is now refused instead of dropping items — you'll need to
  drop it into an empty slot or onto a matching stack.
- A drawer you just built now works with OttoFuel right away. Before,
  OttoFuel ignored new drawers until the world was reloaded.
- Existing drawers need nothing done; they pick this up the first time
  they load.
- Known limitation: mods on two players' machines depositing into or
  withdrawing from the same drawer in the same instant can race, the
  same exposure a vanilla chest has when two players use it at once. A withdrawal made
  through another mod in the exact instant a client disconnects may not
  reach the server.

## 0.9.10 — beta

- An empty drawer now offers "Unassign" on Ctrl+E instead of "Take one",
  and stops offering the two take actions it cannot perform. Releasing a
  drawer's item type so it can hold something else was not discoverable
  anywhere.
- Stopped warning every boot about items whose icon cannot be read. On
  Valheim 1.0.12 that is draugr arrows and the two goblin spears, which
  are mob-only items carrying no icon at all. They are skipped quietly
  now, and an item that has icons but an out-of-range variant falls back
  to its first icon rather than being dropped. Thanks to neuralyze for
  the report (#3).

## 0.9.9 — beta

- Documented the nearby-item pickup that drawers have always had, which
  the description and README never mentioned.
- Fixed crafting taking nothing from a drawer when the recipe needed
  exactly what was left in it. The item was still crafted, so the
  materials were duplicated. Taking less than the full amount always
  worked; only the exact-drain case was affected.

## 0.9.8 — beta

- Build costs are configurable per tier, under `[Recipe]`, as
  `Item:Count` separated by commas. Server-synced like the capacities.
  A recipe that will not parse, or that names an item the game does not
  have, is logged and the default used instead.

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
