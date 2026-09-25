# Summons (Dead Raiser & Spirit Caller summon staffs)

What this covers: what a "summon" (e.g. the skeletons raised by a
staff) actually is in vanilla, whether it already follows its
summoner, who owns its ZDO, and whether RossQoL's existing
`Portals/TamesFollow` machinery could carry summons through a portal
or drive a "recall my summons" feature.

**Read 2026-09-19, game version 1.0.14** (`Version.CurrentVersion` –
not independently re-checked this session but matches the version
already recorded in `eitr-refinery.md`/`wear-and-tear.md`), from a
full `ilspycmd -p` decompile of `assembly_valheim.dll` and a
single-type decompile of `ZInput` out of `assembly_utils.dll`
(ilspycmd 8.2.0.7535-95108c96, per this directory's README invocation).

## Headline answer

**A summon is a `Tameable` that was auto-commanded to follow its
caster at spawn time.** There is no separate summon component, no
`SE_Summoned` status effect, and no "summoner" field distinct from the
ordinary tame-follow machinery. Concretely:

- `SpawnAbility` (the weapon-side spawner attached to the staff's
  attack projectile) instantiates the creature prefab, and if
  `m_commandOnSpawn` is true, immediately calls
  `Tameable.Command(humanoid, message: false)` where `humanoid` is the
  attack's `m_owner` (the caster). `SpawnAbility.cs:255-266`.
- `Tameable.Command` → RPC `"Command"` → `RPC_Command` finds the
  creature has no follow target yet, so it takes the "start following"
  branch: `m_monsterAI.SetFollowTarget(player.gameObject)` and, if the
  **owner** of the ZDO, writes `ZDOVars.s_follow = player.GetPlayerName()`
  into the creature's own ZDO. `Tameable.cs:480-497`.
- So a summoned skeleton is identified as "belonging to" a player
  purely by name string in its ZDO (`ZDOVars.s_follow`, hash of
  `"follow"`), exactly the same field vanilla uses for a bred wolf or a
  tamed boar that's been told to follow. There is **no player ID, no
  ZDOID-of-owner, nothing keyed to a specific character** — just the
  player's display name.

This is important for the two features: **it means a summon and a
regular tame are, at the engine level, the same kind of object once
spawned.** RossQoL's `TameEligibility`/`TameMover`/`ArrivalPlacement`
machinery already operates on exactly this surface (`MonsterAI`
follow target + `ZDOVars.s_follow` + `ZNetView` ownership) and should
generalise with very little change — see Question 2 below for what,
specifically, would need to change.

## Question 1 — what a summon actually is, in detail

### The item / trigger

No `StaffSkeleton` C# type exists in the decompiled assembly (grepped
the full 631-file project dump for `staff|summon|skeleton`, case
insensitive — nothing). The Dead Raiser is therefore an ordinary
`ItemDrop` whose `SharedData.m_attack` (an `Attack`, `Attack.cs`) has
`m_attackProjectile` set to a projectile prefab that itself carries
`SpawnAbility` (confirmed pattern: `Attack.cs:887-900` picks
`m_attackProjectile`, or the ammo's own attack's projectile if set,
and instantiates/launches it; `SpawnAbility.Setup(Character owner, ...)`
is the entry point a projectile-side component calls once spawned).
Which specific prefab name and localization key identify "Dead
Raiser" specifically is **asset data, not in the DLL** — see
"Could not verify" below.

`SpawnAbility` (`SpawnAbility.cs`, full source read) is a
`MonoBehaviour : IProjectile` with a large inspector-driven spawn
recipe: `m_spawnPrefab[]`, `m_minToSpawn`/`m_maxToSpawn`,
`m_maxSpawned` (population cap via `SpawnSystem.GetNrOfInstances`),
`m_targetType` (Caster / ClosestEnemy / RandomEnemy / Position /
RandomPathfindablePosition), and — the field that matters here —
`m_commandOnSpawn`. `Setup(Character owner, ...)` stores `m_owner` and
starts the `Spawn()` coroutine, which instantiates the prefab and,
when `m_commandOnSpawn` is set, does:

```csharp
if (m_commandOnSpawn)
{
    Tameable component4 = gameObject.GetComponent<Tameable>();
    if ((object)component4 != null && m_owner is Humanoid humanoid)
    {
        component4.Command(humanoid, message: false);
        if (humanoid == Player.m_localPlayer)
            Game.instance.IncrementPlayerStat(PlayerStatType.SkeletonSummons);
    }
}
```
(`SpawnAbility.cs:255-266` — note `PlayerStatType.SkeletonSummons`
exists as a named stat, strong corroborating evidence this is exactly
the raised-skeleton path even though the prefab name itself wasn't
found in source.)

### The summoned creature's component set

Standard `Character`-family creature: `Character`, `MonsterAI`
(`: BaseAI`), `ZNetView`, `Tameable`. Nothing summon-specific. No
`SE_Summoned` or any status-effect-based "this is a summon" marker
exists anywhere in the project (grep for "Summon" across all 631 files
turned up only `PlayerStatType.cs`, `ProjectileType.cs`,
`SpawnAbility.cs`, `Tameable.cs`).

### Ownership / "whose summon is this"

Established in the headline: `ZDOVars.s_follow` (string, the player's
display name) is the only marker. `Tameable.UpdateSavedFollowTarget`
(`Tameable.cs:501-528`) re-derives the live `MonsterAI` follow target
from that string every frame the owning client has no live follow
target cached (e.g. after a reload), by scanning `Player.GetAllPlayers()`
for a name match and re-issuing `Command`. There is no per-player ID;
two players with the same display name would collide, which is
vanilla's own limitation, not something to route around.

Separately, `Tameable` also writes `ZDOVars.s_maxInstances` onto the
*prefab*'s spawn-time ZDO when `SpawnAbility.m_levelUpSettings`
applies a level (`SpawnAbility.cs:238-249`), and
`Tameable.UnsummonMaxInstances` (`Tameable.cs:641-707`) uses that cap
to despawn the oldest-spawned excess creature **sharing the same
`m_character.m_name` and the same `ZDOVars.s_follow` value** whenever
a new one starts following — i.e. vanilla's own per-player summon cap
is enforced by scanning `Character.GetAllCharacters()` for matching
name + follow string, exactly the shape a "list my summons" feature
would use.

### Lifetime: duration, despawn, death/logout unsummon

Three independent mechanisms, all on `Tameable`, all read from the
component's own inspector-set fields (asset data — defaults not
necessarily what the Dead Raiser prefab ships with):

1. **Distance unsummon** — `m_unsummonDistance` (float, 0 = off).
   `Tameable.UpdateSummon()` (`Tameable.cs:629-639`), called every
   `Update()`, only on the **owner** (`m_nview.IsOwner()`): if the
   creature is farther than `m_unsummonDistance` from its current
   follow target, `UnSummon()`.
2. **Owner-logout unsummon** — `m_unsummonOnOwnerLogoutSeconds` (float,
   0 = off). Inside `UpdateSavedFollowTarget`: if the followed
   player's name is no longer found among `Player.GetAllPlayers()`
   (i.e. they logged out or are out of range/zone), a per-`FixedUpdate`
   timer (`m_unsummonTime += Time.fixedDeltaTime`) accumulates and
   past the threshold calls `UnSummon()`. `Tameable.cs:520-527`.
3. **Population cap** — `UnsummonMaxInstances`, above, fires the
   moment a *new* same-named, same-follow-string creature starts
   following, killing the oldest excess by `GetTimeSinceSpawned()`.

`UnSummon()` → RPC `"RPC_UnSummon"` to everybody → plays
`m_unSummonEffect`, and the **owner only** does
`ZNetScene.instance.Destroy(base.gameObject)` (`Tameable.cs:709-724`).
No death is involved — this is a plain despawn, not
`Character.OnDeath`. Nothing calls `UnSummon` on player death; a
summon that outlives its caster (caster dies but doesn't log out) is
only caught by the distance/logout timers above, if those fields are
even set non-zero on this prefab.

### Does it already follow the summoner? Through what?

Yes — `MonsterAI.SetFollowTarget(GameObject)` /
`MonsterAI.GetFollowTarget()` (`MonsterAI.cs:945-950`, on `MonsterAI`
itself, not the `BaseAI` base class), driven by `Tameable.Command` as
described above. This is the *exact* mechanism RossQoL's
`PortalTamesManager.CaptureDeparture` already reads
(`character.GetComponent<MonsterAI>().GetFollowTarget()`).

### Who owns the ZDO / drives the AI in multiplayer

No summon-specific rule was found — it is the same ZDO ownership model
as any other `Character`/tame: whichever client's `ZNetView` holds
ownership (`ZDOMan` proximity-based reassignment, `ClaimOwnership`,
`ZDO.IsOwner()`) drives that instance's `MonsterAI`/`Tameable` logic
(every `Tameable` gate above — `UpdateSummon`, `UnsummonMaxInstances`,
`RPC_Command`'s `ZDOVars.s_follow` write, `RPC_UnSummon`'s destroy —
is wrapped in `m_nview.IsOwner()`). This matches what
`TameMover.cs`'s comments already document about `ZDOMan.ReleaseNearbyZDOS`
reassigning ownership by proximity.

## Question 2 — how much of `Portals/TamesFollow` generalises

Read in full: `PortalTamesManager.cs`, `TameMover.cs`,
`PendingArrival.cs`, `PortalPatch.cs`, `PortalTamesConfig.cs`,
`ArrivalPlacement.cs`, `TameCandidate.cs`, `TameEligibility.cs`.

**Selection** (`PortalTamesManager.CaptureDeparture`): iterates
`Character.GetAllCharacters()`, requires a valid `ZNetView`, reads
`MonsterAI.GetFollowTarget() == player.gameObject` **or**
`ZDO.GetString(ZDOVars.s_follow) == player.GetPlayerName()` as the
"following me" test, `character.IsTamed()` for tamed-ness, and
`character.IsAttached() || IsMounted(view)` (via `ZDOVars.s_user`) for
busy. Three of those four — `MonsterAI`, `ZDOVars.s_follow`,
`ZNetView`/`ZDO` — apply unchanged to a summoned skeleton.

**The fourth, `Character.IsTamed()`, does not, and an earlier revision
of this file was wrong to say it did.** See "Why summons were left
behind at portals" below: a raised skeleton's tamed flag is written by
a routed RPC that can be dropped outright, so `IsTamed()` can read
false for the whole life of the creature.

**Movement** (`TameMover.TryMove`): re-asserts `ZDO.SetOwner`, writes
`ZDO.SetPosition`, and if the `GameObject` is still locally
instantiated (`ZNetScene.FindInstance`), also writes
`transform.position` + `Rigidbody.position` + `Physics.SyncTransforms()`.
None of this touches `Tameable` or `MonsterAI` at all — it operates
purely on `ZNetView`/`ZDO`/`Character`'s `Rigidbody`. **Identical for
a summon; no changes needed.** This is also exactly the primitive a
"teleport my summons to me" feature (Question 3) would reuse — see
below.

**Placement** (`ArrivalPlacement`, `PendingArrival`): entirely
position-math, no creature-type awareness at all. **Reusable
unchanged** for either feature.

**Where a summon *would* need different handling — not in the code
above, but in what governs it:**

- `TameEligibility.Qualifies` gates on `IsTamed && IsFollowingPlayer &&
  !IsBusy && withinRadius`. A summon that's mid-way through
  `SpawnAbility`'s spawn coroutine (rare timing window) or that hasn't
  yet had `Command` called (`m_commandOnSpawn` false, or still
  travelling as a projectile) simply won't appear in
  `Character.GetAllCharacters()` yet or won't be following — it's
  excluded the same way an un-commanded tame is, which is correct
  default behavior, not a gap.
- The **portal feature's existing `IsMounted`/`IsAttached` busy check**
  and `IsTamed` check need no summon-specific branch. The one place a
  design choice exists: should `Portals/TamesFollow`'s config
  (`TameFollowRadius`, `TameSearchDistance`) apply identically to
  summons, or does a summon population want its own radius/toggle
  (e.g. because summons are disposable and numerous, unlike a named
  pet)? That's a product decision, not an engine constraint — nothing
  in the decompiled code distinguishes a summon from a tame for this
  purpose.
- `UnsummonMaxInstances`/distance/logout timers (Question 1) keep
  running independent of anything RossQoL does; moving a summon
  through a portal doesn't reset `m_unsummonTime` or the follow
  distance check, so a summon that arrives far from its cap-mates or
  whose logout timer had already been accumulating could still
  self-despawn shortly after arrival. This is vanilla behavior, not a
  bug introduced by moving it.

## Question 3 — middle click while a staff is equipped

### Vanilla's mouse bindings

From `ZInput` (`assembly_utils.dll`, single-type decompile), the
default binding table (`Reset()`/registration method around
`ZInput.cs:3010-3052`):

```csharp
AddButton("MouseMiddle", MouseButtonToPath(MouseButton.Middle));
AddButton("Attack", MouseButtonToPath(MouseButton.Left), ..., rebindable: true);
AddButton("SecondaryAttack", MouseButtonToPath(MouseButton.Middle), ..., rebindable: true);
AddButton("Block", MouseButtonToPath(MouseButton.Right), ..., rebindable: true);
...
AddButton("Remove", MouseButtonToPath(MouseButton.Middle), ..., rebindable: true);
```

**Middle click is already bound twice by default**: `SecondaryAttack`
(used while a weapon/tool is wielded) and `Remove` (used in build
mode, for removing a placed piece). `MouseMiddle` itself is also a
raw registered button name. `PlayerController.UpdateControls` reads it
every frame:

```csharp
bool flag3 = (ZInput.GetButton("SecondaryAttack") || ZInput.GetButton("JoySecondaryAttack"))
    && !flag && !Hud.InRadial();
```
(`PlayerController.cs:88`), which feeds `secondaryAttack` into
`Character.SetControls` and from there into whichever `Attack` the
equipped weapon's `SharedData` defines for its secondary slot, if any.
**So while any weapon is equipped, middle click is consumed as
`SecondaryAttack` input regardless of whether that specific weapon
defines a secondary attack** — the button read happens unconditionally
in `PlayerController`, the *consequence* (an actual secondary attack
firing) is what's weapon-dependent. A staff with no secondary attack
defined would currently just eat the middle-click input with no
visible effect, which is the exploitable gap for this feature, but any
implementation must not break `SecondaryAttack`/`Remove` for every
*other* equipped item, since both are shared, non-item-specific button
names.

### How this project reads input elsewhere

Both existing examples confirm the standing rule and the pattern to
follow: **never Harmony-patch a `ZInput` accessor directly** — its
small methods (`GetButton`, `GetKey`, etc.) are inlined by Mono, so
the patch is silently bypassed — **read `ZInput` state from inside a
`MonoBehaviour.Update`/`LateUpdate` that already runs every frame.**

- `GraveMarkerDisplay.Update` → `HandleKeys` calls `ZInput.GetKeyDown`/
  `ZInput.GetKey` directly each frame, guarded by a chat/console focus
  check (`Chat.instance.HasFocus() || Console.IsVisible()`).
- `PanCameraPatches.cs` instead Harmony-patches
  `PlayerController.LateUpdate` (a real, non-trivial method, not a
  `ZInput` accessor) and calls `ZInput.GetKey`/`ZInput.IsMouseActive`/
  `ZInput.GetMouseDelta` from inside that prefix — i.e. it's fine to
  read `ZInput` from within a patch, as long as the *patched* method
  is not itself one of `ZInput`'s own small members.

For middle click specifically, the same two options exist: a
dedicated `MonoBehaviour.Update` polling `ZInput.GetButtonDown("SecondaryAttack")`
(simplest, matches `GraveMarkerDisplay`'s pattern exactly), or a
Harmony patch on `PlayerController.UpdateControls`/`LateUpdate`
reading the same button from inside a prefix/postfix (matches
`PanCameraPatches`' pattern, useful if the feature needs to *suppress*
vanilla's own consumption of the click, which polling cannot do).

### Identifying the equipped item / the Dead Raiser specifically

`Humanoid` exposes `m_rightItem`/`m_leftItem`
(`ItemDrop.ItemData`, current equipped weapon slots — read on
`Player.m_localPlayer` as a `Humanoid`). Each carries `.m_shared`
(`ItemDrop.ItemData.SharedData`) with `.m_name` (the localization key,
e.g. presumably `"$item_staff_skeleton"` for the Dead Raiser) and
`.m_attack`. **Confirming the exact name/localization key and
`m_itemType` the Dead Raiser ships with is asset data** — not
resolvable from the decompiled C# alone; see below.

### Is there a vanilla "teleport a creature here" primitive to reuse?

No dedicated API for it was found — vanilla doesn't have a "recall my
summons" feature. But the primitive is exactly
`RossQoL/src/RossQoL.Game/Portals/TameMover.cs`'s `TryMove`, whose
three-part write (`ZDO.SetOwner` + `ZDO.SetPosition`, then, if a live
`GameObject` exists, both `transform.position` and `Rigidbody.position`
followed by `Physics.SyncTransforms()`) is derived directly from how
vanilla itself relocates a `Character` (`Character.UnderWorldCheck`
does `transform.position = pos; m_body.position = pos;` — cited in
`TameMover.cs`'s own comments) and from `ZSyncTransform.GetPosition`
preferring the `Rigidbody` over `transform` whenever one exists. That
reasoning is generic to any `Character`, summon or tame alike —
**`TameMover.TryMove` is directly reusable, unmodified, for a
teleport-summons-to-me feature**, called once per summon `ZDOID` with
the caster's current position as destination. The only new code needed
is the selection step (find summons: same as `Portals/TamesFollow`'s
selection, likely narrowed to same-name-as-Dead-Raiser's-spawn-prefab
+ `ZDOVars.s_follow == player name`, per `UnsummonMaxInstances`'s own
matching logic) and the middle-click trigger.

## Why summons were left behind at portals (read 2026-09-19, 1.0.14)

The author reported that raised skeletons never came through a portal,
even though `Portals/TamesFollow`'s four conditions all looked
satisfied. Working them against decompiled source:

- **Following — passes.** `Tameable.Command` (`Tameable.cs:445`) is a
  bare `m_nview.InvokeRPC("Command", ...)` with **no `m_commandable`
  gate**; the `m_commandable` check lives in `Interact`
  (`Tameable.cs:203`), the petting path, not the spawn path. So
  `m_commandable = False` on this prefab does *not* stop the spawn-time
  command. `RPC_Command` (`Tameable.cs:460`) finds no existing follow
  target and takes the else-branch:
  `m_monsterAI.SetFollowTarget(player.gameObject)` on every client that
  receives it, plus `ZDOVars.s_follow = player.GetPlayerName()` on the
  owner.
- **Registered — passes.** `Character.Awake` does
  `s_characters.Add(this)` as its first statement (`Character.cs:663`),
  and `GetAllCharacters()` returns that list directly
  (`Character.cs:4026`). A summon is in it from Awake onward.
- **Busy — passes.** Nothing in the summon path sets `ZDOVars.s_user`
  or attaches the creature.
- **Tamed — this is the one that fails.** `Tameable.Awake` does
  `if (m_startsTamed && (bool)m_character) m_character.SetTamed(tamed: true);`
  (`Tameable.cs:105`). But `Character.SetTamed` (`Character.cs:4316`)
  only fires an RPC, and `RPC_SetTamed` (`Character.cs:4324`) writes the
  flag **solely under `m_nview.IsOwner()`**. Non-owners re-read it from
  the ZDO in `IsTamed(float)` (`Character.cs:4333`) — but that refresh
  is itself gated on `!GetZDO().IsOwner()`, so a client that owns the
  ZDO returns its own local `m_tamed` and never re-reads.

The delivery is the interesting part. `ZNetView.InvokeRPC(string, ...)`
targets `m_zdo.GetOwner()` (`ZNetView.cs:331`), and
`ZRoutedRpc.InvokeRoutedRPC` runs `HandleRoutedRPC` **inline** when the
target is this peer (`ZRoutedRpc.cs:130`) — no queue, no frame delay, so
this is not a race that resolves itself a moment later. For a
ZDO-addressed RPC, `HandleRoutedRPC` (`ZRoutedRpc.cs:189`) resolves the
receiver with `ZNetScene.instance.FindInstance(zDO)`, which reads the
`m_instances` dictionary that `ZNetView.Awake` populates via
`AddInstance` **as its very last statement** (`ZNetView.cs:108`,
`ZNetScene.cs:72`). If `FindInstance` returns null the call is dropped
in silence.

So whether a summon is ever marked tamed *could* depend on whether
`ZNetView.Awake` has finished before `Tameable.Awake` runs. Both execute
inside the single `UnityEngine.Object.Instantiate` in
`ZNetScene.CreateObject` (`ZNetScene.cs:103-105`), and Unity orders
Awake across components of one GameObject by the prefab's serialized
component order — **asset data, not in the DLL** (see README, "What is
not in the DLLs"). The decompile cannot settle whether that race
actually fires. **See "Correcting the diagnosis" below — this
Awake-order theory turned out not to fit the actual symptom (permanent
failure only on a dedicated server, not in single player) and is kept
here only as one candidate mechanism, not the established cause.**

`TameEligibility.Qualifies` tested `IsTamed` *first*, so this is also
the condition that fired first. RossQoL's fix does not try to repair
vanilla's flag: it recognises a raised skeleton by prefab
(`Skeleton_Friendly`) and exempts it from the tamed test only. Nothing
is loosened by that, because the follow test still has to pass, and
`s_follow`/a live follow target is only ever set by `Tameable.Command`,
reachable only from petting an already-tamed creature or from
`SpawnAbility` raising a summon — so "following me" already implies
"mine", and a wild creature can never satisfy it. This part of the fix
does not depend on which of the theories below is correct.

## Correcting the diagnosis: single player vs. dedicated server (read 2026-09-19, continued)

The confirmation capture (`RossQoL-DIAG:` log, single player) showed
`tamed=True summon=True SELECTED=True` for a raised skeleton -- i.e. in
single player the tamed flag was written successfully. The author's
original bug report, however, was from a **dedicated server**. Since
the Awake-order race above is a property of local component ordering on
whichever machine instantiates the prefab, it should fire (or not)
identically in single player and on a dedicated server -- it has no
reason to depend on which of the two is running. A theory that predicts
"works in SP, fails on a server" has to be about something that
actually differs between those two setups: who owns the new ZDO.

**Where ownership of a freshly spawned summon actually comes from.**
`SpawnAbility.Spawn()` (the coroutine `Tameable.Command` is called
from) creates the skeleton with a bare `UnityEngine.Object.Instantiate`
(`SpawnAbility.cs:212`) -- no `ZNetScene`/owner-only guard was found
around it. It runs inside the same `Attack`/`Humanoid.StartAttack` call
chain as any other attack, which is driven by local `ZInput` reads in
`PlayerController.UpdateControls` -- input that only exists on the
machine actually running the summoning player's own client, dedicated
server or not. So the `Instantiate()` call, and therefore the new
skeleton's `ZNetView.Awake()`, runs on the **summoning player's own
client** in both configurations, not on the server.

`ZNetView.Awake()`, when there is no `m_initZDO` (i.e. this is a truly
new object, which a freshly-instantiated skeleton is), creates its ZDO
via `ZDOMan.CreateNewZDO` (`ZNetView.cs:91`), which immediately does
`zDO.SetOwnerInternal(m_sessionID)` using the **local** `ZDOMan`'s own
session id (`ZDOMan.cs:750-757`) -- i.e. whichever peer ran `Awake()`
becomes the owner synchronously, before any network round trip. On a
dedicated server this is still the summoning client's own session id,
not the server's.

`Character.SetTamed`'s RPC (`RPC_SetTamed`) is sent via
`ZNetView.InvokeRPC(string, ...)`, which targets `m_zdo.GetOwner()`
(`ZNetView.cs:331`) -- and per the above, that owner is the summoning
client itself, whether playing solo or on a server. I could not find a
decompiled path where the dedicated server claims ownership of the
skeleton before the summoning client's own `Tameable.Awake` runs:
`ZDOMan.ReleaseNearbyZDOS` (`ZDOMan.cs:938-951`), the server's only
proximity-based ownership reassignment, runs on a 2-second timer, only
on the server (`ZDOMan.Update`, `ZDOMan.cs:878-888`), and hands a
persistent ZDO to whichever peer's reference position is nearest to it
-- which is the summoning player themselves, standing right next to
their own new skeleton. That logic would *keep* ownership with the
summoner, not move it away.

**So the decompiled evidence does not support "the server owns the
summon and the client can't write it" as I was able to trace it.**
Creation-time ownership, RPC routing, and the server's own reassignment
rule all point at the summoning client staying the owner throughout.
That leaves the SP-vs-server discrepancy genuinely unexplained by
static analysis: the Awake-order race is the only concrete failure
mechanism visible in source, but it doesn't predict a difference
between single player and a dedicated server, and nothing else found
here does either. Plausible reasons the two setups still behave
differently exist -- e.g. server/client frame or RPC-queue timing under
real network load, or a server-side shadow instantiation of the object
interacting with delivery in some way this reading didn't trace all the
way through -- but none of them were confirmed against source. **See
"Why a correctly-selected summon never arrives at a portal" below --
the SP-vs-server split turned out not to need this theory at all; the
real cause was a second, later bug (the unsummon-distance check firing
mid-transit) that a short single-player hop happens not to trigger.**
Rely only on what's confirmed here: `IsTamed()` can be false for
a summon's whole life regardless of the exact mechanism, so
`TameCandidate.IsSummon` exempting it from the tamed test is correct
either way.

## Things I could not verify (need a runtime dump)

- **The Dead Raiser's exact prefab name, `ItemDrop.ItemData.SharedData.m_name`
  localization key, and `m_itemType`.** No `StaffSkeleton` (or any
  skeleton/staff-named) C# type exists — the weapon is a plain
  `ItemDrop` configured entirely through the Unity inspector, which is
  asset data, not in the DLL. Dump the equipped item on the Dead
  Raiser in-game: `Player.m_localPlayer.GetRightItem().m_shared.m_name`
  (and `.m_attack.m_attackProjectile.name`, and that projectile's
  `SpawnAbility.m_spawnPrefab[]` entries) to get the real strings to
  match against.
- **Whether the Dead Raiser's `Attack` actually defines a
  `m_secondaryAttack`.** If it does, this feature's middle-click hook
  would collide with an existing vanilla behavior for this specific
  weapon and needs a different trigger (e.g. a held/long-press
  variant, or a different key entirely). Check
  `SharedData.m_secondaryAttack` on the live item instance.
  (`m_attack`/`m_secondaryAttack` fields exist on `SharedData` per the
  general `Attack`/`Humanoid` reading above, but this project's
  decompile did not include a full `Humanoid.StartAttack` dump to
  confirm the secondary-attack field name precisely — grep
  `Humanoid.cs` for `m_secondaryAttack` before hooking.)
- **The Dead Raiser's actual `Tameable` inspector values** —
  `m_unsummonDistance`, `m_unsummonOnOwnerLogoutSeconds`,
  `m_commandable`, `m_maxSpawned`/`m_maxSpawned` on its `SpawnAbility`,
  and the raised-skeleton prefab's `m_character.m_name`. All are
  serialized prefab data, not field initializers in source (the
  `Tameable.cs`/`SpawnAbility.cs` defaults shown above — e.g.
  `m_unsummonDistance = 0` via no initializer — are compile-time
  defaults only). Needed to know, concretely, how far a summon can
  wander before self-despawning and whether it survives caster logout
  at all on this specific prefab. Dump the live raised-skeleton
  prefab's `Tameable` component (`ZNetScene.GetPrefab(...)` or an
  instantiated one via `character.GetComponent<Tameable>()` reflection
  of its serialized fields) at runtime.
- **Whether `SecondaryAttack` fires an `Attack` at all for the Dead
  Raiser**, vs. being entirely unbound for it (no visible effect on
  middle click today) — only a live probe (log
  `ZInput.GetButtonDown("SecondaryAttack")` while wielding it, or
  inspect `Humanoid.StartAttack`'s dispatch) settles this definitively.
- **The order Unity runs `Awake` across the components of the
  `Skeleton_Friendly` prefab** — specifically whether `ZNetView.Awake`
  (whose last statement registers the instance via
  `ZNetScene.AddInstance`) completes before `Tameable.Awake` calls
  `SetTamed`. That order is the prefab's serialized component order, so
  it is asset data and not in the DLL. See "Correcting the diagnosis"
  above: this ordering, even if confirmed, would not by itself explain
  why the bug reproduces on a dedicated server but not in single player.
- ~~Why the failure is dedicated-server-only~~ — **resolved, see below.**
  The confirmation capture ran in single player and showed `IsTamed()`
  succeeding; the original bug report was from a dedicated server, and
  decompiled evidence traces ownership of a freshly spawned summon to
  the summoning client in *both* configurations (see "Correcting the
  diagnosis" above), which didn't explain the split on its own. The
  actual explanation, found afterwards (see "Why a correctly-selected
  summon never arrives at a portal"), needed no server-vs-client
  difference at all: it was `m_unsummonDistance`, and a single-player
  test hop happened to be shorter than that distance while the
  dedicated-server hop was not.

## Why a correctly-selected summon never arrives at a portal

**Read 2026-09-19 from a fresh `ilspycmd -p` dump. Note the version has
moved on: `Version.CurrentVersion` in this dump is
`new GameVersion(1, 0, 15)`, not the 1.0.14 the rest of this file was
read from. Nothing quoted below differs from what 1.0.14 recorded, but
line numbers may have drifted.**

The author's dedicated-server capture showed selection working
perfectly (`tamed=True summon=True SELECTED=True`, `Portal: bringing 1
tame(s)`) and the move failing completely (`Portal: 0 of 1 tame(s)
arrived`). The cause is that **vanilla destroys the summon while the
player is in transit**, so by arrival there is no ZDO left to write to.

### The unsummon path, in full

`Tameable.Update()` — a plain Unity message, running every frame on
every loaded `Tameable` — is exactly two calls:

```csharp
public void Update()
{
    UpdateSummon();
    UpdateSavedFollowTarget();
}
```

and `UpdateSummon` is the distance rule:

```csharp
private void UpdateSummon()
{
    if (m_nview.IsValid() && m_nview.IsOwner() && m_unsummonDistance > 0f && (bool)m_monsterAI)
    {
        GameObject followTarget = m_monsterAI.GetFollowTarget();
        if ((bool)followTarget && Vector3.Distance(followTarget.transform.position, base.gameObject.transform.position) > m_unsummonDistance)
        {
            UnSummon();
        }
    }
}
```

So: **owner only, every frame, against the follow target's LIVE
transform position.**

It destroys the ZDO, not merely the GameObject:

```csharp
private void UnSummon()
{
    if (m_nview.IsValid()) m_nview.InvokeRPC(ZNetView.Everybody, "RPC_UnSummon");
}

private void RPC_UnSummon(long sender)
{
    m_unSummonEffect.Create(...);
    if (m_nview.IsValid() && m_nview.IsOwner()) ZNetScene.instance.Destroy(base.gameObject);
}
```

and `ZNetScene.Destroy` is:

```csharp
public void Destroy(GameObject go)
{
    ZNetView component = go.GetComponent<ZNetView>();
    if ((bool)component && component.GetZDO() != null)
    {
        ZDO zDO = component.GetZDO();
        component.ResetZDO();
        m_instances.Remove(zDO);
        if (zDO.IsOwner()) ZDOMan.instance.DestroyZDO(zDO);
    }
    UnityEngine.Object.Destroy(go);
}
```

Worth contrasting with an ordinary zone unload, which does **not**
destroy the ZDO — `ZNetScene.RemoveObjects` only calls `DestroyZDO` for
a non-persistent one (`if (!zDO.Persistent && zDO.IsOwner())`). So
`ZDOMan.GetZDO(id)` returning null at arrival is specifically evidence
of a real `DestroyZDO`, and `UnSummon` is the path that performs one on
a creature this mod captured.

### Why it fires during a teleport: the player's transform is already at the destination

`Player.UpdateTeleport(float dt)` moves the transform two seconds in,
and only clears `m_teleporting` later, once the destination area has
loaded and a floor has been found:

```csharp
m_teleportTimer += dt;
if (!(m_teleportTimer > 2f)) return;
Vector3 dir = m_teleportTargetRot * Vector3.forward;
base.transform.position = m_teleportTargetPos;      // <-- already at the destination
...
if ((!(m_teleportTimer > 8f) && m_distantTeleport) || !ZNetScene.instance.IsAreaReady(m_teleportTargetPos)) return;
float height = 0f;
if (ZoneSystem.instance.FindFloor(m_teleportTargetPos, out height))
{
    m_teleportTimer = 0f;
    m_teleporting = false;                           // <-- only now
    ResetCloth();
}
```

`IsTeleporting()` is a bare `return m_teleporting;`, so for the whole
window between those two points the player's `transform.position` reads
as the DESTINATION while the summon is still standing at the departure
point and still running its own `Update`. `UpdateSummon` therefore
measures the entire length of the hop against `m_unsummonDistance`, and
for any hop longer than that value it unsummons. Note this means the
unsummon can fire at **any frame** of the transit, not at its ends — a
guard has to cover the whole window.

**The sting: RossQoL's own capture guarantees it.**
`PortalTamesManager.CaptureDeparture` calls `view.ClaimOwnership()` on
every selected creature, and `UpdateSummon` is gated on
`m_nview.IsOwner()` — so capturing a summon makes this client precisely
the one that runs the check that destroys it. A short single-player hop
survived only because it was shorter than `m_unsummonDistance`.

### Question by question

3. **Who owns a summon on a dedicated server** — unchanged from
   "Correcting the diagnosis" above: the summoning client, because
   `ZNetView.Awake` → `ZDOMan.CreateNewZDO` stamps the local session id,
   and the server's `ReleaseNearbyZDOS` hands a ZDO to the nearest
   player's peer, which is the summoner. RossQoL then claims it
   explicitly at departure, so during a hop the owner is definitely the
   teleporting client.
4. **`m_unsummonOnOwnerLogoutSeconds` is NOT involved.** Its timer lives
   in `UpdateSavedFollowTarget`, which returns immediately unless
   `m_monsterAI.GetFollowTarget() == null`, and the timer only advances
   when the followed name is absent from `Player.GetAllPlayers()`. That
   list is `Player.s_players`, added to in `Player.Awake` and removed
   from only in `Player.OnDestroy` — a teleport destroys no Player, so a
   teleporting player is never missing from it. The distance rule is the
   whole story.

### What is still asset data

`m_unsummonDistance`'s actual value on `Skeleton_Friendly` is a
serialized prefab field and is **not** in the DLL (the C# has no
initialiser, so the compile-time default is 0). The reported figure of
150 comes from a runtime dump and is not otherwise recorded in this
repository — treat it as the author's observation, not a verified
constant. The mechanism above does not depend on the exact number, only
on it being non-zero and smaller than the hop.

### The fix RossQoL applies

`Portals/SummonUnsummonGuard` + `Portals/TameableUnsummonPatch`: a
Harmony prefix on `Tameable.Update` that skips the method for exactly
those ZDOIDs the current portal capture is carrying, for the whole
transit, released on every path that ends a capture. Patched on `Update`
rather than the narrower `UpdateSummon`/`UnSummon` because both of those
are tiny private methods and so are inlining candidates that a Harmony
patch would silently miss; `Update` is invoked by the Unity runtime and
never inlined. Nothing mutates `m_unsummonDistance`, so the creature's
behaviour is unchanged for everyone outside those few seconds.

### Confirmed fixed (2026-09-19)

The author confirmed on his dedicated server that summoned skeletons
now come through portals with the guard in place. That closes the loop
opened above: the `Portals/LogPortalTameDiagnostics` diagnostic
(`RossQoL-DIAG:` prefix), added to settle why the tamed flag looked
inconsistent, has done its job and was removed once this fix was
verified — everything it was keeping open (why single player differed
from a dedicated server; whether a raised skeleton's tamed flag is
reliable) is answered above without needing another capture: `IsTamed()`
is not trusted for summons regardless of cause, and the SP-vs-server
split was hop length against `m_unsummonDistance`, not anything
server-specific. `TameMoveOutcome` (`RossQoL.Core.Portals`) is the
permanent piece that survives the diagnostic: `PortalTamesManager`
logs a warning for any arrival that isn't `Moved`, at the ordinary log
level, with no config flag required.

## Replaying an attack animation re-fires the last attack (read 1.0.15)

Traced while fixing a duplication bug: RossQoL's `Items/RecallSummons`
plays the Dead Raiser's own `"staff_summon"` animation for its recall
cast, and that raised a second skeleton for free.

The chain, all from `assembly_valheim` 1.0.15:

- `Humanoid.m_currentAttack` is assigned in `Humanoid.StartAttack`
  (`Humanoid.cs:310`) and cleared in exactly two places: the top of
  `StartAttack` itself (`Humanoid.cs:299-303`) and `UnequipItem`
  (`Humanoid.cs:1296-1300`). **It is never cleared when an attack
  finishes.** After a swing ends, `Attack.Update` calls `Stop()`
  (`Attack.cs:556-559`), which only sets `m_attackDone` -- the character
  keeps holding that `Attack` object indefinitely.
- Attack animation clips carry an animation event that reaches
  `CharacterAnimEvent.Hit()` / `OnAttackTrigger()`
  (`CharacterAnimEvent.cs:244-252`) then `Humanoid.OnAttackTrigger`
  (`Humanoid.cs:553-560`). That guard checks only `m_currentAttack !=
  null` and `GetCurrentWeapon() != null`. **It does not check
  `IsDone()`.**
- `Attack.OnAttackTrigger` (`Attack.cs:607`) has no `m_attackDone`
  early-return of its own (unlike `Attack.Update`, `Attack.cs:516-520`)
  and switches straight to `ProjectileAttackTriggered`
  (`Attack.cs:775`), then `FireProjectileBurst`, then the staff's summon
  projectile and its `SpawnAbility`.
- Eitr, stamina and health are spent in `Attack.Update`
  (`Attack.cs:531-537`), behind that `m_attackDone` early-return, or in
  `FireProjectileBurst` only when `m_perBurstResourceUsage` is set.
  So the re-fire is free.

**Rule: triggering an attack animation on a character outside
`Humanoid.StartAttack` re-fires whatever attack that character last
started.** Any feature that does this must retire `m_currentAttack`
first, the way `StartAttack` does (park it in `m_previousAttack`, which
feeds the next attack's chain level, then null it), and must refuse to
do so while the held attack is not `IsDone()` -- clearing an in-flight
attack swallows the payload its cost has already been paid for.

Ruled out along the way, worth not re-deriving:

- `EffectList.Create` (`EffectList.cs:37`) only `Instantiate`s each
  listed prefab; it never calls `IProjectile.Setup`, so it cannot start
  a `SpawnAbility` coroutine except via `SpawnAbility.Awake`'s
  `m_spawnOnAwake` (`SpawnAbility.cs:108-114`) -- and vanilla's own
  primary attack plays the same `m_startEffect` list while raising
  exactly one skeleton, which settles it.
- `Player.PlayerAttackInput` (`Player.cs:1805-1843`) zeroes
  `m_queuedAttackTimer` whenever a secondary attack is queued, so a
  secondary that returns false never falls through to the primary.

## The summon cap, and why a parked skeleton escapes it (read 1.0.15)

Traced while closing the "tell a skeleton to stay and raise more than the
cap" exploit. Everything below is quoted from a fresh single-type
`ilspycmd -t Tameable` / `-t SpawnAbility` dump of 1.0.15.

### Where the cap number comes from

`ZDOVars.s_maxInstances` is written **once, at spawn**, by
`SpawnAbility.Spawn()`, and only inside the `m_levelUpSettings` block:

```csharp
int num2 = (m_setMaxInstancesFromWeaponLevel ? m_weapon.m_quality : levelUpSettings.m_maxSpawns);
if (num2 > 0 && (bool)component)
    component.GetZDO().Set(ZDOVars.s_maxInstances, num2);
```

So the cap is **per-summon, stamped on the creature's own ZDO**, and its
value is either the staff's upgrade quality or the `m_maxSpawns` of the
highest `LevelUpSettings` entry the caster's Blood Magic skill reaches.
Both are serialized prefab data — **not in the DLL**. Note `m_levelUpSettings`
being empty means the field is never written and the cap never runs at all.
(This is distinct from `SpawnAbility.m_maxSpawned` (default 3), which is a
proximity check via `SpawnSystem.GetNrOfInstances` performed *before*
spawning and has nothing to do with `Tameable`'s cap.)

### When the cap runs

Only from the **"start following"** branch of `RPC_Command`, at its very end:

```csharp
else
{
    m_monsterAI.ResetPatrolPoint();
    m_monsterAI.SetFollowTarget(player.gameObject);
    if (m_nview.IsOwner())
        m_nview.GetZDO().Set(ZDOVars.s_follow, player.GetPlayerName());
    ...
    int @int = m_nview.GetZDO().GetInt(ZDOVars.s_maxInstances);
    if (@int > 0)
        UnsummonMaxInstances(@int);
}
```

Nothing else calls `UnsummonMaxInstances`. There is no periodic sweep: the
count happens **at the instant a creature begins following**, and never
again.

### How it counts

`UnsummonMaxInstances(int maxInstances)` — owner-only — reads the name of
the player this creature is now following, then:

```csharp
foreach (Character item in allCharacters)
{
    if (!(item.m_name == m_character.m_name)) continue;
    ... obj2 = zDO.GetString(ZDOVars.s_follow) ...
    if ((string)obj2 == text)      // text == the follow target player's name
    {
        MonsterAI component3 = item.GetComponent<MonsterAI>();
        if ((object)component3 != null) list.Add(component3);
    }
}
list.Sort((a, b) => b.GetTimeSinceSpawned().CompareTo(a.GetTimeSinceSpawned()));
int num = list.Count - maxInstances;
for (int i = 0; i < num; i++)
    list[i].GetComponent<Tameable>()?.UnSummon();
```

Two conditions, both required: **same `Character.m_name`** and **same
`ZDOVars.s_follow` string**. The oldest excess (largest
`GetTimeSinceSpawned`) is unsummoned.

### The exploit, exactly

The "stay" branch of `RPC_Command` does `m_nview.GetZDO().Set(ZDOVars.s_follow, "")`.
A skeleton told to stay therefore has an **empty** follow string, which can
never equal the summoner's name, so it is not added to `list` and does not
count. Raise three, tell them all to stay, raise three more — each new cast
counts only the skeletons still following, and the parked ones live on. The
cap is not a population limit; it is a "how many are following me right now"
limit.

### Why commanding works at all when `m_commandable` is false

A runtime dump of `Skeleton_Friendly` showed `Tameable.m_commandable = False`,
and vanilla's `Interact` does gate on it:

```csharp
if (Time.time - m_lastPetTime > 1f)
{
    m_lastPetTime = Time.time;
    m_petEffect.Create(...);
    if (m_commandable)
    {
        Command(user);
        Game.instance.IncrementPlayerStat(PlayerStatType.TamedCommand);
        goto IL_010e;          // commanded
    }
    Game.instance.IncrementPlayerStat(PlayerStatType.TamedPetting);
    ...                        // petted, message shown
}
```

**The dump was not misread and vanilla is not the culprit — RossQoL is.**
`Tames/FollowCommand` (`FollowCommandPatch`) prefixes `Tameable.Interact`
and sets `m_commandable = true` for the length of the call on any tame that
has a `MonsterAI` and passes `Tameable.IsTamed()`. `Tameable.IsTamed()` is

```csharp
if (!m_character || !m_character.IsTamed()) return m_startsTamed;
return true;
```

and `Skeleton_Friendly` ships `m_startsTamed` true, so a raised skeleton
passes and gets commanded. That is the whole mechanism: **the exploit exists
only with `FollowCommand` on**, and it is the interaction, not the cap, that
is ours to withdraw.

Worth noting for any future feature: `Tameable.Command` itself
(`m_nview.InvokeRPC("Command", ...)`) has **no `m_commandable` gate** — the
flag only ever guards `Interact`. So the spawn-time follow
(`SpawnAbility.m_commandOnSpawn`) and `UpdateSavedFollowTarget`'s
reload recovery both go straight through `Command`, and a block placed there
would leave summons that follow nobody. `Interact` is the only correct place.

### What removing the interaction costs

`Interact`'s tamed branch offers exactly three things, and a summon loses all
three: the pet effect and message, the follow/stay toggle (only via
`FollowCommand`), and — on the `alt` path — `SetName()`, vanilla's rename.
Nothing else reaches `Tameable.Interact`. "Stay" has no effect beyond
clearing the follow target and calling `MonsterAI.SetPatrolPoint()`, so what
is genuinely lost is the ability to park a summon as a stationary guard; that
is the exploit itself, not a separate feature. Renaming a creature with a
`m_unsummonDistance` leash and a few minutes to live was judged not worth a
half-working prompt.

`Tameable.GetHoverText`'s tamed branch appends two prompt lines
(`"\n[<color=yellow><b>$KEY_Use</b></color>] $hud_pet"` and the `$hud_rename`
line, which is the gamepad `$KEY_AltKeys` variant when
`ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive()`), so a
feature that blocks `Interact` must rewrite the hover text or the prompt
advertises something that no longer happens. RossQoL's
`Tames/NoSummonCommands` does both: a prefix on `Interact` returning false,
and a postfix on `GetHoverText` rebuilding just the name and
`" ( $hud_tame, " + GetStatusString() + " )"`. Both methods are long and
branchy — neither is an inlining candidate, unlike `UpdateSummon`/`UnSummon`.

## Re-verifying the cap's selection step, and its IL (read 1.0.15)

Re-read while making the cap despawn the worst-wounded summon rather than
the oldest (`Tames/CullWoundedSummons`). Fresh single-type dumps,
`ilspycmd -t Tameable` for the C# and `ilspycmd -il -t Tameable` for the
IL, against the same installed `assembly_valheim.dll`.

**The quote above under "How it counts" is still accurate at 1.0.15.** The
sort line is verbatim what ships:

```csharp
list.Sort((BaseAI a, BaseAI b) => b.GetTimeSinceSpawned().CompareTo(a.GetTimeSinceSpawned()));
```

`UnsummonMaxInstances` is at `Tameable.cs:641-707` in this dump, which is
the same range the earlier note recorded — the line numbers had not
drifted.

Three things the earlier note left out or slightly misstated, all of which
matter to anything rewriting this method:

- **The list is `List<BaseAI>`, not `List<MonsterAI>`.** The elements are
  `MonsterAI`s (`item.GetComponent<MonsterAI>()` is what gets added), but
  the declared local is `List<BaseAI>`, so the sort is
  `List<BaseAI>.Sort(Comparison<BaseAI>)` and `GetTimeSinceSpawned` is
  resolved on `BaseAI`, not `MonsterAI`. Any transpiler matching that
  `Sort` call has to name the `BaseAI` instantiation or it will not match.
- **The method does one more thing after despawning**, which the earlier
  excerpt cut off:

  ```csharp
  if (num > 0 && (bool)Player.m_localPlayer)
      Player.m_localPlayer.Message(MessageHud.MessageType.Center, m_maxSummonReached);
  ```

  So reimplementing the method wholesale means owning that message too
  (`m_maxSummonReached` is a serialized string field — asset data, not in
  the DLL). Replacing only the sort avoids it entirely.
- **`GetTimeSinceSpawned` returns a `TimeSpan`, not a float**
  (`BaseAI.cs:403`), and returns `TimeSpan.Zero` when the `ZNetView` is
  missing or invalid — so an un-networked creature reads as freshly
  spawned, i.e. last in vanilla's despawn order.

### Inlining: `UnsummonMaxInstances` is safe to patch, its helpers are not

Measured rather than assumed, from the IL dump's method header:

```
instance void UnsummonMaxInstances (int32 maxInstances) cil managed
{
    // Header size: 12
    // Code size: 365 (0x16d)
```

365 bytes, with a `foreach` over `Character.GetAllCharacters()`, a cached
closure delegate and a `for` loop. Mono's inliner takes only tiny bodies
(its `INLINE_LENGTH_LIMIT` is on the order of 20 IL bytes), so this is
about eighteen times past the threshold and is reached reliably. It is
also called from exactly one place, the "start following" branch of
`RPC_Command` (`IL_0112: call instance void Tameable::UnsummonMaxInstances(int32)`).

The helpers it calls are the opposite and must never be patched:
`BaseAI.GetTimeSinceSpawned`, `Character.GetHealth`
(`return m_nview.GetZDO()?.GetFloat(ZDOVars.s_health, GetMaxHealth()) ?? GetMaxHealth();`),
`Character.GetMaxHealth` and `Character.GetHealthPercentage`
(`return GetHealth() / GetMaxHealth();`) are all one- or two-line bodies
and are exactly the shape Mono inlines silently past a Harmony patch.
Calling them is fine; patching them is not.

### The selection step is replaceable on its own

The sort compiles to a plain call with the list and the comparison on the
stack and nothing left behind:

```
IL_00ed: ldloc.3                       // List<BaseAI>
IL_00ee: ldsfld  Comparison`1<BaseAI> Tameable/'<>c'::'<>9__36_0'
         ... cached-delegate creation ...
IL_010d: callvirt instance void List`1<BaseAI>::Sort(Comparison`1<!0>)
IL_0112: ldloc.3
```

so swapping that one `callvirt` for a `call` to a static
`(List<BaseAI>, Comparison<BaseAI>) -> void` of our own is stack-neutral
and leaves every other instruction vanilla's. That is what
`Tames/CullWoundedSummonsPatch` does; it keeps vanilla's own comparison as
the argument and runs it unchanged when the feature is off or its own
ordering throws.

### Health, for ordering purposes

`Character.GetHealth()` and `Character.GetMaxHealth()` are both public.
`GetMaxHealth` reads `ZDOVars.s_maxHealth` off the ZDO, falling back to
`GetMaxHealthBase()` (`m_health`, multiplied by
`Game.m_worldLevel * Game.instance.m_worldLevelEnemyHPMultiplier` for any
non-player when the world level is raised). A star level therefore shows
up as a larger max, which is why a wounded-first rule has to compare the
fraction and not the raw number. `GetHealth` falls back to `GetMaxHealth()`
when there is no ZDO or no stored health, so a creature with no health
recorded reads as full rather than as dead.

### Still not verifiable from the DLL

- Whether the Dead Raiser can ever raise a **starred** skeleton. The
  `SpawnAbility.m_levelUpSettings` entries that would set a level are
  serialized prefab data. The reported answer is no, which makes the
  fraction rule and a raw-health rule pick the same creature in practice
  today; the fraction is the one that stays correct if that changes.
- The cap's actual number for any given staff — still
  `m_setMaxInstancesFromWeaponLevel` / `LevelUpSettings.m_maxSpawns`, both
  asset data, as recorded above.
- Whether a summon's health ever differs from full in a way the ZDO has
  not yet replicated to the machine running the cap. The cap is owner-only
  and the owner is the summoning client (established above), and that is
  the machine the skeletons were fighting on, so the health it reads is
  its own — but this was not confirmed against a live capture.

## Factions, `m_tamed`, and who a summon counts as an enemy (read 1.0.15)

Traced while investigating a report (later retracted) that tamed wolves
attacked raised skeletons. No code was changed; the facts below are
worth keeping because they settle how a summon is classified for target
acquisition.

### The faction enum

`Character.Faction` (`Character.cs:8-24`), in declaration order:
`Players, AnimalsVeg, ForestMonsters, Undead, Demon, MountainMonsters,
SeaMonsters, PlainsMonsters, Boss, MistlandsMonsters, Dverger,
PlayerSpawned, TrainingDummy, DeepNorth`. The field is
`public Faction m_faction = Character.Faction.AnimalsVeg;`
(`Character.cs:83`) with `GetFaction()` a bare `return m_faction`
(`Character.cs:800`). **Which value `Skeleton_Friendly` and `Wolf`
actually ship is serialized prefab data and is NOT in the DLL** — the
initialiser above is only the compile-time default. To read them, dump
`character.GetFaction()` on a live raised skeleton and on a live tamed
wolf. Note `PlayerSpawned` exists as a distinct faction and is the
obvious candidate for a summon, but that is inference, not verified.

### `BaseAI.IsEnemy` — the whole rule

`public static bool IsEnemy(Character a, Character b)`
(`BaseAI.cs:1195-1292`), quoted in the order it tests:

```csharp
if (a == b) return false;
if (!a || !b) return false;
string group = a.GetGroup();
if (group.Length > 0 && group == b.GetGroup()) return false;
Character.Faction faction = a.GetFaction();
Character.Faction faction2 = b.GetFaction();
bool flag  = a.IsTamed();
bool flag2 = b.IsTamed();
bool flag3 = (bool)a.GetBaseAI() && a.GetBaseAI().IsAggravated();
bool flag4 = (bool)b.GetBaseAI() && b.GetBaseAI().IsAggravated();
if (flag || flag2)
{
    if ((flag && flag2)
        || (flag  && faction2 == Character.Faction.Players)
        || (flag2 && faction  == Character.Faction.Players)
        || (flag  && faction2 == Character.Faction.Dverger && !flag4)
        || (flag2 && faction  == Character.Faction.Dverger && !flag3))
        return false;
    return true;
}
...                       // aggravated rules, then faction == faction2,
                          // then the per-faction switch
```

Three things follow, all load-bearing:

1. **`m_tamed` short-circuits faction entirely.** The moment either
   party is tamed, the faction switch at the bottom is never reached;
   the only outcomes are the five exemptions above. So for any pair
   involving a tame, faction matters only as `Players` or `Dverger`.
2. **Two tamed creatures are never enemies, whatever their factions.**
   `(flag && flag2) → return false` is unconditional. A tamed wolf and a
   raised skeleton that both read as tamed cannot target each other —
   the skeleton staying `Undead`/`PlayerSpawned` is irrelevant.
3. **It is symmetric in outcome but not in form.** Every clause comes in
   an a/b pair, so `IsEnemy(x, y) == IsEnemy(y, x)` for the tamed branch:
   if one side would attack, so would the other. There is no one-sided
   "wolf hates skeleton" state to fix.

Therefore: if a player's tame ever did attack his own summon, the only
mechanism inside `IsEnemy` is **the summon's `IsTamed()` reading false
on whichever machine runs the attacker's AI** — the known-fragile flag
documented at length above (`RPC_SetTamed` writes only under
`m_nview.IsOwner()`; the non-owner refresh in `IsTamed(float)` is itself
gated on `!GetZDO().IsOwner()` and rate-limited to once a second via
`m_lastTamedCheck`, `Character.cs:4333-4345`). With `flag2` false and
the skeleton's faction not `Players`, the tamed branch falls straight to
`return true` in both directions.

`GetGroup()` is the other escape hatch: a non-empty `m_group` string
shared by both prefabs makes them non-enemies before faction or tamedness
is even read. `m_group`'s value per prefab is asset data, not in the DLL.

### Where it is consulted, and what is safe to patch

`BaseAI.FindEnemy()` (`BaseAI.cs:1397-1420`) walks
`Character.GetAllCharacters()` and skips anything `IsEnemy(m_character,
item)` rejects, then applies `IsDead`/`m_aiSkipTarget`/`IsSleeping`/
`CanSenseTarget`. The static `IsEnemy` is also the gate for the *friend*
queries (`HaveFriendsInRange`, `HaveHurtFriendInRange`,
`FindNearestFriend` — `BaseAI.cs:1355`, `1373`, `1589`) and for
`FindClosestEnemy`/`FindClosestCreature` (`BaseAI.cs:1610-1642`). So
forcing a pair to "not enemies" also makes them count as friends to each
other, which is exactly what vanilla already does for two tames.

Inlining: the instance overload `public bool IsEnemy(Character other)`
(`BaseAI.cs:1190-1193`) is a one-line delegation and **is** an inlining
candidate — patching it would be silently bypassed. The static
`IsEnemy(Character, Character)` is ~100 lines with a fourteen-case
switch and is far past any inliner threshold, and the instance overload
calls it, so a patch there covers both. `FindEnemy` is likewise large
and safe. That is the hook to use if a summon/tame targeting rule is
ever actually needed.

## Placing a creature inside a dungeon interior (read 1.0.15)

An interior is instantiated at its zone's centre plus **5000 on Y**
(`Location.cs:60`), and `Character.InInterior(Vector3)` is exactly
`position.y > 3000f` (`Character.cs:4371-4374`) — that IS vanilla's own
test, and the right one to branch on.

Every world-column query is written for the overworld and behaves badly
up there. What each one actually does:

- `ZoneSystem.IsBlocked(p)` (`ZoneSystem.cs:2728`) — `p.y += 2000f`, then
  raycast **down 10000 m** over "Default", "static_solid",
  "Default_small", "piece". For a point inside an interior that column
  runs from about y=7000 to about y=-3000: it contains the interior's own
  ceiling **and the whole overworld below it**. It answers "blocked" for
  essentially everywhere inside a dungeon. **Do not use it in an
  interior.**
- `ZoneSystem.GetGroundHeight(p)` (`ZoneSystem.cs:2734`) — origin forced
  to **y=6000**, down 10000 m, terrain layer only. For an interior
  position it returns the **overworld** terrain height. This is the trap
  the naive "correct the height" fix falls into.
- `ZoneSystem.GetSolidHeight(p, out h, heightMargin)`
  (`ZoneSystem.cs:2768`) — `p.y += heightMargin`, down **2000 m**. With a
  small margin this is interior-safe: from y≈5000 the ray spans roughly
  5002..3002 and cannot reach the overworld at all. It returns false (not
  a bogus height) when it finds nothing, so "did it find a floor" is a
  usable validity test at any Y.

What happens to a creature that ends up below a dungeon floor:
`Character.UnderWorldCheck` (`Character.cs:883-903`) only rescues a
character once it is below `GetGroundHeight - 1`, i.e. below the
**overworld** terrain — five kilometres from an owner still inside the
dungeon. That is far past `Tameable.m_unsummonDistance`, so
`Tameable.UpdateSummon` (`Tameable.cs:629-637`, a plain 3D
`Vector3.Distance` to the live follow target, checked every frame on the
ZDO's owner) calls `UnSummon` → `RPC_UnSummon` → `ZNetScene.Destroy`
(`Tameable.cs:709-723`). **A summon that leaves a dungeon interior by any
route is destroyed, not merely displaced.**

RossQoL's placement rule (`RossQoL.Core.Portals.PlacementFooting`, applied
everywhere now, not only inside interiors): ask `GetSolidHeight` with a
small margin whether there is floor at the candidate within ~1.5 m of the
player's own height, refuse a candidate whose floor is under the terrain
surface when the player's own position is not, and treat "no floor found"
as unusable so the search falls back to the player's own position. See
`dungeons.md` §7 for why the interior-only version was not enough.

## Spirit Caller staff (`StaffSpiritCaller`) — runtime-verified, 1.0.15

The Deep North blood-magic staff is the same kind of thing as the Dead
Raiser, confirmed by a throwaway runtime diagnostic (a Harmony patch on
`ObjectDB.Awake` that logged the live `ItemDrop`/`Attack` fields and each
summoned prefab's components — none of these are in the DLL; they are
Unity-serialized asset data). Read from a running 1.0.15 client, dev
profile:

- **Item id `StaffSpiritCaller`** is present in `ObjectDB.m_items`, found by
  the same `m_dropPrefab.name` match used for `StaffSkeleton`.
- Its `SharedData.m_attack.m_attackAnimation` (primary) is `staff_summon`,
  the **same** animation the Dead Raiser uses — so filling the blank
  secondary with a clone of the primary and replaying `staff_summon` works
  identically for both staffs.
- Its `SharedData.m_secondaryAttack.m_attackAnimation` is `""` — the same
  blank-secondary "this item has no secondary attack" state
  (`ItemDrop.ItemData.HaveSecondaryAttack`) that `RecallSummonsItemPatch`
  fills for the Dead Raiser.
- The primary attack's `m_attackProjectile` is `staff_SpiritCaller_spawn`
  (a projectile carrying `SpawnAbility`, exactly like the skeleton path).
- The spawn is **random**: over repeated casts it produced
  `Bjorn_spiritcaller`, `Moose_spiritcaller`, `Wolf_spiritcaller` and
  `Boar_spiritcaller`. Each is a full `Character`-family creature with
  `Tameable` (`tameable = true`), `commandable = false` (same as the
  skeleton — the spawn-time `Command` still runs regardless, see the portal
  section above), following its summoner by `ZDOVars.s_follow` = player
  name like any summon.

**Recognition rule (RossQoL).** Because the creature is chosen at random
from a set that could grow, RossQoL matches these summons by the shared
prefab suffix **`_spiritcaller`** (case-insensitive, after stripping Unity's
`(Clone)`), not an explicit four-name list. Skeletons stay matched by their
exact prefab `Skeleton_Friendly`. The staffs themselves are matched by exact
id (`StaffSkeleton`, `StaffSpiritCaller`). All three rules live in and are
tested from `RossQoL.Core.Items.SummonKinds`; the Game-side `SummonedMinion`
recogniser and the three `RecallSummons` patches call into it, so the Dead
Raiser and Spirit Caller cannot drift apart.

## How a multi-creature staff picks its creature (read 1.0.15)

Read while making the Spirit Caller favour creatures you do not have out
(`Items/SpiritCallerVariety`), from `ilspycmd -t SpawnAbility`.

The pick is one line inside the `Spawn()` coroutine (`SpawnAbility.cs:192`
in this dump), per creature raised:

```csharp
int toSpawn = UnityEngine.Random.Range(m_minToSpawn, m_maxToSpawn);   // int overload: max exclusive
...
GameObject prefab = m_spawnPrefab[UnityEngine.Random.Range(0, m_spawnPrefab.Length)];
if (m_maxSpawned > 0 && SpawnSystem.GetNrOfInstances(prefab, targetPosition, 0f) >= m_maxSpawned)
{
    if (m_owner is Player player) player.Message(MessageHud.MessageType.Center, m_maxSummonReached);
    continue;
}
```

- Uniform, memoryless — nothing looks at what is already out.
- `m_maxSpawned` is a **second, per-prefab** limit, separate from
  `Tameable.UnsummonMaxInstances`. `GetNrOfInstances` with range `0` counts
  every loaded `BaseAI` whose object name is `prefab.name + "(Clone)"` —
  **anyone's**, not just the caster's. Its value on the Spirit Caller's
  projectile is serialized asset data (field default `3`), not verified.
- `Setup(owner, …)` sets `m_owner`/`m_weapon` then `StartCoroutine("Spawn")`.
  Unity runs a coroutine synchronously to its first `yield`; with
  `m_initialSpawnDelay == 0` and a non-pathfinding target type there is no
  yield before the pick, so **the prefab is chosen inside `Setup`** — a
  postfix is too late, a prefix is not.
- `Setup` is only ever reached via `GetComponent<IProjectile>()?.Setup(...)`
  from `Attack` (interface dispatch), so it cannot be inlined past a patch.
- The projectile is instantiated per cast and destroyed at the end of
  `Spawn()` (unless `m_spawnOnAwake`), so assigning a new `m_spawnPrefab`
  array on it affects that one cast only.
