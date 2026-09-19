# Changelog

## 0.21.0

- **Progression / TeleportUnlocks:** dragon eggs may go through a portal once
  Moder is dead, and mechanical springs and Dvergr extractors once the Queen
  is dead. Same rule as ore and metal: vanilla's own refusal stands until
  then.
- **Portals / TamesFollow:** skeletons raised by the Dead Raiser now come
  through portals with you, like any other creature following you. They use
  the same `TameFollowRadius` and `TameSearchDistance` as your tames, rather
  than settings of their own.
- **Items / RecallSummons:** the Dead Raiser staff's secondary attack (middle
  click) now calls every skeleton it raised, and that is still following
  you, back to your side, spread out around you instead of piling on top of
  each other. Plays the staff's own summoning-cast animation and sound, and
  takes a brief half-second to complete, so it feels like you're casting a
  spell rather than teleporting your skeletons instantly (`RecallCastSeconds`,
  default 0.5 seconds). Costs no eitr, stamina or health, and is on a
  cooldown set by `RecallCooldownSeconds` (default 8 seconds). Vanilla
  previously did nothing with this staff's secondary attack.

## 0.20.0

- **Production / SafeRefinery:** the Eitr Refinery no longer spits the
  damaging, knocking-back projectile that otherwise fires out of it while
  it runs -- its steam, light, sound and smelting are unchanged. Applies to
  whoever owns a given refinery, so everyone on a server needs it on for it
  to be safe. A refinery already running when this is switched on keeps its
  old behaviour until it is next turned off and back on, or the area is
  reloaded.
- **Crafting / CraftFromChests:** crafting from chests no longer occasionally
  hands out free items on a server. A chest now counts towards a craft only
  if that same craft can actually be charged for it -- before, a chest could
  be readable but not writable, so its contents made a recipe look affordable
  and then paid nothing towards it, and the game had already given you the
  item by then. Payment takes charge of each chest at the moment it removes
  from it, and takes the same quality the recipe was priced against, so a
  better stack is never eaten for a craft that was costed on a plainer one.
  Nothing extra is ever taken from your own pack to make the sums add up.
- **World / NoWeathering:** rain no longer wears down or greys your
  buildings -- the grey "weathered" look was always just health dropping
  low enough to swap in the worn model, so keeping rain from chipping away
  at health keeps wood looking new. Pieces still collapse if they lose
  support, and still take damage from creatures and players; DeepNorth
  snow and AshLands ash and lava are untouched too. Applies to whoever
  owns the piece, so everyone on a server needs it for consistent results.

## 0.19.0

- **Death / GraveMarker:** a marker sits over your grave while it's in view
  and slides to the edge of the screen, pointing toward it, when it's not --
  so finding your way back after a death doesn't mean squinting at the map.
  A quiet hint line under it names `ClearGraveKey` (hold to dismiss) and,
  once you have died more than once, `CycleGraveKey` (press to cycle,
  `PageDown` by default) -- the cycle hint only appears once that key is
  bound to something. It disappears once the grave is emptied. Vanilla's
  own death pin on the map is untouched; this is a second, on-screen
  indicator alongside it, not a replacement for it.
- **Death / RespawnFood:** respawning after a death hands you a meal instead
  of an empty belly -- berries early in a run, something more substantial
  once the world has got further along. `RespawnFoodCount` and
  `RespawnFoodsByFrontier` control how much and what. Logging in on its own
  never triggers this, only a death does.
- **Death / RespawnRested:** respawning after a death also guarantees at
  least `RestedMinutes` of the Rested buff, so the walk back is not also
  spent at reduced stamina regeneration. A better Rested you already have
  from your own house is left alone.
- **Death / CorpseRun:** a modest buff called "Just Died" that grows with how
  far your grave still is -- a little more stamina regeneration and a
  little cheaper running and jumping the farther you have to go, easing off
  in steps as you close the distance but never all the way to nothing while
  the grave still stands (`CorpseRunMinStrength` is what you get standing
  right at it). It always tracks your newest grave, even while
  `CycleGraveKey` has the on-screen marker pointed at an older one, and it
  ends the moment you loot the grave, or after `CorpseRunMinutes` of real
  time if you don't make it back in time (a fresh death always gets its own
  full `CorpseRunMinutes`, and the clock restarts if you relog). Looting is
  still the deliberate handoff: emptying the grave hands you straight into
  Valheim's own, much stronger Corpse Run reward, untouched by this mod, so
  this buff is only meant to ease the walk there.
- **Death / SkillLoss:** `SkillLossMultiplier` scales how much skill a death
  costs against vanilla's usual loss -- 0.5 for half, 0 for none. A soft
  death (dying again within seconds) still costs nothing, same as vanilla;
  and vanilla's own "skills lowered" message still appears even when the
  multiplier removes the loss entirely, since that message is not part of
  what this setting scales.

## 0.18.0

- **Progression / DungeonRespawn:** a dungeon you have not entered for
  `RespawnDays` (24 by default) is rebuilt as it was first found -- chests
  stocked, creatures home, ore veins whole -- but only in a biome whose boss
  is dead. Burial chambers and troll caves wait for the Elder, sunken crypts
  for Bonemass, frost caves for Moder, infested mines for the Queen. The
  rebuild uses the dungeon's own seed, which Valheim derives from the world
  seed and the dungeon's position, so the layout that comes back is the
  layout that was there.
- Rebuilding destroys everything in the dungeon, so `ProtectPlayerBuilds`
  is on by default: a dungeon holding anything you built -- a portal, a
  stash -- stops respawning instead of being cleared out. A dungeon is also
  never rebuilt while anyone is near it.
- Dungeons in a world that predates this feature start their clock the first
  time they load, so an old save does not rebuild every crypt at once.

## 0.17.3

- **Tames / QuietChickens:** the mating sound is quiet too. It is named
  "fx_hen_love" rather than anything with "sfx_chicken" in it, so the name
  check that silenced the rest of the bird never caught it; a sound is now
  a bird's either by its own name or by hanging under a Hen or a Chicken.

## 0.17.2

- **Crafting / CraftFromChests:** every requirement row now reads
  "have/need" instead of the bare cost -- what your pack and the nearby
  containers hold together, against what the craft asks for. Vanilla never
  showed the first number at all, so a craft drawing on chests gave no sign
  of how close it was. Crafting, upgrading and the build HUD all show it.

## 0.17.1

- Built and tested against Jotunn 2.30.1, which the dependency now asks for.
  Nothing else changed.

## 0.17.0

- **Tames / QuietChickens:** chicks and hens make no sound at all -- no
  peeping, clucking, wing flapping, pecking, footsteps, hurt or death. The
  sound is stopped as this machine plays it, so birds another player owns are
  as quiet as your own, and every other creature is unchanged.

## 0.16.0

- **Food / NoFalloff:** a meal gives its full health, stamina and eitr for its
  whole duration instead of fading as the timer runs down. Vanilla scales all
  three by the time remaining, so most of a meal is spent worth less than the
  tooltip says; now the value holds and then ends.
- **Food / ExpiryWarning:** a meal says so `WarningSeconds` before it runs
  out, 60 by default, while there is still time to eat something. Vanilla only
  tells you once it has already gone.

- **Progression / MiningPower:** rock and ore in a biome whose boss you have
  killed take `MiningMultiplier` times the damage, 2 by default -- the Black
  Forest gives up its copper faster once the Elder is down, the swamp its
  scrap once Bonemass is. The ground decides, not the tool, and trees are
  unaffected.
- **Progression / SmeltingYield:** ore from a biome whose boss is dead smelts
  into `SmeltingMultiplier` bars instead of one, 2 by default, for the same
  ore and fuel. Only metals tied to a boss are affected; coal and flour are
  unchanged.

- **Crafting / CraftFromChests:** crafting, upgrading and building may draw
  materials from containers within `CraftRadius` (40 m) as well as from your
  pack. Your own inventory is always spent first and only the shortfall comes
  out of a chest, because the charge is measured after vanilla has taken what
  it can rather than predicted. Containers you may not open and other
  players' warded chests are left alone, and a container is only written when
  this client's ownership of it has settled. `BuildFromChests` covers the
  hammer as well as the workbench. Recipes that take any one of several
  ingredients -- mead bases, cooked dishes -- are covered too: they never go
  through the usual paying path, so the ingredient is sourced from a container
  and the removal topped up from it.

## 0.15.0

- **Items / WisplightCarry:** a wisplight in your inventory works exactly as
  if it were equipped -- the wisp circling you, its light and the mist it
  pushes back -- without costing you the utility slot, so it never competes
  with the Megingjord. It is not an imitation: everything the equipped
  wisplight does comes from the item's own equip status effect, and that is
  what this grants.

- **World / ComfortRange:** furniture counts toward comfort from
  `ComfortRadius` metres away, 20 by default, instead of vanilla's 10. Which
  pieces count, how duplicates collapse and the need to be under shelter are
  all unchanged -- vanilla does the same work on a longer list.

- **Progression / ClearMist:** killing the Queen clears the mist from the
  Mistlands. Emitters stop through vanilla's own switch and the mist already
  in the air is cleared; nothing is destroyed and nothing is written to the
  world, so turning it off brings the mist straight back. Wisplights are
  unchanged and still worth carrying underground.

## 0.14.0

- **Items / StackableMeadBases:** barley wine bases stack too, not just mead
  bases. They are a fermenter input under another name, and were the one
  brewing item still taking a slot each.

- **Crafting / BenchRange:** crafting stations reach `BuildRange` metres
  instead of vanilla's 10, with `BuildRangePerType` for stations that want
  their own radius and `ExtensionRange` for how far attachments may sit from
  their station. Switching it off puts every station back to the range its
  prefab shipped with.
- **Progression / TeleportUnlocks:** metal and ore may go through a portal
  once you have killed the boss of the biome it comes from -- the Elder for
  copper and tin, Bonemass for iron, Moder for silver, Yagluth for black
  metal. Per biome, not cumulative: a later boss says nothing about an
  earlier biome. Nothing is written into saved items, so turning it off
  simply refuses the ore again.
- The startup compatibility check no longer fills the console with HarmonyX
  warnings. It asks whether each Valheim member it depends on still exists by
  trying field, then property, then method, and AccessTools logs every miss --
  so each method it confirmed cost two warnings, 236 of them on a full load.
  It now asks through plain reflection, which is quiet. Nothing about the
  check itself changes: a member that really is missing is still reported as
  an error naming the feature.

## 0.13.1

- **Crafting / MultiCraft:** typing an amount now makes that many, instead of
  five. Vanilla hides the Craft button while a craft runs, the amount box went
  with it, and the amount was put back to vanilla's five before the craft
  finished -- and because the finished craft reads that number for both what
  it makes and what it costs, you were given five and charged for five. The
  amount now holds from the moment the button is pressed until the craft ends.

## 0.13.0

- **Production / AutoFeed:** smelters, kilns, blast furnaces, windmills and
  spinning wheels take ore and fuel from nearby containers; ovens and shield
  generators take fuel; an empty fermenter takes a mead base and starts it.
  One item moves per attempt and an attempt runs every second, so a producer
  fills gradually and a container is never emptied in one go.
  Everything is handed over with vanilla's own RPCs, one unit at a time, and
  containers are used under the same rules as harvesting: ward access at the
  producer, the container's privacy, fresh contents and settled ownership.
  `MinimumLeftBehind` and `MinimumPerItem` keep a reserve across the chests
  near a producer, counted as a total for the area rather than per chest,
  `KilnFuel` limits what a kiln may burn (plain wood by default), and
  `MaxOutput` stops a producer once you have enough of what it makes. Ore
  into metal is never capped.
- **Fires / AutoFeed:** fire pits, hearths, torches, braziers and bathtubs
  take fuel from nearby containers, using the Production feed settings.
- **Fires / InfiniteFuel:** a fire at maximum fuel stops burning down and
  stays lit without spending more. Below maximum it burns as it always did.
- **Crafting / MultiCraft:** the amount is now a box beside the Craft button
  rather than a second button above it, which covered the ingredient list.
  The Craft button itself makes as many as the box says, and vanilla's own
  multi-craft path drives the label, the ingredient list and the greying-out,
  so they all agree. The box starts at 1, so crafting is vanilla's until you
  type a number, and the `MultiCraftAmount` setting is gone: the box is the
  setting, and it is in front of you.
- **Items / StackableMeadBases:** mead bases stack to `MeadBaseStackSize`
  (20 by default) instead of taking a slot each. Unstack them before turning
  it off.

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
