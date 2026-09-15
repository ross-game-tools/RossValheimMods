# Changelog

## 0.7.1

- **Interface / Clock:** a thin dark outline keeps the clock readable over
  bright backgrounds such as snow and sky.

## 0.7.0

- **Crafting / RecipeSearch:** the search box also finds recipes by type:
  `armor`, `helmet`, `weapon`, `food` and more, or a weapon's skill such as
  `axes` or `bows`.

## 0.6.1

- **Portals / TamesFollow:** tames following you come through portals even
  when another player's game is looking after them, such as a tame someone
  else tamed. Before, only the player whose game owned the tame could bring
  it.

## 0.6.0

- **Tames / FeedFromContainers:** a hungry tame with no food on the ground
  nearby eats from a container within `FeedRadius` (default 10 m), drawers
  included. Only creatures that are already tame. Server-controlled.
- **Tames / SilentBirths:** tames give birth without the birth sound.
  Server-controlled.

## 0.5.0

- **Tames / FollowCommand:** every tamed creature can be told to follow or
  stay by pressing Use on it, not just wolves. Server-controlled.

## 0.4.0

- **Interface / Clock:** the in-game day and time under the minimap, in
  24-hour or 12-hour format.
- **Production / AutoHarvest:** beehives, sap collectors and fermenters
  near a player empty themselves into containers within `HarvestRadius`
  (default 40 m), first into containers already holding that item, then
  the nearest with room. Works with containers from storage mods, such as
  ItemDrawers drawers. Output that fits nowhere stays in the producer; a
  fermenter's batch goes into one container whole and is never split.
  Server-controlled.

## 0.3.0

- **Combat / InstantLoot:** a killed creature's loot drops the moment it
  dies instead of when its corpse fades. The corpse stays. Server-controlled.
- **Terrain / UnlimitedHeight:** raise and dig terrain up to `MaxRaise` and
  `MaxDig` metres (default 200) instead of vanilla's 8. Server-controlled.

## 0.2.0

- **Crafting / RecipeSearch:** a search box above the crafting recipe
  list filters it by name as you type. `SearchAutoFocus` puts the cursor
  in it when you open a crafting station.
- **Live config:** edits to the config file apply without a restart
  (`General / HotReload`); server-controlled settings reloaded on a server
  reach connected players. Turning a feature off is immediate; turning on
  one that was off at launch still needs a restart.

## 0.1.0

First public build.

- **Portals / TamesFollow:** tames following you within `TameFollowRadius`
  come through portals with you and are placed at a clear spot near your
  arrival. (Previously planned as the separate RossPortalTames mod, which
  was never released.)
- **Startup / ContinueButton:** main menu button resuming your last local
  world or server with the character you used.
- **Startup / SkipSplash:** no launch logos, no menu intro video.
- **Startup / SkipValkyrie:** no Valkyrie intro for new characters.
- Every feature and category can be switched off in one config file.
