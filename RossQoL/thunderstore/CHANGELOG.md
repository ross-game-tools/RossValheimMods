# Changelog

## 0.12.0

- **Crafting / MultiCraft:** a number box and a second craft button sit above
  the Craft button. Type how many you want and press it to make that many of
  a stackable item at once, for the materials they all cost. The pair appears
  only for recipes that stack, and is greyed out when you cannot afford the
  amount typed; upgrades and items that do not stack are unchanged. The box
  starts at `MultiCraftAmount`, default 10.
- **Crafting / AutoRepair:** opening a crafting station repairs everything
  that station can repair, with one sound and one message instead of a click
  per item. A station still only mends what its own type and level allow, so
  a workbench will not mend a bronze axe and a level 1 forge will not mend
  what needs level 2. The repair button works as before.
- **Interface / PanCamera:** panning up no longer looks down. Vertical
  panning was inverted against the rest of the game; the game's own
  invert-mouse setting still applies.

## 0.11.1

- **Tames / QuietWolves:** tamed wolves owned by another player are quiet
  too. A howl is a networked sound made by whichever client owns the wolf,
  so silencing the idle timer only ever quieted this client's own wolves --
  in multiplayer, usually none of them. The howl is now silenced as it is
  played, which every client does for itself. Wild wolves still howl.

## 0.11.0

- **Production / AutoHarvest:** windmills empty themselves into nearby
  containers as they mill, instead of holding flour until someone empties
  them by hand. Flour moves whole or not at all and stays in the windmill
  when nothing nearby has room. Kilns, smelters, blast furnaces and
  spinning wheels are unchanged. Turn it off with `HarvestWindmills`.

## 0.10.0

- **Interface / ProductionTimers:** beehives, sap collectors and fermenters
  show how long until the next honey or sap, until they are full, and until
  a batch is ready.
- **Interface / PanCamera:** hold left Alt (`PanKey`) to look around with
  the mouse without turning your character. Releasing it snaps the camera
  back.
- **Tames / QuietWolves:** tamed wolves stop howling. Wild wolves are
  unchanged.
- **World / AoeRepair:** repairing with the hammer also repairs every
  damaged piece within `RepairRadius` (default 15 m) of the one you
  clicked, for one swing's stamina and durability. Server-controlled.

## 0.9.0

- **World / FloatingItems:** dropped items float on water instead of
  sinking, as wood does. Live fish are unchanged. Server-controlled.
- **World / FastSleep:** sleeping is quicker. The night passes in
  `SleepSkipSeconds` (default 2) instead of vanilla's 12, and the black
  screen fades in `SleepFadeSeconds` (default 0.5) instead of 3.
  Server-controlled.

## 0.8.0

- **Portals / InstantPortals:** portals skip vanilla's fixed wait. You
  arrive as soon as the destination has loaded, instantly when it already
  is. Personal setting.

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
