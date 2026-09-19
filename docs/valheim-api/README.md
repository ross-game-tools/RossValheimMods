# Valheim game API notes

Verified notes on vanilla Valheim internals, for writing patches against.
One file per subsystem:

- `containers.md` (read at 1.0.14) — how `Container` persists inventory
  to its ZDO, whether a non-owner's write sticks, and what `ZNetView
  .ClaimOwnership()` / `ZDO.SetOwner()` actually guarantee.
- `summons.md` (read at 1.0.14; its final section, "Why a
  correctly-selected summon never arrives at a portal", read at 1.0.15
  — quoted bodies are unchanged but line numbers may have drifted) —
  what the Dead Raiser's summoned skeletons actually are (`Tameable` +
  `MonsterAI`, no separate summon component), how ownership/follow/
  lifetime work, how much of `Portals/TamesFollow` generalises to
  them, and (read at 1.0.15) how the summon cap actually counts — same
  `Character.m_name` plus same `ZDOVars.s_follow`, only at the instant one
  starts following — plus where `m_commandable` does and does not gate,
  and (re-verified at 1.0.15) the cap's selection step down to its IL: it
  sorts a `List<BaseAI>` oldest-first, `UnsummonMaxInstances` is 365 IL
  bytes and so safely patchable, and every health/age helper it calls is
  an inlining candidate that must not be.
- `death-and-respawn.md` (read at 1.0.14) — death/respawn flow, status
  effects, food, skill loss, HUD projection, per-character/per-world
  persistence.
- `eitr-refinery.md` (read at 1.0.14) — what damages the player near a
  Mistlands Eitr Refinery (`EffectArea`/`Aoe`), and whether a fix is
  client- or owner-side.
- `wear-and-tear.md` (read at 1.0.14) — `WearNTear` weathering vs
  structural decay: what causes it, ownership/ZDO writes, and how to
  suppress only the weather component.
- `dungeons.md` (read at 1.0.15) — where dungeon interiors live in world
  space, why `Heightmap.FindBiome` is a poor way to ask which biome a
  dungeon belongs to, what identity a dungeon carries instead
  (`Location.m_biome`, `ZoneLocation.m_biome`, `DungeonGenerator
  .m_themes` / `m_algorithm`, `Room.Theme`), and what a dungeon door is
  (`Teleport`, and why `Player.TeleportTo` is the one place every
  teleport in the game passes through), and why an instanced interior is
  per-location asset data rather than a fact about dungeons — so
  `Character.InInterior` is the wrong way to ask whether a terrain sample
  means anything where you are standing (`Character.UnderWorldCheck` vs
  `ZoneSystem.GetGroundHeight`/`GetSolidHeight`/`IsBlocked`).
- `dropped-items.md` (read at 1.0.15) — `ItemDrop`'s auto-destroy clock
  (`TimedDestruction`, owner-only, every 10s via `SlowUpdate`), the
  permanent `IsInsideBase()` exemption that leaves base litter
  undecaying, and the age/player-range/tar/piece conditions any fix
  must reproduce exactly.
- `teleport-unlocks.md` (read at 1.0.14) — which prefabs vanilla sets
  `m_teleportable = false` on, verified from a live game: the full
  blocked count, the three prefab names `TeleportUnlocks` needed, and
  what was checked and confirmed *not* blocked.
- `notifications.md` (read at 1.0.15) — how `MessageHud` shows top-left
  messages (one `TMP_Text`/`Image` pair drained from a `Queue<MsgData>`,
  not a pooled list), the fade timings (code constants), who raises an
  item-pickup message (`Character.ShowPickupMessage`, called from
  `Humanoid.Pickup`) and a skill-up message (`Skills.RaiseSkill`), what
  the game knows at the moment skill XP is gained
  (`Skills.Skill.Raise`/`Game.m_skillGainRate`), and why a stacking,
  update-in-place notification list has to suppress vanilla's path
  rather than take over its single display slot.

## Convention

- Everything here is copied out of decompiled game code, never written
  from memory. If a signature isn't in a file here, decompile and check
  it; don't guess it.
- Each file records the game version it was read from, in a header block
  at the top. Notes read from an older version are still useful, but
  member names and especially line numbers drift — re-verify anything
  load-bearing against the installed version.
- **Check here before decompiling again.** Decompiling the whole assembly
  takes a couple of minutes and burns a lot of context; reading the file
  takes seconds.
- When something new is verified, **append to the existing file for that
  subsystem** rather than starting a new dump. Two half-overlapping
  documents on the same subsystem is worse than one long one.
- Keep the "Things I could not verify" section at the end of each file
  current. It is the most valuable part: it stops the next session
  re-deriving the same dead ends, and it names exactly which values must
  be read at runtime instead of trusted from source.

## What is not in the DLLs

Serialized Unity asset data is not in the assemblies and cannot be
decompiled out of them. That includes status effect `m_ttl`, `m_icon` and
stat fields; prefab references like `Player.m_tombstone`; and the
inspector values that override C# field initialisers on any
`MonoBehaviour` or `ScriptableObject`. A field initialiser in the
decompiled source (`public float m_hardDeathCooldown = 10f;`) is only the
compile-time default — the shipped prefab may carry a different value.
Read those from the live instance at runtime and record what you find.

## How to regenerate

Install the decompiler once:

```
dotnet tool install -g ilspycmd
```

Dump every type into one file per type (what these notes were built
from — about 690 files for `assembly_valheim.dll`, roughly two minutes):

```
ilspycmd -p -o <outdir> -r "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed" "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll"
```

`-r` points at the Managed folder so references resolve; without it the
output is littered with unresolved types. Do the same for
`assembly_utils.dll` — `Utils`, `ZInput`, `ZLog` and the string
extensions (`GetStableHashCode`) live there, not in `assembly_valheim`.

Drop `-p` to get a single large file instead, which is occasionally handy
for grepping across type boundaries (extension methods, for instance).

For a single type, no project dump needed:

```
ilspycmd -t Player "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll"
```

Write the output to the scratchpad directory, not into the repo — only
the distilled notes belong here.

The game version comes from the assembly, not from the install
directory: `Version.CurrentVersion` in `Version.cs`. The `changelog.txt`
at the Valheim install root is BepInEx's changelog and says nothing about
the game version.
