# RossQoL

Quality-of-life tweaks for Valheim 1.0 in one BepInEx mod. Design:
[docs/superpowers/specs/2026-09-13-rossqol-design.md](docs/superpowers/specs/2026-09-13-rossqol-design.md).

## Layout

```
src/RossQoL.Core/<Category>/   engine-free rules, unit-tested
src/RossQoL.Game/Framework/    Feature, Category, activation, compat checks
src/RossQoL.Game/<Category>/   features, Harmony patches, Unity adapters
tests/RossQoL.Core.Tests/      xunit
thunderstore/                  manifest, README, CHANGELOG, icon
```

## Build, test, deploy

```bash
dotnet test RossQoL/RossQoL.sln
bash RossQoL/deploy.sh          # r2modman dev profile only
bash RossQoL/package.sh         # Thunderstore zip
```

Needs `VALHEIM_INSTALL` and `JOTUNN_INSTALL` (see `Directory.Build.props`).

## Adding a feature

See "Adding a feature" at the end of the design spec. In short: pick a
category by what the feature modifies, choose `Client` or `Synced`, put
testable rules in Core, subclass `Feature` in Game, list every patch class
and every Valheim member it reaches by name, and register it in
`FeatureRegistry`.
