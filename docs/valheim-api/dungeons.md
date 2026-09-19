# Dungeons: locations, generators, interiors and biome

Read from `assembly_valheim.dll`, game version 1.0.15, on 2026-09-19
(`ilspycmd -t <Type>`). Everything quoted below is decompiled code.

Written while diagnosing a report that infested mines respawned on a
dedicated server with the Queen still alive (`Progression/DungeonRespawn`),
and extended with what a dungeon door is (`Portals/TamesFollow`).

**Outcome of that investigation: the reported bug did not exist.** What was
seen was vanilla respawning creatures inside infested mines, which is not a
dungeon rebuild at all, and a live capture showed the gate correctly
refusing to rebuild a Mistlands dungeon with the Queen alive. The change
that came out of it (deciding from the dungeon's own identity rather than
the terrain under its entrance) is hardening against the failure modes in
§2, not a fix for an observed fault.

## 1. Where a dungeon interior actually lives

`Location.Awake` (Location.cs:54):

```csharp
private void Awake()
{
    s_allLocations.Add(this);
    if (m_hasInterior)
    {
        Vector3 zoneCenter = GetZoneCenter();
        GameObject obj = Object.Instantiate(
            position: new Vector3(zoneCenter.x, base.transform.position.y + 5000f, zoneCenter.z),
            original: m_interiorPrefab, rotation: Quaternion.identity, parent: base.transform);
        ...
    }
}

private Vector3 GetZoneCenter() => ZoneSystem.GetZonePos(ZoneSystem.GetZone(base.transform.position));
```

So an interior is at its **own zone's centre in x/z**, 5000 m up. For a
location using `m_useCustomInteriorTransform` (the tooltip says it exists
so a dungeon can "fill out the entire zone"), `ZoneSystem.SpawnLocation`
does the same thing explicitly, and the construction is designed to land
the generator on the zone centre whatever the location's rotation
(ZoneSystem.cs:2426-2438):

```csharp
Vector3 zonePos = GetZonePos(GetZone(pos));
component.m_generator.transform.localPosition = Vector3.zero;
Vector3 vector3 = zonePos + vector + vector2 - pos;
Vector3 localPosition3 = (Matrix4x4.Rotate(Quaternion.Inverse(rot)) * Matrix4x4.Translate(vector3)).GetColumn(3);
localPosition3.y = component.m_interiorTransform.localPosition.y;
component.m_interiorTransform.localPosition = localPosition3;
component.m_interiorTransform.localRotation = Quaternion.Inverse(rot);
```

**Consequence:** `ZoneSystem.GetZone(generator.transform.position)` is the
same zone as the entrance, because `GetZone` uses x/z only
(ZoneSystem.cs:2973). Looking a dungeon's location up by its generator's
zone is sound.

## 2. `Heightmap.FindBiome` ignores y entirely

`Heightmap.IsPointInside` (Heightmap.cs:1109) compares x and z only — no
y term at all — so `FindBiome` (Heightmap.cs:1148) answers for the ground
under a point 5000 m in the air exactly as it would for the surface:

```csharp
public static Biome FindBiome(Vector3 point)
{
    Heightmap heightmap = FindHeightmap(point);
    if (!heightmap) return Biome.None;
    return heightmap.GetBiome(point);
}
```

`GetBiome` (Heightmap.cs:478) returns the corner biome when all four
corners agree, and otherwise picks the **highest-weighted corner**, with
ties broken toward the **lowest `BiomeIndex`**: None, Meadows, Swamp,
Mountain, BlackForest, Plains, AshLands, DeepNorth, Ocean, **Mistlands
last**. So on any tile that straddles a biome edge, Mistlands is the
value most likely to be lost, and Swamp/Mountain/BlackForest the values
most likely to win — the three whose bosses a Mistlands-era player has
already killed.

**Both failure modes of a terrain sample are therefore real:**
`Biome.None` when no heightmap is loaded for that point (a dungeon
loaded, its zone's terrain not), and a neighbouring biome's name near an
edge. Neither can be ruled out from the assemblies, because which
heightmaps are loaded and which corners disagree are world and runtime
facts.

## 3. What identity a dungeon carries instead

Verified members, all public:

```csharp
public class Location : MonoBehaviour           // Location.cs
{
    public bool m_hasInterior;                  // :22
    public float m_interiorRadius = 20f;        // :24
    public bool m_useCustomInteriorTransform;   // :31
    public DungeonGenerator m_generator;        // :33
    public Heightmap.Biome m_biome;             // :52  <- the biome it was placed FOR
}

public class ZoneSystem.ZoneLocation            // ZoneSystem.cs:148, nested -> "ZoneSystem+ZoneLocation"
{
    public string m_name;                       // :150
    [HideInInspector] public string m_prefabName;   // :155
    public SoftReference<GameObject> m_prefab;  // :157
    [BitMask(typeof(Heightmap.Biome))]
    public Heightmap.Biome m_biome;             // :160  <- the placement rule itself
    public float m_interiorRadius;              // :191
}

public struct ZoneSystem.LocationInstance       // ZoneSystem.cs:264
{
    public ZoneLocation m_location;
    public Vector3 m_position;                  // the ENTRANCE, per RegisterLocation (:2598)
    public bool m_placed;
}

public class DungeonGenerator : MonoBehaviour   // DungeonGenerator.cs
{
    public enum Algorithm { Dungeon, CampGrid, CampRadial }   // :38
    public Algorithm m_algorithm;               // :51
    public Room.Theme m_themes = Room.Theme.Crypt;            // :65
    public Vector3 m_zoneCenter, m_zoneSize;    // :94, :96
}

public enum Room.Theme                          // Room.cs:7
{
    None = 0, Crypt = 1, SunkenCrypt = 2, Cave = 4, ForestCrypt = 8,
    GoblinCamp = 0x10, MeadowsVillage = 0x20, MeadowsFarm = 0x40,
    DvergerTown = 0x80, DvergerBoss = 0x100, ForestCryptHildir = 0x200,
    CaveHildir = 0x400, PlainsFortHildir = 0x800, AshlandRuins = 0x1000,
    FortressRuins = 0x2000, Hole = 0x4000, NorthVillage = 0x10000,
    MorkHalla = 0x20000
}
```

`DungeonGenerator.m_algorithm` is how a surface camp (goblin village,
farm) is told from a real dungeon: the same component builds both.

**Neither `Biome` nor `Room.Theme` is declared `[Flags]`** (only
`Heightmap.BiomeArea` is, Heightmap.cs:47), although both are edited
through `[BitMask(...)]`. `ToString()` on a value with two bits set
therefore prints a *number*, not names — decompose the bits yourself and
skip the aggregate members `Biome.All` (0x37F) and `Biome.Land` (0x27F)
by taking single-bit values only.

## 4. Global keys

`ZoneSystem.GetGlobalKey(string)` (ZoneSystem.cs:3200) is a lookup in
`m_globalKeysValues` **lower-cased**:

```csharp
public bool GetGlobalKey(string name) => GetGlobalKey(name, out var _);
public bool GetGlobalKey(string name, out string value) => m_globalKeysValues.TryGetValue(name.ToLower(), out value);
```

so `"defeated_queen"` is a valid lookup and no enum round trip is
involved. Keys reach a client wholesale from the server (`RPC_GlobalKeys`,
ZoneSystem.cs:727).

## 5. A dungeon door is `Teleport`, and it is the same machinery as a portal

The thing you interact with at a crypt mouth is a `Teleport`
MonoBehaviour — **not** `TeleportWorld`, which is the buildable portal.
The whole type is small (Teleport.cs):

```csharp
public class Teleport : MonoBehaviour, Hoverable, Interactable
{
    public string m_hoverText = "$location_enter";
    public string m_enterText = "";
    public Teleport m_targetPoint;          // :9  the far side of the pair
    public float m_hoverOffset;

    public bool Interact(Humanoid character, bool hold, bool alt)
    {
        if (hold) return false;
        if (m_targetPoint == null) return false;
        if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBossPortals) && character.InInterior()
            && Location.IsInsideActiveBossDungeon(character.transform.position))
        { character.Message(MessageHud.MessageType.Center, "$msg_blockedbyboss"); return false; }

        if (character.TeleportTo(m_targetPoint.GetTeleportPoint(), m_targetPoint.transform.rotation,
                                 distantTeleport: false))
        {
            Game.instance.IncrementPlayerStat(
                character.InInterior() ? PlayerStatType.PortalDungeonOut : PlayerStatType.PortalDungeonIn);
            ...
            return true;
        }
        return false;
    }

    private Vector3 GetTeleportPoint() =>
        base.transform.position + base.transform.forward - base.transform.up;
}
```

Facts that follow, all load-bearing:

- **One component, both sides.** A `Teleport` sits on the entrance and
  another on the interior side, each wired to its partner through
  `m_targetPoint`. There is no separate "exit" type.
- **Both directions share one code path.** `Interact` branches on
  `Character.InInterior()` only to choose which `PlayerStatType` to
  increment. Going in and coming out are otherwise the same call.
- **It is `Player.TeleportTo`, same as a portal.**
  `TeleportWorld.Teleport(Player)` ends in
  `player.TeleportTo(pos, rotation, distantTeleport: true)`
  (TeleportWorld.cs:148); a door calls the identical method with
  `distantTeleport: false`. So `Player.TeleportTo` is the single point
  every teleport in the game goes through, and patching it once covers
  portals and dungeon doors both — with no risk of double-firing against a
  `TeleportWorld.Teleport` patch, because that one is not a second
  teleport, it is the caller of this one.
- **`TeleportTo` returns true only on acceptance** (Player.cs:5889): false
  if this client is not the owner (it forwards `RPC_TeleportTo` instead),
  false if already teleporting, false inside the 2 s `m_teleportCooldown`.
  It sets `m_teleporting = true` before returning true, so
  `IsTeleporting()` is already true in a postfix.
- **`UpdateTeleport` finishes a non-distant hop the same way**
  (Player.cs:5916): after the fixed 2 s it moves the transform to
  `m_teleportTargetPos`, and clears `m_teleporting` once
  `ZoneSystem.FindFloor` succeeds, or — for `!m_distantTeleport` — on the
  next pass regardless, putting the player back where they started with
  `$msg_portal_blocked` if there was no floor. Either way the
  teleporting → not-teleporting transition still fires exactly once, which
  is what arrival placement hangs off.
- **The unsummon guard matters more here, not less.** An interior is at its
  zone centre plus 5000 m on y (§1), and `Tameable.m_unsummonDistance` is
  150 m, so a summon left behind at the door is destroyed outright.
- **Arrival placement must use the footing rules.** A spot computed
  against `ZoneSystem.IsBlocked` is meaningless inside a dungeon (see
  `PortalTamesManager.StandingRoom` and §2 here: the block raycast
  runs a 10 km column that contains the whole overworld), which is how a
  wolf ends up under the floor. The footing test — is there solid within
  about 1.5 m of the player's own height, failing safe to the player's
  position — is the same code path for a door as for a portal, and §7
  says why it now runs outdoors too.

## 6. Getting from inside a dungeon back to its entrance

Read at 1.0.15 while fixing the grave marker for a death in a crypt
(`Death/GraveMarker`). §5's pairing holds, and this is exactly how to use
it from the inside.

`Character.InInterior` (Character.cs:4362-4375), all three overloads:

```csharp
public bool InInterior() => InInterior(base.transform);
public static bool InInterior(Transform me) => InInterior(me.position);
public static bool InInterior(Vector3 position) => position.y > 3000f;
```

A bare height test, with a public static `Vector3` overload — so any
point, not just a character, can be asked. 3000 m is comfortably above
any terrain and comfortably below the 5000 m interiors sit at (§1).

**The exterior position is `teleport.m_targetPoint.transform.position`.**
`m_targetPoint` is the only public link between the two halves of a pair,
and it is a `Teleport`, so its `transform` is readable. The inner door of
a pair is therefore the `Teleport` for which:

- `m_targetPoint != null`, and
- `Character.InInterior(door.transform.position)` is true, and
- `Character.InInterior(m_targetPoint.transform.position)` is false.

Nothing else can be caught by that test: a buildable portal is a
`TeleportWorld`, an unrelated type.

**`Teleport.GetTeleportPoint()` is `private`** (§5), so the exact arrival
spot — `transform.position + transform.forward - transform.up` — is not
reachable by name without reflection. It differs from the transform
position by about 1.4 m, which is nothing at marker ranges, so the plain
transform position is what to use.

**When to ask matters more than how.** A dungeon's `Teleport` pair only
exists while the interior is instantiated, which happens in
`Location.Awake` (§1) — i.e. only while that zone is loaded. Standing on
the surface a few hundred metres away, or across the map, there is no
`Teleport` to find. Anything that needs a dungeon's entrance from a
distance must therefore capture it while the player is **inside**, and
store it; resolving it live fails in precisely the case it is wanted.

## 7. Not every dungeon is instanced, and `InInterior` is the wrong question

Read at 1.0.15 while diagnosing a report that in *some* infested mines —
"entrances which are a spiral staircase down" — both a recall and walking
out through the door put a skeleton **outside the entrance** rather than
beside the player.

**Instancing is per-location asset data, not a property of being a
dungeon.** `Location.Awake` (§1) instantiates the interior 5000 m up only
`if (m_hasInterior)`, and `m_hasInterior` is a serialized `public bool` on
the location prefab (Location.cs:22) — not in the assemblies (see
README). `DungeonGenerator` itself contains no altitude offset at all: it
builds every room relative to `base.transform.position`
(`PlaceStartRoom`/`PlaceRooms`, DungeonGenerator.cs:318, 356, 739), which
is why the same component also builds goblin villages and farms on the
surface via `m_algorithm` (§3). So nothing in code says a dungeon is at
+5000; only the asset does.

**And the entrance structure is never instanced either way.** The
`Teleport` pair (§5) is the boundary: everything on the outside half —
including a staircase that descends inside the entrance building, and
whatever terrain has been carved or built around it — sits at ordinary
world altitude. `Character.InInterior(Vector3 position) => position.y >
3000f` (§6) is a bare altitude test, so anywhere on that side it is
**false**, and so is the door's own exterior arrival point.

**What the surface branch then does to a position underground.** Three
decompiled calls, all `ZoneSystem`:

```csharp
public bool IsBlocked(Vector3 p)                      // :2728
{ p.y += 2000f; return Physics.Raycast(p, Vector3.down, 10000f, m_blockRayMask); }

public float GetGroundHeight(Vector3 p)               // :2734
{ Vector3 origin = p; origin.y = 6000f;
  if (Physics.Raycast(origin, Vector3.down, out var hitInfo, 10000f, m_terrainRayMask))
      return hitInfo.point.y;
  return p.y; }

public bool GetSolidHeight(Vector3 p, out float height, int heightMargin = 1000)  // :2768
{ p.y += heightMargin;
  if (Physics.Raycast(p, Vector3.down, out var hitInfo, 2000f, m_solidRayMask)
      && !hitInfo.collider.attachedRigidbody)
  { height = hitInfo.point.y; return true; }
  height = 0f; return false; }
```

with the masks (ZoneSystem.cs:676-678):

```csharp
m_terrainRayMask = LayerMask.GetMask("terrain");
m_blockRayMask   = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
m_solidRayMask   = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
```

So: **`IsBlocked` cannot see terrain at all** — a candidate buried inside
a hillside, or hanging over a forty-metre drop, is "not blocked". And
**the height correction only searches downward** — `GetSolidHeight` starts
at `p.y + margin` and casts 2000 m *down*, so it can never lift a creature
to the surface; at worst it drops one a long way, or (returning false)
leaves it at the player's height inside rock.

**The thing that actually puts it outside is vanilla.**
`Character.UnderWorldCheck` (Character.cs:883), called from `Update` every
frame and acting every 5 s per creature (every frame for a player):

```csharp
float groundHeight = ZoneSystem.instance.GetGroundHeight(base.transform.position);
if (base.transform.position.y < groundHeight - 1f)
{
    Vector3 position = base.transform.position;
    position.y = groundHeight + 0.5f;
    base.transform.position = position;
    m_body.position = position;
    m_body.linearVelocity = Vector3.zero;
}
```

`GetGroundHeight` samples the **terrain** layer from a fixed y = 6000, so
for anything below the surface it returns the overworld height at that
x/z, and the creature is teleported to it. For a creature placed part-way
down an entrance shaft — or at a candidate a couple of metres to the side
that happens to lie under the hillside — that destination is precisely
"outside the entrance", within five seconds, whatever the mod wrote. The
player is not moved with it because the player is standing somewhere that
passes this same check.

**Conclusion: `InInterior` is not the question.** "Is this point above
3000 m" identifies an *instanced* interior and nothing else; it says
nothing about whether the terrain sample is meaningful where the player
is standing. The rule RossQoL uses instead
(`RossQoL.Core.Portals.PlacementFooting`) is: a creature is never placed
at a height the player is not at — accept a candidate only when a
downward probe finds solid within ~1.5 m of the player's own height, and
additionally refuse one whose floor is under the terrain surface by
vanilla's own margin unless the player's own position is too (in which
case the terrain sample is meaningless for both). `InInterior` survives
only as the reason to skip the useless `IsBlocked` call, where a wrong
answer costs a nicer spread, never a wrong height.

## 8. Things I could not verify

- **Which themes and declared biomes each dungeon location actually
  carries.** `Location.m_biome`, `ZoneLocation.m_biome` and
  `DungeonGenerator.m_themes` are serialized Unity asset data and are not
  in the assemblies (see README). Whether `Location.m_biome` is even
  authored on shipped location prefabs is unknown; `ZoneLocation.m_biome`
  certainly is, because `GenerateLocationsTimeSliced` places by it
  (ZoneSystem.cs:1921).
- **Whether `Theme.Crypt` means burial chambers or something else**, and
  whether frost caves and troll caves really share `Theme.Cave`. Until a
  live capture says otherwise, `DungeonUnlock` requires every boss a
  theme might mean.
- **Whether a dedicated-server client has any `m_locationInstances` at
  all.** `GenerateLocations` runs on the server; clients receive only
  location *icons* (`RPC_LocationIcons`). The loaded-`Location` path is
  the one expected to run on a client, but this was not confirmed at
  runtime.
- **What the terrain sample actually returned for an infested mine.** The
  two mechanisms in §2 are both plausible and neither is provable from
  source. A live capture on the dedicated server showed the gate shut for
  a Mistlands dungeon with the Queen alive — i.e. neither mechanism was
  misfiring in the case that prompted the investigation — but it does not
  prove they cannot fire on some other tile. The temporary diagnostic that
  produced that capture has been removed.
- **Which prefabs actually carry a `Teleport`, and whether any location
  uses more than one pair.** §5 is read from the type, not from the
  location assets, which are serialized Unity data (see README).
- **Whether any shipped dungeon location has `m_hasInterior == false`**,
  i.e. is generated in place at ordinary altitude rather than instanced at
  +5000 (§7). The field is serialized asset data. The placement rule no
  longer depends on the answer, but it is worth a live read
  (`Location.m_hasInterior` on a loaded entrance) if anything else ever
  does.
- **How far below the terrain surface the walkable part of a descending
  entrance actually is**, and therefore how often a candidate beside the
  player is under terrain while the player is not. Terrain deltas around a
  location are world data; this is a runtime measurement
  (`ZoneSystem.instance.GetGroundHeight(pos) - pos.y` at the player and at
  a point a few metres to the side), not something the assemblies can say.
