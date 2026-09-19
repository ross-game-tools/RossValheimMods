# WearNTear: weathering vs structural decay

What this covers: exactly which conditions in `WearNTear` reduce a
piece's health over time, whether that's owner- or client-driven,
whether it's a genuine world-state (ZDO) change, and how to suppress
only the weather component without touching support-loss collapse or
combat damage. Also covers vanilla's existing `GlobalKeys` coverage.

Produced on 2026-09-18 by decompiling `assembly_valheim.dll` with
ilspycmd 8.2 (`8.2.0.7535-95108c96`). Game version from the assembly:
`Version.CurrentVersion = new GameVersion(1, 0, 14)`. `WearNTear.cs` was
read in full (1430 lines).

## The periodic update

`public void UpdateWear(float time)` is the entry point. It is **not**
self-scheduling inside `WearNTear` — nothing in this file calls
`InvokeRepeating` for it (unlike `Smelter.UpdateSmelter`, which is). It
is driven from outside (not decompiled this session — likely `WearNTear
.Instances`/`s_allInstances` walked elsewhere, e.g. by `Piece` update
logic or a manager). The whole body is gated:

```csharp
public void UpdateWear(float time)
{
    if (!m_nview.IsValid()) return;
    if (m_nview.IsOwner() && ShouldUpdate(time))
    {
        ...all damage accumulation happens only inside this block...
    }
    if ((bool)m_snow && m_nview.IsValid()) { m_snowBuildup = m_nview.GetZDO().GetFloat(ZDOVars.s_snow); }
    UpdateVisual(triggerEffects: true);
}
```

**Everything that can reduce health runs only on `m_nview.IsOwner()`.**
Non-owner clients still run `UpdateVisual`/read `m_snowBuildup` from the
ZDO for rendering, but never compute or apply damage themselves.

## What accumulates into `num` (percent-of-max-health damage this tick)

All of the following add to one local `float num`, inside the
owner-only block, before a single shared write at the end:

1. **Rain/weather** — gated by `m_noRoofWear && !flag(shielded) &&
   GetHealthPercentage() > 0.5f`:
   ```csharp
   m_rainWet = !flag && !m_haveRoof && m_noRoofWear && EnvMan.IsWet();
   if (m_noRoofWear && !flag && GetHealthPercentage() > 0.5f)
   {
       if (IsWet())
       {
           if (m_rainTimer == 0f) { m_rainTimer = time; }
           else if (time - m_rainTimer > 60f) { m_rainTimer = time; num += 5f; }
       }
       else { m_rainTimer = 0f; }
   }
   ```
   Constants: `c_RainDamageTime = 60f` (seconds between ticks),
   `c_RainDamage = 5f` (**percent** of `m_health` per tick — the final
   `damage = num / 100f * m_health` conversion is shared by every
   category below). `c_RainDamageMax = 0.5f` exists as a constant but is
   never referenced in this method — dead/unused in this build, or used
   elsewhere (not verified).
2. **Support loss** (`m_noSupportWear`): `UpdateSupport(); if
   (!HaveSupport()) num = 100f;` — a full-health **overwrite**, not an
   addition, and it can be reduced back down by later categories only
   in the sense that later `+=` still stack on top of 100. This is
   structural decay from lacking support, explicitly **not** weathering
   — the user wants this kept.
3. **Persistent-event damage** (`m_requiredPersistentEvent`,
   e.g. some world event mechanic) — `num += m_eventDamage +
   Random.Range(-m_eventDamageDeviation, m_eventDamageDeviation)`. Not
   weather; a separate, deliberate mechanic.
4. **DeepNorth heavy snow**: gated by biome, `!m_snowDamageImmune ||
   GlobalKeys.AllHeavySnow`, and not `GlobalKeys.NoHeavySnow`; adds
   `Game.instance.m_snowDamage` (value is a `Game` instance field, real
   number is asset/config data) when support color is below
   `m_snowSupportLevel`.
5. **AshLands ambient ash damage**: gated by `Game.instance.m_ashDamage
   > 0f && !flag(shielded) && !m_ashDamageImmune`, roof-checked via a
   **separate** ash-roof flag (`m_haveAshRoof`, see below); ticks every
   5s (`c_AshDamageTime`), and is halved to `0.33x` if `m_ashDamageResist`
   and health is still above 10%.
6. **AshLands lava proximity**: `m_lavaValue > 0.2f && m_groundDist <
   1.5f && !m_ashDamageImmune`; every 2s (`c_LavaDamageTime`),
   `c_LavaDamage = 70f` (shielded: `c_LavaDamageShielded = 30f`),
   further `*0.33` if `m_ashDamageResist`.
7. **Required-biome mismatch**: `num += m_outsideRequiredBiomeDamage`
   if `m_requiredBiome` is set and doesn't match — a design mechanic
   (some Mistlands/event pieces only "belong" in one biome), not
   weathering either.

Then:

```csharp
m_ashDamageTime = (flag2 ? 5 : 0);
if (num > 0f && !CanBeRemoved()) { num = 0f; }
if (num > 0f)
{
    float damage = num / 100f * m_health;
    if (!ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildingFall)) { ApplyDamage(damage); }
}
```

`CanBeRemoved()` delegates to `Piece.CanBeRemoved()` (not decompiled
this session) — a piece that can't be removed also can't take any of
this accumulated damage, of any category.

## `m_noRoofWear` and roof detection

`m_noRoofWear` (default `true`) is the switch for whether rain damage
applies to this piece at all — **not** a distinguishing factor between
material tiers in code; it's a per-`WearNTear`-instance bool, so a
prefab author can turn it off per piece (e.g. an already-weatherproof
object), but nothing in `MaterialType`-branching logic reads it
differently by tier.

Roof state is two **separate** cached bools, each recomputed on its own
schedule inside `UpdateCover(float dt)` (called externally, not
self-scheduled; throttled to once per `c_UpdateCoverFrequency = 4f`
seconds and only recomputed when relevant):

```csharp
public void UpdateCover(float dt)
{
    m_updateCoverTimer += dt;
    if (!(m_updateCoverTimer <= 4f))
    {
        if (EnvMan.IsWet() || m_biome == Heightmap.Biome.DeepNorth) { m_haveRoof = HaveRoof(); }
        if (m_inAshlands) { m_haveAshRoof = HaveAshRoof(); }
        m_updateCoverTimer = 0f;
    }
}
```

`HaveRoof()`/`HaveAshRoof()` both do a `Physics.SphereCastNonAlloc`
straight up from the piece (`HaveRoof` offset by `m_roofCheckOffset`),
looking for any collider on the `piece/Default/static_solid/
Default_small/terrain` layers that isn't this piece's own ancestor and
isn't tagged `"leaky"`, and cache the hit `GameObject` (`m_roof`/
`m_ashroof`) so repeated calls are free until invalidated. **Rain roof
check only runs when `EnvMan.IsWet()` is true** (or in DeepNorth) — it
is not evaluated every tick unconditionally.

## Material tiers: no code-level distinction for weather

`GetMaterialProperties(MaterialType)` (`Wood, Stone, Iron, HardWood,
Marble, Ashstone, Ancient, Ice, Timberwood`) supplies only
**support-related** numbers (`maxSupport`, `minSupport`,
`horizontalLoss`, `verticalLoss`) — e.g. Wood: max 100/min 10, Stone:
max 1000/min 100, Iron: max 1500/min 20, Ashstone: max 2000/min 100,
Ancient: max 5000/min 100. **None of these feed into the rain/snow/ash/
lava/biome damage math** — that path only reads `m_noRoofWear`,
`m_snowDamageImmune`, `m_ashDamageImmune`, `m_ashDamageResist`, all
independent per-instance bools. **Whether vanilla's actual stone/iron
pieces have those bools set differently from wood pieces (e.g. stone
prefabs shipping with `m_noRoofWear = false`) is prefab/asset data, not
verifiable from the DLL** — the field initialiser
`m_noRoofWear = true` in source is only the compile-time default; the
shipped prefab may override it per piece.

## Ownership and ZDO — this is genuine world state

- Gated by `m_nview.IsOwner()` — only the current ZDO owner computes and
  applies weather damage, exactly as with `Smelter`.
- The result is written straight to the ZDO:
  `ApplyDamage(float damage, HitData hitData = null)`:
  ```csharp
  public bool ApplyDamage(float damage, HitData hitData = null)
  {
      float @float = m_nview.GetZDO().GetFloat(ZDOVars.s_health, m_health);
      if (@float <= 0f) return false;
      @float -= damage;
      m_nview.GetZDO().Set(ZDOVars.s_health, @float);   // <-- ZDO write
      if (@float <= 0f) { Destroy(hitData); }
      else { m_nview.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", @float); }
      return true;
  }
  ```
  This is **not a cosmetic client-side value** — it's the piece's real,
  persisted health, broadcast to every other client via
  `RPC_HealthChanged`, and it can destroy the piece
  (`m_nview.GetZDO().Set(ZDOVars.s_health, 0f)` inside `Destroy()`,
  which also drops resources via `m_piece.DropResources`). Suppressing
  it is a genuine world-state change, not a per-viewer cosmetic tweak —
  same "every player needs the mod" caveat as the refinery
  (`eitr-refinery.md`), since ZDO ownership of a given piece can end up
  on any connected client depending on proximity/load.

## Distinguishing weather damage from combat/creature damage in code

`ApplyDamage(float damage, HitData hitData = null)` is called from
**two** places:

- `UpdateWear`'s weather/support/event/biome block: always
  `ApplyDamage(damage)` — **single-argument, `hitData` defaults to
  `null`**.
- `RPC_Damage(long sender, HitData hit)` (creature/player/tool attacks,
  arriving via `WearNTear.Damage(HitData) -> RPC "RPC_Damage"`): always
  `ApplyDamage(totalDamage, hit)` — **`hitData` is never null here**.

So `hitData == null` cleanly separates "this call came from `UpdateWear`"
from "this call came from an actual hit" — **but it does not separate
weather from support-loss**, since both `num += 5f` (rain) and `num =
100f` (no support) funnel into the same single `ApplyDamage(damage)`
call at the bottom of `UpdateWear`. There is no cheap signature-level
way to tell those two apart after the fact — they share one local `num`
and one call site.

## Candidate hook points

1. **Transpiler on `UpdateWear`** removing only the weather-contributing
   `num +=`/`num =` statements (rain block; DeepNorth heavy-snow block;
   AshLands ash block; AshLands lava block — arguably also the
   required-biome block, a design judgement call) while leaving
   `UpdateSupport()` and its `if (!HaveSupport()) num = 100f;` byte code
   untouched. **Most surgical option** — the only one that keeps
   support-loss collapse, event damage, and visuals(`UpdateVisual`,
   snow rendering) fully intact. Trade-off: IL-level transpilers are
   fragile across game updates and require re-verification every patch;
   this file's line numbers alone already say as much.
2. **Prefix on `WearNTear.ApplyDamage(float, HitData)` filtered on
   `hitData == null`.** Simple, signature-based, no inlining risk
   (public method, multiple real call sites, default-parameter methods
   aren't inlined by Mono/JIT the way a one-line getter is). **Cleanly
   preserves all combat/creature damage** (always passes non-null
   `hitData`). **Does not** distinguish weather from support-loss —
   blocking `hidData == null` also blocks the `num = 100f`
   support-collapse destruction the user wants kept. Only acceptable if
   "no support" collapse is reimplemented to go through a different
   path (e.g. `Damage(HitData)`/`RPC_Damage` with a synthetic
   `HitData`), which is itself a nontrivial, riskier change to vanilla
   behaviour — **not recommended** without that extra work.
3. **Do not** patch `GetMaterialProperties` or gate by `MaterialType` —
   confirmed above that material tier has no bearing on weather damage
   in code; a patch keyed on it would silently do nothing.
4. **Do not** patch `WearNTear.Damage(HitData)`/`RPC_Damage` — that is
   exclusively the creature/player/tool attack path; weather never goes
   through it.
5. **Do not** flip `GlobalKeys.NoBuildingFall` — see below, it takes out
   support-loss collapse too.

## Vanilla's existing `GlobalKeys`/world-modifier coverage

`UpdateWear` already checks two global keys directly:

- `GlobalKeys.NoBuildingFall` — guards the **entire** final
  `ApplyDamage(damage)` call, i.e. **all** of rain, snow, ash, lava,
  event, and required-biome damage **and** the support-loss 100%-health
  overwrite, in one flag. **This is not a "no weathering" switch** — it
  is a "buildings never take any of this kind of damage, including
  collapsing from lack of support" switch. Reusing it would satisfy "no
  weathering" but would also disable the support-loss collapse the user
  explicitly wants to keep — so it does not meet the stated requirement
  on its own.
- `GlobalKeys.NoHeavySnow` / `GlobalKeys.AllHeavySnow` — narrower,
  DeepNorth-snow-only toggles, already exposed; don't touch rain/ash/
  lava.

No other `GlobalKeys` member or `WorldModifiers`/`WorldModifierOption`
enum value (`ModifierEnumsExtentions.cs`/`KeySlider.cs`, see
`death-and-respawn.md` §6 for the enum listing) targets weathering
specifically — there is no existing vanilla "rain/ash/lava wear off"
toggle narrower than `NoBuildingFall`. A mod-side setting that isolates
just the weather categories is not reimplementing something vanilla
already exposes; it would be filling a real gap.

## Recommendation

Option 1 (transpiler scoped to the rain/snow/ash/lava `num +=`
statements inside `UpdateWear`) is the only approach that meets the
user's stated requirement — weathering off, structural support-loss
collapse and creature/player damage untouched — without changing
vanilla's `ApplyDamage`/`RPC_Damage` call contracts. It must run on
whichever client ends up owning each piece's ZDO (this is a genuine
ZDO/world-state change, not a cosmetic one), so every player needs the
mod for consistent results, the same constraint already documented for
the Eitr Refinery.

## The grey "weathered" look — driven entirely by health, no separate aging effect

The author's actual ask ("I don't want wood pieces to turn gray") is
about the **visual** state swap, not raw health loss. Traced fully:

`SetHealthVisual(float health, bool triggerEffects)` is the only place
that touches `m_new`/`m_worn`/`m_broken`:

```csharp
private void SetHealthVisual(float health, bool triggerEffects)
{
    if (m_worn == null && m_broken == null && m_new == null) return;
    if (health > 0.75f) { ...; m_new.SetActive(true); }
    else if (health > 0.25f) { if (triggerEffects && !m_worn.activeSelf) m_switchEffect.Create(...); ...; m_worn.SetActive(true); }
    else { if (triggerEffects && !m_broken.activeSelf) m_switchEffect.Create(...); ...; m_broken.SetActive(true); }
    UpdateSnowVisual();
}
```

`m_new`/`m_worn`/`m_broken` are three separate **GameObjects** (whole
mesh/texture variants, swapped via `SetActive`, not a shader parameter)
— exactly the fields the coordinator named. Thresholds are hard-coded:
**>75% health → `m_new`, >25%–75% → `m_worn`, ≤25% → `m_broken`.** Only
two call sites exist in the whole class:

- `UpdateVisual(bool triggerEffects)` → `SetHealthVisual(GetHealthPercentage(), triggerEffects)`,
  called from `Awake()` (once, `triggerEffects: false`) and from the
  tail of `UpdateWear(float time)` (every owner tick, `triggerEffects:
  true`) — i.e. it's driven by the same weather/support/event health
  loss documented above.
- `RPC_HealthChanged(long peer, float health)` → recomputes
  `m_healthPercentage` from the broadcast value and calls
  `SetHealthVisual(health2, triggerEffects: true)` — this is how
  **non-owner clients** get the same worn/broken visual: it arrives via
  RPC off the owner's `ApplyDamage` write, not computed locally.

**No independent aging/weathering mechanism exists in code.** Searched
specifically for a shader-driven or timer-driven material aging effect
and found none:

- `MaterialVariation` (own file): picks **one** random material variant
  per `m_materialIndex` **once**, the first time the ZDO is checked
  (`"MatVar" + index` written once by the owner, read by everyone else
  via RPC) — a fixed cosmetic variety pick at placement time, not
  something that drifts over time or with health.
- `RandomMaterialValues`: seeds a **one-time** random shader vector
  value (`"RandMatSeed"` in the ZDO) per instance at placement, used for
  things like per-piece color/tint jitter — again fixed at spawn, never
  updated afterward.
- `MaterialVariationWorld`: swaps materials based on **dungeon theme or
  biome**, once, on `Update()`'s first real pass, then disables itself
  (`base.enabled = false`) — biome-driven material variety, not
  time/weather-driven.
- `WearNTear`'s only other per-frame material writes are
  `s_AshlandsDamageShaderID` (`_TakingAshlandsDamage`, fed by
  `Max(m_lavaTimer, m_ashDamageTime, m_burnDamageTime)` — an AshLands-
  only heat-shimmer effect, not a grey/wood effect) and `m_wet`
  (`SetActive(m_rainWet)`, a transient just-got-rained-on sheen tied to
  `EnvMan.IsWet()`, not cumulative/persistent). Neither produces the
  permanent grey look being described.
- `Piece.cs` was grepped for `worn`/`gray`/`health` and has no hits at
  all — it defers entirely to `WearNTear` for this.

## Answering the four questions directly

1. **Yes, the grey look is a pure function of health.** `SetHealthVisual`'s
   three thresholds are the whole mechanism; no separate code path
   ages the material.
2. **No separate shader/material weathering effect exists in the
   decompiled code.** The only candidates found (`MaterialVariation`,
   `RandomMaterialValues`, `MaterialVariationWorld`) are all one-time,
   placement/biome-driven, not time-or-weather-driven.
3. **It's the health-driven swap, full stop — not "both contribute."**
   Stopping rain damage (as designed above, via the `UpdateWear`
   transpiler keeping health at/near max) means `GetHealthPercentage()`
   never drops out of the `> 0.75f` band, so `SetHealthVisual` never
   activates `m_worn`/`m_broken`. **The two requirements collapse into
   one feature**: the weather-damage suppression already scoped in the
   previous section is sufficient by itself to stop wood turning grey.
   No additional visual-only patch is needed or exists to make.
4. **Candidate hook points**, now that the goal is understood to be
   "never let health cross the visual thresholds, but still show real
   damage":
   - **Preferred: the `UpdateWear` transpiler already recommended above.**
     It prevents the health drop at the source, so `SetHealthVisual`
     naturally never shows worn/broken from weather while still
     reacting correctly to `RPC_Damage`-sourced combat/troll damage
     (health drop from an actual hit still crosses the same thresholds
     and still shows worn/broken, which is the desired behaviour —
     "genuinely smashed should still look smashed").
   - **Not recommended: patching `SetHealthVisual` or `UpdateVisual`
     directly** (e.g. clamping the `health` argument to `>0.75f`) —
     this would suppress the worn/broken look for **all** health loss,
     including real combat damage, which the user explicitly wants
     preserved. Also `SetHealthVisual` is a plain non-virtual method
     with several branches (not a one-liner), so it is not at inlining
     risk either way, but it's the wrong place to intervene given the
     "still look smashed" requirement.
   - **Not recommended: patching `RPC_HealthChanged`** — same problem,
     it can't distinguish "health dropped because of weather" from
     "health dropped because of a troll" after the fact; the source
     (`UpdateWear` vs `RPC_Damage`) is the only point where that
     distinction still exists.
5. **This is server/world-authoritative state, not a client cosmetic.**
   The visual is driven by `m_healthPercentage`, which is derived from
   the real ZDO `s_health` value (see `ApplyDamage` above) — the same
   value already established to be written to the ZDO and broadcast via
   `RPC_HealthChanged`. There is no independent client-only "grey"
   flag to toggle. Confirms the scope conclusion already reached: since
   suppressing this is really suppressing the weather-damage write
   itself, it is a genuine world-state change requiring the patch on
   whichever client owns the piece — i.e. every player needs the mod,
   not a per-viewer visual toggle.

## Correction / extended search: is there a continuous weathering effect independent of health?

The author (who plays daily, and reports other mods already disable
"the weathering" specifically) says wood visibly greys/ages as a
continuous effect, not only via the health-threshold swap above. That
report is evidence the search below had to explicitly try to falsify,
not dismiss. Re-searched the **full `-p` dump of every type in
`assembly_valheim.dll`** (not just `WearNTear.cs`) for shader/material
names suggestive of ageing or weather —
`_Wear|_Weather|_Age|_Aging|_Grey|_Gray|_Wet|_Snow|_Frost|_Dirt|_Moss|_Fade`
— and for anything driving a value on a building piece's renderer over
time.

Matches, all checked and ruled out:

- `WearNTear.s_AshlandsDamageShaderID = Shader.PropertyToID("_TakingAshlandsDamage")`
  — already covered above; fed by `Max(m_lavaTimer, m_ashDamageTime,
  m_burnDamageTime)`, **AshLands-only**, has nothing to do with wood or
  rain.
- `WearNTear.s_snowLevel = Shader.PropertyToID("_SnowLevel")` — already
  covered; DeepNorth snow buildup only.
- `WearNTearUpdater.s_ashlandsWearTexture =
  Shader.PropertyToID("_AshlandsWearTexture")` — this is the type that
  actually **drives `UpdateWear`/`UpdateCover` for every `WearNTear`
  instance** (answers a "could not verify" item from the first pass):
  ```csharp
  private void UpdateWearNTear(float deltaTime, float time)
  {
      List<WearNTear> allInstances = WearNTear.GetAllInstances();
      if (m_sleepUntilNext.Equals(m_sleepUntil))
      {
          m_sleepUntilNext = time + 1f;
          Shader.SetGlobalTexture(s_ashlandsWearTexture, m_ashlandsWearTexture);
          foreach (WearNTear item in allInstances) { if (item.enabled) item.UpdateCover(deltaTime); }
          foreach (WearNTear item2 in allInstances) { item2.UpdateAshlandsMaterialValues(time); }
          return;
      }
      // paginated: m_updatesPerFrame WearNTear.UpdateWear(time) calls per frame, cycling through allInstances
  }
  ```
  `_AshlandsWearTexture` is a **global** shader texture (one texture for
  the whole scene, set once a second), used for the AshLands ash/scorch
  look — again not wood, not rain, not a per-piece continuous ageing
  value.
- `SE_Wet`, `SE_Frost`, `Smoke.cs`, `EffectFade.cs`, `MaterialFader.cs`,
  `VisEquipment.cs` — all matched only incidentally (character status
  effects, generic fade-in/out for spawned effects, character gear
  rendering); none reference a building piece's renderer or run
  continuously against `WearNTear`/`Piece`.
- `MaterialMan` (read in full) is pure generic infrastructure — a
  per-`GameObject` shader-property-block cache with `SetValue`/
  `ResetValue`/`UpdateBlock`. It has no logic of its own that decides
  *what* to age; it only applies whatever any caller sets. Confirmed
  callers touching a piece's renderer are exactly the ones already
  named above (`WearNTear`'s Ashlands/snow values, `RandomMaterialValues`'s
  one-time seeded vector, `WearNTear.Highlight/ResetHighlight`'s
  selection-highlight color) — no additional caller exists in
  `assembly_valheim`.
- No `GlobalKeys` member relates to piece appearance/weathering — the
  full enum (52 members, quoted from `GlobalKeys.cs`) has world-level,
  rate, death, snow (`NoHeavySnow`/`AllHeavySnow`), boss-defeated, and
  building-collapse (`NoBuildingFall`) entries, nothing about visual
  ageing.
- No placement/creation timestamp feeds a continuous ageing value:
  `Piece.GetCreator()` returns a **player ID** (`ZDOVars.s_creator`),
  not a time; `WearNTear.m_createTime = Time.time` exists only to gate
  `ShouldUpdate`'s 30-second grace window after placement (so a
  freshly-placed, still-loading piece isn't instantly rain-damaged) and
  is never read again after that. No `ZDOVars` entry for a build/place
  timestamp exists (`ZDOVars.cs` checked for `creat`/`build`/`place`
  substrings — only `s_creator`/`s_creatorIndex`/`s_creatorName`, all
  identity, not time).
- `WearNTear` has no `LateUpdate`/`CustomLateUpdate` and does not
  implement `IMonoUpdater` — its only externally-driven entry points
  are `UpdateWear`/`UpdateCover`/`UpdateAshlandsMaterialValues`, all
  called from `WearNTearUpdater` as quoted above, and all already fully
  accounted for.

**Conclusion: no continuous, health-independent wood-ageing effect
exists in `assembly_valheim.dll`.** This search covered that entire
assembly (not just the `WearNTear`/`Piece` files); `assembly_utils.dll`
was not re-dumped for this pass — it holds `Utils`/`ZInput`/`ZLog`/
string-extension helpers per the README, not gameplay `MonoBehaviour`s,
so a wood-ageing effect living there would be surprising, but it is
**not personally ruled out this session**.

This does **not** mean the author is wrong — it means the effect, if
real, is Unity **asset data**: either (a) a shader on the wood
materials themselves that reads a *built-in* Unity/URP time uniform
(e.g. `_Time`) or a **global** value another system sets (fog/weather
volumes, a post-process, a terrain-style "wetness" or "grunge" mask
driven by `EnvMan`/weather state rather than by `WearNTear` at all —
`EnvMan.cs` was not decompiled this session and is exactly where a
global "it's been raining, darken/desaturate everything wood-shaded"
uniform would live), or (b) baked directly into the `_worn` mesh/texture
variant itself so that what reads as "continuous greying" to a player
is actually still the same `SetHealthVisual` swap, just with a `m_worn`
texture that *looks* like weather-grime rather than damage — which
would make the two mechanisms visually indistinguishable in play while
remaining, in code, the single mechanism already documented above.
**The DLL search cannot distinguish these two possibilities**; both are
consistent with everything found.

### What to dump at runtime to settle it

1. On a freshly-placed wood piece at full health, list every
   `Renderer`/`MeshRenderer` under it and every shader property name
   and value on its material(s) and `MaterialPropertyBlock` (a `spawn
   eitrrefinery`-style debug command or a small dump mod using
   `Renderer.sharedMaterial.shader` + `Renderer.GetPropertyBlock`).
2. Leave that same piece, same health (protect it from combat so only
   time/weather passes), for a long exposure (several in-game days,
   through rain), and re-dump the same properties/values, diffing
   against step 1. A changed value with no change in
   `GetHealthPercentage()`/ZDO `s_health` proves a genuine independent
   ageing channel and names exactly which shader property drives it.
3. In parallel, check whether `EnvMan`'s live instance exposes any
   global shader value tied to "time it's been raining on this piece"
   or similar — `Shader.GetGlobalFloat`/`GetGlobalVector` for anything
   set by `EnvMan`/`EnvSetup` (neither decompiled this session).
4. Cross-check against what the "other mods" the author mentioned
   actually patch — their Harmony patch target (visible via
   BepInEx/dnSpy on their DLL) would settle this immediately and is
   likely faster than the runtime dump above.

## Correction: `eitrrefinery` prefab confirmed to exist, in asset data

The author confirms `eitrrefinery` autocompletes in the `spawn` console
command, which means it **is** a prefab registered in `ZNetScene` under
exactly that name at runtime. This is consistent with, not contrary to,
the original finding in `eitr-refinery.md`: the *string* `"eitrrefinery"`
does not appear anywhere in `assembly_valheim.dll`'s IL because prefab
registration is data-driven (`ZNetScene.m_prefabs`, populated from
serialized asset references, not a hard-coded name list in code) — the
prefab and every component wired onto it (including whatever damages
the player) are Unity asset data, not compiled code. This confirms, not
changes, the earlier recommendation: identifying the exact damage
component on `eitrrefinery` requires a runtime/asset dump (e.g. `spawn
eitrrefinery` in a debug world, then `GetComponentsInChildren<Component>()`
on the result), not further decompilation.

## Checked installed mods for a weathering patch: none found

Decompiled every large/plausible mod assembly in the local test profile
(`ilspycmd -p`, same toolchain) and grepped for `WearNTear`,
`SetHealthVisual`, `UpdateWear`, `m_noRoofWear`, `weath`, `grey`/`gray`.
None of them patch, reference, or even mention the weathering/health-
visual mechanism documented above:

- One large multi-module quality-of-life mod: zero `WearNTear` hits of
  any kind anywhere in its ~120 decompiled files. Its only `grey`/`gray`
  matches are unrelated UI tint code (a HUD icon background, a disabled-
  settings-row colour, an inventory slot tint) — nothing touching
  building pieces.
- A server admin-command mod: does reference `WearNTear`, but only for
  an explicit `repair` console command (sets a targeted piece's health
  back to max on demand) and a piece-selector utility (raycast/overlap
  helpers reading `m_colliders`/`m_bounds`) — no automatic or continuous
  suppression of health loss, no patch on `UpdateWear`/`ApplyDamage`/
  `SetHealthVisual`.
- Three further mods reference `WearNTear` only incidentally: minimap/
  radar pin tracking that hooks a piece's `Awake`/`OnDestroy` to place a
  map marker, a build-restriction check reading `CanRemove`, and a
  portal-placement tracker reading `m_createTime`/hooking `OnPlaced`/
  `OnDestroy` for its own bookkeeping. None reduce, freeze, or read
  health for visual purposes.
- The remaining installed mods (a plant-placement helper, an armory/
  display mod, a boat mod, an item-chest mod) contain no `WearNTear`
  string at all in their assemblies.

**No installed mod suppresses or patches the weathering/greying
mechanism.** This is a real, checked negative result, not an omission —
it neither confirms nor refutes the author's report; it means "other
mods disable the weathering" cannot be corroborated by inspecting what's
actually installed on this machine, and the only mod here that touches
`WearNTear` health at all does so via an explicit manual repair command,
which is consistent with (not contrary to) the conclusion above that
health is the only lever that exists to pull. The next step to settle
the author's report, if still open, is the runtime dump already
specified above (renderer/shader property diff over a real weather
exposure), not further static analysis — the DLL and mod-assembly
search space is now exhausted.

## External corroboration: a maintained third-party patch set targets exactly this, and it confirms health-loss prevention, not a visual patch

A maintained third-party Harmony patch collection ships an opt-in option
("removes the weather damage from rain and water erosion") whose
description matches the author's report precisely. Its public patch
source was read (GitHub, patch code only, nothing copied into this
repo) to settle the open question from the previous section: does it
suppress `WearNTear`'s health loss, or does it touch a visual/material
value directly?

**It patches health, not the visual.** Four Harmony patches, all on
`WearNTear`, none touching `SetHealthVisual`/`UpdateVisual`/`m_new`/
`m_worn`/`m_broken`/any shader or material property:

1. A **prefix on `UpdateWear`** that resets the rain-wet accumulation
   timer every call, so the 60-second-interval rain-damage branch this
   doc already documented (§"What accumulates into `num`", item 1)
   never accumulates long enough to add its 5%-of-health tick.
2. A **postfix on `HaveSupport`** that forces the result to `true`,
   which starves the `num = 100f` support-collapse overwrite (item 2 in
   the same section) of its trigger — collapse can never fire once this
   is active. *(Reusing this alone is not what our fix should do — see
   caution below; it takes support-loss with it, which the requirement
   here explicitly wants kept.)*
3. A **prefix on `ApplyDamage`** that returns `false` (skipping the
   original) for player-built structures, short-circuiting the same
   `float, HitData` method this doc already identified as the shared
   write point for every `num` category (§"Distinguishing weather
   damage from combat/creature damage in code"). Because it isn't
   filtered on `hitData == null`, it is even broader than option 2 from
   this doc's own "candidate hook points" list — it would suppress
   combat/creature damage too, not just weather, unless narrowed.
4. A **postfix on `GetMaterialProperties`** that scales down
   `verticalLoss`/`horizontalLoss` by a configurable percentage — a
   softer, partial version of the same support-math lever as #2, not a
   visual change either.

None of these four patch the health-threshold visual swap this doc
already traced to `SetHealthVisual`. That is exactly consistent with,
and independently corroborates, the conclusion already reached above:
**the greying is not a separate effect; it is `SetHealthVisual` reacting
to real health loss, and every third-party approach found — including
this one — reaches "no more greying" purely by keeping health up, never
by touching the swap itself.** No further visual-side mechanism exists
to find.

**What not to copy:**
- The `ApplyDamage` prefix is unfiltered on `hitData` — it is a
  structure-wide damage-immunity switch, not a weather-only one. Ours
  must stay scoped to the `num` categories this doc already isolated
  (rain/snow/ash/lava), the way the transpiler-based option 1 above
  does, or it will silently absorb legitimate combat/creature damage
  the same way.
- The `HaveSupport` postfix is an even blunter instrument: forcing
  `true` unconditionally removes support-loss collapse network-wide
  for as long as the option is on, which this doc's own requirements
  explicitly rule out (the user wants collapse kept). Do not force this
  return value; if support math needs touching at all, prefer the
  `GetMaterialProperties` multiplier approach (item 4) since it scales
  rather than disables.
- Two of the four patches (`UpdateWear` prefix, `ApplyDamage` prefix)
  intervene by skipping/returning early rather than editing accumulated
  values in place — simpler than a transpiler and less likely to break
  across game updates, but only because they act at method-boundary
  granularity; that granularity is exactly why the `ApplyDamage` one
  can't distinguish weather from combat. A transpiler scoped to the
  `num +=`/`num =` statements inside `UpdateWear` (this doc's own
  "candidate hook points" §1) is more surgical but, as already noted
  there, correspondingly more fragile to decompile and re-verify on
  every game update — this external example doesn't change that
  trade-off, it just confirms transpilers are already in active use
  against this same method by others, for a related (heavy-snow/lava)
  purpose.

The patch set targets Valheim versions through the current 1.0.x line
and has been updated alongside recent AshLands-era releases, so its
`WearNTear` member names (`UpdateWear`, `HaveSupport`, `ApplyDamage`,
`GetMaterialProperties`) match this doc's own 1.0.14 decompilation
directly — no renamed-member concern here, unlike patch sets written
against pre-1.0 builds.

## Things I could not verify from the assemblies

1. What actually calls `WearNTear.UpdateWear`/`UpdateCover` and on what
   schedule (not decompiled this session — likely `Piece` or a manager
   walking `WearNTear.GetAllInstances()`).
2. Real numeric values for `Game.instance.m_snowDamage`,
   `m_ashDamage`, `m_snowSupportLevel`, `m_worldLevelPieceHPMultiplier`
   — `Game` instance field values are set on the live `Game` prefab/
   asset, not in the DLL.
3. Whether vanilla's actual wood/stone/iron/ashwood piece prefabs differ
   in `m_noRoofWear`, `m_ashDamageImmune`, or `m_ashDamageResist` — all
   per-instance serialized bools; the C# defaults
   (`m_noRoofWear = true`, others `false`) are compile-time only and
   may be overridden per prefab.
4. Whether `c_RainDamageMax = 0.5f` (declared, unreferenced in
   `UpdateWear`) is used elsewhere in the class or dead code — not
   traced this session.
5. Game version — confirmed the same way as `death-and-respawn.md`:
   `Version.CurrentVersion = new GameVersion(1, 0, 14)` read from the
   assembly, not the install directory's `changelog.txt` (that file is
   BepInEx's).
