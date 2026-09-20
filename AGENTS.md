# AGENTS.md

Everything an agent needs to work in this repository. Read this before
touching anything; it records the decisions and traps that the code itself
does not explain.

## What this is

BepInEx mods for Valheim, one directory per mod, each independently
buildable, testable and packaged for Thunderstore. Shared conventions live
at the root; anything mod-specific lives under that mod's directory, so a
mod can be extracted later without untangling it from the others.

| Directory | Mod | Notes |
|---|---|---|
| `ItemDrawers/` | RossItemDrawers | Drawers that hold one item type in bulk. Uses Jotunn. |
| `RossQoL/` | RossQoL | Many independently switchable quality-of-life features. Uses Jotunn. |

Each mod's own `README.md` covers what it does and how it is structured.
This file covers how to work on them.

## Environment

Versions currently targeted — check the log of a real run rather than
trusting this table, which ages:

| | |
|---|---|
| Valheim | 1.0.15, network version 40 |
| BepInExPack Valheim | 5.4.2350 |
| Jotunn | 2.30.1 |
| Plugin target framework | `netstandard2.1` |
| Test target framework | `net8.0`, xunit |
| Mod manager | r2modman (`r2modmanPlus-local`) |

Both plugins declare
`[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]`,
so a client and server may differ in patch version but not minor.

### Building

Valheim assemblies are referenced by path and never committed. Set
`VALHEIM_INSTALL` and `JOTUNN_INSTALL` either as environment variables or
by copying `<Mod>/Directory.Build.local.props.sample` to
`Directory.Build.local.props` (gitignored) and editing it. Defaults in
`Directory.Build.props` assume a standard Steam install and the r2modman
`dev` profile's Jotunn.

Jotunn is a `HintPath` reference to an installed `Jotunn.dll`, deliberately
not a `PackageReference`: JotunnLib on nuget.org ships `lib/net462` only, so
a `netstandard2.1` project restores it with zero usable references, no
compile error and no warning.

```bash
dotnet build ItemDrawers/ItemDrawers.sln -c Release   # or RossQoL/RossQoL.sln
dotnet test  ItemDrawers/ItemDrawers.sln -c Release
```

Builds must be warning-clean. `MSB3277` is downgraded to a message in
`Directory.Build.props` and is expected; nothing else should appear.

## Architecture: Core and Game

Every mod splits in two, and the split is enforced by a test, not by
convention:

- `src/<Mod>.Core` — pure C#. No Unity, no Valheim, no BepInEx, no Jotunn,
  no Harmony. All the logic worth testing lives here.
- `src/<Mod>.Game` — the adapter. Harmony patches, Unity components,
  Jotunn registration, config.

`tests/<Mod>.Core.Tests/ArchitectureTests.cs` fails the build if Core ever
references a game or engine assembly — it checks both the compiled
assembly's references and the csproj text, so a reference added "for later"
before any code uses it is caught too. If that test fails, the design has
drifted; do not relax the test.

The practical consequence: when you need to add behaviour, ask what part of
it is arithmetic or state machine (Core, with unit tests) and what part is
genuinely engine interaction (Game). Multiplayer timing rules in particular
belong in Core, where two peers can be simulated directly — see
`ItemDrawers.Core/ViewFlushPolicy.cs` and its two-peer tests, which
reproduce a livelock that needs a second player on a server to observe live.

## Verified Valheim internals go in `docs/valheim-api/`

Both mods here patch Valheim by name, so a wrong signature or a
half-remembered method body costs a build-deploy-test cycle to find.
Everything we learn about the game's own code is therefore written down:

- **Look there first.** Before decompiling anything, read
  `docs/valheim-api/README.md` and the file for the subsystem you are
  touching. Re-deriving what is already recorded wastes a decompile pass.
- **Add to it as you go.** Whenever you verify a Valheim type, member,
  signature or method body — during design, while writing a patch, or while
  debugging one — append it to the relevant file in the same session, with
  the real decompiled code quoted. Do not start a fresh dump beside an
  existing one; extend the existing one.
- **Only verified facts.** Nothing in there comes from memory. Every file
  records the game version it was read from, and anything that could not be
  confirmed from the assemblies is called out as unverified — Unity
  serialized values (status effect durations, icons, prefab fields) are not
  in the DLLs at all and must be read at runtime.
- **Write it down even when it turns out you were wrong.** A member that
  does not exist, a hook that never fires, an inlined method a patch cannot
  reach: those are the entries that save the most time later.

Decompile with `ilspycmd -t <FullTypeName> <assembly.dll>`; the game
assemblies are under `<VALHEIM_INSTALL>/valheim_Data/Managed/`.

## Two recurring bug classes

**Editor-set fields.** A prefab authored in the Unity Editor carries its
serialized field values; a `GameObject` built in code gets the bare C#
default instead. This has bitten this repo repeatedly:
`ZNetView.m_persistent` (drawers vanished on reload), `Piece.m_icon`,
`Piece.m_enabled`, `Piece.m_usage` (drawer missing from its build tab),
`Container.m_privacy` (defaults to Private, so nobody else could use a
drawer), `Piece.m_placeEffect` (built and repaired in silence), and
`GameObject.layer` (drawers sat on `Default` instead of `piece`, so nothing
could snap to them and nearby-container mods could not find them).

When adding any component in code, check every field the vanilla prefab
would have had set, and say in a comment why the value you chose is right.

**Harmony versus inlining.** Patches on tiny methods get bypassed when Mono
inlines them. Patch `Update`/`FixedUpdate` or mutate state instead; a method
under roughly 32 IL bytes is a candidate for inlining and cannot be relied
on as a patch target.

Both mods run a startup compatibility check (`ValheimCompat.Verify`) over
every Valheim member reached by name, and every Harmony patch class has a
`Prepare()` that skips the patch if its target is missing. Follow that
pattern for new patches: a game update should produce one clear log line
naming the missing member, not a stack trace from inside `Awake`.

## Deploying and testing

```bash
bash ItemDrawers/deploy.sh          # defaults to the 'dev' profile
bash RossQoL/deploy.sh dev
```

- **Never deploy to the `Default` profile.** r2modman owns it. `deploy.sh`
  refuses by name, but do not work around that — Ross installs released
  builds there through r2modman, and overwriting them silently makes his
  daily game a test build.
- **Watch for duplicate installs.** A profile can hold both the folder
  `deploy.sh` writes (`plugins/ItemDrawers/`) and an r2modman-installed
  `Ross-RossItemDrawers` of a released version. BepInEx loads only the
  higher version number and logs a mild "Skipping … because a newer version
  exists", so a test can silently exercise the wrong build. `deploy.sh`
  warns; heed the warning. This has already cost one session's worth of
  confusing results.
- **Read the log.** `~/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/<profile>/BepInEx/LogOutput.log`.
  Confirm the version line, the compat check, and the Harmony patch list
  before trusting any in-game observation.
- **The dev profile carries Ross's published mods**, so compatibility with
  the real mod set is exercised. Use local builds only for the mod being
  worked on.

Unit tests prove Core logic. They cannot prove a prefab registers, a patch
applies, or a piece behaves — those need an in-game pass, and for anything
touching multiplayer, a dedicated server with two clients.

## Releasing

1. The version lives in **two** places and `package.sh` refuses to build if
   they disagree: `thunderstore/manifest.json` (`version_number`) and the
   plugin's `PluginVersion` constant (`DrawerPlugin.cs`,
   `RossQoLPlugin.cs`).
2. Add a `thunderstore/CHANGELOG.md` entry under the new version, written
   for players: what changed and what it means for them, not which method
   was patched.
3. `bash <Mod>/package.sh` → a zip in the gitignored top-level `builds/`.
   Ross uploads it to Thunderstore manually.
4. **Check the version is not already published** before choosing it. Both
   mods release often and another session may have taken the next number.

## Git workflow

These rules exist because several agent sessions work in this repo at the
same time. Violating them has repeatedly come close to destroying another
session's work.

- **Work in a worktree**, never the main checkout:
  `git worktree add .claude/worktrees/<name> -b <branch> origin/main`.
  Branch from `origin/main`, not from whatever `main` happens to be.
- **Feature branches only.** Never commit directly to `main`.
- **Never merge until Ross says so.** "Merge" from him also means he has
  tested it — report what shipped rather than caveating it as untested.
- **Squash merge, always**, one commit per feature, and **push immediately**
  after merging.
- **Check `git diff --stat origin/main...HEAD` before committing or
  merging.** Twice a branch has carried thousands of lines of another
  session's work, and once a squash would have reverted a released version.
  If the diff contains files outside your mod, stop and work out why.
- **Clean up** the feature branch and worktree when done.
- **Never use bare `git stash` / `git stash pop`.** The stash stack is
  shared across worktrees and other sessions may push or pop concurrently.
  Prefer a temporary WIP commit; if you must stash, use
  `git stash push -u -m "<unique-tag>"` and restore with
  `git stash apply <sha>`.
- **Leave other sessions' worktrees and branches alone**, including any
  marked `locked`.

Commit messages explain the reasoning, not just the change — what was
wrong, why it hid, and what the fix turns on. `git log` is the main record
of why the code looks the way it does.

## Code conventions

- **Comments explain why, not what.** The prevailing style is a block
  comment above anything non-obvious, recording the decision, the
  alternative rejected, and the bug that motivated it — often quoting the
  decompiled vanilla code that proves the point. Match that density; it is
  the repo's main defence against re-introducing a fixed bug.
- **Refuse rather than guess** when persisted identity cannot be
  determined. Drawers store prefab names, never `m_shared.m_name`, which is
  a localization token — mixing them up silently destroys data.
- **No config entry for a value that has an on-screen control.** BepInEx
  keeps stale file values forever, and the two sources then disagree.
- **Keep features independently switchable** in RossQoL, and register them
  through `FeatureRegistry` — see "Adding a feature" in its design spec.
- **Do not name or detect a mod whose feature RossQoL reimplements.**
  Mentioning compatibility with a mod is fine.

## Working with Ross

- **GitHub issues: never reply.** Fix the problem and report in chat; Ross
  handles all communication on issues and PRs.
- **Risky changes get a written test plan** — local dedicated server, two
  clients, the extra compatibility mods, and backups — before he runs it.
- **Report outcomes faithfully.** If a build ships with a known defect, say
  so before deploying rather than after.

## Not in the repo

Deliberately gitignored, so do not expect to find them and do not commit
them: `builds/` (packaged zips), `**/docs/superpowers/` (design specs,
plans and test plans, kept locally), `Directory.Build.local.props`,
`.claude/worktrees/`, and all build output.
