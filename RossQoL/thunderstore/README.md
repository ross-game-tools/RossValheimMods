# RossQoL

Quality-of-life tweaks for Valheim. Every tweak has its
own switch, and every category has a master switch, so you keep only what
you want.

All config lives in `BepInEx/config/com.rossdwest.rossqol.cfg`. Each
setting's description says whether it is a **personal setting** or
**server-controlled when connected**. Changing a feature's on/off switch
takes effect after a restart.

Every player on a server needs RossQoL, at the same minor version.

## Crafting

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All crafting station tweaks. |
| `RecipeSearch` | `true` | A search box above the crafting recipe list. Typing narrows the list to recipes whose name contains the text, ignoring case and spaces. Works on the Craft and Upgrade tabs; clears when the panel closes. |
| `SearchAutoFocus` | `true` | Puts the cursor in the search box when you open a crafting station, so you can type straight away. Not for the plain inventory, or with a gamepad. |

While the search box has the cursor, game keys are ignored: E and Tab
type letters instead of closing the panel. Press Enter or click elsewhere
to leave the box, or close the panel with Esc.

## Portals

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | All portal tweaks. |
| `TamesFollow` | `true` | Tames following you come through portals with you. |
| `TameFollowRadius` | `20` (metres) | How close a following tame must be to come along. Measured in three dimensions. `0` brings nothing. |
| `TameSearchDistance` | `6` (metres) | How far from your arrival point to look for a clear spot for each tame, before placing it at your own position. Lower it for tight portal huts. |

Ridden creatures (a saddled lox, for example) are not brought along.

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

## Dependencies

- BepInEx 5.4.2350
- Jotunn 2.30.0
