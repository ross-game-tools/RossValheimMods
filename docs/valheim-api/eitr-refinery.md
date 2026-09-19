# Eitr Refinery damage

What this covers: what mechanism can hurt a player standing near a
Mistlands Eitr Refinery (`eitrrefinery` prefab), and whether a mod that
suppresses it needs to run on the owner or the victim.

Produced on 2026-09-18 by decompiling `assembly_valheim.dll` with
ilspycmd 8.2 (`8.2.0.7535-95108c96`). Game version from the assembly:
`Version.CurrentVersion = new GameVersion(1, 0, 14)`.

## What is NOT in the DLLs (read this first)

There is **no C# type named `Refinery` or `EitrRefinery`**, and the
string `"eitrrefinery"`/`"refinery"` appears **nowhere** in
`assembly_valheim.dll`. `grep -rli refinery` across a full `-p` dump of
every type returns zero files. The Eitr Refinery is a `Smelter` prefab
(decompiled in full below — 712 lines, no damage code of any kind: no
`Damage(`, no `Aoe`, no status effect) wired up entirely from serialized
Unity prefab data — which GameObjects/components are children of
`eitrrefinery.prefab`, and what values they carry, is not recoverable
from the assemblies. **Which of the two mechanisms below (or both) the
prefab actually uses could not be verified from decompiled code alone**
— it requires either reading the prefab asset (AssetStudio/AssetRipper
against `resources.assets`, not ilspycmd) or observing it at runtime.

What decompiling `Smelter` in full does establish: the refinery's core
loop (`Smelter.UpdateSmelter`, `Awake`, RPCs) carries zero hit-dealing
code. Whatever hurts the player is a **separate component** attached to
the prefab, built from one of two generic, reusable, non-Mistlands-
specific building blocks that exist in the assembly for exactly this
purpose (environmental damage zones):

## Mechanism A — `EffectArea` (trigger volume + status effect)

`EffectArea` (own file) is a `MonoBehaviour` : `IMonoUpdater`, a generic
trigger volume that applies a status effect to characters standing in
it — this is the same building block that makes a campfire keep you
`WarmCozyArea`-flagged, or a `Burning`-flagged floor hurt you. Relevant
in full:

```csharp
private void OnTriggerEnter(Collider other)
{
    m_collisions++;
    if (m_isHeatType || m_statusEffectHash != 0)
    {
        Character component = other.GetComponent<Character>();
        if ((bool)component && component.IsOwner() && (!m_playerOnly || component.IsPlayer())
            && !m_collidedWithCharacter.Contains(component))
        {
            m_collidedWithCharacter.Add(component);
        }
    }
}

public void CustomFixedUpdate(float deltaTime)
{
    if (m_collisions <= 0 || m_collidedWithCharacter.Count == 0 || ZNet.instance == null) return;
    foreach (Character item in m_collidedWithCharacter)
    {
        if (m_statusEffectHash != 0)
        {
            item.GetSEMan().AddStatusEffect(m_statusEffectHash, resetTime: true, 0, 0f, -1);
        }
        if (m_isHeatType) { item.OnNearFire(base.transform.position); }
    }
}
```

The gating condition `component.IsOwner()` in `OnTriggerEnter` means
**only the character's own owning client** ever adds itself to
`m_collidedWithCharacter` — a player always owns their own `Character`,
so this is **the victim's own client tracking and buffing itself**, not
the refinery owner reaching out to hit someone.

If the applied status effect is (or resembles) `SE_Smoke`:

```csharp
public class SE_Smoke : StatusEffect
{
    public HitData.DamageTypes m_damage;
    public float m_damageInterval = 1f;
    public override void UpdateStatusEffect(float dt)
    {
        base.UpdateStatusEffect(dt);
        m_timer += dt;
        if (m_timer > m_damageInterval)
        {
            m_timer = 0f;
            HitData hitData = new HitData { m_point = m_character.GetCenterPoint(), m_damage = m_damage,
                m_hitType = HitData.HitType.Smoke };
            m_character.ApplyDamage(hitData, showDamageText: true, triggerEffects: false);
        }
    }
}
```

`StatusEffect.UpdateStatusEffect` runs as part of `SEMan`'s per-frame
update on the character that owns the status effect — i.e. **the
victim's own client**, calling `Character.ApplyDamage` directly, no RPC.
`m_damage` (a `HitData.DamageTypes`) and `m_damageInterval` are fields
on the `StatusEffect` **ScriptableObject asset** — real numeric values
are prefab/asset data, not in the DLL.

**Conclusion for Mechanism A: 100% client-side on the victim.** The
refinery owner is not involved at all; each nearby player's own client
decides to add the effect and deals the damage to itself.

## Mechanism B — `Aoe` (periodic damage sphere)

`Aoe` (own file, 774 lines) is the general damage-dealing hit-sphere
component. If the refinery instead uses one (e.g. its vent is a
child GameObject carrying an `Aoe`), ownership is different:

```csharp
public void CustomFixedUpdate(float fixedDeltaTime)
{
    if (m_nview != null && !m_nview.IsOwner()) return;   // <-- owner-gated
    ...
    if (m_hitInterval > 0f && !m_useTriggers)
    {
        m_hitTimer -= fixedDeltaTime;
        if (m_hitTimer <= 0f) { m_hitTimer = m_hitInterval; Initiate(); }
    }
    ...
}
```

`Awake()`: `m_nview = GetComponentInParent<ZNetView>();` — an `Aoe`
childed under the refinery's `Smelter`/`Piece` hierarchy inherits the
**refinery's own `ZNetView`**, so `m_nview.IsOwner()` here means the
**refinery's ZDO owner**, not the victim. `Initiate() -> CheckHits() ->
FindHits()` (a `Physics.OverlapSphereNonAlloc`) `-> OnHit() ->
component.Damage(hitData)`, and for a `Character` target,
`Character.Damage` round-trips through an RPC to the target's own
owner to actually apply it (not verified in this pass — out of scope,
`Character.cs` was not decompiled this session). Either way, **the
decision of when/whether to hit is owner-authoritative** here, unlike
Mechanism A.

**Conclusion for Mechanism B: driven by whoever currently owns the
refinery's ZDO**, not the victim.

## Which client a fix belongs on

- If Mechanism A: a client-side patch is sufficient and self-contained
  — the fix only needs to run on the machine of the player who'd
  otherwise take damage, exactly like the existing SoftDeath/status-
  effect patterns already in this repo.
- If Mechanism B: suppressing it client-side-only protects nobody but
  the refinery's current ZDO owner; for it to protect every player it
  must run on whichever client(s) end up owning eitrrefinery ZDOs — in
  practice this means **every player needs the mod** (same
  `EveryoneMustHaveMod` pattern RossQoL already uses), not a genuine
  server simulation.

Given the trigger-volume design is the standard vanilla idiom for
"standing near X hurts you" (used for fire, ash, cold, etc. — see
`wear-and-tear.md` for the same idiom reused for Ashlands/lava wear),
**Mechanism A is the more likely candidate**, but this is inference from
vanilla's usual patterns, not verified against the actual prefab.

## Candidate hook points, in order of preference

1. **Prefix on `EffectArea.CustomFixedUpdate`**, filtering by instance
   (e.g. `base.gameObject.name` or a cached "is this the refinery's
   hazard area" check done once in a postfix on `Awake`). Trade-off:
   `EffectArea` is reused by many hazards (`Burning`, `Heat`,
   `WarmCozyArea`, any other `m_statusEffect` volume) — an unfiltered
   patch on this method is **too broad**; it must gate on the specific
   instance, which in turn requires identifying that instance (parent
   prefab name / `ZNetView` prefab hash), itself something to confirm
   at runtime since it's asset data. **Recommended**, once the specific
   instance can be identified.
2. **Postfix on `EffectArea.Awake` that nulls `m_statusEffectHash` when
   the instance belongs to `eitrrefinery`** (leave `m_isHeatType`/
   `OnNearFire` alone if the refinery is also meant to still radiate
   warmth). This is a one-time state mutation rather than a per-frame
   patch — cheaper and can't be silently bypassed the way patching a
   tiny method can. `Awake` and `CustomFixedUpdate` are both called via
   Unity's message system / an interface-dispatched list
   (`MonoUpdaters`/`Instances`), not direct compiled call sites, so
   Mono inlining is **not** a risk for either.
3. If Mechanism B applies instead: **prefix on `Aoe.Initiate()` or
   `Aoe.CheckHits()`**, filtered the same way by parent prefab identity.
   Same blast-radius caveat — `Aoe` backs a very large fraction of the
   game's combat and hazard effects (arrows, AOE attacks, traps,
   `MovementDamage`'s run-damage object — see `Awake()` in
   `MovementDamage.cs`), so an unfiltered patch is out of the question.
4. **Do not** patch `Character.ApplyDamage`/`Character.Damage`
   generically and filter by `HitData.HitType` or proximity — both are
   central chokepoints for all damage in the game (player combat,
   fall damage, drowning, everything); a bug in a proximity/hit-type
   filter here has a much larger blast radius than either component-
   level option above.
5. **Do not** try to patch `Smelter` itself — confirmed above to carry
   no damage logic; there is nothing to intercept there.

## Blast radius if patched unfiltered

- `EffectArea`: shared by every vanilla "standing near X" hazard/buff
  (fire warmth, `Burning` floor tiles, ash zones, etc.) — see its
  `[Flags] enum Type { Heat, Fire, PlayerBase, Burning, Teleport,
  NoMonsters, WarmCozyArea, PrivateProperty }`.
- `Aoe`: shared by essentially all AOE damage in the game — enemy AOE
  attacks, thrown/lobbed weapons, traps (`Trap.cs`), siege machines
  (`SiegeMachine.cs`), shield generators, and the sprint self-damage
  hook in `MovementDamage.cs`.

Both mean **any suppression must be scoped to the specific refinery
instance**, never applied to the type globally.

## Second pass: ruling out a literal `Projectile`/`IProjectile` mechanism

A published mod exists that specifically targets this ("removes damaging
projectiles from [the refinery] and [an Eitr-related item]" per its own
listing page). **It ships no source repository** (no linked GitHub, no
`website_url` in its Thunderstore package metadata) — nothing beyond the
one-line description was available to read, so what follows is this
repo's own decompilation, prompted by that description's wording.

Its name implies an actual spawned `GameObject` carrying an `IProjectile`
(`Projectile.cs`), not a trigger-volume aura. A second decompilation pass
went looking for exactly that, specifically for anything that spawns a
physically-simulated (gravity/velocity/raycast) child object the way a
thrown/shot `Projectile` would, but isn't part of player combat:

- **`CinderSpawner`/`Cinder`** (own files) are the only such generic,
  reusable "spit out a physically-simulated ember" building block in the
  assembly (used by campfires/bonfires and Ashlands ambient fire spread —
  gated by `CanSpawnCinder`'s `GlobalKeys.Fire`/`Heightmap.Biome.AshLands`
  check). `Cinder` is a real flying object (gravity, wind drift, a
  `Physics.Raycast` each `FixedUpdate`, matching the raymask that includes
  `character`/`character_net`) — but its `OnHit` **never calls
  `Damage(`, `ApplyDamage`, or constructs a `HitData`**. It only checks
  `CanBurn()` (true only for `WearNTear.m_burnable`, `TreeBase`, or
  `TreeLog` targets) to decide whether to instantiate a fire prefab at the
  hit point; a `Character` collider fails every `CanBurn` branch, so a
  `Cinder` that hits a player does nothing but play a hit VFX. **Ruled
  out** as a direct damage source, confirmed by full decompilation.
- `Turret.cs` (`Hoverable`/`Interactable`/`IPieceMarker`) is the
  player-built Ashlands defense turret, unrelated to a passive structure
  like the refinery. Ruled out by inspection of its header (ammo/aiming
  fields, ownable/craftable piece surface).
- No other type in `assembly_valheim.dll` implements `IProjectile` or
  spawns a raycast/velocity-simulated child object outside player/creature
  combat (`Attack.cs`) and the two ruled out above.

**Conclusion of the second pass:** nothing in the compiled code
contradicts the original finding — Mechanisms A (`EffectArea`+
`SE_Smoke`) and B (`Aoe`) below remain the only damage-capable code paths
that exist in the assembly for a stationary hazard. If `eitrrefinery`
truly carries a literal `Projectile` component (as the mod's own name
claims), it is wired up entirely from serialized prefab data with no
supporting C# on the refinery side — same limitation as before, and still
only resolvable by an asset-level or runtime dump (see below).

## Ownership, generalized across every spawner-style component checked

Every damage-*capable* or hazard-*spawning* component this repo has now
decompiled that is parented under a structure's own `ZNetView` — `Aoe`
(`m_nview = GetComponentInParent<ZNetView>()`), `CinderSpawner`
(`m_nview = GetComponentInParent<ZNetView>()`) — gates its spawn/hit
decision behind **that parent's ZDO ownership**, not the potential
victim's. A `Projectile` instance itself owns a *separate* `ZNetView`
(`m_nview = GetComponent<ZNetView>()`, not `GetComponentInParent`), but
Unity/ZDO convention gives a freshly-`Instantiate`d networked object to
the client that created it — and every spawn call site found (`Aoe.
Initiate`, `CinderSpawner.SpawnCinder`) only runs after its own
`m_nview.IsOwner()` check passes. So **if** the refinery spawns real
`Projectile` objects, the spawning client is the refinery's ZDO owner,
and that owner's client also then drives the projectile's flight/hit
logic (`Projectile.FixedUpdate` is itself gated the same way:
`if (!m_nview.IsOwner()) return;`). This matches Mechanism B's
conclusion, not Mechanism A's: **the refinery's owner, not the victim,
would be the one deciding whether/when the hit lands**, for any
mechanism built the way every other environmental hazard in this
assembly is built.

## Things I could not verify from the assemblies

1. Which of Mechanism A or B (or something else entirely) the
   `eitrrefinery` prefab actually uses — this is prefab wiring, not in
   `assembly_valheim.dll`. Needs an asset-level tool (AssetRipper/
   AssetStudio against the `resources.assets`/bundle files) or runtime
   inspection (e.g. dump `GetComponentsInChildren<Component>()` on the
   live prefab via a debug mod).
2. The exact status effect name/hash, `m_damage` values, damage type
   (fire vs poison vs a Mistlands-specific type), interval, and radius
   — all serialized asset fields, not compiled into the DLL.
3. `Character.Damage(HitData)`'s exact RPC path for Mechanism B was not
   decompiled this session (out of scope) — needed to confirm whether
   an `Aoe`-driven hit is applied via RPC to the victim's owner or some
   other path.
4. Whether the refinery's hazard is presented as a `Mistlands`-only
   status effect asset, and whether wearing an item (e.g. a mask) is
   coded via `Character.m_tolerateSmoke`-style flag or an entirely
   different asset-defined immunity — `m_tolerateSmoke` exists on
   `Character` and gates `SE_Smoke.CanAdd`, but whether the refinery's
   actual effect reuses `SE_Smoke` or is a distinct status effect
   asset is unverified (see point 1).
5. Whether `eitrrefinery.prefab` carries a literal `Projectile`
   component anywhere in its hierarchy at all — a mod's public listing
   (no source published) describes the hazard as "damaging projectiles",
   but nothing in `assembly_valheim.dll` wires such a component to a
   `Smelter`, and the one generic "spawn a simulated ember" building
   block that exists (`CinderSpawner`/`Cinder`) is confirmed incapable of
   damaging a `Character` at all. A runtime
   `GetComponentsInChildren<Component>(true)` dump on the live
   `eitrrefinery` instance — printing type name + `gameObject.name` for
   every component, including inactive ones — is the cheapest way to
   settle this: look for `Projectile`, `Aoe`, or `EffectArea` in the
   list, and if `EffectArea`, read its `m_statusEffectHash`/
   `m_isHeatType`/`m_playerOnly` field values directly off the instance.
