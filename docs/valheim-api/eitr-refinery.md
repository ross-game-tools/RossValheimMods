# Eitr Refinery damage

What this covers: what mechanism hurts a player standing near a
Mistlands Eitr Refinery (`eitrrefinery` prefab), and whether a fix
belongs on the victim's client or the refinery's owner.

**Resolved 2026-09-18, game version 1.0.14** (`Version.CurrentVersion =
new GameVersion(1, 0, 14)`), by a runtime dump of the live
`eitrrefinery` prefab hierarchy (`ZNetScene.GetPrefab("eitrrefinery")`)
followed by decompiling `assembly_valheim.dll` with ilspycmd 8.2
(`8.2.0.7535-95108c96`). This supersedes every earlier theory in this
file's history (`EffectArea`+`SE_Smoke`, a generic `Aoe`, and a
speculative literal-`Projectile` guess prompted by another mod's
listing text) — all of that was written without having seen the actual
prefab hierarchy. The runtime dump settles it directly:

- Root: `Piece`, `ZNetView`, `Smelter`, `WearNTear`, `LODGroup`.
- `_enabled/Radiator (2)` and `_enabled/Radiator (3)` — each a
  `Radiator` + `BoxCollider`. `_enabled` is the subtree the refinery
  activates while running.
- The only `EffectArea` on the prefab is on `PlayerBase`
  (`type=PlayerBase`, `isHeatType=False`, `playerOnly=False`, no status
  effect, `statusEffectHash=0`) — the player-base marker, not a hazard.
  **`EffectArea` is ruled out.**
- No `Aoe` component anywhere on the prefab. **`Aoe` is ruled out** as
  a *standing* hazard on the refinery itself (see below for how `Aoe`
  still enters the picture, once removed, via the spawned projectile).
- `Smelter` itself carries **no damage code of any kind** — confirmed
  by full decompilation of `Smelter.cs` (712 lines: `UpdateSmelter`,
  `Awake`, RPCs). Its effect lists (`m_oreAddedEffects`,
  `m_fuelAddedEffects`, `m_produceEffects`) are cosmetic VFX/SFX only.

## What `Radiator` actually does

Full decompiled source (`Radiator.cs`, `assembly_valheim.dll`):

```csharp
public class Radiator : MonoBehaviour
{
    public GameObject m_projectile;
    public Collider m_emitFrom;
    public float m_rateMin = 2f;
    public float m_rateMax = 5f;
    public float m_velocity = 10f;
    public float m_offset = 0.1f;
    private ZNetView m_nview;

    private void Start()
    {
        m_nview = GetComponentInParent<ZNetView>();
    }

    private void OnEnable()
    {
        StartCoroutine("UpdateLoop");
    }

    private IEnumerator UpdateLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(m_rateMin, m_rateMax));
            if (m_nview.IsValid() && m_nview.IsOwner())
            {
                Vector3 onUnitSphere = Random.onUnitSphere;
                Vector3 position = base.transform.position;
                if (onUnitSphere.y < 0f)
                {
                    onUnitSphere.y = 0f - onUnitSphere.y;
                }
                if ((bool)m_emitFrom)
                {
                    position = m_emitFrom.ClosestPoint(m_emitFrom.transform.position + onUnitSphere * 1000f) + onUnitSphere * m_offset;
                }
                Object.Instantiate(m_projectile, position, Quaternion.LookRotation(onUnitSphere, Vector3.up))
                    .GetComponent<Projectile>().Setup(null, onUnitSphere * m_velocity, 0f, null, null, null);
            }
        }
    }
}
```

**Radiator does not damage anyone directly.** It has no `Damage(`,
`ApplyDamage`, or status-effect call anywhere in its own code. It is a
periodic *spawner*: on a random interval between `m_rateMin` and
`m_rateMax` seconds, it instantiates a real, literal `m_projectile`
prefab (a `GameObject` carrying its own `Projectile` component — this
confirms the earlier speculative "damaging projectile" theory, this
time from decompiled code, not a mod's marketing text) at a random
point on the unit sphere around itself (or on the surface of
`m_emitFrom`, offset by `m_offset`), fired outward at `m_velocity`
units/sec via `Projectile.Setup(owner: null, velocity, ...)`. The
projectile then flies, ray-casts each `FixedUpdate`
(`Projectile.FixedUpdate`), and on hitting a `Character` calls
`destructible.Damage(hitData)` (`Projectile.OnHit`, or `Projectile.DoAOE`
if the spawned projectile's own `m_aoe > 0`) using the **projectile
prefab's own** `m_damage`/`m_hitType`/`m_aoe`/etc. fields — none of
which live on `Radiator`.

So the actual damage-dealing code lives in `Projectile.cs`, decompiled
in full this session (901 lines). `Radiator` is purely the trigger that
decides *when* and *in what direction* to fire one.

## 1. Direct damage vs. status effect

Neither, directly — see above. It's the spawned `Projectile` instance
that deals damage, via `Character.Damage(hitData)`/`IDestructible.Damage`,
constructed in `Projectile.OnHit`/`Projectile.DoAOE`. `hitData.m_hitType`
falls back to `HitData.HitType.EnemyHit` when `m_owner` isn't a `Player`
(`Setup` is called with `owner: null`, so `m_owner` is null — every
Eitr Refinery hit is typed `EnemyHit`, not e.g. `Smoke` or `Fire`; the
concrete `HitData.DamageTypes` numbers are the spawned projectile
prefab's own asset data, not in the DLL — see "What configures it"
below).

## 2. Who applies it — ownership

`Radiator.Start()`: `m_nview = GetComponentInParent<ZNetView>();` — the
**refinery's own `ZNetView`** (walking up from `_enabled/Radiator (2)`
through `_enabled` to the prefab root, which carries the `ZNetView`),
not the victim's. The coroutine only fires
`if (m_nview.IsValid() && m_nview.IsOwner())` — so **only the client
that currently owns the refinery's ZDO** ever spawns a projectile.

The spawned `Projectile` then gets its *own*, separate `ZNetView`
(`Projectile.Awake`: `m_nview = GetComponent<ZNetView>();`, not
`GetComponentInParent`). Per Unity/ZDO convention a freshly
`Instantiate`d networked object belongs to whichever client created it
— the refinery's owner. `Projectile.FixedUpdate` gates its own flight
and hit-detection the same way: `if (!m_nview.IsOwner()) return;`. So
the refinery's owner client also flies the projectile and decides when
it hits, calling `Damage(hitData)` directly (no RPC for the hit
decision itself — `RPC_OnHit` only exists to stop the emitter VFX and
is invoked by the hitting client after the fact).

**Conclusion: owner-authoritative, not victim-authoritative.** A
client-side-only suppression protects nobody but whichever player
currently owns the refinery's ZDO. To protect every player, the fix
must run on whichever client(s) end up owning `eitrrefinery` ZDOs — in
practice, every player needs the mod (the same `EveryoneMustHaveMod`
pattern RossQoL already uses), not a genuine server simulation. **This
is `FeatureScope.Synced`, not `FeatureScope.Client`.**

## 3. What configures it — fields and where they live

All six fields are `public` on `Radiator`, settable per-prefab from the
Unity inspector:

| Field | Code default | Meaning |
|---|---|---|
| `m_projectile` | `null` | Projectile prefab to spawn. Asset reference — **must** be set per-instance in the prefab, since a null value would throw on `Instantiate`. |
| `m_emitFrom` | `null` | Optional collider to emit from the surface of, instead of this transform's origin. Asset reference. |
| `m_rateMin` | `2f` | Minimum seconds between spawns. Code default; may be overridden. |
| `m_rateMax` | `5f` | Maximum seconds between spawns. Code default; may be overridden. |
| `m_velocity` | `10f` | Launch speed, units/sec. Code default; may be overridden. |
| `m_offset` | `0.1f` | Distance to offset the spawn point along the emit normal. Code default; may be overridden. |

The refinery has **two** `Radiator` instances (`Radiator (2)` and
`Radiator (3)`), which strongly implies at least `m_emitFrom` (and
possibly `m_projectile`) differ between them (e.g. two separate vents).
None of `Radiator`'s own field *values* as shipped on the refinery are
recoverable from the DLL — field initializers above are compile-time
defaults only. **The real damage numbers are one more step removed**:
they live on the `m_projectile` prefab's own `Projectile` component
(`m_damage`, `m_aoe`, `m_hitType`, `m_dodgeable`, `m_blockable`, etc.),
which is itself asset data.

**To settle the real numbers, extend the runtime dump** to, for each of
the two `Radiator` instances, print: `m_rateMin`, `m_rateMax`,
`m_velocity`, `m_offset`, `m_emitFrom` (name/null), and
`m_projectile.name`; then run the same dump against
`ZNetScene.instance.GetPrefab(radiator.m_projectile.name)` and print
that prefab's `Projectile` component fields (`m_damage`, `m_aoe`,
`m_hitType`, `m_hitMidFlight`, `m_dodgeable`, `m_blockable`,
`m_ttl`, `m_gravity`, `m_drag`) — that fully resolves damage amount,
type, and blast radius per hit.

## 4. Update path

`OnEnable()` calls `StartCoroutine("UpdateLoop")` — a Unity coroutine
(compiler-generated state machine `IEnumerator`), not `OnTriggerStay`,
not a raw `FixedUpdate`/`Update` tick, and not `IMonoUpdater`. It reruns
every time the `_enabled` subtree is toggled active (the refinery
turning on), since `OnEnable` fires on each activation.

This is **not** a tiny-method-inlining risk: `OnEnable` and `Start` are
Unity message methods dispatched through Unity's reflection-based
message system, not called from a direct compiled call site the JIT
could inline away — the same reasoning already established for
`EffectArea.Awake`/`CustomFixedUpdate` in this repo's other notes. The
coroutine body itself (`UpdateLoop`) is a poor Harmony target (a
compiler-generated `MoveNext` state machine is awkward to patch
reliably), but there's no need to touch it — see hook points below.

## 5. What else uses `Radiator`

A full per-type decompile of `assembly_valheim.dll` (`ilspycmd -p`,
~690 files) was grepped for `Radiator`: **the only file referencing the
type is `Radiator.cs` itself.** No other compiled type constructs,
casts to, or calls into `Radiator` — every other place it's attached to
a prefab is pure Unity asset wiring, invisible to the decompiler, same
limitation noted elsewhere in this repo's docs for prefab composition.

So **the code confirms `Radiator` is a small, generic, reusable
building block with no built-in restriction to Mistlands or to the
refinery** — but which other prefabs carry one cannot be determined
from the DLL. **To settle the blast radius, extend the runtime dump**
to iterate `ZNetScene.instance.m_namedPrefabs` (or
`GetPrefab`/`m_prefabs`), call
`GetComponentsInChildren<Radiator>(true)` on each, and list every
prefab that has one. That list is cheap to get and removes all
guesswork about which other structures (furnaces, kilns, natural
hazards) would be affected by an unfiltered patch.

## 6. Candidate hook points, in order of preference

Goal: suppress only the refinery's two `Radiator` instances — not their
spawned projectiles' cosmetic effects if any, and definitely not the
steam/light/sound or the refinery's smelting function, none of which
route through `Radiator` at all (confirmed above: `Smelter` has no
damage code, and the only `EffectArea` on the prefab is the harmless
`PlayerBase` marker).

1. **Prefix on `Radiator.OnEnable`, filtered by walking up to the
   owning `Piece`/`ZNetView` and checking its prefab name is
   `"eitrrefinery"`, returning `false` to skip `StartCoroutine`
   entirely.** Cheapest, most targeted: the refinery simply never
   starts firing projectiles from those two components. Doesn't touch
   `Smelter`, `WearNTear`, or any VFX/SFX. Re-evaluates every time
   `_enabled` toggles active, so it stays correct if the subtree is
   toggled off and on. **Recommended**, pending the blast-radius dump
   in point 5 to confirm no other in-use prefab also needs `Radiator`
   left alone at the type level (this patch is already instance-
   filtered, so it's safe regardless, but the dump confirms nothing was
   missed).
2. **Postfix on `Radiator.Start` that sets `this.enabled = false` for
   the refinery's instances** — works only if `Start` still runs before
   the *first* `OnEnable` a given session; per Unity's callback order
   `OnEnable` fires before `Start` on initial activation, so this
   **does not reliably block the first firing** and is not recommended
   over option 1.
3. **One-time destroy**: postfix on `Piece.Awake` (or wherever the
   refinery's `ZNetView`/`Piece` first initializes) that finds and
   `Object.Destroy`s the two `Radiator` components on `eitrrefinery`
   instances at spawn time, before `_enabled` is ever toggled active.
   Slightly more invasive (permanent, not togglable at runtime without
   re-spawning the piece) but avoids any per-activation patch
   surface entirely. Reasonable alternative to option 1 if the mod
   wants this to be unconditional rather than config-gated.
4. **Do not** patch `Projectile.OnHit`/`Projectile.DoAOE`/
   `Character.Damage` generically and filter by projectile prefab name
   or `HitData.HitType` — `Projectile` backs essentially all ranged
   damage in the game (arrows, thrown weapons, enemy ranged attacks,
   every other `Radiator`-driven hazard if any exist per point 5); an
   unfiltered or loosely-filtered patch here has a much larger blast
   radius than gating on `Radiator.OnEnable` by parent prefab name.

## Ownership pattern, generalized

Every spawner-style hazard component this repo has now decompiled that
sits under a structure's own `ZNetView` — `Radiator`
(`GetComponentInParent<ZNetView>()`), and by the same pattern documented
elsewhere for `Aoe` and `CinderSpawner` — gates its spawn decision on
**that parent structure's ZDO ownership**, never the potential victim's.
A spawned `Projectile` then owns a *separate* `ZNetView`
(`GetComponent<ZNetView>()`, not `GetComponentInParent`), but Unity/ZDO
convention hands a freshly instantiated networked object to its creator
— here, the refinery's owner — and that same client also drives the
projectile's flight and hit resolution. This is a consistent vanilla
idiom: **structure-owned hazards are owner-authoritative**, which is
why point 2's conclusion (`FeatureScope.Synced`) follows directly from
the code, not from inference.

## Things I could not verify from the assemblies

1. The real field values on the refinery's two live `Radiator`
   instances (`m_rateMin`/`m_rateMax`/`m_velocity`/`m_offset`/
   `m_emitFrom`/`m_projectile`) — asset data, not in the DLL. See the
   dump extension proposed in section 3.
2. The `m_projectile` prefab's own `Projectile` component fields
   (`m_damage`, `m_aoe`, `m_hitType`, etc.) — the actual damage amount,
   type, and hit radius. Asset data. See the dump extension proposed in
   section 3.
3. Every other prefab in the game that also carries a `Radiator`
   component — pure Unity wiring, invisible to the decompiler. See the
   dump extension proposed in section 5.
