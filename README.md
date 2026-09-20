# RossValheimMods

BepInEx mods for Valheim 1.0. One directory per mod, each independently
buildable and independently packaged for Thunderstore.

| Mod | What it does | State |
|---|---|---|
| [ItemDrawers](ItemDrawers/) | Wood, stone and black marble drawers that each hold a large quantity of one item and show it on the front. Clean-room rewrite; ships no art. | Released on Thunderstore |
| [RossQoL](RossQoL/) | Around fifty quality-of-life tweaks in fourteen categories, each independently switchable — crafting, death and recovery, tames, portals, production, interface and more. | Released on Thunderstore |

Both require [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)
and must be installed on the server and on every client.

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

```bash
dotnet test ItemDrawers/ItemDrawers.sln -c Release
bash ItemDrawers/deploy.sh      # to the r2modman 'dev' profile
bash ItemDrawers/package.sh     # Thunderstore zip into builds/
```

Working on this repo as an agent? Start with [AGENTS.md](AGENTS.md).

## License

[MIT](LICENSE), copyright Ross West. This repository exists in part
because three earlier Valheim drawer mods were abandoned and two of them
shipped with no license at all, so nobody could legally pick them up and
continue them. Every mod here ships under MIT precisely so that fate does
not repeat: if a mod here is ever abandoned, anyone can fork and continue
it without asking permission that may no longer be reachable.
