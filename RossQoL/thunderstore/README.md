# RossQoL

Quality-of-life tweaks for Valheim. Every tweak has its
own switch, and every category has a master switch, so you keep only what
you want.

All config lives in `BepInEx/config/com.rossdwest.rossqol.cfg`. Each
setting's description says whether it is a **personal setting** or
**server-controlled when connected**.

Edits to the file apply while the game is running; there is no need to
restart. Turning a feature **off** takes effect straight away. Turning on
a feature that was off when the game launched takes effect after a restart.
Server-controlled settings changed on a server are pushed to every
connected player; while connected, your own edits to them are ignored.

Every player on a server needs RossQoL, at the same minor version.

## General

| Setting | Default | What it does |
|---|---|---|
| `HotReload` | `true` | Apply edits to the config file without a restart. |

## Combat

Server-controlled when connected.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All combat and creature tweaks. |
| `InstantLoot` | `true` | A killed creature's loot drops the moment it dies, at its body, instead of when the corpse fades a few seconds later. The corpse still falls and fades as normal. |

Other mods that drop a corpse's loot early do not cause any loot to drop
twice alongside this.

## Crafting

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All crafting station tweaks. |
| `RecipeSearch` | `true` | A search box above the crafting recipe list. Typing narrows the list to recipes whose name or type contains the text, ignoring case and spaces. Works on the Craft and Upgrade tabs; clears when the panel closes. |
| `SearchAutoFocus` | `true` | Puts the cursor in the search box when you open a crafting station, so you can type straight away. Not for the plain inventory, or with a gamepad. |
| `MultiCraft` | `true` | Adds a number box beside the Craft button; that button then makes as many as the box says, 1 to 100. Stackable items only. Server-controlled. |
| `BenchRange` | `true` | Crafting stations reach further than vanilla's 10 m. Server-controlled. |
| `BuildRange` | `40` (metres) | How far from a crafting station you can build, 1 to 100. |
| `BuildRangePerType` | (empty) | Per-station ranges overriding `BuildRange`, e.g. `piece_workbench:30, forge:15`. |
| `CraftFromChests` | `true` | Crafting, upgrading and building may take materials from nearby containers. Your pack is spent first. Requirement rows read "have/need", counting pack and containers together. Server-controlled. |
| `CraftRadius` | `40` (metres) | How far to look for containers to craft from, 1 to 100. |
| `BuildFromChests` | `true` | The hammer draws on containers too, not just crafting stations. |
| `ExtensionRange` | `10` (metres) | How far an attachment may sit from its station, 1 to 50. Vanilla is 5. Applies to attachments as they load. |
| `AutoRepair` | `true` | Opening a crafting station repairs everything it is able to repair, in one go. Server-controlled. |

Besides names, you can search by type: `helmet`, `chest`, `legs`, `cape`,
`armor`, `shield`, `utility`, `tool`, `torch`, `ammo`, `food`,
`material`, `trinket` and `weapon`, plus a weapon's skill in your game's
language (`axes`, `bows`, `knives` and so on). Type words are matched
like names, so `bow` also finds crossbows.

While the search box has the cursor, game keys are ignored: E and Tab
type letters instead of closing the panel. Press Enter or click elsewhere
to leave the box, or close the panel with Esc.

## Fires

Server-controlled when connected.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All fire tweaks. |
| `AutoFeed` | `true` | Fire pits, hearths, standing and wall torches, braziers and bathtubs take fuel from nearby containers. |
| `InfiniteFuel` | `true` | A fire at maximum fuel stops burning down and stays lit without spending more. |

How far fires reach for fuel, how often they try, and how much wood they
leave behind are the Production settings `FeedRadius`, `FeedInterval`,
`MinimumLeftBehind` and `MinimumPerItem`, shared with the workshop so a
reserve of wood is set in one place.

A fire below maximum burns as it always did, so one that is never filled
still goes out. Fires that are eternal in vanilla are left exactly as
they are.

## Food

Server-controlled when connected, except the warning, which is yours.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All food tweaks. |
| `NoFalloff` | `true` | Food gives its full effect for its whole duration instead of fading as the timer runs down. |
| `ExpiryWarning` | `true` | Warns you before a meal runs out rather than after. |
| `WarningSeconds` | `60` | How long before a meal runs out to warn you, 5 to 600. |

## Items

Server-controlled when connected.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All item tweaks. |
| `StackableMeadBases` | `true` | Mead bases and barley wine bases stack instead of taking a slot each. Finished drinks are unchanged. |
| `MeadBaseStackSize` | `20` | How many bases fit in one slot, 1 to 100. |
| `WisplightCarry` | `true` | A wisplight in your inventory works exactly as if equipped -- wisp, light and mist -- without using the utility slot. |

Stack sizes are written into saved items, so everyone in a world needs to
agree on them: this is a server setting, and a player without the mod
would see stacks their game does not expect. Unstack your mead bases
before turning it off, or a stack larger than vanilla allows is left in
your chest.

## Interface

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All HUD and interface tweaks. |
| `Clock` | `true` | Shows the in-game day and time under the minimap, e.g. `Day 42  14:30`. Midnight is 00:00, sunrise 06:00, sunset 18:00. Hidden whenever the minimap is, including on worlds without a map. |
| `Clock24Hour` | `true` | 24-hour time. Off shows 12-hour time, e.g. `2:30 PM`. |
| `ProductionTimers` | `true` | Beehives, sap collectors and fermenters show how long until the next honey or sap, until they are full, and until a batch is ready. |
| `PanCamera` | `true` | Hold `PanKey` to look around with the mouse without turning your character. |
| `PanKey` | `LeftAlt` | The key to hold for panning. |
| `PanMaxPitch` | `70` (degrees) | How far up or down panning can look, 10 to 89. |

Production countdowns are in real minutes and seconds, and read the same
for everyone, not just the player the producer belongs to. They follow
each producer's own rate, so a mod or server that changes how fast things
produce is reflected.

## Portals

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All portal tweaks. |
| `TamesFollow` | `true` | Tames following you come through portals with you. |
| `TameFollowRadius` | `20` (metres) | How close a following tame must be to come along. Measured in three dimensions. `0` brings nothing. |
| `TameSearchDistance` | `6` (metres) | How far from your arrival point to look for a clear spot for each tame, before placing it at your own position. Lower it for tight portal huts. |
| `InstantPortals` | `true` | Portals skip the fixed wait: you arrive as soon as the destination has loaded, instantly when it already is. Other teleports are unchanged. |

Ridden creatures (a saddled lox, for example) are not brought along.

With `InstantPortals`, a portal to somewhere already loaded, such as the
other end of a portal hub, is instant. A far portal still shows the
loading screen while the destination actually loads.

## Production

Server-controlled when connected.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All production tweaks. |
| `AutoHarvest` | `true` | Beehives, sap collectors, fermenters and windmills near a player empty themselves into nearby containers. |
| `HarvestBeehives` | `true` | Include beehives. |
| `HarvestSapCollectors` | `true` | Include sap collectors. |
| `HarvestFermenters` | `true` | Include fermenters. A finished batch moves whole or not at all. |
| `HarvestWindmills` | `true` | Include windmills. Flour goes into a container as it is milled, instead of waiting to be emptied by hand. |
| `HarvestRadius` | `40` (metres) | How far from a producer to look for containers, 1 to 100. Measured in three dimensions. |
| `HarvestInterval` | `10` (seconds) | Time between harvest attempts for each producer, 1 to 3600. |
| `AutoFeed` | `true` | Producers take what they need from nearby containers. |
| `FeedSmelters` | `true` | Include smelters. |
| `FeedKilns` | `true` | Include charcoal kilns. See `KilnFuel`. |
| `FeedBlastFurnaces` | `true` | Include blast furnaces. |
| `FeedWindmills` | `true` | Include windmills. |
| `FeedSpinningWheels` | `true` | Include spinning wheels. |
| `FeedOvens` | `true` | Include ovens. Fuel only; what to cook is still put in by hand. |
| `FeedShieldGenerators` | `true` | Include shield generators. |
| `FeedFermenters` | `true` | An empty fermenter takes a mead base and starts it. |
| `FeedRadius` | `40` (metres) | How far from a producer to look for containers to take from, 1 to 100. Fires use this too. |
| `FeedInterval` | `1` (second) | Time between feed attempts for each producer, 1 to 3600. One item moves per attempt, so this is also how fast a producer fills. Fires use this too. |
| `MinimumLeftBehind` | `0` | How many of an item to leave across the containers near a producer. A total for the area, not per chest. |
| `MinimumPerItem` | (empty) | Per-item totals overriding `MinimumLeftBehind`, e.g. `Wood:50, Barley:20`. |
| `KilnFuel` | `Wood` | What a kiln may be fed, comma-separated. Empty allows anything it accepts. |
| `MaxOutput` | (empty) | Stop feeding a producer once this many of what it makes sit in nearby containers, e.g. `Coal:200`. |

Output goes first into containers that already hold that item, then into
the nearest container with room. Containers from storage mods count too:
ItemDrawers drawers take only the item they hold, up to their capacity.
Output that fits nowhere stays in the producer; a fermenter's batch goes
into one container whole and is never split. A windmill's milled flour
moves the same way, so it no longer has to be emptied by hand, and stays
in the windmill when nothing nearby has room. Kilns, smelters, blast
furnaces and spinning wheels are left alone and keep dropping their
output on the ground.

Harvesting is done by the game of the player the producer belongs to at
the moment, usually whoever is nearest, so a base empties only while
someone is near it. Beehives and sap collectors fill up as normal while
nobody is. A producer is harvested only while that player has ward
access where it stands, the same as it would take to use it by hand.

A container is used only if you could open it yourself: not private to
someone else, not behind a ward you have no access to, and not open. A
container another player's game is looking after is skipped until they
leave the area.

## Progression

Server-controlled when connected.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All boss progression tweaks. |
| `TeleportUnlocks` | `true` | Metal and ore may go through a portal once the boss of its biome is dead. |
| `ClearMist` | `true` | Killing the Queen clears the mist from the Mistlands. |
| `MiningPower` | `true` | Rock and ore in a biome whose boss is dead take more damage per swing. Trees are unaffected. |
| `MiningMultiplier` | `2` | How much harder you hit that rock, 1 to 10. |
| `SmeltingYield` | `true` | Ore from a biome whose boss is dead smelts into more bars for the same fuel. |
| `SmeltingMultiplier` | `2` | How many bars one such ore yields, 1 to 10. |
| `DungeonRespawn` | `true` | Dungeons in a biome whose boss is dead come back as first found, `RespawnDays` after your last visit: burial chambers and troll caves (Elder), sunken crypts (Bonemass), frost caves (Moder), infested mines (Queen). Rebuilt from the dungeon's own seed, so the layout is unchanged. Server-controlled. |
| `RespawnDays` | `24` (in-game days) | Days after your last visit before a dungeon is rebuilt, 1 to 1000. |
| `ProtectPlayerBuilds` | `true` | A dungeon holding anything you built stops respawning, rather than being cleared out. A rebuild destroys everything inside, a portal or stash included. |

Each biome answers for itself: the Elder frees copper, tin and bronze;
Bonemass frees iron; Moder frees silver; Yagluth frees black metal. Those
four are the whole list, because those are the four biomes whose materials
vanilla refuses to carry. Killing a later boss says nothing about an
earlier biome, so a fresh character on an old world still has to beat the
Elder before carrying copper home.

Anything else vanilla refuses to teleport, it still refuses, and nothing is
written into your saved items: turn this off and the ore is simply refused
again.

## Startup

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All startup and main menu tweaks. |
| `ContinueButton` | `true` | A **Continue** button on the main menu resumes your last session: the local world or server you last played, with the character you used. |
| `SkipSplash` | `true` | Skips the logos at launch and the main menu intro video. |
| `SkipValkyrie` | `true` | Skips the Valkyrie flight and intro text on a new character's first spawn. You start at the sacrificial stones as normal. |

About Continue:

- Local worlds resume **private**. To host for friends, use Start game as usual.
- Server passwords are never stored; the normal password prompt appears.
- Crossplay servers are rejoined by join code when one is known, falling
  back to the server's id. Codes change when a host restarts.
- The button is hidden when the recorded character or world is gone, or
  when the game was launched with `+connect`, `-joincode` or
  `-joinserverwithcharacter`.

## Tames

Server-controlled when connected.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All tame tweaks. |
| `FollowCommand` | `true` | Every tamed creature can be told to follow you or stay, like a wolf: press Use on it to switch. Creatures vanilla already lets you command are unchanged. |
| `FeedFromContainers` | `true` | A hungry tame with no food on the ground near it eats one item it likes from a container within `FeedRadius`. Only creatures that are already tame. |
| `FeedRadius` | `10` (metres) | How far from a hungry tame to look for food in containers, 1 to 50. Measured in three dimensions. |
| `SilentBirths` | `true` | Tames give birth without the birth sound. The birth's other effects still play. |
| `QuietWolves` | `true` | Tamed wolves stop howling, including wolves another player owns. Wild wolves still howl, and other creatures keep their own sounds. |
| `QuietChickens` | `true` | Chicks and hens make no sound: no peeping, clucking, wing flapping, pecking, mating, footsteps, hurt or death. Birds another player owns are quiet too, and other creatures keep their own sounds. |

A creature told to follow keeps following after you log out and back in,
as a wolf does, even if `FollowCommand` is turned off in between; tell it
to stay first. With Portals/TamesFollow, following tames come through
portals with you.

A hungry tame looks for food on the ground first, as usual, and only then
in the nearest container holding something it eats. It eats one item each
time it would look for food, so it stays fed as it would from food on the
ground. Containers follow the same rules as AutoHarvest: only ones you
could open yourself, not open, and not being looked after by another
player's game. Drawers count too. In multiplayer a tame is fed by the game
of whoever is nearest to it, so it eats from containers near that player;
a container someone else is standing by waits until they move away.

## Terrain

Server-controlled when connected.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All terrain tweaks. |
| `UnlimitedHeight` | `true` | Raise and dig terrain beyond vanilla's 8 metres from the original ground, up to `MaxRaise` and `MaxDig`. |
| `MaxRaise` | `200` (metres) | How high ground can be raised above its original height, 1 to 200. Vanilla is 8. |
| `MaxDig` | `200` (metres) | How deep ground can be dug below its original height, 1 to 200. Vanilla is 8. |

Edits past 8 metres are saved in the world. With `UnlimitedHeight` off, or
RossQoL removed, that ground is drawn at 8 metres, and reappears when it is
turned back on, **as long as nobody edits it in the meantime**. Raising,
digging or levelling ground while the feature is off, or after lowering
`MaxRaise` or `MaxDig`, permanently cuts the edited area down to the lower
limit. Lower the limits only on ground you do not mind losing.

## World

Server-controlled when connected.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All world and item tweaks. |
| `FloatingItems` | `true` | Dropped items float on water instead of sinking, as wood does. |
| `FloatDepth` | `0.4` (metres) | How deep a floating item sits below the surface, 0 to 2. |
| `FastSleep` | `true` | Sleeping is quicker: the night passes in `SleepSkipSeconds` and the black screen fades in `SleepFadeSeconds`. |
| `SleepSkipSeconds` | `2` (seconds) | How long the night takes to pass while everyone sleeps, 0.1 to 12. Vanilla is 12. |
| `SleepFadeSeconds` | `0.5` (seconds) | How long the screen takes to fade to black and back when sleeping, 0 to 3. Vanilla is 3. |
| `AoeRepair` | `true` | Repairing with the hammer also repairs every damaged piece within `RepairRadius` of the one you clicked. |
| `ComfortRange` | `true` | Furniture counts toward comfort from further away. Server-controlled. |
| `ComfortRadius` | `20` (metres) | How far from you furniture still counts toward comfort, 5 to 100. Vanilla is 10. |
| `RepairRadius` | `15` (metres) | How far the hammer's repair reaches, 1 to 50. |

Items get vanilla's own floating behaviour, so they bob, splash and ride
waves like wood, and float in tar too. Live fish are left alone, so
fishing works as normal; a fish you drop from your inventory floats like
any other item. Items already lying on a seabed stay there until someone
picks them up and drops them again.

A hammer repair costs one swing of stamina and durability however many
pieces it fixes. Pieces behind a ward you have no access to are skipped,
and so are undamaged ones. A message says how many extra pieces were
repaired.

Sleeping keeps every vanilla rule: everyone must be in a bed, the same
night passes, and you wake rested. Only the waiting is shorter, about 3
seconds end to end instead of 18. The night is skipped by the server, so
`SleepSkipSeconds` comes from the server you are on.

## Dependencies

- BepInEx 5.4.2350
- Jotunn 2.30.0 or newer
