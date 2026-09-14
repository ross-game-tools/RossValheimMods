# RossPortalTames — Design

**Date:** 2026-09-13
**Status:** Approved, not yet implemented

## Goal

Tames that are following you come through the portal with you.

Vanilla leaves them behind, which makes portals unusable for anyone who
travels with a wolf pack: you either abandon them or walk. This mod
closes that gap and does nothing else.

## Scope

**In:** tamed creatures whose follow target is the local player, within a
configurable radius, moved to the destination when the player uses a
portal.

**Out:** every other kind of teleport. The hook is on portals
specifically, not `Player.TeleportTo`, so dying does not drag your wolves
to your spawn bed and `goto` does not either. Also out: taming,
breeding, follow behaviour, pathfinding, and anything the base game does
with tames once they arrive.

## Decisions

Recorded with their reasons, because each was a live choice:

| Decision | Reason |
|---|---|
| Eligibility is "following me AND within a radius" | A tame stuck on the far side of a wall should not be yanked across the world. |
| No cap on how many come through | The radius already bounds it in practice, and "any tame that is following you" is the plain reading of the feature. |
| Move the creature, never destroy and recreate it | A respawn has to copy level, name, tameness, health and pregnancy by hand, and anything missed is silently lost. Moving touches none of it. |
| Portals only, not all teleports | Matches the request literally and avoids surprising interactions with death and console commands. |
| No Jotunn dependency | No prefabs, no assets, no recipes — BepInEx alone suffices. One less dependency and no version coupling. |
| Config is local, not server-synced | Consequence of no Jotunn. Acceptable because every setting only affects which of your own tames follow you. |

## Architecture

Two layers, mirroring ItemDrawers, and for the same reason: the rules
worth testing should not require a running game.

```
RossPortalTames.Core     pure C#, no engine references, unit-tested
RossPortalTames.Game     Harmony patches and Unity adapter
```

### Core

**`TameEligibility`** — given plain descriptions of candidate creatures
(position, is-tamed, follow-target id, is-ridden, is-attached) and the
player's position and radius, returns which qualify. No Unity types
cross this boundary; the Game layer converts.

**`ArrivalPlacement`** — computes where each tame lands. Takes the
player's arrival position and facing, the number of tames, and a
predicate `bool IsFree(Vector3)`; returns one position per tame. The
predicate is supplied by the Game layer (backed by
`ZoneSystem.IsBlocked` / `GetSolidHeight`) and by tests (backed by a
fake world), which is what makes the wall case directly assertable.

### Game

**`PortalPatch`** — Harmony patch on `TeleportWorld.Teleport`, guarded by
a `Prepare` that logs when the target method cannot be found rather than
skipping silently.

**`PortalTamesManager`** — the only ticking object. Holds the pending
list and performs arrival placement. One `Update` for the whole mod.

**`TameMover`** — re-asserts ownership and writes the ZDO position.

**`PortalTamesConfig`** — three entries, below.

## Flow

1. **Departure.** The patch fires on `TeleportWorld.Teleport` for the
   local player. It scans `Character.s_characters` for creatures that
   are tamed, whose `MonsterAI.GetFollowTarget()` is the local player,
   within `FollowRadius`, and not ridden or attached. It records their
   `ZDOID`s and claims ownership of each while they are still loaded.

2. **Transit.** The player teleports. The creatures' zone unloads and
   their GameObjects are destroyed on this client. Their ZDOs persist.

3. **Arrival.** Once `Player.IsTeleporting()` is false, the manager
   computes placements and, for each recorded ZDOID, looks the ZDO up
   through `ZDOMan.GetZDO`, **re-asserts ownership**, and writes the new
   position — `SetOwner` then `SetPosition`, back to back.

4. **Instantiation.** The destination zone loads and each creature
   instantiates at its new position, still tamed, still following.

### Why ownership is re-asserted at step 3

This is the subtle part and the easiest thing to get wrong.

`ZDOMan.ReleaseNearbyZDOS` reassigns ZDO ownership by proximity on a
roughly two-second cadence. Between departure and arrival the player is
far from the creatures, so the departure-time claim **can be taken away
mid-flight**. A design that claimed only at departure would write into a
ZDO it no longer owned, and the write would be lost or overwritten by
whoever picked it up — intermittently, and more often on a busy server.

Both `SetOwner` and `SetPosition` operate on a bare ZDO with no live
GameObject, so the absence of the creature object at arrival is not an
obstacle. The departure-time claim is kept as a cheap first move but is
not load-bearing.

## Safety property

**The mod never destroys anything.** The only write in the whole flow is
a new position on an existing ZDO. There is no despawn, no respawn, no
inventory copy, no state reconstruction.

This is deliberate, and it is what makes every failure mode degrade the
same harmless way — the tame stays where it was, alive and yours:

| Failure | Result |
|---|---|
| Teleport aborts, or the player logs out in transit | Pending list dies with it. Nothing moved. |
| Tame is killed while in transit | `GetZDO` returns null; skipped. |
| Ownership cannot be re-acquired at arrival | Write refused; tame stays put. |
| Placement finds nowhere safe | Tame is left behind rather than buried in terrain. |
| Patch fails to apply after a game update | No tames follow. Annoying, not destructive. |

The pending list expires after a few seconds, so an arrival that never
registers drops the entry rather than moving a creature to a stale
destination later.

## Placement

A ring around the player was considered and rejected: portals are
commonly built against a wall, inside a small hut, and a ring puts half
the pack inside the geometry.

Instead, a validated candidate search. The player's arrival position is
by definition a legal standing spot, so it anchors the search. Candidates
are generated outward from it, starting in the direction the player faces
— away from the destination portal, since Valheim places you in front of
it — and fanning sideways in increasing steps. Each candidate is tested
with `ZoneSystem.IsBlocked` and its Y snapped with `GetSolidHeight`.

If no free candidate is found within `SearchDistance`, the tame is placed
**at the player's own arrival position**. Overlapping creatures resolve
themselves through normal physics within a second; a creature embedded in
a wall does not. Cosmetic problems that self-correct are preferable to
positional ones that do not.

## Multiplayer

**Client-side only. No server component is required.**

Valheim's server is a relay and a persister, not an authority: the owning
client is authoritative for a ZDO's contents, and nothing server-side
validates that a position change is plausible. The only validation RPC in
`ZNet` is `RPC_ValidatedSimulationDistance`, which concerns simulation
range, not ZDO writes. This mod therefore uses the ordinary replication
mechanism rather than working around it.

Everything keys off `Player.m_localPlayer`, and eligibility requires the
follow target to be that player. A creature has exactly one follow
target, so two clients can never both move the same tame. Other players
do not need the mod installed; they see the tame appear via normal
replication.

**Caveat to state plainly:** a server running anti-cheat mods that watch
for implausible ZDO movement could flag this. None of the mods currently
installed do.

**Must be verified before release:** that a moved tame persists at its
new location after the destination zone unloads and reloads on a real
dedicated server. This is the single assumption most worth disproving
early.

## Compatibility

Every mod in the dev profile was checked by reading its assembly's
member references and type definitions for portal and teleport surface.

| Mod | Portal/teleport surface | Conflict |
|---|---|---|
| XPortal | Patches `Game.ConnectPortals`, `ZDOMan.ConnectPortals`, `TeleportWorld.GetHoverText`; references `TeleportWorld.m_nview` | None. It changes portal *pairing* and hover UI, never `TeleportWorld.Teleport`. |
| NoVikingLeftBehind | Has a `PortalTrailModule`; only teleport-related reference is `SharedData.m_teleportable` | None. That is the item-carry flag, not the teleport act. |
| Others (PlantEasily, PlantEverything, Heightmap Unlimited, OneMapToRuleThemAll, OdinShip, ValheimArmory, InstantMonsterLootDrop, OttoFuel, Dive In, ItemDrawers) | No portal or teleport references | None. |

XPortal is not merely non-conflicting, it is **upstream by
construction**: it decides where a portal points, then vanilla's
`Teleport` runs with the destination already resolved. Reading the
destination from the player's actual arrival — rather than from the
portal's configured target — means any rerouting by XPortal or a future
mod is respected automatically, and a portal with no target simply never
teleports the player, so the patch sees nothing to follow.

Re-checking after a mod update is mechanical: dump each assembly's
member references and look for `TeleportWorld.Teleport` or
`Player.TeleportTo`.

## Configuration

Three entries, local to each client.

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | true | Master switch. |
| `FollowRadius` | 20 m | How close a following tame must be to come along. |
| `SearchDistance` | 6 m | How far to look for a clear arrival spot before falling back to the player's own position. Lower it for tight portal huts. |

## Testing

**Core unit tests** cover the two pure pieces:

- `TameEligibility`: tamed vs. wild, following me vs. following another
  player vs. not following, inside vs. outside the radius, ridden and
  attached exclusions.
- `ArrivalPlacement`: the wall case asserted directly — a blocking
  predicate on one side must push placements to the other; exhausted
  search must fall back to the player's position; placements must not
  duplicate a position.

**Manual verification** in the dev profile, which has XPortal installed:

1. Wolves following, portal in the open — all arrive.
2. Portal with a wall directly behind it — none placed inside geometry.
3. A following tame outside `FollowRadius` — stays put.
4. A tame killed mid-transit — no error, others arrive.
5. An XPortal portal with no target set — no teleport, no tames moved.
6. Ridden lox — excluded.

**Dedicated server verification**, outstanding until testable: the
persistence check under Multiplayer above, then two-client behaviour.

## Repository layout

Follows the existing per-mod convention:

```
RossPortalTames/
  docs/            design notes and specs
  src/
    RossPortalTames.Core/
    RossPortalTames.Game/
  tests/
    RossPortalTames.Core.Tests/
  thunderstore/    manifest, icon, README
```
