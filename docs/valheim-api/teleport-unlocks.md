# Teleport-blocked items (`ItemDrop.ItemData.SharedData.m_teleportable`)

What this covers: which prefabs vanilla actually sets
`m_teleportable = false` on. This is Unity-serialized asset data, not
something a decompile of the assemblies can show -- it has to be read
from a live game.

**Read 2026-09-19, game version 1.0.14**, with a temporary diagnostic
(`RossQoL.Game.Progression.TeleportDiagnostics`, since deleted; added
in `de08223`, removed once these rules were written) that walked
`ObjectDB.m_items` from a `ObjectDB.CopyOtherDB` postfix and logged
every item's prefab name, name token, and `m_teleportable` value.

## Full blocked list

28 items had `m_teleportable == false`. The three this was run to
confirm, with their localized display names:

| Prefab | Display name |
|---|---|
| `DragonEgg` | Dragon Egg |
| `MechanicalSpring` | Mechanical Spring |
| `DvergrNeedle` | Dvergr Extractor |

`DvergrNeedle` is worth flagging explicitly: it's the prefab behind
the carried item that places a sap extractor, but its prefab name
never got updated to match the display name -- a name search for
"extractor" or "sap" in the prefab list will not find it.

These three are the basis for the `TeleportUnlocks` rules added in
this same change: `DragonEgg` waits for Moder (it's a Mountain item,
same as silver), `MechanicalSpring` and `DvergrNeedle` wait for the
Queen (both are Mistlands items).

## Checked and confirmed *not* blocked

So nobody re-derives this and adds a pointless rule: these are
`m_teleportable == true` and need no entry in `TeleportUnlocks`.

- `Sap` -- despite `DvergrNeedle` (the thing that places a sap
  extractor) being blocked, the sap it produces carries fine.
- `PowderedDragonEgg` -- the processed/cooked form of the dragon egg,
  unlike the raw egg itself.
- `DvergrKey`, `DvergrKeyFragment` -- teleportable despite the "Dvergr"
  name association with the blocked extractor.

The remaining ~25 blocked items not listed above are outside what this
change needed and were not individually matched to a display name or
biome; re-run the same diagnostic pattern (or check `m_teleportable`
directly) before adding further `TeleportUnlocks` entries rather than
guessing from this list.
