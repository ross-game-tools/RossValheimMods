# Guardian powers (boss powers)

Verified Valheim internals for the guardian-power / boss-power pipeline:
how a `GP_*` power is written onto a player at a guardian stone
(`ItemStand`), how it is activated (`Player` + `SEMan`), how the cooldown
is tracked, and how the HUD shows it (`Hud`). Written for the "Multiple
Boss Powers" work.

- **Game version:** `GameVersion(1, 0, 15)` — from
  `Version.CurrentVersion` in `Version.cs`:
  ```csharp
  public static GameVersion CurrentVersion { get; } = new GameVersion(1, 0, 15);
  ```
- **Source:** decompiled `assembly_valheim.dll` (dump at
  `C:/Users/ross/val_dump`). Line numbers are from that dump and will
  drift; the quoted bodies are what matters.

---

## `ItemStand.DelayedPowerActivation()` — the SetGuardianPower patch target

The guardian stone is an `ItemStand`. When the player interacts with the
stand and a `m_powerActivationDelay` elapses, `DelayedPowerActivation` is
`Invoke`d (scheduled by name from `UseItem`, `ItemStand.cs:225`
`Invoke("DelayedPowerActivation", m_powerActivationDelay);`). This is the
one method whose body calls `localPlayer.SetGuardianPower(m_guardianPower.name)`
**unconditionally** -- before the `switch (base.name)`, which only bumps a
player-stat counter. The power is therefore always applied regardless of the
stand's name; the switch is cosmetic.

**`base.name` is NOT a reliable power identity.** Vanilla's own boss stones
are named `GP_Eikthyr`/`GP_TheElder`/… (the switch cases), but a placed or
mod-provided guardian stand can be a bare `itemstand` -- observed live in a
player's log as `Missing stat for guardian power: itemstand` (the `default`
arm). It still grants its power (`SetGuardianPower` ran first) and still has
`m_guardianPower` set. **The reliable identity is `m_guardianPower.name`** --
the exact string `SetGuardianPower` stores and `GetGuardianPowerName` returns.
Keying a patch off `base.name` / `StartsWith("GP_")` silently misses these
stands: RossQoL's `GuardianStonePatch` originally did this and never added
such powers to the loadout, so it was fixed to gate on `m_guardianPower != null`
and read `m_guardianPower.name`, never the stand GameObject name.

`ItemStand.cs:247`:
```csharp
private void DelayedPowerActivation()
{
	Player localPlayer = Player.m_localPlayer;
	if (!(localPlayer == null))
	{
		localPlayer.SetGuardianPower(m_guardianPower.name);
		Game.instance.IncrementPlayerStat(PlayerStatType.SetGuardianPower);
		switch (base.name)
		{
		case "GP_Eikthyr":
			Game.instance.IncrementPlayerStat(PlayerStatType.SetPowerEikthyr);
			break;
		case "GP_TheElder":
			Game.instance.IncrementPlayerStat(PlayerStatType.SetPowerElder);
			break;
		case "GP_Bonemass":
			Game.instance.IncrementPlayerStat(PlayerStatType.SetPowerBonemass);
			break;
		case "GP_Moder":
			Game.instance.IncrementPlayerStat(PlayerStatType.SetPowerModer);
			break;
		case "GP_Yagluth":
			Game.instance.IncrementPlayerStat(PlayerStatType.SetPowerYagluth);
			break;
		case "GP_Queen":
			Game.instance.IncrementPlayerStat(PlayerStatType.SetPowerQueen);
			break;
		case "GP_Ashlands":
			Game.instance.IncrementPlayerStat(PlayerStatType.SetPowerAshlands);
			break;
		case "GP_DeepNorth":
			Game.instance.IncrementPlayerStat(PlayerStatType.SetPowerDeepNorth);
			break;
		default:
			ZLog.LogWarning("Missing stat for guardian power: " + base.name);
			break;
		}
	}
}
```

The "already active" guard the stand uses to decide whether to offer the
hook, `ItemStand.cs:242`:
```csharp
private bool IsGuardianPowerActive(Humanoid user)
{
	return (user as Player).GetGuardianPowerName() == m_guardianPower.name;
}
```

### `ItemStand.Interact(Humanoid, bool hold, bool alt)` — the prefix point for a second slot

`Interact` (`ItemStand.cs:169`, `public bool`) is the use hook. Its guardian
branch (`ItemStand.cs:212-226`) is what schedules
`Invoke("DelayedPowerActivation", ...)` and returns true. A Harmony **prefix**
on `Interact` returning false (with `__result = true`) suppresses the whole
vanilla interaction -- so `SetGuardianPower` never runs and slot 1 is left
untouched. That is how RossQoL's MultiplePowers diverts a stone's power to a
second slot when an assign modifier is held (read with `ZInput.GetKey`), while
a normal use (no modifier) falls straight through to vanilla. Guard on
`m_guardianPower != null` (null on a weapon/armour stand). The `alt` parameter
is vanilla's own (orientation cycling for item-holding stands), not a free
modifier to reuse.

---

## `Player.SetGuardianPower` / `GetGuardianPowerHUD` — where the single power lives

Vanilla stores exactly one power. `SetGuardianPower` overwrites
`m_guardianPower` (string), recomputes `m_guardianPowerHash`, and resolves
the `StatusEffect` via `ObjectDB`. `Player.cs:6267`:
```csharp
public void SetGuardianPower(string name)
{
	m_guardianPower = name;
	m_guardianPowerHash = ((!string.IsNullOrEmpty(name)) ? name.GetStableHashCode() : 0);
	m_guardianSE = ObjectDB.instance.GetStatusEffect(m_guardianPowerHash);
	if ((bool)ZoneSystem.instance && !string.IsNullOrEmpty(name))
	{
		AddUniqueKey(name);
	}
}
```

`GetGuardianPowerName` (used by the ItemStand guard) and
`GetGuardianPowerHUD` (used by the HUD) — `Player.cs:6278`:
```csharp
public string GetGuardianPowerName()
{
	return m_guardianPower;
}

public void GetGuardianPowerHUD(out StatusEffect se, out float cooldown)
{
	se = m_guardianSE;
	cooldown = m_guardianPowerCooldown;
}
```

---

## `Player.ActivateGuardianPower` — how a power fires

Applies `m_guardianSE` to every player within 10 m via that player's
`SEMan`, optionally grants adrenaline, and arms the cooldown from the
status effect's `m_cooldown`. `Player.cs:6340`:
```csharp
public bool ActivateGuardianPower()
{
	if (m_guardianPowerCooldown > 0f)
	{
		return false;
	}
	if (m_guardianSE == null)
	{
		return false;
	}
	List<Player> list = new List<Player>();
	GetPlayersInRange(base.transform.position, 10f, list);
	foreach (Player item in list)
	{
		item.GetSEMan().AddStatusEffect(m_guardianSE.NameHash(), resetTime: true, 0, 0f, -1);
	}
	if (m_adrenalineGuardianPower != 0f)
	{
		AddAdrenaline(m_adrenalineGuardianPower);
	}
	m_guardianPowerCooldown = m_guardianSE.m_cooldown;
	return false;
}
```

---

## `SEMan.AddStatusEffect(int nameHash, …)` — the overload `ActivateGuardianPower` calls

Note the trailing `short variant = -1` parameter (five args total, not
four). The call in `ActivateGuardianPower` passes `(hash, true, 0, 0f, -1)`.
Ownership-aware: applies locally when owner, otherwise routes an RPC.
`SEMan.cs:137`:
```csharp
public StatusEffect AddStatusEffect(int nameHash, bool resetTime = false, int itemLevel = 0, float skillLevel = 0f, short variant = -1)
{
	if (nameHash == 0)
	{
		return null;
	}
	if (!m_nview.IsValid())
	{
		return null;
	}
	if (m_nview.IsOwner())
	{
		return Internal_AddStatusEffect(nameHash, resetTime, itemLevel, skillLevel, variant);
	}
	m_nview.InvokeRPC("RPC_AddStatusEffect", nameHash, resetTime, itemLevel, skillLevel, (int)variant);
	return null;
}
```

`Character.GetSEMan()` (the `SEMan` accessor used above), `Character.cs:4357`:
```csharp
public SEMan GetSEMan()
{
	return m_seman;
}
```

---

## `Player.m_adrenalineGuardianPower` (private) + `Player.AddAdrenaline`

The field is **private** with a compile-time default of `10f`; a patch
that needs to read or override it must use reflection (or replace the
`ActivateGuardianPower` logic). `Player.cs:352`:
```csharp
private float m_adrenalineGuardianPower = 10f;
```
(The `10f` is only the source default; a Player prefab could override it —
read at runtime if it matters. See "Things I could not verify".)

`Player.AddAdrenaline` (called from `ActivateGuardianPower`), `Player.cs:4528`:
```csharp
public override void AddAdrenaline(float v)
{
	float maxAdrenaline = GetMaxAdrenaline();
	if (v > 0f && maxAdrenaline > 0f)
	{
		float time = GetAdrenaline() / GetMaxAdrenaline();
		m_adrenalineDegenTimer = m_adrenalineDegenDelay.Evaluate(time);
		v *= Game.m_adrenalineRate;
		v *= m_adrenalineGainMultiplier.Evaluate(time);
		m_seman.ModifyAdrenaline(v, ref v);
	}
	if (v < 0f || (v > 0f && m_adrenaline < maxAdrenaline))
	{
		m_adrenaline += v;
	}
	// … (full-adrenaline SE handling elided) …
}
```

---

## `Player.UpdateGuardianPower(float dt)` + cooldown tick, called from `FixedUpdate`

The cooldown counts down every physics tick. `Player.cs:6364`:
```csharp
private void UpdateGuardianPower(float dt)
{
	m_guardianPowerCooldown -= dt;
	if (m_guardianPowerCooldown < 0f)
	{
		m_guardianPowerCooldown = 0f;
	}
}
```

**Correction to the brief:** the cooldown tick is driven from
`Player.FixedUpdate()`, **not** `Player.Update()`. `Player.Update()` exists
(`Player.cs:853`, `private void Update()`) but does not call
`UpdateGuardianPower`. The actual caller is `FixedUpdate`, passing
`Time.fixedDeltaTime`. `Player.cs:808`:
```csharp
private void FixedUpdate()
{
	float fixedDeltaTime = Time.fixedDeltaTime;
	UpdateAwake(fixedDeltaTime);
	if (m_nview.GetZDO() == null)
	{
		return;
	}
	UpdateTargeted(fixedDeltaTime);
	UpdateBreathParticles(fixedDeltaTime);
	if (!m_nview.IsOwner())
	{
		return;
	}
	// … owner + alive branch …
	else if (!IsDead())
	{
		UpdateActionQueue(fixedDeltaTime);
		PlayerAttackInput(fixedDeltaTime);
		UpdateAttach();
		UpdateDoodadControls(fixedDeltaTime);
		UpdateCrouch(fixedDeltaTime);
		UpdateDodge(fixedDeltaTime);
		UpdateCover(fixedDeltaTime);
		UpdateStations(fixedDeltaTime);
		UpdateGuardianPower(fixedDeltaTime);
		UpdateBaseValue(fixedDeltaTime);
		UpdateStats(fixedDeltaTime);
		UpdateTeleport(fixedDeltaTime);
		// … more …
	}
}
```
So `UpdateGuardianPower(dt)` only ticks while the player is owner and
alive.

---

## `ObjectDB.GetStatusEffect(int nameHash)` — resolving a power's StatusEffect

Linear scan of `m_StatusEffects` by `NameHash()`. `ObjectDB.cs:70`:
```csharp
public StatusEffect GetStatusEffect(int nameHash)
{
	foreach (StatusEffect statusEffect in m_StatusEffects)
	{
		if (statusEffect.NameHash() == nameHash)
		{
			return statusEffect;
		}
	}
	return null;
}
```

---

## `StatusEffect.m_cooldown` and `StatusEffect.NameHash()`

`m_cooldown` is the guardian-power cooldown length copied into
`m_guardianPowerCooldown` on activation. It is a public field under a
`[Header("__Guardian power__")]` block, so it is an inspector value on the
SE ScriptableObject (its shipped value is asset data, not visible here).
`StatusEffect.cs:59`:
```csharp
[Header("__Guardian power__")]
public float m_cooldown;
```

`NameHash()` lazily caches the stable hash of the SE's `name`.
`StatusEffect.cs:355`:
```csharp
public int NameHash()
{
	if (m_nameHash == 0)
	{
		m_nameHash = base.name.GetStableHashCode();
	}
	return m_nameHash;
}
```
(`private int m_nameHash;` — `StatusEffect.cs:75`.)

---

## `Hud` — how the guardian power is shown

The HUD fields are a single set (one power slot), `Hud.cs:155`:
```csharp
[Header("Guardian power")]
public RectTransform m_gpRoot;

public TMP_Text m_gpName;

public TMP_Text m_gpCooldown;

public Image m_gpIcon;

public UIInputHandler m_gpTouchButton;
```

`Hud.UpdateGuardianPower(Player player)` — called from the HUD update
(`Hud.cs:529`, `UpdateGuardianPower(localPlayer);`). It shows/hides
`m_gpRoot` and writes the icon sprite/color, the name, and the cooldown
**text**. `Hud.cs:1599`:
```csharp
private void UpdateGuardianPower(Player player)
{
	player.GetGuardianPowerHUD(out var se, out var cooldown);
	if ((bool)se)
	{
		if (!m_gpRoot.gameObject.activeSelf)
		{
			m_gpRoot.gameObject.SetActive(value: true);
		}
		m_gpIcon.sprite = se.m_icon;
		m_gpIcon.color = ((cooldown <= 0f) ? Color.white : s_colorRedBlueZeroAlpha);
		m_gpName.text = Localization.instance.Localize(se.m_name);
		if (cooldown > 0f)
		{
			m_gpCooldown.text = StatusEffect.GetTimeString(cooldown);
		}
		else
		{
			m_gpCooldown.text = Localization.instance.Localize("$hud_ready");
		}
	}
	else if (m_gpRoot.gameObject.activeSelf)
	{
		m_gpRoot.gameObject.SetActive(value: false);
	}
}
```

**Correction to the brief:** there is **no cooldown _fill_ image**. The
brief assumed an "icon + cooldown fill/label" with transform paths. In
1.0.15 the method touches only four members directly on the `Hud`
component — `m_gpIcon` (`Image`, sprite + color), `m_gpName` (`TMP_Text`),
`m_gpCooldown` (`TMP_Text`, shown as a remaining-time string or
`$hud_ready`), and `m_gpRoot` (the `RectTransform` it activates/deactivates).
The cooldown is communicated by the `m_gpIcon.color` tint plus the
`m_gpCooldown` text, not a radial/linear fill. `UpdateGuardianPower`
resolves these fields directly (they are wired in the prefab), so it never
walks child transform paths.

---

## Things I could not verify

These are asset/runtime facts that are not in the DLL and must be read
from a live instance:

- **The serialized `m_gpRoot` child layout.** `m_gpRoot`, `m_gpIcon`,
  `m_gpName`, `m_gpCooldown`, `m_gpTouchButton` are inspector-wired
  references on the `Hud` prefab. The GameObject names / child transform
  paths under `m_gpRoot` (what `m_gpIcon` etc. actually sit on) are prefab
  data, not decompilable. If a multi-power HUD needs to clone the slot,
  read the live hierarchy under `Hud.m_gpRoot` at runtime.
- **`m_adrenalineGuardianPower`'s effective value.** Source default is
  `10f`, but a Player prefab inspector override would win. Read it off the
  live `Player` (via reflection — the field is private) if the exact grant
  matters.
- **`StatusEffect.m_cooldown` per `GP_*` power.** `m_cooldown` (and
  `m_icon`, `m_name`, ttl, etc.) are ScriptableObject asset values on each
  guardian `StatusEffect`; the DLL only declares the fields. Read the
  actual cooldown seconds from the resolved `StatusEffect` at runtime.
- **`s_colorRedBlueZeroAlpha`.** The tint constant used for the on-cooldown
  icon color is a static in `Hud`; its exact RGBA was not quoted here.
  Read it if pixel-exact color matching is needed.

## When vanilla lets the power key fire (read 1.0.15)

From `ilspycmd -t Player`. `Player.Update` reads the key only inside
`bool flag2 = TakeInput(); ... if (flag2) { ... }`, and there only as

```csharp
if (!Hud.InRadial() && !Hud.IsPieceSelectionVisible() && (ZInput.GetButtonDown("GP") || ...))
    StartGuardianPower();
```

`Player.TakeInput()` (`protected override`, reachable through the
publicized assembly):

```csharp
bool result = (!Chat.instance || !Chat.instance.HasFocus()) && !Console.IsVisible() && !TextInput.IsVisible()
    && !StoreGui.IsVisible() && !InventoryGui.IsVisible() && !Menu.IsVisible()
    && (!TextViewer.instance || !TextViewer.instance.IsVisible()) && !Minimap.IsOpen()
    && !GameCamera.InFreeFly() && !PlayerCustomizaton.IsBarberGuiVisible()
    && !Hud.instance.m_buildUi.SearchFieldFocused;
if (IsDead() || InCutscene() || IsTeleporting()) result = false;
```

A raw `ZInput.GetKeyDown` read from a `Player.Update` postfix has none of
this, which is how RossQoL's second-power key fired while typing in chat
(fixed in 0.30.0). `TakeInput` knows only vanilla's own text boxes; a mod's
focused `TMP_InputField` is not covered, so check
`EventSystem.current.currentSelectedGameObject` for a focused field too.
