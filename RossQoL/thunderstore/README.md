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

Besides names, you can search by type: `helmet`, `chest`, `legs`, `cape`,
`armor`, `shield`, `utility`, `tool`, `torch`, `ammo`, `food`,
`material`, `trinket` and `weapon`, plus a weapon's skill in your game's
language (`axes`, `bows`, `knives` and so on). Type words are matched
like names, so `bow` also finds crossbows.

While the search box has the cursor, game keys are ignored: E and Tab
type letters instead of closing the panel. Press Enter or click elsewhere
to leave the box, or close the panel with Esc.

## Interface

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All HUD and interface tweaks. |
| `Clock` | `true` | Shows the in-game day and time under the minimap, e.g. `Day 42  14:30`. Midnight is 00:00, sunrise 06:00, sunset 18:00. Hidden whenever the minimap is, including on worlds without a map. |
| `Clock24Hour` | `true` | 24-hour time. Off shows 12-hour time, e.g. `2:30 PM`. |

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
| `AutoHarvest` | `true` | Beehives, sap collectors and fermenters near a player empty themselves into nearby containers. |
| `HarvestBeehives` | `true` | Include beehives. |
| `HarvestSapCollectors` | `true` | Include sap collectors. |
| `HarvestFermenters` | `true` | Include fermenters. A finished batch moves whole or not at all. |
| `HarvestRadius` | `40` (metres) | How far from a producer to look for containers, 1 to 100. Measured in three dimensions. |
| `HarvestInterval` | `10` (seconds) | Time between harvest attempts for each producer, 1 to 3600. |

Output goes first into containers that already hold that item, then into
the nearest container with room. Containers from storage mods count too:
ItemDrawers drawers take only the item they hold, up to their capacity.
Output that fits nowhere stays in the producer; a fermenter's batch goes
into one container whole and is never split.

Harvesting is done by the game of the player the producer belongs to at
the moment, usually whoever is nearest, so a base empties only while
someone is near it. Beehives and sap collectors fill up as normal while
nobody is. A producer is harvested only while that player has ward
access where it stands, the same as it would take to use it by hand.

A container is used only if you could open it yourself: not private to
someone else, not behind a ward you have no access to, and not open. A
container another player's game is looking after is skipped until they
leave the area.

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

Items get vanilla's own floating behaviour, so they bob, splash and ride
waves like wood, and float in tar too. Live fish are left alone, so
fishing works as normal; a fish you drop from your inventory floats like
any other item. Items already lying on a seabed stay there until someone
picks them up and drops them again.

## Dependencies

- BepInEx 5.4.2350
- Jotunn 2.30.0
