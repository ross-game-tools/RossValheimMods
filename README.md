# RossValheimMods

BepInEx mods for Valheim 1.0. One directory per mod, each independently
buildable and independently packaged for Thunderstore.

| Mod | What it does | State |
|---|---|---|
| [ItemDrawers](ItemDrawers/) | Wall-mountable drawers that each hold a large quantity of one item and show it on the front. Readable and withdrawable by container-aware mods such as OttoFuel and NoVikingLeftBehind. | Design approved, not yet implemented |

## Layout

```
<ModName>/
  docs/                 design notes and specs
  src/                  source
  tests/                unit tests for the pure-logic core
  thunderstore/         manifest, icon, README for packaging
```

Shared conventions live at the root; anything mod-specific lives under
that mod's directory, so a mod can be extracted later without untangling
it from the others.

## Building

Requires a .NET SDK (targets `netstandard2.1`) and a Valheim install.
Valheim assemblies are referenced through
`BepInEx.AssemblyPublicizer.MSBuild` and are never committed.
