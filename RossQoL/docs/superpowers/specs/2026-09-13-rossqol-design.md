# RossQoL — Design

**Date:** 2026-09-13
**Status:** Approved in conversation, spec under review
**Game version verified against:** Valheim 1.0.12 (network version 40)

## Goal

One mod for the small quality-of-life tweaks that would otherwise each be
their own mod, in the spirit of what Valheim Plus used to do. Each tweak is
an independently switchable feature; players and server admins turn off
what they do not want.

This spec covers the **framework** and the first three features:

| Category | Feature | Scope |
|---|---|---|
| Portals | Tames follow you through portals (formerly RossPortalTames) | Client |
| Startup | Continue button (last local world or last server) | Client |
| Startup | Skip splash logos and menu intro video | Client |
| Startup | Skip the Valkyrie intro on a new character | Client |

Later tweaks each get a short spec of their own on top of this framework.

## Scope

**In:** the plugin, the feature/category model, config and sync rules,
compatibility checking, and the three features above.

**Out:** ItemDrawers, which stays a separate mod — it adds build pieces and
already has a public release. Also out, for now: every Valheim Plus-style
gameplay tweak (stamina, carry weight, building, crafting, map). The
framework must support them; this spec does not design them.

## Decisions

| Decision | Reason |
|---|---|
| One mod, many features, each with its own toggle | Many small mods means many manifests, releases and config files for things too small to justify them. |
| Features grouped into **categories** by what they modify | Directories and config sections read by area (`Portals`, `Startup`, later `Player`, `Building`, …) rather than by a flat list of tweak names. |
| **One** config file; one section per category | A player configures everything in one place. |
| Every category has a master `Enabled` | Turn off a whole area at once. |
| Jotunn for config sync and version enforcement | Already proven in ItemDrawers in this repo; `AdminOnly` entries and `NetworkCompatibility` give server authority, late-join sync and live reload without code of our own. |
| Server enforces gameplay-affecting (`Synced`) features | A client must not set its own carry weight on a shared world. Personal features (`Client`) stay personal. |
| `EveryoneMustHaveMod`, `VersionStrictness.Minor` | Synced features only work if every peer runs the same rules. |
| PortalTames loses its "no Jotunn" property | Consequence of the bundle choice; Jotunn is already on nearly every modded install and ships with ItemDrawers. |
| Server passwords are never stored | Continue shows vanilla's password prompt instead. |
| Continue resumes local worlds **private** (not open to others) | The server open/public/password choices are not saved per world by vanilla, and the password is deliberately not stored. Hosting for friends still goes through the normal Start screen. |

## Architecture

Two layers per project, mirroring ItemDrawers and RossPortalTames: rules
worth testing must not need a running game.

```
RossQoL/
  docs/
  src/
    RossQoL.Core/          pure C#, no engine references, unit-tested
      Framework/
      Portals/
      Startup/
    RossQoL.Game/          plugin, Harmony patches, Unity adapters
      Framework/
      Portals/
      Startup/
  tests/
    RossQoL.Core.Tests/
      Portals/
      Startup/
  thunderstore/
```

Namespaces follow directories (`RossQoL.Game.Portals`). The existing
architecture test that forbids engine references in Core carries over and
covers every category.

### The feature model

```csharp
enum FeatureScope { Client, Synced }   // Core

abstract class Feature                 // Game
{
    abstract string Key { get; }            // its own toggle, e.g. "TamesFollow"
    abstract FeatureScope Scope { get; }
    abstract string Description { get; }
    virtual void BindSettings(ConfigFile config, string section);   // entries beyond the toggle
    abstract IEnumerable<Type> PatchClasses { get; }                // [HarmonyPatch] classes it owns
    virtual IEnumerable<CompatMember> RequiredMembers { get; }      // Valheim members reached by name
    virtual void OnActivated(GameObject host);                      // e.g. add a component to the shared host
    bool IsActive { get; }                                          // category Enabled && own toggle
}

sealed class Category
{
    string Section { get; }        // "Portals"
    IReadOnlyList<Feature> Features { get; }
}
```

An abstract class rather than an interface, because every feature carries
the same toggle, category back-reference and `IsActive`.

A `FeatureRegistry` in the Game layer lists the categories explicitly — no
reflection discovery — so the set of features is readable in one file.

**Enabled state.** A feature is active when its category's `Enabled` **and**
its own toggle are both true. Patches read a single cached
`feature.IsActive` rather than the two entries.

### Scope and the section toggle

- A `Client` feature's entries are ordinary local config.
- A `Synced` feature's entries are marked Jotunn `AdminOnly`; a connected
  server's values override the client's.
- A category's `Enabled` takes the **strictest scope of its features**: if
  any feature in the category is `Synced`, the category toggle is
  `AdminOnly` too. Otherwise a client could turn off a synced feature by
  turning off its section.

Consequence recorded for future specs: prefer not to mix Client and Synced
features in one category, since a mixed category's master switch leaves the
client features under server control.

### The patching rule

- **Client features** are patched at startup only if active. Disabled means
  untouched, which removes any chance of conflicting with another mod.
  Changing a client toggle requires a restart; each such entry's description
  says so.
- **Synced features** are always patched and check `IsActive` on every call,
  because a server can switch them on after the client has loaded.

Patching keeps PortalTames' per-class `try/catch`: each patch class is
applied on its own, and a failure logs the class and the **feature** it
belongs to, then continues. Harmony's `PatchAll` is not used, since one
throwing class can abort the loop and leave later classes silently
unpatched. This also isolates us from broken mods that patch the same
methods (for example the deprecated Valkyrie-skip mods that fail on 1.0).

### Compatibility check

`ValheimCompat` becomes shared. At startup it checks every
`RequiredMembers` entry of every feature, **before** patching, and logs
failures grouped by feature: "Startup/ContinueButton needs
`FejdStartup.OnWorldStart` — not found; this feature is disabled." A
feature with a missing member is not patched even if enabled. The
`RequireMethod` Prepare helper stays for patch classes.

### Plugin

| | |
|---|---|
| GUID | `com.rossdwest.rossqol` (permanent: it names the config file) |
| Name | `RossQoL` |
| Version | `0.1.0` |
| Dependency | `ValheimModding-Jotunn-2.30.0` (`[BepInDependency(Jotunn.Main.ModGuid)]`) |
| Network | `[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]` |

`Awake`: bind config → compat check → patch per rule → create the single
manager object that carries the mod's only `Update` → log loaded.

## Feature: Portals / TamesFollow

The RossPortalTames design (`docs/superpowers/specs/2026-09-13-portal-tames-design.md`,
moved into this directory as history) carries over unchanged in behaviour:
eligibility, validated candidate placement, departure capture on the
`TeleportWorld.Teleport` postfix, and ownership re-assertion plus Rigidbody
sync at arrival.

Changes are structural only:

| Before | After |
|---|---|
| `RossPortalTames.Core.*` | `RossQoL.Core.Portals.*` |
| `RossPortalTames.Game.*` | `RossQoL.Game.Portals.*` |
| `PortalTamesPlugin` | removed; logic that belonged to the plugin moves to the framework |
| `PortalTamesManager` on its own GameObject | a component on the single shared RossQoL host object, added only when the feature is active |
| `[General] Enabled / FollowRadius / SearchDistance` | `[Portals] TamesFollow / TameFollowRadius / TameSearchDistance` |
| "Config is local because there is no Jotunn" | Config is local because the feature is `Client` scope: every setting only affects which of your own tames follow you. |

RossPortalTames was never released, so no config migration is needed. The
outstanding in-game short-hop test and dedicated-server persistence check
remain outstanding and are re-run against RossQoL.

## Feature: Startup / ContinueButton

A button at the top of the main menu that resumes your most recent session
— a local world or a server — with the character you used, skipping the
character and world screens.

### What is recorded

Vanilla remembers the last character and last world separately
(`PlatformPrefs` `profile`, `world`) and servers in a recent list, but not
**which** you did last, and `profile` omits the character's file source
(local/cloud), which makes same-named characters ambiguous. RossQoL keeps
its own record.

`LastSession` (Core, plain data):

| Field | Local world | Server |
|---|---|---|
| `Kind` | `LocalWorld` | `Server` |
| `CharacterFile`, `CharacterSource` | profile filename and `FileHelpers.FileSource` | same |
| `WorldName`, `WorldSource` | world `m_name` and file source | — |
| `ServerKind` | — | `Dedicated`, `SteamUser` or `PlayFab` |
| `ServerAddress` | — | `host:port`, host Steam id, or PlayFab remote player id |
| `JoinCode` | — | crossplay join code when one was known, else empty |
| `DisplayName` | world name | server name as shown in the server list, else the address |

Stored as one string under `PlatformPrefs` key `RossQoL.LastSession`,
serialized by Core (`LastSessionFormat`), with a version prefix so the format
can change later. Not in the config file and not synced: it is per machine.
Passwords are never part of it.

### When it is recorded

Only once a session has **actually started**, so a failed connect or a
wrong password never replaces a good record:

1. **Intent.** Postfix `FejdStartup.JoinServer`: stash `GetServerToJoin()`
   with its display name (`MultiBackendMatchmaking.GetServerName`) and, for
   PlayFab servers, the join code from
   `MultiBackendMatchmaking.GetServerMatchmakingData(...).m_joinCode` when
   the matchmaking data has one. Prefix `FejdStartup.OnWorldStart`: clear
   any stashed server intent.
2. **Commit.** On `Game.m_playerInitialSpawn` (fires after connection and
   password succeed):
   - `ZNet.instance.IsServer()` and not dedicated → record `LocalWorld` from
     `ZNet.World` and `Game.instance.GetPlayerProfile()`.
   - otherwise → record `Server` from the stash: `Dedicated` stores
     `ServerJoinDataDedicated.ToString()` (`host:port`), `SteamUser` the
     host's Steam id, `PlayFab` the remote player id plus any join code.
     Steam invites and lobby joins resolve to the host's `SteamUser` entry
     before `JoinServer`, so they are recorded like any Steam host and
     reconnect while that host is online. A spawn with no valid stash
     records nothing, leaving the previous record intact.

### The button

- Postfix `FejdStartup.SetupGui`. Clone the "Start game" button — found via
  `m_menuButtons` by its `OnStartGame` listener, falling back to
  `m_menuButtons[0]` — name the clone `RossQoL_Continue`, and place it at the
  sibling index **directly above Start game** rather than at a fixed index 0,
  so it coexists with mods like ServerQuickConnect and menu redesigns that
  also insert at the top.
- Reassign `m_menuButtons` so keyboard and gamepad navigation includes it.
- Label: `Continue: <character> on <DisplayName>`, truncated to fit.
- **Hidden** when any of these hold (decided by Core's `ContinuePolicy`,
  given plain facts from the Game layer):
  - no record, or the record fails to parse;
  - the recorded character file is not in `SaveSystem.GetAllPlayerProfiles()`
    for its source (includes cloud saves being unavailable);
  - `LocalWorld` whose world is not in `SaveSystem.GetWorldList()` for its
    source, or has `m_dataError != None`;
  - the game was launched with `+connect`, `-joincode` or
    `-joinserverwithcharacter`, so the button never races vanilla's own join.

### What clicking does

Both paths go through vanilla's own methods, so Jotunn's and ServerSync's
version-mismatch handling and error dialogs keep working.

**Local world:**
1. Select the character: set `m_profileIndex` to the matching profile and call
   `SelectCharacter(file, source)`.
2. Set `m_world` to the matching `World` (never `World.GetCreateWorld`, which
   creates a world if the name is missing).
3. Make whatever `OnWorldStart` reads for open/public/password say private
   (not open, not public, no password). The exact members are pinned down
   during planning from the decompiled `OnWorldStart`.
4. Call `OnWorldStart()`.

**Server:**
1. Select the character as above.
2. Build `ServerJoinData`: `ServerJoinDataDedicated(address)`,
   `ServerJoinDataSteamUser(ulong)` or `ServerJoinDataPlayFabUser(id)`. For
   `PlayFab` with a join code while logged in to PlayFab, call
   `ZPlayFabMatchmaking.ResolveJoinCode` first and join the resolved
   `remotePlayerId`, falling back to the stored id if the code no longer
   resolves (codes are regenerated when a host restarts; dedicated crossplay
   ids are expected to be stable).
3. `SetServerToJoin(data)`, then `JoinServer()`. Vanilla adds it to the recent
   list and shows the password prompt if the server has one.

**Failures** are vanilla's: an offline server, a failed join-code resolve or a
refused version shows the normal error and leaves the player on the main
menu. The record is unchanged, so Continue stays available.

## Feature: Startup / SkipSplash

- Postfix `SceneLoader.Awake`: set `_showLogos = false`. Only the logo fades
  are skipped; save-data initialisation and scene loading still wait
  normally.
- Prefix `FejdStartup.Start`: set `CinematicsManager.s_instance.m_introOnStartup = false`
  when the instance exists, so the main menu's intro video never starts.
  Blocking the `PlayIntroCinematic` coroutine instead (as one public mod does)
  is rejected: it also suppresses the menu's fade-in trigger.
- Not done: `FejdStartup.m_firstStartup = false` (used by another public mod),
  because it also disables vanilla's `+connect` launch option; and editing
  `globalgamemanagers`, which modifies game files.

**Must be verified in game:** that BepInEx has applied the patch before the
boot scene's `SceneLoader.Awake` runs. If it has not, the logos cannot be
skipped by patching, and the feature is reduced to the menu intro video, with
the config description saying so.

## Feature: Startup / SkipValkyrie

- Postfix `Game.Start`: set `m_queuedIntro = false`.
- The player then spawns through vanilla's normal path,
  `SpawnPlayer(point, false)`, which still finds the start location, counts
  the spawn, and clears `PlayerProfile.m_firstSpawn`.
- This also skips the new-world intro text that is queued by the same flag.
  The config description says so.
- Rejected: blocking `Player.OnSpawned` or the Valkyrie spawn. That leaves the
  player stuck in intro mode, and skips work other mods (Jotunn) do in
  `Player.OnSpawned` postfixes. Clearing the flag late, after `Game.Start`,
  is too late to stop the Valkyrie.

## Configuration

`BepInEx/config/com.rossdwest.rossqol.cfg`:

| Section | Key | Default | Scope | Meaning |
|---|---|---|---|---|
| Portals | `Enabled` | true | Client | All portal tweaks. |
| Portals | `TamesFollow` | true | Client | Tames following you come through portals with you. |
| Portals | `TameFollowRadius` | 20 | Client | Metres; 3D distance. 0 brings nothing. |
| Portals | `TameSearchDistance` | 6 | Client | Metres to search for a clear arrival spot before falling back to your own position. |
| Startup | `Enabled` | true | Client | All startup tweaks. |
| Startup | `ContinueButton` | true | Client | Main menu button resuming your last world or server. |
| Startup | `SkipSplash` | true | Client | Skip the logos at launch and the menu intro video. |
| Startup | `SkipValkyrie` | true | Client | Skip the Valkyrie and intro text on a new character's first spawn. |

Every description ends with its scope ("Personal setting." / "Server-controlled
when connected.") and, for client toggles, "Requires restart."

## Error handling

- Missing Valheim members disable the affected feature only, logged by
  feature at startup.
- A patch class that fails to apply is logged with its feature; other features
  are unaffected.
- A corrupt `RossQoL.LastSession` string parses to "no record" and hides the
  button; it is overwritten by the next successful session.
- Continue never catches and hides vanilla connection errors.

## Compatibility

Checked against every mod in the dev profile (decompiled) and the public mods
that touch the same areas.

| Mod | Surface | Result |
|---|---|---|
| Jotunn | `FejdStartup.Awake`/`SetupGui`, `ShowConnectError`, ZNet handshake, `Game.Start`, `Player.OnSpawned` postfixes | Compatible. Both only add UI at `SetupGui`; we never skip `Player.OnSpawned`, and Continue uses vanilla's join so Jotunn's mod-mismatch handling still runs. |
| ServerSync-bundling mods (PlantEverything, OdinShip, NoVikingLeftBehind, OttoFuel, Dive_In), OneMapToRuleThemAll | `ShowConnectError`, ZNet connection methods | Compatible through the vanilla join path. |
| XPortal | `Game.Awake` prefix, `Game.Start` postfix, portal pairing | Compatible; see the PortalTames spec for portals. |
| Other dev-profile mods | Nothing relevant | Compatible. |
| Radamanto ServerQuickConnect | `SetupGui` postfix inserting a cloned button at index 0 | Button placement relative to Start game, not index 0, so both buttons appear. |
| MaxGerman Skip_Intro_Video | `SceneLoader.Awake` `_showLogos`, blocks `PlayIntroCinematic` | Doubling up is harmless. |
| VB_QOL NoIntroNoValkyrie | Late clear of `m_queuedIntro` / `m_inIntro` | Harmless alongside ours. |
| Deprecated Valkyrie-skip mods (purpledxd, FioteBear) | `Player.OnSpawned` prefix with a field removed in 1.0 | Fail to patch on 1.0; per-class patching keeps that failure from affecting ours. |
| iskarian SkipMenu, aedenthorn QuickLoad (deprecated) | Rewrite menu flow; pre-1.0 APIs | Likely broken on 1.0. Not specifically handled. |
| bdew QuickConnect | Own server window, handshake password prefix for its own joins | Compatible. |
| ValheimPlus forks | `FejdStartup.Awake`/`SetupGui` cosmetics | Compatible. |

## Testing

**Core unit tests:**
- Framework: active = category ∧ feature; category toggle scope is the
  strictest of its features; a feature with missing members is not patched.
  (Registry and scope rules modelled in Core with plain data.)
- Portals: the existing 29 tests, moved.
- `LastSessionFormat`: round-trips every kind; rejects unknown versions,
  truncated and garbage strings; handles names containing the separator.
- `ContinuePolicy`: each hidden condition above, and the visible cases.
- `SessionCapture`: a spawn without intent commits nothing; a local world
  start clears a stale server intent; a server spawn records the stashed
  kind, address, join code and name.

**In game, dev profile:**
1. Continue into a local world, quit, relaunch: button shows it; click resumes
   with the right character, private.
2. Join a dedicated server by IP, quit, relaunch: Continue rejoins; password
   prompt appears when the server has one.
3. Crossplay join code, if a server is available to test against.
4. Wrong password or offline server: vanilla error, record unchanged.
5. Delete the recorded world: button hidden.
6. Launch with `+connect`: button hidden.
7. SkipSplash: no logos, no menu video; verify the boot-scene timing question.
8. SkipValkyrie: new character spawns at the start location, can move, and
   `m_firstSpawn` is cleared on the next load.
9. Toggle each section `Enabled` off: its features do nothing and are unpatched
   (log).
10. PortalTames manual checks from its spec, plus the outstanding short-hop test.

## Packaging and repository

- `RossQoL/deploy.sh` and `package.sh` adapted from RossPortalTames; deploy to
  the r2modman **dev** profile only.
- `thunderstore/manifest.json`: `RossQoL` 0.1.0, depends on Jotunn 2.30.0.
  README: a section per category with a features-and-scope table.
- Icon from `tools/make-icon.py`.
- `.gitignore`: RossQoL build output and zip; RossPortalTames entries removed.
- Root README: RossPortalTames row replaced by RossQoL.
- Files are moved with `git mv` from `RossPortalTames/` so history follows;
  the PortalTames spec and plan move into `RossQoL/docs/` unchanged, as history.

## Adding a feature

1. Pick the category by what the feature modifies; create one if none fits.
2. Decide scope. If it is gameplay-affecting on a shared world, it is `Synced`.
   Avoid putting it in a category of `Client` features.
3. Put rules worth testing in `Core/<Category>/`, patches and adapters in
   `Game/<Category>/`.
4. Subclass `Feature`: bind entries in the category section, list patch
   classes and every Valheim member reached by name.
5. `Synced` patches check `IsActive` on every call.
6. Register it in `FeatureRegistry`, add its config rows to the Thunderstore
   README and its compatibility notes to this spec's Compatibility table.

### Synced-scope caveats

- A `Synced` feature's `OnActivated` runs even when it is off at startup
  (`FeatureRules.ShouldPatch` still patches the category so a later toggle
  works without a restart) -- anything `OnActivated` adds (subscriptions,
  UI, background state) must itself check `IsActive` before doing anything
  visible or persistent.
- A `Client` feature that was off at startup stays unpatched until restart,
  even if a `Synced` category toggle in the same category later turns it on:
  scope, not category membership, decides whether a feature can react to a
  toggle without restarting.
