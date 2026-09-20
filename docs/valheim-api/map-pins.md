# Map pins (`Minimap` / `Minimap.PinType`)

What this covers: how a mod adds/removes its own map pins and controls their
icon, plus the one non-obvious fact — which `PinType` is the portal icon.
Written for RossPortals' "show every portal on the map" feature.

**Read at game version 1.0.15**, decompiling `Minimap` from
`assembly_valheim.dll` with `ilspycmd`, plus one in-game diagnostic (a
temporary `Minimap.AddPin` postfix that logged `PinType` — since removed) to
confirm the portal pin type from a live pin.

## Adding and removing pins

```csharp
public PinData AddPin(Vector3 pos, PinType type, string name, bool save,
    bool isChecked, long ownerID = 0L, PlatformUserID author = default);
public void RemovePin(PinData pin);
public bool RemovePin(Vector3 pos, float radius);
```

- `save: false` makes a **transient** pin — never written to the map save.
  Re-derive such pins each session from your own source of truth; that avoids
  both duplication on reload and stale pins after the thing they marked is gone.
- `Minimap.instance` is null on a dedicated server (no map) — guard with
  `GUIManager.IsHeadless()` / a null check.

## `PinType` is gated to the enum

`AddPin` starts with:

```csharp
if ((int)type >= m_visibleIconTypes.Length || type < PinType.Icon0)
{
    ZLog.LogWarning($"Trying to add invalid pin type: {type}");
    type = PinType.Icon3;
}
```

`m_visibleIconTypes` is sized to `Enum.GetValues(typeof(PinType)).Length`, so
you **cannot** invent a custom `PinType` value beyond the enum without also
growing that array. Use an existing type.

## The drawn sprite comes from `PinData.m_icon`, and you can override it

`AddPin` sets `pinData.m_icon = GetSprite(type)`, and the renderer draws that
field: `pin.m_iconElement.sprite = pin.m_icon`. The game itself overrides it for
location pins. So to draw a custom sprite on a pin, set `pin.m_icon` *after*
`AddPin` returns. (RossPortals no longer needs this — see below — but it's the
escape hatch if you ever want a sprite that isn't in `m_icons`.)

`GetSprite(type)` = `m_icons.Find(x => x.m_name == type).m_icon`. The
`PinType -> Sprite` mapping (`m_icons`) is Unity-serialized on the Minimap
prefab, so it is NOT in the DLLs — read it at runtime if you ever need it.

## `PinType.Icon4` is the portal icon

The vanilla placeable pin icons are `Icon0`..`Icon4`. The **portal** one is
`PinType.Icon4` — confirmed from a live player-placed portal pin, which logged
`type=Icon4 (6)`. The `(6)` is the enum's integer value: the enum order is
`Icon0, Icon1, Icon2, Icon3, Death, Bed, Icon4, ...`, so `Icon4 == 6`, not 4.

So a portal-marked pin is just `AddPin(pos, PinType.Icon4, name, save: false,
isChecked: false)` with no `m_icon` override — the game supplies the sprite.

## `Splatform.dll` reference

`Minimap.AddPin`'s optional `PlatformUserID author = default` parameter lives in
`Splatform`. Calling `AddPin` at all makes the compiler need that assembly, so
add a reference to `valheim_Data/Managed/Splatform.dll` or you get `CS0012`
("PlatformUserID is defined in an assembly that is not referenced").

## Pins are saved compressed in the player profile

`Minimap.GetMapData()` writes `int textureSize`, the explored byte arrays, then
`int pinCount` and per pin `name / pos / type / checked / ownerID / author`, all
**deflate-compressed** (`ZPackage.WriteCompressed`) into the character's
`PlayerProfile` map data. It's written only when the game saves (logout / quit /
periodic autosave), so a player's pins are NOT readable from disk while the game
is running. (This is why reading a live player's example pins from the save
mid-session doesn't work — use the in-game diagnostic-patch route instead.)

## Things I could not verify

- The `PinType -> Sprite` table (`m_icons`) — Unity-serialized on the Minimap
  prefab, not in the DLLs. `Icon4 == portal` is confirmed empirically, but the
  other Icon0..3 sprites weren't individually identified.
- Whether a shared-map mod (e.g. OneMapToRuleThemAll, present on the test
  server) relocates pin storage; it stores exploration as a separate
  `<world>.one_map_to_rule_them_all.explored` file but no pins file was found.
