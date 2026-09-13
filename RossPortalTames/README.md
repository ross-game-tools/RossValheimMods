# RossPortalTames

Tames that are following you come through the portal with you.

Valheim's portals don't bring your tames along: walk your wolves up to a
portal, step through, and they're left standing on the far side of the
map. This mod fixes that — tames that are actually following you and
within range step through with you and arrive near your destination.

**Design:** [docs/superpowers/specs/2026-09-13-portal-tames-design.md](docs/superpowers/specs/2026-09-13-portal-tames-design.md)

## Behaviour

- Tames following you within `FollowRadius` at the moment you use a
  portal come through with you.
- Each arriving tame is placed at a clear spot near your destination,
  searching outward up to `SearchDistance`; if nothing clear is found it
  falls back to your own arrival position, where creatures separate
  themselves naturally.
- A ridden creature (a saddled lox, for example) is excluded — it isn't
  left behind in the first place.
- If a followed tame dies mid-transit, or the portal has no target set,
  nothing errors and the remaining tames still arrive normally.

## Client-side only

This mod is entirely client-side. There is nothing to install on a
dedicated server — install it only for the players who want it, and it
works fine on servers that don't have it at all.

## Config

Three settings, all under `[General]`, all local to your own client (see
`PortalTamesConfig` for why these are not server-synced):

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Whether tames following you come through portals at all. |
| `FollowRadius` | `20` (metres) | How close a following tame must be, in three dimensions, to come along. Set to `0` to bring nothing. |
| `SearchDistance` | `6` (metres) | How far from your arrival point to look for a clear spot to place each tame before falling back to your own position. Lower it for tight portal huts. |

## Building

Requires a .NET SDK (targets `netstandard2.1`) and a Valheim install.
Valheim assemblies are referenced through
`BepInEx.AssemblyPublicizer.MSBuild` and are never committed. No Jotunn
dependency.

```bash
cd RossPortalTames
dotnet build -c Release
```

## Deploying to r2modman

`./deploy.sh [profile]` builds Release and copies every produced
assembly (`RossPortalTames.dll` and `RossPortalTames.Core.dll` — the
plugin depends on the Core DLL, so both must ship together) into a
r2modman profile's `BepInEx/plugins/RossPortalTames` folder.

Defaults to the `dev` profile. **Never point it at `Default`** —
r2modman owns that profile, and hand-copying into it desynchronises what
is installed from what r2modman believes is installed.

## Packaging

`./package.sh` builds Release and assembles the Thunderstore zip
`RossPortalTames-<version>.zip`. It refuses to build if
`thunderstore/manifest.json`'s `version_number` disagrees with
`PortalTamesPlugin.PluginVersion`, and refuses to package if either
assembly is missing from the build output.

## Status

Implemented and buildable, with Thunderstore packaging, icon, and
documentation in place. Outstanding before release: a human in-game
verification pass (see the task brief's manual verification checklist)
and a dedicated-server check, since the design assumes moved tames
persist across zone unload/reload.
