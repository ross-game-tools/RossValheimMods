# Producers: smelters, kilns, ovens (and the Deep North frost stations)

What this covers: how `Smelter` and `CookingStation` take fuel and turn
inputs into outputs, and the two Deep North (game 1.0) stations that bite a
"feed producers from nearby containers" feature (RossQoL's
`Production/AutoFeed`). Read at runtime from a 1.0.15 client and decompiled
from the installed `assembly_valheim.dll`; the prefab names and fuel/
conversion assignments are Unity asset data, **not in the DLL**, so they were
read at runtime with a throwaway diagnostic.

## The two frost stations, from a runtime scan

- **Frigid Kiln** — prefab `piece_FrostKiln`, a **`Smelter`**:
  `m_fuelItem = Ice`, `m_maxFuel = 25`, **`m_maxOre = 0`**, and one conversion
  `m_conversion = [ null -> FrozenFuel ]` (the `m_from` is null). So it burns
  Ice as fuel and produces `FrozenFuel` with **no ore input at all** — a
  fuel-driven producer, unlike the charcoal kiln (which takes Wood as an ore
  conversion input and has `m_maxOre > 0`).
- **Frost Foundry** — prefab `piece_FrostFoundry`, a **`CookingStation`**:
  `m_useFuel = true`, `m_fuelItem = FrozenFuel`, `m_maxFuel = 20`. It is an
  "oven" in AutoFeed's terms, fed via the `FeedOvens` path.

## `CookingStation` only calls `UpdateFuel` when it already has fuel

`CookingStation` runs `UpdateCooking` every second, unconditionally, via
`InvokeRepeating("UpdateCooking", 0f, 1f)` (set up in `Awake`). `UpdateFuel`
is called **from inside** `UpdateCooking`, but only when a gate passes:

```csharp
bool flag = (m_requireFire && IsFireLit())
    || (m_useFuel && GetFuel() > 0f && (m_useFueldWhileEmpty || HaveUncookedItem()));
if (m_nview.IsOwner() && flag)
{
    UpdateFuel(deltaTime);
    ...
}
```

For a fuel-burning station (`m_useFuel`, no `m_requireFire`) the gate needs
**`GetFuel() > 0f`**. So `UpdateFuel` never runs while the station is empty —
a Harmony patch on `UpdateFuel` therefore can **never fill an empty station**;
it only tops up one a player has already primed by hand. Patch
`UpdateCooking` instead (it always ticks) and do the owner/interval checks
yourself. This is why the Frost Foundry "would not auto-fill": it starts
empty and stays empty.

`RPC_AddFuel` adds one fuel and refuses past `m_maxFuel - 1`
(`GetFuel() > m_maxFuel - 1`), exactly as a player at the switch does. Fields:
`m_useFuel`, `m_fuelItem` (ItemDrop), `m_maxFuel`, `m_secPerFuel`,
`m_useFueldWhileEmpty` (default true).

## `Smelter` fuel/output, and capping a fuel-driven producer

`Smelter` is unchanged in shape: `m_fuelItem` (ItemDrop) + `m_maxFuel`,
`m_conversion` (`ItemConversion { m_from, m_to }`) + `m_maxOre`, `GetFuel()`,
`GetQueueSize()`, `RPC_AddFuel`/`RPC_AddOre`. New-ish fields seen in 1.0:
`m_requiresRoof`, `m_haveFuelObject`, `m_fuelPerProduct`, `m_noSourceConversion`.

Consequence for an output cap ("stop feeding once N of the output are nearby"):
a normal smelter is driven by **ore**, so refusing ore idles it. A fuel-driven
producer like the Frigid Kiln has `m_maxOre == 0` and makes its output from
**fuel** — so it must be capped by refusing its **fuel**, not its ore. RossQoL
gates both: ore per conversion, and fuel once *every* conversion output has hit
its cap (`SmelterFeeder.AllOutputsAtCap`).
