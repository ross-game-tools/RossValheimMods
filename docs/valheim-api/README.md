# Valheim game API notes

Verified notes on vanilla Valheim internals, for writing patches against.
One file per subsystem:

- `containers.md` — how `Container` persists inventory to its ZDO,
  whether a non-owner's write sticks, and what `ZNetView
  .ClaimOwnership()` / `ZDO.SetOwner()` actually guarantee.
- `death-and-respawn.md` — death/respawn flow, status effects, food,
  skill loss, HUD projection, per-character/per-world persistence.
- `eitr-refinery.md` — what damages the player near a Mistlands Eitr
  Refinery (`EffectArea`/`Aoe`), and whether a fix is client- or
  owner-side.
- `wear-and-tear.md` — `WearNTear` weathering vs structural decay:
  what causes it, ownership/ZDO writes, and how to suppress only the
  weather component.

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
