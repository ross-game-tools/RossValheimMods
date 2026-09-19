# Death, respawn, status effects, food, skills, HUD projection

What this covers: the player death and respawn flow, the vanilla
post-death buff, the tombstone, status effect construction and
application, programmatic food, skill loss on death, world-to-screen HUD
projection, and per-character/per-world persistence.

Produced on 2026-09-18 by decompiling
`valheim_Data/Managed/assembly_valheim.dll` and
`assembly_utils.dll` with ilspycmd 8.2 (`8.2.0.7535-95108c96`). Game
version read from the assembly itself:
`Version.CurrentVersion = new GameVersion(1, 0, 14)`. The `changelog.txt`
at the Valheim install root is BepInEx's, not the game's, and is not a
version source.

Everything quoted below is real decompiled code. Line numbers refer to
the per-type decompilation (`ilspycmd -p`), so they move between game
versions; the member names and signatures are the durable part.

## 1. Death + respawn flow

**`Player.OnDeath()` — `public override void OnDeath()`** (Player.cs:3313).
Owner-only, runs on the dying client:

```csharp
public override void OnDeath()
{
    if (!m_nview.IsOwner()) { Debug.Log("OnDeath call but not the owner"); return; }
    bool flag = HardDeath();
    m_nview.GetZDO().Set(ZDOVars.s_dead, value: true);
    m_nview.InvokeRPC(ZNetView.Everybody, "OnDeath");
    Game.instance.IncrementPlayerStat(PlayerStatType.Deaths);
    ... // big switch on m_lastHit.m_hitType for DeathBy* stats
    Game.instance.GetPlayerProfile().SetDeathPoint(base.transform.position);
    CreateDeathEffects();
    CreateTombStone();
    m_foods.Clear();
    if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.DeathSkillsReset)) { m_skills.Clear(); }
    else if (flag) { m_skills.OnDeath(); }
    m_seman.RemoveAllStatusEffects();
    Game.instance.RequestRespawn(10f, afterDeath: true);
    m_timeSinceDeath = 0f;
    if (!flag) { Message(MessageHud.MessageType.TopLeft, "$msg_softdeath"); }
    Message(MessageHud.MessageType.Center, "$msg_youdied");
    ShowTutorial("death");
    Minimap.instance.AddPin(base.transform.position, Minimap.PinType.Death,
        $"$hud_mapday {EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds())}", save: true, isChecked: false, 0L);
    if (m_onDeath != null) { m_onDeath(); }
    ...
}
```

Related members:

- `public GameObject m_tombstone;` (Player.cs:193) — public serialized
  prefab field.
- `public void CreateTombStone()` (Player.cs:3279): honours
  `GlobalKeys.DeathKeepInventory / DeathKeepEquip / DeathDeleteItems /
  DeathDeleteUnequipped`, then
  `Instantiate(m_tombstone, GetCenterPoint(), transform.rotation)`,
  `GetComponent<Container>().GetInventory().MoveInventoryToGrave(m_inventory)`,
  and `component.Setup(playerProfile.GetName(), playerProfile.GetPlayerID())`.
- `private bool HardDeath() => m_timeSinceDeath > m_hardDeathCooldown;`
  with `public float m_hardDeathCooldown = 10f;` (Player.cs:183) and
  `private float m_timeSinceDeath = 999999f;` (Player.cs:560), advanced
  by real seconds in `private void UpdateStats(float dt)`
  (Player.cs:2089): `m_timeSinceDeath += dt;` (skipped while
  `InIntro() || IsTeleporting()`). Also `public void ClearHardDeath()`
  sets it to `m_hardDeathCooldown + 1f`.
- `public void OnRespawn()` (Player.cs:3451)
  `{ m_nview.GetZDO().Set(ZDOVars.s_dead, value: false); SetHealth(GetMaxHealth()); }`
  — **it has zero callers anywhere in assembly_valheim** (grep for
  `.OnRespawn(` = no matches). Do not hook it.
- `public void OnSpawned(bool spawnValkyrie)` (Player.cs:2492) — runs for
  **every** spawn (login and death-respawn alike); it only plays spawn
  effects, optionally spawns the Valkyrie intro, and reads/writes the
  `invrows` unique key. It carries no "was this a death" information.

**`Game`** (all the respawn state is `private`):

```csharp
private PlayerProfile m_playerProfile;      // Game.cs:82
private bool m_requestRespawn;              // :84
private bool m_respawnAfterDeath;           // :86
private bool m_haveSpawned;                 // :96
private bool m_firstSpawn = true;           // :98
public static Game instance { get; private set; }   // :232
public static event Action m_playerInitialSpawn;    // :237

public void RequestRespawn(float delay, bool afterDeath = false)   // :618
{
    m_respawnAfterDeath = afterDeath;
    CancelInvoke("_RequestRespawn");
    Invoke("_RequestRespawn", delay);
}

private void _RequestRespawn()   // :625
{
    ZLog.Log("Starting respawn");
    if ((bool)Player.m_localPlayer) { m_playerProfile.SavePlayerData(Player.m_localPlayer); }
    if ((bool)Player.m_localPlayer) { ZNetScene.instance.Destroy(Player.m_localPlayer.gameObject); ZNet.instance.SetCharacterID(ZDOID.None); }
    m_respawnWait = 0f; m_requestRespawn = true; MusicMan.instance.TriggerMusic("respawn");
}

private void UpdateRespawn(float dt)   // :731, called from FixedUpdate
{
    if (!m_requestRespawn || !FindSpawnPoint(out var point, out var usedLogoutPoint, dt)) return;
    if (!usedLogoutPoint) { m_playerProfile.SetHomePoint(point); }
    SpawnPlayer(point, m_playerProfile.m_firstSpawn && m_inIntro);
    EnvMan.instance.ForceInstantEnvironmentSwitch();
    m_playerProfile.m_firstSpawn = false;
    m_inIntro = false;
    m_requestRespawn = false;
    if (m_firstSpawn) { m_firstSpawn = false; Chat.instance.SendText(...); ... Game.m_playerInitialSpawn?.Invoke(); }
    instance.CollectResourcesCheck();
}

private Player SpawnPlayer(Vector3 spawnPoint, bool spawnValkyrie)   // :486
{
    Player component = Object.Instantiate(m_playerPrefab, spawnPoint, Quaternion.identity).GetComponent<Player>();
    component.SetLocalPlayer();
    m_playerProfile.LoadPlayerData(component);
    ZNet.instance.SetCharacterID(component.GetZDOID());
    component.OnSpawned(spawnValkyrie);
    ...
}
```

Login path: `FixedUpdate` →
`if (!m_haveSpawned && connected) { m_haveSpawned = true; RequestRespawn(0f); }`
(Game.cs:687-691), i.e. `afterDeath: false`.

`m_respawnAfterDeath` is read only in `FindSpawnPoint` (Game.cs:539:
`if (!m_respawnAfterDeath && m_playerProfile.HaveLogoutPoint())`). **It is
never reset to false after a death-respawn** — it stays true until the
next `RequestRespawn(...)` without the flag. So reading it in a postfix on
`SpawnPlayer`/`OnSpawned` is correct at that instant, but don't treat it
as a persistent "died recently" flag.

**Where to hook for "only on a death-respawn"** — there is no single
vanilla flag. Two reliable options:

1. Set your own flag in a `Postfix` on `Player.OnDeath()` (guard
   `__instance == Player.m_localPlayer`), then consume it in a `Postfix`
   on `Player.OnSpawned(bool)` (the newly spawned local player).
   `Player.OnDeath` → `Game.RequestRespawn(10f, true)` → `_RequestRespawn`
   → `UpdateRespawn` → `SpawnPlayer` → `OnSpawned` happens in-process,
   same session, so a static bool survives it. It does **not** survive a
   logout between death and respawn (`_RequestRespawn` saves the profile;
   a quit-and-rejoin would spawn with the flag clear) — persist it in
   `Player.m_customData` (section 8) if you need that.
2. Read `Game.instance`'s private `m_respawnAfterDeath` via
   reflection/AccessTools inside a `Postfix` on `Game.SpawnPlayer` or
   `Player.OnSpawned`. `m_respawnAfterDeath` is in-memory only and resets
   to `false` on a fresh `Game`, so this does not cover the logout case
   either — option 1 plus customData is the robust choice.

`Player.OnDeath` is `public override`, non-inlined (large), and safe to
patch. `Player.OnSpawned(bool)` is `public` and small but not trivially
inlinable (it calls several methods); if paranoid, patch
`Game.SpawnPlayer` (private, returns `Player`) instead.

## 2. The vanilla post-death buff

**There is no `CorpseRun` symbol anywhere in `assembly_valheim.dll`** —
case-insensitive grep for `corpserun` over all 692 decompiled types: no
matches. (The only `Corpse` hit is `Corpse.cs`, an unrelated
MonoBehaviour that despawns looted creature corpses.)

The vanilla post-death buff is **`SoftDeath`**, referenced by hash:

```csharp
// SEMan.cs:24
public static readonly int s_statusEffectSoftDeath = "SoftDeath".GetStableHashCode();
```

It is applied in `Player.UpdateStats(float dt)` (Player.cs:2167-2170),
*not* in `OnDeath` (OnDeath calls `m_seman.RemoveAllStatusEffects()`):

```csharp
if (!HardDeath() && m_seman.GetStatusEffect(SEMan.s_statusEffectSoftDeath) == null)
{
    m_seman.AddStatusEffect(SEMan.s_statusEffectSoftDeath, resetTime: false, 0, 0f, -1);
}
```

So: after `OnDeath` sets `m_timeSinceDeath = 0f`, `HardDeath()` is false
until `m_timeSinceDeath > m_hardDeathCooldown` (code default `10f`
seconds — see the caveat below), during which SoftDeath is kept
re-applied, and a second death in that window skips `m_skills.OnDeath()`
and shows `$msg_softdeath`.

**Verified from a live world (2026-09-18, game version 1.0.14).** The
SoftDeath asset's class and every field are now known — it is a plain
`StatusEffect`, **not `SE_Stats` and not a subclass of it**, so it
carries no stats at all: no stamina regen, no drain modifiers, no
damage modifiers, nothing. It is purely a HUD indicator tile — the
tile you see after dying does nothing but tell you that you died
recently:

```
type=StatusEffect            (not SE_Stats, not a subclass)
m_ttl=600
m_name="$se_softdeath_name"  m_tooltip="$se_softdeath_tooltip"
m_icon != null = True   m_hidden=False   m_cooldownIcon=False
m_flashIcon=False       m_category=""
```

Captured with a temporary diagnostic
(`RossQoL.Game.Death.BuffDiagnostic`, since deleted) that fetched
SoftDeath via `ObjectDB.GetStatusEffect(SEMan.s_statusEffectSoftDeath)`
on `ObjectDB.CopyOtherDB` and logged its concrete type and every common
`StatusEffect` field.

Likewise `m_hardDeathCooldown = 10f` and `Skills.m_DeathLowerFactor =
0.25f` are *code* defaults for serialized fields — the shipped Player
prefab / Skills component may carry different inspector values. Read
them at runtime (`Player.m_localPlayer.m_hardDeathCooldown`) rather
than trusting these numbers; `SoftDeath`'s own values above are now
confirmed, so no runtime read is needed for it.

**`SE_Stats` real field names** (SE_Stats.cs,
`[CreateAssetMenu(menuName = "StatusEffects/SE_Stats")] public class SE_Stats : StatusEffect`)
— all `public float` unless noted. The three commonly assumed ones are
all real:

- `m_staminaRegenMultiplier = 1f` (:80), applied in
  `ModifyStaminaRegen(ref float staminaRegen)`:
  `if (m_staminaRegenMultiplier > 1f) staminaRegen += m_staminaRegenMultiplier - 1f; else staminaRegen *= m_staminaRegenMultiplier;`
- `m_runStaminaDrainModifier` (:40) →
  `ModifyRunStaminaDrain(float baseDrain, ref float drain, Vector3 dir)`:
  `drain += baseDrain * m_runStaminaDrainModifier;`
- `m_jumpStaminaUseModifier` (:42) → `ModifyJumpStaminaUsage`:
  `staminaUse += baseStaminaUse * m_jumpStaminaUseModifier;`

Other useful ones: `m_healthRegenMultiplier`, `m_eitrRegenMultiplier`,
`m_staminaDrainPerSec`, `m_runStaminaUseModifier`,
`m_attackStaminaUseModifier`, `m_blockStaminaUseModifier`,
`m_blockStaminaUseFlatValue`, `m_dodgeStaminaUseModifier`,
`m_swimStaminaUseModifier`, `m_sneakStaminaUseModifier`,
`m_homeItemStaminaUseModifier`, `m_speedModifier`, `m_swimSpeedModifier`,
`Vector3 m_jumpModifier`, `m_addMaxCarryWeight`, `m_addArmor`,
`m_armorMultiplier`, `m_damageModifier = 1f`,
`HitData.DamageTypes m_percentigeDamageModifiers` (sic),
`m_noiseModifier`, `m_stealthModifier`, `m_fallDamageModifier`,
`m_maxMaxFallSpeed`, `m_healthUpFront`/`m_staminaUpFront`/`m_eitrUpFront`,
`m_healthOverTime` (+`Duration`/`Interval`), `m_staminaOverTime`
(+`Duration`, `m_staminaOverTimeIsFraction`), `m_eitrOverTime`
(+`Duration`), `m_tickInterval`/`m_healthPerTick`/
`m_healthPerTickMinHealthPercentage`/`HitData.HitType m_hitType`,
`Skills.SkillType m_raiseSkill` + `m_raiseSkillModifier`,
`m_skillLevel`/`m_skillLevelModifier` (+`2` variants),
`List<HitData.DamageModPair> m_mods`, `m_windMovementModifier`,
`m_windRunStaminaModifier`, `m_adrenalineUpFront`/`m_adrenalineModifier`,
`m_staggerModifier`, `m_timedBlockBonus`.

**Adding / obtaining status effects** (SEMan.cs, all `public`):

```csharp
public StatusEffect AddStatusEffect(int nameHash, bool resetTime = false, int itemLevel = 0, float skillLevel = 0f, short variant = -1);
public StatusEffect AddStatusEffect(StatusEffect statusEffect, bool resetTime = false, int itemLevel = 0, float skillLevel = 0f, short variant = -1);
public bool RemoveStatusEffect(StatusEffect se, bool quiet = false);
public bool RemoveStatusEffect(int nameHash, bool quiet = false);
public void RemoveAllStatusEffects(bool quiet = false);
public bool HaveStatusEffect(int nameHash);           // HashSet lookup
public StatusEffect GetStatusEffect(int nameHash);
public List<StatusEffect> GetStatusEffects();
public void GetHUDStatusEffects(List<StatusEffect> effects);
```

Key semantics:

- The **hash overload requires ObjectDB**: `Internal_AddStatusEffect` does
  `StatusEffect statusEffect2 = ObjectDB.instance.GetStatusEffect(nameHash); if (statusEffect2 == null) return null;`.
  It is also network-routed: if `!m_nview.IsOwner()` it sends
  `RPC_AddStatusEffect` and returns `null`.
- The **instance overload does not touch ObjectDB** — it calls
  `statusEffect.CanAdd(m_character)`, then
  `StatusEffect statusEffect3 = statusEffect.Clone(); m_statusEffects.Add(...); statusEffect3.Setup(m_character); statusEffect3.SetLevel(itemLevel, skillLevel);`
  and returns the **clone**. It is purely local (no RPC). This is the
  overload to use for a runtime-created SE.
- Both overloads return `null` when the effect is *already present* (they
  only `ResetTime()` it).
- `ObjectDB`: `public static ObjectDB instance => m_instance;`,
  `public List<StatusEffect> m_StatusEffects` (public, so a custom SE can
  be registered into it if the hash overload / network path is needed),
  `public StatusEffect GetStatusEffect(int nameHash)` — linear scan
  comparing `statusEffect.NameHash()`; **there is no by-string overload**.
- `StatusEffect.NameHash()` is `public int NameHash()` and hashes
  **`base.name`** (the UnityEngine.Object name), cached in
  `private int m_nameHash`:

```csharp
public int NameHash() { if (m_nameHash == 0) m_nameHash = base.name.GetStableHashCode(); return m_nameHash; }
```

  So a custom SE **must** have its `.name` set
  (`so.name = "RossCorpseRun"`). `GetStableHashCode` is
  `public static int GetStableHashCode(this string str)` in
  `StringExtensionMethods` (assembly_utils).

## 3. TombStone

`public class TombStone : MonoBehaviour, Hoverable, Interactable`
(TombStone.cs). Fields:

```csharp
private static float m_updateDt = 2f;
public string m_text = "$piece_tombstone";
public GameObject m_floater;
public TMP_Text m_worldText;
public float m_spawnUpVel = 5f;
public StatusEffect m_lootStatusEffect;          // buff granted to the owner when the grave empties
public EffectList m_removeEffect = new EffectList();
public float m_hoverOffset;
private Container m_container;
private ZNetView m_nview;
private Floating m_floating;
private Rigidbody m_body;
private bool m_localOpened;
```

There is **no `m_pickupDelay`**. Ownership/identity is entirely in the
ZDO, not in fields:

```csharp
public void Setup(string ownerName, long ownerUID)
{
    m_nview.GetZDO().Set(ZDOVars.s_ownerName, ownerName);
    m_nview.GetZDO().Set(ZDOVars.s_owner, ownerUID);
    if ((bool)m_body) m_body.linearVelocity = new Vector3(0f, m_spawnUpVel, 0f);
}
private long GetOwner() => m_nview.IsValid() ? m_nview.GetZDO().GetLong(ZDOVars.s_owner, 0L) : 0L;
private bool IsOwner()                                  // "is this the local player's grave?"
{
    long owner = GetOwner();
    long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
    return owner == playerID;
}
public string GetOwnerName();                            // censored ZDOVars.s_ownerName
private Player FindOwner() => Player.GetPlayer(GetOwner());
```

`Awake()` also stamps `ZDOVars.s_timeOfDeath`
(`ZNet.instance.GetTime().Ticks`) and `ZDOVars.s_spawnPoint` (world pos)
once, on the owner, and starts `InvokeRepeating("UpdateDespawn", 2f, 2f)`.

**Emptied / destroyed detection** — polling, owner-side only:

```csharp
private void UpdateDespawn()
{
    if (!m_nview.IsValid()) return;
    if (m_floater != null) UpdateFloater();
    if (m_nview.IsOwner())
    {
        PositionCheck();
        if (!m_container.IsInUse() && m_container.GetInventory().NrOfItems() <= 0)
        {
            GiveBoost();
            m_removeEffect.Create(transform.position, transform.rotation);
            m_nview.Destroy();
        }
    }
}
private void GiveBoost()
{
    if (!(m_lootStatusEffect == null))
    {
        Player player = FindOwner();
        if ((bool)player) player.GetSEMan().AddStatusEffect(m_lootStatusEffect.NameHash(), resetTime: true, 0, 0f, -1);
    }
}
```

**The buff players call "Corpse Run" is this reward effect, granted
only when the grave is emptied — verified from a live world
(2026-09-18, game version 1.0.14).** Read important: `GiveBoost()` above
runs from `UpdateDespawn()` only once the container is empty, i.e.
*after* you have already reached the grave and looted it. **Nothing
with stats is applied during the run back to a grave** — the only
status effect present on that run is `SoftDeath` (§2), which has no
stats at all. Getting this backwards — assuming the cheap-stamina,
resistance buff applies while running toward the grave — would be
wrong; it applies only on the way back, once the loot is already in
your inventory.

`m_lootStatusEffect` is `SE_Stats` (`m_ttl=50`,
`m_name="$se_corpserun_name"`, `m_tooltip="$se_corpserun_tooltip"`,
icon sprite named `CorpseRun`), with these non-default fields (14
differed of 86 public `SE_Stats` fields checked against a fresh
default instance):

```
m_runStaminaDrainModifier = -0.75
m_jumpStaminaUseModifier  = -0.75
m_addMaxCarryWeight       = 150
m_tickInterval            = 1
m_healthPerTick           = 5
m_healthOverTimeInterval  = 0        (default 5)
m_startMessageType        = Center   (default TopLeft)
m_mods (HitData.DamageModPair) count=3:  Blunt=Resistant, Slash=Resistant, Pierce=Resistant
m_damageModifier = 1 (default, i.e. not itself scaled)
m_armorMultiplier = 0 (unset)   m_addArmor = 0 (unset)
m_percentigeDamageModifiers = (empty)
plus m_icon / m_name / m_tooltip / m_ttl / m_startEffects / m_stopEffects / m_tickEffect set
```

So vanilla grants 50 seconds of 75%-cheaper running and jumping, +150
carry weight, 5 health per second, and physical (Blunt/Slash/Pierce)
damage resistance — but only starting the moment the grave is
emptied, not during the approach.

Captured by the same temporary diagnostic
(`RossQoL.Game.Death.BuffDiagnostic`, since deleted) via an independent
`ZNetScene.Awake` postfix (§10 explains why `ObjectDB.CopyOtherDB`
can't reach `ZNetScene`). **Practical note:**
`ZNetScene.instance.GetPrefab("TombStone")` missed — the tombstone
prefab is not registered under that name. The diagnostic recovered it
by scanning `ZNetScene.instance.m_prefabs` for the first entry with a
`TombStone` component, which succeeded; use the same scan rather than
`GetPrefab("TombStone")` if you need the prefab by component instead
of by name.

There is also a `Container.m_onTakeAllSuccess` hook combined in `Awake` →
`OnTakeAllSuccess()` (pickup effect + `$piece_tombstone_recovered`
message), and `Interact` auto-loots everything via
`m_container.TakeAll(character)` when `IsOwner()` and
`EasyFitInInventory(player)`.

ZDOID: the tombstone's own is `m_nview.GetZDO().m_uid`
(`GetComponent<ZNetView>().GetZDO()`); `ZDOVars` keys used are
`s_timeOfDeath` ("timeOfDeath"), `s_ownerName` ("ownerName"), `s_owner`,
`s_spawnPoint` ("spawnpoint"), `s_inWater`.

**Death map pin — yes, vanilla adds one.** `Minimap.PinType`
(Minimap.cs:24):

```csharp
public enum PinType { Icon0, Icon1, Icon2, Icon3, Death, Bed, Icon4, Shout, None, Boss, Player, RandomEvent, Ping, EventArea, Hildir1, Hildir2, Hildir3, Memorial }
```

Added directly in `Player.OnDeath()` (Player.cs:3442, quoted in section
1) with `save: true`. It is a normal user pin: **it is not removed when
the tombstone is looted** — nothing removes it except the player deleting
it on the map. It is also *excluded from persistence* (Minimap.cs:2697 and
2707, in the shared-map save):

```csharp
foreach (PinData pin in m_pins) { if (pin.m_save && pin.m_type != PinType.Death) num++; }
```

so death pins vanish on relog. Separately there is a legacy auto-pin path
that is **disabled**: `private const bool m_enableLastDeathAutoPin = false;`
(Minimap.cs:377), and `UpdateProfilePins()` (Minimap.cs:1325) calls
`playerProfile.HaveDeathPoint();` and then unconditionally removes
`m_deathPin` (dead code left in).

Pin API:
`public PinData AddPin(Vector3 pos, PinType type, string name, bool save, bool isChecked, long ownerID = 0L, PlatformUserID author = default)`
(Minimap.cs:2320) and `public void RemovePin(PinData pin)`
(Minimap.cs:2262). `public static Minimap instance => s_instance;`.

## 4. Status effects / SE_Rested / custom runtime SEs

**`public class SE_Rested : SE_Stats`** (SE_Rested.cs):

```csharp
[Header("__SE_Rested__")] public float m_baseTTL = 300f;
public float m_TTLPerComfortLevel = 60f;
private const float c_ComfortRadius = 10f;

public override void Setup(Character character) { base.Setup(character); UpdateTTL(); ... "$se_rested_start ..." }
public override void ResetTime() { UpdateTTL(); }          // NOTE: does not call base.ResetTime()
private void UpdateTTL()
{
    Player player = m_character as Player;
    float num = m_baseTTL + (float)(player.GetComfortLevel() - 1) * m_TTLPerComfortLevel;
    float num2 = m_ttl - m_time;
    if (num > num2) { m_ttl = num; m_time = 0f; }   // only ever extends, never shortens
}
public static int CalculateComfortLevel(Player player);
public static int CalculateComfortLevel(bool inShelter, Vector3 position);
```

Granted/refreshed via the hash in `Player.SetSleeping(bool sleep)`
(Player.cs:6587):
`m_seman.AddStatusEffect(SEMan.s_statusEffectRested, resetTime: true, 0, 0f, -1);`
(plus `$msg_goodmorning`, `m_wakeupTime`).
`SEMan.s_statusEffectRested = "Rested".GetStableHashCode()`.

Raising the TTL is safe in the sense that `m_ttl`/`m_time` are plain
fields on the **clone** held in `SEMan.m_statusEffects` and `IsDone()` is
just `m_ttl > 0f && m_time > m_ttl`; but `UpdateTTL()` will clobber the
raise on the next `ResetTime()` if the value is below the comfort-derived
one, and `m_time` is `protected` (patch or reflect). The SE instance to
mutate is the clone
(`GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested)`), never the
ObjectDB asset — mutating the asset leaks into the whole session.

**`StatusEffect : ScriptableObject`** (StatusEffect.cs) — the pieces that
matter for a custom SE:

```csharp
public string m_name = "";        // localization token, shown in the HUD tile
public string m_category = "";
public Sprite m_icon;             // REQUIRED for the HUD to show it
public bool m_flashIcon;
public bool m_cooldownIcon;
public bool m_hidden;             // hidden => excluded from HUD
[TextArea] public string m_tooltip = "";
public StatusAttribute m_attributes;
public MessageHud.MessageType m_startMessageType = MessageHud.MessageType.TopLeft;
public string m_startMessage = "";
public MessageHud.MessageType m_stopMessageType = ...;  public string m_stopMessage = "";
public MessageHud.MessageType m_repeatMessageType = ...; public string m_repeatMessage = ""; public float m_repeatInterval;
public float m_ttl;
public bool m_effectsOnlyOnPlayer;
public EffectList m_startEffects, m_stopEffects;
public float m_cooldown;  public string m_activationAnimation = "gpower";
[NonSerialized] public bool m_isNew = true;
public Character m_character;
protected float m_time;
private int m_nameHash;

public StatusEffect Clone() => MemberwiseClone() as StatusEffect;
public virtual bool CanAdd(Character character) => true;
public virtual void Setup(Character character);       // sets m_character, start message, TriggerStartEffects()
public virtual void Stop();                            // NOTE: dereferences m_character for m_stopEffects.Create
public virtual void UpdateStatusEffect(float dt);      // m_time += dt; repeat messages
public virtual bool IsDone() => m_ttl > 0f && m_time > m_ttl;
public virtual void ResetTime() => m_time = 0f;
public virtual void SetLevel(int itemLevel, float skillLevel) {}
public float GetDuration() => m_time;   public float GetRemaningTime() => m_ttl - m_time;
public virtual string GetIconText();     // formatted remaining time when m_ttl > 0
public static string GetTimeString(float time, bool sufix = false, bool alwaysShowMinutes = false);
public int NameHash();
public enum StatusAttribute { None = 0, ColdResistance = 1, DoubleImpactDamage = 2, SailingPower = 4, TamingBoost = 8 }
```

**Constructing and applying a custom SE without ObjectDB** — supported by
the code as written:

```csharp
var se = ScriptableObject.CreateInstance<SE_Stats>();
se.name = "RossCorpseRun";                 // NameHash() hashes base.name; must be unique
se.m_name = "$se_myeffect_name";           // or a literal; Localization.Localize is applied by the HUD
se.m_tooltip = "...";
se.m_icon = <Sprite>;                      // no icon => GetHUDStatusEffects skips it entirely
se.m_ttl = 120f;
se.m_staminaRegenMultiplier = 1.5f;
se.m_runStaminaDrainModifier = -0.5f;
se.m_jumpStaminaUseModifier = -0.5f;
Player.m_localPlayer.GetSEMan().AddStatusEffect(se, resetTime: true);   // instance overload, returns the CLONE
```

Caveats verified in code:

- `SEMan.AddStatusEffect(StatusEffect, ...)` stores a `Clone()`, so keep
  the returned reference to mutate the live instance.
- It is local-only — no RPC, no ZDO, nothing syncs to other clients,
  which suits a client-side feature.
- `SE_Stats.Setup` calls `base.Setup` then `StartupEffects()`
  (`m_healthUpFront`/`m_staminaUpFront`/`m_eitrUpFront`/
  `m_adrenalineUpFront` applied immediately, and again on every
  `ResetTime()`).
- `EffectList m_startEffects`/`m_stopEffects` default to empty instances
  so `TriggerStartEffects()`/`Stop()` are safe.
- `Stop()` dereferences `m_character.transform` — harmless in normal
  flow, but don't stop an SE whose character was destroyed.
- To also make `AddStatusEffect(hash)` and the RPC path work, add the
  instance to `ObjectDB.instance.m_StatusEffects` (a plain
  `public List<StatusEffect>`) — but re-add it after any
  `ObjectDB.CopyOtherDB`, which reassigns `m_StatusEffects`.

**How the HUD picks it up** — `Hud.Update()` (Hud.cs:526-528):

```csharp
m_tempStatusEffects.Clear();
localPlayer.GetSEMan().GetHUDStatusEffects(m_tempStatusEffects);
UpdateStatusEffects(m_tempStatusEffects);
```

and `SEMan.GetHUDStatusEffects`:
`if ((bool)statusEffect.m_icon && !statusEffect.m_hidden) effects.Add(statusEffect);`
— **an SE with no `m_icon` never appears**.
`Hud.UpdateStatusEffects(List<StatusEffect>)` (Hud.cs:1635) instantiates
`m_statusEffectTemplate` under `m_statusEffectListRoot` and fills, per
tile: child `"Icon"` → `Image.sprite = statusEffect.m_icon` (tinted
red-ish flashing if `m_flashIcon`), child `"Cooldown"` →
`SetActive(statusEffect.m_cooldownIcon)`, the first `TMP_Text` in children
→ `Localization.instance.Localize(statusEffect.m_name)`, child
`"TimeText"` → `statusEffect.GetIconText()` (hidden when empty), and an
`Animator` trigger `"flash"` on `m_isNew`. Relevant Hud fields (all
`public`):
`RectTransform m_statusEffectListRoot; RectTransform m_statusEffectTemplate; float m_statusEffectSpacing = 55f; int m_effectsPerRow = 7;`.

## 5. Food

```csharp
public class Player.Food            // nested public class, Player.cs:22
{
    public string m_name = "";      // set from item.m_dropPrefab.name
    public ItemDrop.ItemData m_item;
    public float m_time;            // remaining burn time
    public float m_health, m_stamina, m_eitr;
    public bool CanEatAgain() => m_time < m_item.m_shared.m_foodBurnTime / 2f;
}
private readonly List<Food> m_foods = new List<Food>();   // Player.cs:338 — PRIVATE
public List<Food> GetFoods() => m_foods;                  // Player.cs:2487
public void ClearFood();  public bool RemoveOneFood();

public bool CanEat(ItemDrop.ItemData item, bool showMessages)   // Player.cs:2282
public bool EatFood(ItemDrop.ItemData item)                     // Player.cs:2345
```

`EatFood` behaviour: returns false if `!CanEat(item, showMessages: false)`;
shows a center message built from
`m_shared.m_food`/`m_foodStamina`/`m_foodEitr`; if the same
`m_shared.m_name` is already in `m_foods` it refreshes that slot (only
when `CanEatAgain()`, else returns false); otherwise if
`m_foods.Count < 3` it appends a new `Food`
(`m_name = item.m_dropPrefab.name` — **`m_dropPrefab` must be non-null or
this NREs**); otherwise it overwrites `GetMostDepletedFood()` (that branch
does **not** set `m_eitr`, a vanilla bug). Every success path calls
`Game.instance.IncrementPlayerStat(PlayerStatType.FoodEaten)`,
`IncrementStatFoodEaten(...)`, then `UpdateFood(0f, forceUpdate: true)`
which recomputes and calls
`SetMaxHealth`/`SetMaxStamina`/`SetMaxEitr`.

`EatFood` does **not** consume an inventory item and does **not** apply
`m_shared.m_consumeStatusEffect` — callers do that (ItemDrop.cs:1667-1674,
Feast.cs:97-104).

`EatFood` takes the `ItemData` by reference and stores it in the `Food`
slot, so **pass a clone, not a prefab's shared instance** when
synthesising food. To put a named food in the belly programmatically:

```csharp
GameObject prefab = ObjectDB.instance.GetItemPrefab("CookedMeat");   // public GameObject GetItemPrefab(string name) => GetItemPrefab(name.GetStableHashCode())
ItemDrop.ItemData data = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
data.m_dropPrefab = prefab;                                          // required: EatFood reads m_dropPrefab.name
Player.m_localPlayer.EatFood(data);
```

`ObjectDB` accessors (all public): `GetItemPrefab(string)`,
`GetItemPrefab(int hash)`,
`GetItemPrefab(ItemDrop.ItemData.SharedData)`,
`TryGetItemPrefab(string|int|SharedData, out GameObject)`,
`int GetPrefabHash(GameObject)`. `ItemDrop.ItemData.Clone()` is
`public ItemData Clone()` — `MemberwiseClone` plus a fresh `m_customData`
dictionary (the `m_shared` reference is shared, which is what the game
expects). `public GameObject m_dropPrefab;` is a real public field on
`ItemData` (ItemDrop.cs:450); on a prefab it is set only for instantiated
drops (`m_itemData.m_dropPrefab = itemPrefab;`, ItemDrop.cs:1267), so set
it yourself when cloning off a prefab.

Persistence: `Player.Save` writes `m_foods.Count` then each `food.m_name`
+ `food.m_time`; on load the item is re-resolved from ObjectDB by that
name. A food whose `m_name` isn't a real prefab name will not survive a
save/load.

## 6. Skill loss on death

```csharp
// Skills.cs
public float m_DeathLowerFactor = 0.25f;    // :105, public serialized field on the Skills component
public bool m_useSkillCap;  public float m_totalSkillCap = 600f;

public void OnDeath()                        // :329
{
    LowerAllSkills(m_DeathLowerFactor * Game.m_skillReductionRate);
}

public void LowerAllSkills(float factor)     // :334
{
    foreach (KeyValuePair<SkillType, Skill> skillDatum in m_skillData)
    {
        float num = skillDatum.Value.m_level * factor;
        skillDatum.Value.m_level -= num;
        skillDatum.Value.m_accumulator = 0f;
    }
    m_player.Message(MessageHud.MessageType.TopLeft, "$msg_skills_lowered");
}
public void Clear() => m_skillData.Clear();
public void RaiseSkill(SkillType skillType, float factor = 1f);
public float GetSkillLevel(SkillType);  public float GetSkillFactor(SkillType);
public List<Skill> GetSkillList();  public float GetTotalSkill();
```

`Skills.Skill.Raise` uses `Game.m_skillGainRate`; `Skills.OnDeath` uses
`Game.m_skillReductionRate`. Both are **public static floats on `Game`**:

```csharp
public static float m_skillGainRate = 1f;        // Game.cs:212
public static float m_skillReductionRate = 1f;   // Game.cs:214
```

fed from world modifiers in
`public static void UpdateWorldRates(HashSet<string> globalKeys, Dictionary<string, string> globalKeysValues)`
(Game.cs:1364):

```csharp
trySetScalarKey(GlobalKeys.SkillGainRate, out m_skillGainRate);
trySetScalarKey(GlobalKeys.SkillReductionRate, out m_skillReductionRate);
```

`trySetScalarKey` is a local function reading
`globalKeysValues[key.ToString().ToLower()]` (default `1f`,
`multiplier: 100f` — the stored value is a percentage). So the relevant
**global keys are `"skillreductionrate"` and `"skillgainrate"`**
(lowercased enum names), not `"deathpenaltymultiplier"`.

The world-modifier enums are
`WorldModifiers { Default, Combat, DeathPenalty, Resources, Raids, Portals }`
× `WorldModifierOption { Default, None, Less, MuchLess, More, MuchMore, Casual, VeryEasy, Easy, Hard, VeryHard, Hardcore, Most }`
(ModifierEnumsExtentions.cs / KeySlider.cs); the `DeathPenalty` preset
expands to the underlying `GlobalKeys` — **`DeathPenalty` is a UI preset
name only, it is not itself a runtime key**.

Death-related `GlobalKeys` members (GlobalKeys.cs): `DeathKeepEquip`,
`DeathDeleteItems`, `DeathDeleteUnequipped`, `DeathSkillsReset`,
`DeathKeepInventory`, plus `SkillGainRate`, `SkillReductionRate`.
`Player.OnDeath` checks `DeathSkillsReset` (→ `m_skills.Clear()`)
*before* the `HardDeath()` branch; `CreateTombStone` checks the other
four. `Game.m_worldLevel` exists
(`public static int m_worldLevel = 0`, clamped 0..10 from
`GlobalKeys.WorldLevel`) but has nothing to do with skill loss.

**To multiply vanilla's skill loss by a configurable factor**, the
cleanest patch targets, in order of preference:

1. Prefix on `Skills.LowerAllSkills(float factor)` rewriting `factor`
   (`__0 *= configFactor`). Single parameter, covers `OnDeath` and any
   other caller, and preserves `Game.m_skillReductionRate` and the
   `$msg_skills_lowered` message.
2. Prefix on `Skills.OnDeath()` that calls
   `LowerAllSkills(__instance.m_DeathLowerFactor * Game.m_skillReductionRate * config)`
   and returns `false`. Also fine — `m_DeathLowerFactor` is public.

Do **not** mutate `Game.m_skillReductionRate` itself: it is public static
and shared, and `UpdateWorldRates` overwrites it on every global-key
change. Note `Skills.OnDeath` only runs when `HardDeath()` is true and
`DeathSkillsReset` is unset (Player.cs:3425-3432), so a soft death
legitimately loses nothing.

## 7. HUD / screen projection

**World-to-screen**, the function vanilla actually uses, in
`assembly_utils` `Utils.cs:1352`:

```csharp
public static Vector3 WorldToScreenPointScaled(this Camera camera, Vector3 worldPos)
{
    Vector3 result = camera.WorldToScreenPoint(worldPos);
    result.x *= (float)Screen.width / (float)camera.pixelWidth;
    result.y *= (float)Screen.height / (float)camera.pixelHeight;
    return result;
}
public static Camera GetMainCamera()   // Utils.cs:281 — frame-cached Camera.main
{
    int frameCount = Time.frameCount;
    if (lastFrameCheck == frameCount) return lastMainCamera;
    lastMainCamera = Camera.main; lastFrameCheck = frameCount; return lastMainCamera;
}
```

There is **no `Utils.WorldToScreenPoint`**; use
`Utils.GetMainCamera().WorldToScreenPointScaled(pos)`. Use
`Utils.GetMainCamera()` rather than `Camera.main` (cheaper, and matches
vanilla's behaviour with render-scale).

**`EnemyHud` is the reference implementation** (EnemyHud.cs:160-248).
Fields: `public GameObject m_hudRoot;` plus
`m_baseHud`/`m_baseHudBoss`/`m_baseHudPlayer`/`m_baseHudMount` templates;
`public static EnemyHud instance => m_instance;`. It instantiates
`Object.Instantiate(original, m_hudRoot.transform)` and positions each
marker in `LateUpdate`-driven `UpdateHuds`:

```csharp
Camera mainCamera = Utils.GetMainCamera();
if (!mainCamera) return;
...
Vector3 zero = value.m_character.IsPlayer() ? (value.m_character.GetHeadPoint() + Vector3.up * 0.3f) : value.m_character.GetTopPoint();
Vector3 position = mainCamera.WorldToScreenPointScaled(zero);
if (position.x < 0f || position.x > (float)Screen.width || position.y < 0f || position.y > (float)Screen.height || position.z > 0f)
{ value.m_gui.transform.position = position; value.m_gui.SetActive(value: true); }
else { value.m_gui.SetActive(value: false); }
```

The marker is set via `transform.position` in **screen space** — these HUD
canvases are Screen Space Overlay — and the visibility test above is the
vanilla-inverted one. `Chat`/`NpcDialogueText` use the more conventional
`screenPos.z < 0f` form plus a `ClampToScreenEdge(screenPos, rt, ...)`
helper when the point is off-screen; that off-screen clamp pattern (in
the single-file decompile around `assembly_valheim.decompiled.cs:41868-41886`)
is the one to copy for a directional off-screen marker.

**Parents for a full-screen overlay element**:
`public GameObject m_rootObject;` on `Hud` (Hud.cs:28) is the whole HUD
root — `Hud.SetVisible` moves it to `s_notVisiblePosition` when hidden, so
parenting to `Hud.instance.m_rootObject.transform` gets free hide/show
with the HUD. `EnemyHud.instance.m_hudRoot.transform` is the sibling used
for world-anchored markers and is toggled by
`m_hudRoot.SetActive(!Hud.IsUserHidden())` — the better parent for a
world-projected marker, since it is already the screen-space container
vanilla positions markers inside. Other public `Hud` RectTransforms usable
as anchors: `m_statusEffectListRoot`, `m_healthBarRoot`, `m_healthPanel`,
`m_foodBarRoot`, `m_pieceListRoot`. `public static Hud instance => m_instance;`.

**TMP text template to clone**: `Minimap.m_biomeNameSmall` is
`public TMP_Text m_biomeNameSmall;` (Minimap.cs:162) — the one the Clock
feature clones; its GameObject also has an `Animator` with a `"pulse"`
trigger (used at Minimap.cs:2669). Alternatives: `Hud.m_buildSelection` /
`Hud.m_pieceDescription` (`public TMP_Text`), `Hud.m_healthText`,
`Hud.m_foodTime[]`, or `Minimap.m_biomeNameLarge`.

For map-space (not screen-space) markers, the pin machinery is
`public RectTransform m_pinRootSmall; public RectTransform m_pinRootLarge;`
with `private void WorldToMapPoint(Vector3 p, out float mx, out float my)`
(Minimap.cs:1792) and
`private Vector2 MapPointToLocalGuiPos(float mx, float my, Rect uvRect, Rect transformRect)`
(Minimap.cs:1753) — both **private**, plus
`private bool IsPointVisible(Vector3 p, RawImage map)`; the public entry
points are `AddPin`/`RemovePin`.

## 8. `Player.m_customData`, player ID, current world

```csharp
[HideInInspector]
public Dictionary<string, string> m_customData = new Dictionary<string, string>();   // Player.cs:580-581
```

Saved in `Player.Save(ZPackage pkg)` right after the skills block
(Player.cs:4742):

```csharp
m_skills.Save(pkg);
pkg.Write(m_customData.Count);
foreach (KeyValuePair<string, string> customDatum in m_customData) { pkg.Write(customDatum.Key); pkg.Write(customDatum.Value); }
pkg.Write(GetStamina()); pkg.Write(GetMaxEitr()); pkg.Write(GetEitr());
```

Loaded in `Player.Load(ZPackage pkg)` under
`if (playerData >= Version.PlayerData.EitrStamina)` (Player.cs:4962-4968):
reads count, then `m_customData[key3] = value3;` — **entries are merged,
never cleared**, so stale keys from a previous session survive.

The write path is: `PlayerProfile.SavePlayerData(Player player)` →
`player.Save(zPackage); m_playerData = zPackage.GetArray();` →
`PlayerProfile.SavePlayerToDisk()` writes `m_playerData` as a byte array
into the `.fch` character file. It is called from
`Game._RequestRespawn()` (so **it is saved on every death**, before the
player object is destroyed), from `Game.UpdateSaving`/`SavePlayerProfile`,
and on logout. Read path: `PlayerProfile.LoadPlayerData(Player player)` →
`player.SetPlayerID(m_playerID, GetName()); player.Load(pkg);` called from
`Game.SpawnPlayer`.

**Size concerns:** it is `ZPackage.Write(string)` per key and value
(length-prefixed UTF-8) inside the single character blob, and the whole
`.fch` is rewritten on every save; there is no per-entry or
per-dictionary cap in code. The only size guard anywhere near it is
cloud-quota related:
`FileHelpers.OperationExceedsCloudCapacity(m_playerProfile.m_fileSource, 1048576uL, 1, ...)`
(Game.cs:460) — **1 MiB** for the character file on cloud saves;
exceeding it silently demotes the profile to `FileSource.Local`. Keep
custom data to a handful of short keys. Note also
`PlayerProfile.m_playerData` is a `byte[]` snapshot: changes to
`m_customData` made after the last `SavePlayerData` are lost.

A parallel, already-persisted-and-parsed store also exists and is what
vanilla itself uses for per-character settings: the unique keys,
`private readonly HashSet<string> m_uniques` (Player.cs:310), saved and
loaded as a plain string set (Player.cs:4709, 4846-4851), with
`"key value"` space-separated semantics:

```csharp
public override bool HaveUniqueKey(string name) => m_uniques.Contains(name);
public bool HaveUniqueKeyValue(string key, string value);
public bool TryGetUniqueKeyValue(string key, out string value);
public bool RemoveUniqueKeyValue(string key);
public void AddUniqueKeyValue(string key, string value);   // remove-then-add "key value"
public override void AddUniqueKey(string name);            // also triggers ZoneSystem.UpdateWorldRates() + UpdateEvents()
public List<string> GetUniqueKeys();  public void ResetUniqueKeys();
```

(`OnSpawned` uses it for `"invrows"`.) Caveat: keys and values are
lower-cased on lookup and **must not contain spaces**, and `AddUniqueKey`
kicks `ZoneSystem.UpdateWorldRates()` every call — `m_customData` is the
quieter choice for mod state.

**Player ID / name**:

```csharp
public void SetPlayerID(long playerID, string name)   // Player.cs:743 — only sets if current ID == 0
public long GetPlayerID()                             // Player.cs:752
{ if (!m_nview.IsValid()) return 0L; return m_nview.GetZDO().GetLong(ZDOVars.s_playerID, 0L); }
public string GetPlayerName();                        // ZDOVars.s_playerName, default "..."
```

and the authoritative profile-side value
`public long PlayerProfile.GetPlayerID() => m_playerID;` /
`public string GetName() => m_playerName;`, reached via
`Game.instance.GetPlayerProfile()`. `TombStone.IsOwner()` compares against
the *profile* ID, so use `Game.instance.GetPlayerProfile().GetPlayerID()`
when matching graves.

**Current world**:

```csharp
// ZNet.cs
private static World m_world = null;      // :236
public static World World => m_world;     // :324  (public static property)
public long GetWorldUID() => m_world.m_uid;          // :2077  — NOT null-guarded
public string GetWorldName()                          // :2082
{ if (m_world != null) return m_world.m_name; return null; }
public World GetWorld();                              // :2091
```

`World.m_uid` (long) is the key `PlayerProfile` itself uses to partition
per-world data (`GetWorldData(ZNet.instance.GetWorldUID())` behind
`SetDeathPoint`/`HaveDeathPoint`/`GetDeathPoint`/`SetLogoutPoint`/
`SetCustomSpawnPoint`/`SetMapData`/...), so **`ZNet.instance.GetWorldUID()`
is the right world identity for keying saved state**, with
`GetWorldName()` (nullable) for display only. `World.m_seedName` and
`m_startingGlobalKeys` are also public.
`WorldGenerator.Initialize(m_world)` is how the generator gets it.

Per-world death point, already maintained by vanilla and set inside
`Player.OnDeath`
(`Game.instance.GetPlayerProfile().SetDeathPoint(transform.position)`):

```csharp
public void SetDeathPoint(Vector3 point);   public bool HaveDeathPoint();   public Vector3 GetDeathPoint();
```

These persist in the `.fch` per world UID
(`WorldPlayerData.m_haveDeathPoint` / `m_deathPoint`, written at
PlayerProfile.cs:330-331, read at :528-532 behind
`Version.Player.DeathPoint`) — a ready-made, logout-surviving "where did I
die in this world" source that can be read on spawn, at no maintenance
cost.

## Things I could not verify from the assemblies

1. ~~The `SoftDeath` status effect asset itself~~ — **verified, see §2**:
   it is a plain `StatusEffect` (not `SE_Stats`), `m_ttl=600`, has an
   icon, and carries no stats. ~~The tombstone's `m_lootStatusEffect`~~ —
   **verified, see §3**: `SE_Stats`, `m_ttl=50`, the "Corpse Run" reward
   granted only on emptying the grave, full field list given there.
   `Rested` and `Encumbered` remain unverified the same way; inspect at
   runtime via `ObjectDB.instance.GetStatusEffect(...)`.
2. **Serialized-field defaults vs shipped values** —
   `Player.m_hardDeathCooldown = 10f`, `Player.m_tombstone`,
   `Skills.m_DeathLowerFactor = 0.25f`, `SE_Rested.m_baseTTL = 300f` /
   `m_TTLPerComfortLevel = 60f`, `Hud.m_statusEffectSpacing = 55f` /
   `m_effectsPerRow = 7` are C# initializers; Unity's serialized
   prefab/asset values override them at load. Read them from the live
   instance.
3. **HUD/Minimap prefab hierarchies** — only the child names the code
   looks up by string are confirmed: status tile children `"Icon"`,
   `"Cooldown"`, `"TimeText"` (plus a TMP_Text and an Animator with
   trigger `"flash"`); enemy-hud children `"Health/health_fast"`,
   `"Health/health_slow"`, `"Health/health_fast_friendly"`,
   `"Stamina/stamina_fast"`, `"Stamina/StaminaText"`,
   `"Health/HealthText"`, `"level_2"`, `"level_3"`, `"Alerted"`,
   `"Aware"`, `"Name"`; map pin child `"Checked"`. Anything else about
   those prefabs (canvas settings, anchors, sprite assets) needs runtime
   inspection.
4. **`"deathpenaltymultiplier"`** — no such string or key exists anywhere
   in `assembly_valheim`. The real runtime knob is
   `GlobalKeys.SkillReductionRate` → `"skillreductionrate"` →
   `Game.m_skillReductionRate`; `WorldModifiers.DeathPenalty` is only a
   menu preset label.
5. **Game version** — `Version.CurrentVersion = new GameVersion(1, 0, 14)`;
   the `changelog.txt` at the Valheim install root is BepInEx's, not the
   game's, so the version was confirmed from the assembly instead.

## 9. `CompatMember` and nested types / extension methods — a real bug, not a hypothetical

These are two instances of one family: a `CompatMember` entry that names
a type `AccessTools.TypeByName` can never resolve to the member's actual
declaring type, so `ValheimCompat.FindMissing` reports the member
"missing" forever. Check for both when adding or auditing a
`CompatMember`.

`ItemDrop.ItemData` is a type **nested inside** `ItemDrop`. Its
`Type.FullName` is `ItemDrop+ItemData` (a `+`, not a `.`), same as any
nested type in .NET. A `CompatMember` (or anywhere else feeding
`AccessTools.TypeByName`, which `ValheimCompat.FindMissing` uses to
resolve the `Type` string) written as `"ItemDrop.ItemData"` resolves to
**nothing** — `TypeByName` matches on `FullName`/`Name`, and the dotted
form is neither. Verified directly against the HarmonyX 2.7.0 assembly
this project references:
`AccessTools.TypeByName("System.Environment.SpecialFolder")` → `null`;
`AccessTools.TypeByName("System.Environment+SpecialFolder")` → resolves.

The failure mode is silent and easy to miss: the feature reports the
member "missing" on every startup (indistinguishable in the log from an
actual Valheim rename), `FeatureRules.ShouldPatch` returns false, the
feature never patches, and nothing about a clean build or the Core test
suite catches it — `RequiredMembers` strings are never compiled against
the real types.

**Rule: any nested Valheim type named in a `CompatMember` must use `+`,
not `.`** — e.g. `"ItemDrop+ItemData"`, not `"ItemDrop.ItemData"`. This bit
`RespawnFoodFeature`'s `RequiredMembers` for `ItemDrop.ItemData.Clone` and
`ItemDrop.ItemData.m_dropPrefab` in Task 7; both were fixed to
`ItemDrop+ItemData`.

### 9a. Extension methods must be named against the class that DEFINES them

An extension method only *reads* as though it belongs to the type it
extends; it is not a member of that type. `Camera.WorldToScreenPointScaled`
looks like it belongs to `Camera`, but per §7 it is declared
`public static Vector3 WorldToScreenPointScaled(this Camera camera, ...)`
in Valheim's `Utils` class (`assembly_utils`). `GraveMarkerFeature`'s
`RequiredMembers` named it as `new CompatMember("Camera",
"WorldToScreenPointScaled", ...)` — `AccessTools.TypeByName("Camera")`
resolves fine, but `Camera` has no such method, so `FindMissing` reports
it missing on every single startup and `GraveMarker`, the Death category's
centrepiece, disabled itself every time, blaming a Valheim update that
never happened. Verified against `ValheimCompat.FindMissing`'s own
resolution path (`RossQoL/src/RossQoL.Game/Framework/ValheimCompat.cs`):
`AccessTools.TypeByName("Utils")` resolves the type, and the member walk
(`HasMember`, which tries field/property/event then
`GetMethod(name, AccessTools.all)`) finds `WorldToScreenPointScaled` as an
ordinary static method once the type named is `Utils`, the one that
defines it — fixed to `new CompatMember("Utils", "WorldToScreenPointScaled",
...)`. The other extension method this file records, §280,
`StringExtensionMethods.GetStableHashCode`, is not used as a `CompatMember`
anywhere in this repo as of this writing, but the same rule applies to it:
name it against `StringExtensionMethods`, never `string`.

**Rule: a `CompatMember` for an extension method must name the class that
defines it (e.g. `Utils`), never the type it appears to extend (e.g.
`Camera`).** The symptom is identical in shape to the nested-type bug
above and just as silent: the feature disables itself on every startup
with a log message blaming a Valheim update, and nothing about a clean
build or the test suite catches it, because `RequiredMembers` strings are
never compiled against the real types.

## 10. `ObjectDB.CopyOtherDB` is the wrong hook for anything needing `ZNetScene`

Task 9's one-shot prefab diagnostic originally assumed a `Postfix` on
`ObjectDB.CopyOtherDB` would see both `ObjectDB` *and* `ZNetScene`
populated — reasoning that `ZNetScene` is needed earlier in the connect
sequence than a world's item set. **That assumption was wrong.** A real
capture (game version 1.0.14, logged via `RossQoL-DIAG:`) showed
`ZNetScene.instance was null` inside the `CopyOtherDB` postfix, every
time. `ObjectDB.CopyOtherDB` runs while `ObjectDB` is populated but
*before* `ZNetScene.instance` exists — do not reach for `ZNetScene` (or
anything that needs its `m_prefabs`) from that hook.

**`ZNetScene.Awake` is late enough**, verified by decompiling
`ZNetScene` (ilspycmd, same 1.0.14 assembly):

```csharp
private void Awake()
{
    s_instance = this;                       // ZNetScene.instance non-null from here on
    foreach (GameObject prefab in m_prefabs) // m_prefabs already populated -- Unity fills
    {                                         // serialized fields before invoking Awake
        m_namedPrefabs.Add(prefab.name.GetStableHashCode(), prefab);
    }
    ...
}
```

`s_instance` is assigned as the very first statement, and the very next
statement iterates `m_prefabs` to build a lookup table — so `m_prefabs`
is already fully populated by the time `Awake` runs at all (it is a
serialized field, filled by Unity before any lifecycle method fires).
`ZNetScene` has no `Start()` method, so `Awake` is the earliest hook
available and it is sufficient. `ZNetScene` is only ever instantiated
during a real world load, never at the main menu, so a `Postfix` here
does not need a menu/world guard.

For anything needing both `ObjectDB` and `ZNetScene` populated, patch
both hooks independently rather than assuming one implies the other —
the connect-sequence ordering between them is not what it looks like
from the outside, and only a decompile (or a real capture) settles it.

## 11. Boss / creature `m_defeatSetGlobalKey` — verified from a live world (2026-09-18)

These keys are Unity-serialized asset data on each Character prefab
(`m_defeatSetGlobalKey`) — they are not in the DLLs at all and cannot be
found by decompiling. They were read out of a real 1.0.14 world with a
temporary diagnostic (since deleted; it was `PrefabDiagnostic.cs`, Task 9
of the Death spec) that walked `ZNetScene.instance.m_prefabs`, took the
`Character` component of every prefab, and logged every one whose
`m_defeatSetGlobalKey` was non-empty. 29 Character prefabs matched. The
five classic bosses (Eikthyr, gd_king, Bonemass, Dragon, GoblinKing) all
appear with their long-known keys, which is the sanity check that this
capture is trustworthy.

| Prefab(s) | `m_defeatSetGlobalKey` |
|---|---|
| `Eikthyr` | `defeated_eikthyr` |
| `gd_king` | `defeated_gdking` |
| `Bonemass` | `defeated_bonemass` |
| `Dragon` | `defeated_dragon` |
| `GoblinKing` | `defeated_goblinking` |
| `SeekerQueen` | `defeated_queen` |
| `Fader` | `defeated_fader` |
| `FrozenKing` | `defeated_frozenking` |
| `FrozenKing_p3` | `defeated_frozenking_p3` |
| `Hive` | `defeated_hive` |
| `Serpent` | `defeated_serpent` |
| `Writhan` | `defeated_writhan` |
| `ElakingMole` | `elakingmole_defeated` |
| `JotunWarrior`, `JotunWarriorDualWield`, `JotunWitch` | `jotun_killed` |
| `Troll`, `Troll_sleeping`, `Troll_Summoned` | `KilledTroll` |
| `Bat`, `Bat_Swamp` | `KilledBat` |
| `Surtling` | `killed_surtling` |
| `Frysling` | `killed_frysling` |
| `Skeleton_Hildir` | `BossHildir1` |
| `Fenring_Cultist_Hildir` | `BossHildir2` |
| `GoblinBruteBros` | `BossHildir3` |

`WorldFrontier`'s ladder uses `defeated_queen` (opens Ashlands) and
`defeated_fader` (opens DeepNorth); `defeated_frozenking` is the Deep
North's own boss key and opens no further tier since DeepNorth is the
last one.

**Food name correction**: the guessed Swamp-tier prefab `SausageS` does
not exist in a real ObjectDB. The real prefab is `Sausages`. Confirmed by
the same capture session's food-guess dump (`RespawnFoods.DefaultTable`
against `ObjectDB.GetItemPrefab`), and already fixed in
`RespawnFoods.cs` (commit 8b6525f).

## 12. ZDO identity is NOT stable across a save/load — and the destroy path

Read from the same 1.0.14 assemblies
(`ilspycmd -t ZDO / ZDOMan / ZNetScene / ZRoutedRpc / Container / TombStone`),
while diagnosing a grave record that could never be cleared after a relog.

**A ZDOID written down in one session identifies nothing in the next.**
`ZDO.Load` re-keys every ZDO it reads from the world save:

```csharp
public void Load(ZPackage pkg, Version.World version)
{
    bool flag = version >= Version.World.ChunkedSave;
    m_uid.SetID(++ZDOID.m_loadID);          // <-- fresh id for every loaded ZDO
    ...
}
```

and `ZDOID.SetID` throws the user half away too:

```csharp
public void SetID(uint id)
{
    ID = id;
    UserKey = UnknownFormerUserKey;         // user id becomes 1, "unknown former user"
}
```

`ZNet.Awake` sets `ZDOID.m_loadID = 0u`, `ZDOMan`'s constructor calls
`ZDOID.Reset()`, and `ZDOMan.LoadChunks` finishes with
`m_nextUid = ZDOID.m_loadID + 1`. So after a world load every persisted
ZDO is `(userID = 1, ID = <its index in load order>)`, while newly created
ZDOs are `new ZDOID(m_sessionID, m_nextUid++)` with a session id that
differs every session.

Consequence for mods: **never persist a ZDOID and expect to look the
object up later.** `ZDOMan.GetZDO(savedId)` returns null forever, which is
indistinguishable from "that object was destroyed". Identify a persisted
object by something in its own data instead — its position, plus a ZDO
field such as `ZDOVars.s_owner`. Verified against a real save: a grave
recorded with `ZdoUserId = 688260319` (the session it was made in), looked
up in a later session whose `SessionID` was `1696348721` and whose 223,681
loaded ZDOs all carry user id 1.

`ZDOID` equality is by `UserKey`, an index into a static table, not by the
raw user id:

```csharp
public static bool operator ==(ZDOID a, ZDOID b) => a.UserKey == b.UserKey && a.ID == b.ID;
public ZDOID(long userID, uint id) { UserKey = AddUser(userID); ID = id; }
```

`AddUser` de-duplicates, so reconstructing `new ZDOID(userId, id)` within
one session does compare equal to the original — the reconstruction is
sound; it is the id's lifetime that is not.

**The destroy path, for the record** (this part behaves as you would
hope). `TombStone.UpdateDespawn` (§3) calls `m_nview.Destroy()`:

```csharp
// ZNetView
public void Destroy() { ZNetScene.instance.Destroy(base.gameObject); }

// ZNetScene
public void Destroy(GameObject go)
{
    ZNetView component = go.GetComponent<ZNetView>();
    if ((bool)component && component.GetZDO() != null)
    {
        ZDO zDO = component.GetZDO();
        component.ResetZDO();
        m_instances.Remove(zDO);
        if (zDO.IsOwner()) { ZDOMan.instance.DestroyZDO(zDO); }
    }
    UnityEngine.Object.Destroy(go);
}

// ZDOMan
public void DestroyZDO(ZDO zdo) { if (zdo.IsOwner()) m_destroySendList.Add(zdo.m_uid); }
```

`DestroyZDO` does **not** touch `m_objectsByID`. The dictionary entry goes
one `ZDOMan.Update` later, via `SendDestroyed()` ->
`ZRoutedRpc.InvokeRoutedRPC(0L, "DestroyZDO", pkg)`; a target of 0 makes
`InvokeRoutedRPC` call `HandleRoutedRPC` locally as well as routing it, so
the destroying peer removes it too (`RPC_DestroyZDO` ->
`HandleDestroyedZDO` -> `m_objectsByID.Remove(zDO.m_uid)` +
`ZDOPool.Release`). So `GetZDO` does go null for a destroyed object, about
a frame later, on every peer — a dead ZDO does not linger. `GetZDO` itself
is a plain dictionary lookup with no dead-marking:

```csharp
public ZDO GetZDO(ZDOID id)
{
    if (id == ZDOID.None) return null;
    if (m_objectsByID.TryGetValue(id, out var value)) return value;
    return null;
}
```

(The server additionally keeps `m_deadZDOs[uid] = ticks` after removal,
but that is a separate map for rejecting stale peer data, not something
`GetZDO` consults.)

**`Container.IsInUse` is purely local** — `private bool m_inUse;`, set by
`SetInUse` on the owner — so the tombstone's `!m_container.IsInUse()`
despawn test only means "the owner does not have the container UI open".
`TombStone.Interact` auto-loots through `m_container.TakeAll`, and
`Container.RPC_TakeAllResponse` calls `m_nview.ClaimOwnership()` before
`MoveAll`, so after an auto-loot the looting client owns the ZDO and is
the peer that runs `UpdateDespawn` and destroys the tombstone.
