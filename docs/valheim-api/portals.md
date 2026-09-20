# Portals: destination, connection, tag, and the placement/sync hooks

What this covers: everything RossPortals (a from-scratch XPortal
replacement) reaches by name in vanilla Valheim. The mod lets a player
pick a portal's destination from a searchable, grouped list and drops
the vanilla "two portals must share a tag" rule. It stores the chosen
destination in the portal's own ZDO, but leaves the jump itself to
vanilla by writing the vanilla portal *connection* so that
`TeleportWorld.Teleport` runs unchanged. This file records how the
vanilla connection/tag are read and written, how the server keeps a
portal list, how a routed RPC reaches every peer, and how placement /
destruction are detected — each with the real decompiled body, plus a
note on which members are small enough to be Harmony-inlining hazards.

Produced on 2026-09-20 by decompiling `assembly_valheim.dll` and
`assembly_utils.dll` with ilspycmd 8.2 (`8.2.0.7535-95108c96`). Game
version from the assembly: `Version.CurrentVersion = new GameVersion(1,
0, 15)` (read from `Version.cs`; the install folder and BepInEx's
`changelog.txt` are not the game version). Types read: `TeleportWorld`
(full), `TextInput` (full), `ZDOMan`, `ZDO`, `ZRoutedRpc` (full),
`ZDOVars`, `Piece`, `WearNTear`, `ZNetScene`, `Game` in the relevant
part, and `StringExtensionMethods.GetStableHashCode` from
`assembly_utils.dll`.

## Inlining hazard, up front

The mod must not Harmony-patch a method the JIT inlines, and must not
count on a trivial getter being a patch point. The trivial one-liners
below (delegate-and-return bodies, dictionary lookups, two-field
setters) are all realistic inline candidates and are flagged inline
where they appear. The load-bearing safe targets are the ones with real
bodies: `TeleportWorld.Interact`, `TeleportWorld.Teleport`,
`TeleportWorld.RPC_SetTag`, `Piece.SetCreator`, and
`ZDOMan.GetPortalList` (which allocates and loops). Members are IL-sized
by eye from the decompiled body, not measured; treat "candidate" as
"assume it can be inlined, do not patch it, call it instead."

## `TeleportWorld` — the vanilla portal component

Full type read. The mod's whole "destination" concept rides on the
vanilla **connection**, which `Teleport` is the only consumer of.

### How the connection is read and the jump performed

```csharp
public void Teleport(Player player)
{
    if (!TargetFound())
    {
        return;
    }
    if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoPortals))
    {
        player.Message(MessageHud.MessageType.Center, "$msg_blocked");
        return;
    }
    if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBossPortals) && (RandEventSystem.instance.GetBossEvent() != null || (ZoneSystem.instance.GetGlobalKey(GlobalKeys.activeBosses, out float value) && value > 0f)))
    {
        player.Message(MessageHud.MessageType.Center, "$msg_blockedbyboss");
        return;
    }
    if (!player.IsTeleportable(m_allowAllItems))
    {
        player.Message(MessageHud.MessageType.Center, "$msg_noteleport");
        return;
    }
    ZLog.Log("Teleporting " + player.GetPlayerName());
    ZDO zDO = ZDOMan.instance.GetZDO(m_nview.GetZDO().GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal));
    if (zDO != null)
    {
        Vector3 position = zDO.GetPosition();
        Quaternion rotation = zDO.GetRotation();
        Vector3 vector = rotation * Vector3.forward;
        Vector3 pos = position + vector * m_exitDistance + Vector3.up;
        player.TeleportTo(pos, rotation, distantTeleport: true);
        Game.instance.IncrementPlayerStat(PlayerStatType.PortalsUsed);
    }
}
```

The destination is entirely `GetConnectionZDOID(ZDOExtraData
.ConnectionType.Portal)` → the target portal's ZDO → its position and
rotation. So if the mod writes that connection to the ZDOID of the
chosen destination portal, this vanilla body teleports there with no
patch. `Teleport` has a real multi-branch body (global-key checks, item
check, the connection lookup) — **not an inline candidate; safe patch
target** if the mod ever needs to intercept the jump.

### `HaveTarget` / `TargetFound` — connection presence

```csharp
private bool HaveTarget()
{
    if (m_nview == null || m_nview.GetZDO() == null)
    {
        return false;
    }
    return m_nview.GetZDO().GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None;
}

private bool TargetFound()
{
    if (m_nview == null || m_nview.GetZDO() == null)
    {
        return false;
    }
    ZDOID connectionZDOID = m_nview.GetZDO().GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
    if (connectionZDOID == ZDOID.None)
    {
        return false;
    }
    if (ZDOMan.instance.GetZDO(connectionZDOID) == null)
    {
        ZDOMan.instance.RequestZDO(connectionZDOID);
        return false;
    }
    return true;
}
```

Both are **`private`**. `HaveTarget` is a short null-guard + comparison
— an **inline candidate**; do not patch it. `TargetFound` additionally
calls `RequestZDO` when the target ZDO is not yet local, so it has a
real body, but being private and small it is still a poor patch target
— prefer to read the connection yourself (`GetConnectionZDOID`) rather
than hook these. Both are the vanilla gate: a portal "has a target" iff
its Portal connection is set, and "found" iff that target's ZDO is
locally resolvable (else a fetch is kicked off and it reads false this
frame).

### `Interact` — where the vanilla tag dialog is opened

```csharp
public bool Interact(Humanoid human, bool hold, bool alt)
{
    if (hold)
    {
        return false;
    }
    if (!PrivateArea.CheckAccess(base.transform.position))
    {
        human.Message(MessageHud.MessageType.Center, "$piece_noaccess");
        return true;
    }
    TextInput.instance.RequestText(this, "$piece_portal_tag", 10);
    return true;
}
```

This is the vanilla "set portal tag" entry point the mod replaces:
on use it opens `TextInput` with topic `"$piece_portal_tag"` and a
10-char limit. The mod suppresses this and shows its own destination
picker (see `TextInput` below). `Interact` has a real body (ward check,
the `RequestText` call) — **safe patch target** (prefix returning
`true` to swallow vanilla, then show the mod UI).

### `GetHoverText` / `GetTagInfo` — the hover string and tag read

```csharp
public string GetHoverText()
{
    TagInfo tagInfo = GetTagInfo();
    string text = "";
    if (!string.IsNullOrEmpty(tagInfo.text))
    {
        text = tagInfo.text.RemoveRichTextTags();
    }
    string text2 = (HaveTarget() ? "$piece_portal_connected" : "$piece_portal_unconnected");
    string text3 = ((tagInfo.censored && ZNet.m_onlineBackend == OnlineBackendType.PlayFab) ? "$piece_portal_censored_warning\n" : "");
    return Localization.instance.Localize("$piece_portal $piece_portal_tag:\"" + text + "\" [" + text2 + "]\n" + text3 + "[<color=yellow><b>$KEY_Use</b></color>] $piece_portal_settag");
}

private TagInfo GetTagInfo()
{
    ZDO zDO = m_nview.GetZDO();
    if (zDO == null)
    {
        return default(TagInfo);
    }
    string @string = zDO.GetString(ZDOVars.s_tagauthor);
    PlatformUserID userId = (string.IsNullOrEmpty(@string) ? PlatformUserID.None : new PlatformUserID(@string));
    string string2 = zDO.GetString(ZDOVars.s_tag);
    TagInfo result = default(TagInfo);
    result.text = CensorShittyWords.FilterUGC(string2, UGCType.Text, userId, 0L);
    result.censored = CensorShittyWords.Filter(string2, out var _);
    return result;
}
```

`GetHoverText` is `public`, real-bodied, string-building — **safe patch
target** (this is where the mod re-labels the hover to show the chosen
destination instead of "connected/unconnected"). `GetTagInfo` is
`private`; note `TagInfo` is a private nested struct, so a Harmony
signature referencing it needs reflection. The vanilla tag lives in two
ZDO string keys: `ZDOVars.s_tag` and `ZDOVars.s_tagauthor` (hashes
below).

### How the tag write manipulates the connection (`RPC_SetTag`)

```csharp
public void SetText(string text)
{
    if (m_nview.IsValid())
    {
        m_nview.InvokeRPC("RPC_SetTag", text, PlatformManager.DistributionPlatform.LocalUser.PlatformUserID.ToString());
    }
}

private void RPC_SetTag(long sender, string tag, string authorId)
{
    if (m_nview.IsValid() && m_nview.IsOwner())
    {
        GetTagSignature(out var tagRaw, out var authorId2);
        if (!(tagRaw == tag) || !(authorId2 == authorId))
        {
            ZDO zDO = m_nview.GetZDO();
            zDO.UpdateConnection(ZDOExtraData.ConnectionType.Portal, ZDOID.None);
            ZDOID connectionZDOID = zDO.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
            SetConnectedPortal(connectionZDOID);
            zDO.Set(ZDOVars.s_tag, tag);
            zDO.Set(ZDOVars.s_tagauthor, authorId);
        }
    }
}
```

This is vanilla's tag→connection coupling that the mod removes: on a tag
change vanilla *breaks* the current connection (`UpdateConnection(...,
ZDOID.None)`), disconnects the old peer via `SetConnectedPortal`, then
stores the new tag. RossPortals instead writes the connection directly
to the picked destination and never relies on tag matching.
`RPC_SetTag` is `private` but registered as an RPC and has a real body —
**safe patch target**. `SetText` is the `TextReceiver` callback the
vanilla `TextInput` dialog invokes.

`m_nview` is the private field the whole component funnels through:

```csharp
private ZNetView m_nview;
```

set in `Awake` via `GetComponent<ZNetView>()`. The mod reaches the ZDO
through `m_nview.GetZDO()` exactly as vanilla does.

## `TextInput` — the vanilla tag dialog to suppress

Full type read. `Interact` above calls `RequestText`; the mod suppresses
the vanilla dialog and shows its own picker.

```csharp
public static TextInput instance => m_instance;

public void RequestText(TextReceiver sign, string topic, int charLimit)
{
    m_queuedSign = sign;
    Show(topic, sign.GetText(), charLimit);
}
```

`instance` is a static property over `m_instance` (set in `Awake`,
nulled in `OnDestroy`) — a getter, **inline candidate**; read it, don't
patch it. `RequestText` is `public`, three real statements — **safe
patch target** (a prefix that returns `false` swallows the vanilla
dialog when the queued sign is a portal). `Show` (private) is what
actually activates the panel; suppressing at `RequestText` is cleaner.

## `ZDOMan` — the portal list and ZDO plumbing

`GetPortalList()` **does exist** (this was the open question):

```csharp
public List<ZDO> GetPortalList()
{
    List<ZDO> list = new List<ZDO>();
    foreach (var (_, collection) in m_portalObjects)
    {
        list.AddRange(collection);
    }
    return list;
}
```

`public`, allocates a fresh `List<ZDO>` and loops every sector bucket —
**not an inline candidate; safe to call and to patch.** It flattens
`m_portalObjects`, the server-side index of every portal ZDO:

```csharp
private readonly Dictionary<ZoneSystem.SectorIndex, List<ZDO>> m_portalObjects = new Dictionary<ZoneSystem.SectorIndex, List<ZDO>>();
```

This is the authoritative list the mod's server-side sync should build
from. Membership is keyed by prefab hash via `Game.instance
.PortalPrefabHash` (see `Game` below) — a ZDO is added to
`m_portalObjects` on load / when its prefab is set and removed when the
ZDO is destroyed, e.g.:

```csharp
// in RemoveFromSector's caller (ZDO destroy path)
RemoveFromSector(zDO, sectorIndex);
if (Game.instance.PortalPrefabHash.Contains(zDO.GetPrefab()))
{
    if (m_portalObjects.TryGetValue(sectorIndex, out var value) && value.Remove(zDO) && value.Count == 0)
    {
        m_portalObjects.Remove(sectorIndex);
    }
    DirtyPortalObjects = true;
}
```

So portal placement/destruction already flips `DirtyPortalObjects`; the
list is inherently server-maintained.

```csharp
public static ZDOMan instance => s_instance;

public ZDO GetZDO(ZDOID id)
{
    if (id == ZDOID.None)
    {
        return null;
    }
    if (m_objectsByID.TryGetValue(id, out var value))
    {
        return value;
    }
    return null;
}

public static long GetSessionID()
{
    return s_instance.m_sessionID;
}

public void ForceSendZDO(ZDOID id)
{
    foreach (ZDOPeer peer in m_peers)
    {
        peer.ForceSendZDO(id);
    }
}
```

- `instance` — static getter, **inline candidate**; read only.
- `GetZDO(ZDOID)` — `public`, a null check + one `TryGetValue`;
  **inline candidate**, call it, do not patch it. This is how the mod
  resolves a chosen-destination ZDOID back to a `ZDO`.
- `GetSessionID()` — `public static`, single field read; **inline
  candidate**. This is the local peer id the mod passes to
  `ZDO.SetOwner` before writing a connection (claim-then-write, exactly
  as vanilla's own `Game.SetConnection` does).
- `ForceSendZDO(ZDOID)` — `public`, a short loop over peers; small but
  real, poor patch target — call it to push a just-written connection
  out immediately. Note there are three overloads: `ForceSendZDO(ZDOID)`
  (all peers), `ForceSendZDO(long peerID, ZDOID id)`, and a private
  nested `ZDOPeer.ForceSendZDO(ZDOID)`.

Vanilla's own server-side connect path is the pattern to copy
(`Game.SetConnection` / `RPC_SetConnection`): claim owner, set the
Portal connection, force-send.

```csharp
// Game.SetConnection (server side)
portal.SetOwner(ZDOMan.GetSessionID());
portal.SetConnection(ZDOExtraData.ConnectionType.Portal, connection);
ZDOMan.instance.ForceSendZDO(portal.m_uid);
```

## `ZDO` — connection, tag, owner, identity

### Connection read / write

```csharp
public ZDOID GetConnectionZDOID(ZDOExtraData.ConnectionType type)
{
    return ZDOExtraData.GetConnectionZDOID(m_uid, type);
}

public void SetConnection(ZDOExtraData.ConnectionType connectionType, ZDOID zid)
{
    if (ZDOExtraData.SetConnection(m_uid, connectionType, zid))
    {
        IncreaseDataRevision();
        if (connectionType == ZDOExtraData.ConnectionType.Portal)
        {
            ZDOMan.instance.SetDirtyPortals();
        }
    }
}

public void UpdateConnection(ZDOExtraData.ConnectionType connectionType, ZDOID zid)
{
    if (ZDOExtraData.UpdateConnection(m_uid, connectionType, zid))
    {
        IncreaseDataRevision();
        if (connectionType == ZDOExtraData.ConnectionType.Portal)
        {
            ZDOMan.instance.SetDirtyPortals();
        }
    }
}
```

- `GetConnectionZDOID` — one-line delegate to `ZDOExtraData`; **inline
  candidate**, call only. This is the destination read.
- `SetConnection` — writes the connection, bumps `DataRevision`, and for
  a Portal connection flags `SetDirtyPortals`. This is the write the mod
  makes so vanilla `Teleport` jumps to the chosen portal. Small but has
  a branch; treat as call-only.
- `UpdateConnection` — same shape but via `ZDOExtraData.UpdateConnection`
  (the "one-sided / replace" variant vanilla uses to break a link with
  `ZDOID.None`).

`ZDOExtraData.ConnectionType.Portal` is the enum member the whole portal
system keys on.

### Tag keys (`ZDOVars`)

```csharp
public static readonly int s_tag = "tag".GetStableHashCode();

public static readonly int s_tagauthor = "tagauthor".GetStableHashCode();
```

These are the two vanilla tag ZDO keys. `s_tag` hashes the string
`"tag"`, `s_tagauthor` hashes `"tagauthor"`, both via
`StringExtensionMethods.GetStableHashCode` (below). `GetString(...)` /
`Set(...)` on those hashes are the tag read/write.

### Set / GetString

```csharp
public void Set(int hash, string value)
{
    if (ZDOExtraData.Set(m_uid, hash, value))
    {
        IncreaseDataRevision();
    }
}

public void Set(string name, string value)
{
    Set(name.GetStableHashCode(), value);
}

public string GetString(int hash, string defaultValue = "")
{
    return ZDOExtraData.GetString(m_uid, hash, defaultValue);
}

public string GetString(string name, string defaultValue = "")
{
    return GetString(name.GetStableHashCode(), defaultValue);
}
```

Every `Set` overload (float/int/long/vec3/quat/bool/byte[]/string) has
the same shape — delegate to `ZDOExtraData.Set`, bump the revision on
change; the `string`-name overloads just hash and forward. All are
one-liners → **inline candidates**; call them, don't patch. If the mod
stores its own metadata per portal, this is the store (pick a private
hashed key, not `s_tag`).

### Identity / owner / position

```csharp
public ZDOID m_uid = ZDOID.None;   // public field
private int m_prefab = -1;         // private field

public ZDOID GetZDOID(string name)
{
    return GetZDOID(GetHashZDOID(name));
}

public Vector3 GetPosition()
{
    return m_position;
}

public long GetOwner()
{
    if (!Owned)
    {
        return 0L;
    }
    return ZDOExtraData.GetOwner(m_uid);
}

public void SetOwner(long uid)
{
    if (ZDOExtraData.GetOwner(m_uid) != uid)
    {
        SetOwnerInternal(uid);
        IncreaseOwnerRevision();
    }
}

public int GetPrefab()
{
    return m_prefab;
}
```

- `m_uid` is a **public field** (`ZDOID`) — direct read, no accessor
  needed; this is the id the mod writes into another portal's connection.
- `m_prefab` is a **private field** (`int`, prefab stable hash, `-1`
  when unset); reachable via `GetPrefab()`. `GetPrefab` / `GetPosition`
  are one-line field reads → **inline candidates**, call only.
- `GetZDOID(string)` reads a ZDOID stored under a hashed key (two longs);
  **inline candidate**.
- `GetOwner` / `SetOwner` — the claim-before-write pair. `SetOwner`
  bumps `OwnerRevision`; combined with `GetSessionID()` this is
  "claim this portal's ZDO locally so my connection write wins," the
  same optimistic last-write-wins model documented in `containers.md`.
  `SetOwner` is call-only (small but branch + revision bump).

## `ZRoutedRpc` — server-authoritative broadcast

Full type read.

```csharp
public const long Everybody = 0L;

public static ZRoutedRpc instance => s_instance;

public void Register(string name, Action<long> f)
{
    m_functions.Add(name.GetStableHashCode(), new RoutedMethod(f));
}

public void InvokeRoutedRPC(long targetPeerID, string methodName, params object[] parameters)
{
    InvokeRoutedRPC(targetPeerID, ZDOID.None, methodName, parameters);
}
```

- `Everybody` is the constant `0L` — the broadcast target id (also used
  as `GetServerPeerID()`'s fallback and the "route to all" sentinel in
  `RouteRPC`). This is what the mod passes as `targetPeerID` to push a
  portal-list update to every peer.
- `instance` — static getter, **inline candidate**; read only.
- `Register(string, Action<long>)` (and the `<T>`…`<T,U,V,B,K,M>`
  generic overloads) hash the method name and store a `RoutedMethod`;
  one-liners → **inline candidates**, call at mod init to register the
  handler.
- `InvokeRoutedRPC(long, string, params object[])` is a one-line
  forward to the 4-arg `InvokeRoutedRPC(long, ZDOID, string, params
  object[])`; **inline candidate**. The real body lives in the 4-arg
  overload (builds `RoutedRPCData`, `methodName.GetStableHashCode()`,
  `ZRpc.Serialize`, then local-dispatch and/or `RouteRPC`). Call the
  2-arg overload with `ZRoutedRpc.Everybody` to broadcast; the server
  re-routes to all ready peers.

## Placement / destruction detection

XPortal detected a freshly placed portal by postfixing `Piece
.SetCreator` and then, a frame later, checking `WearNTear.m_createTime
== -1`. Both hooks are still present.

```csharp
// Piece
private long m_creator;

public void SetCreator(long uid, PlatformUserID platformUserID)
{
    if (!(m_nview == null) && m_nview.IsOwner() && GetCreator() == 0L)
    {
        m_creator = uid;
        m_nview.GetZDO().Set(ZDOVars.s_creator, uid);
        int value = (m_creatorPlatformUserIDIndex = ZNet.World.m_playerHistory.FindIndex((ZNet.CrossNetworkUserInfo a) => a.m_id == platformUserID));
        m_nview.GetZDO().Set(ZDOVars.s_creatorIndex, value);
    }
}

public long GetCreator()
{
    return m_creator;
}
```

`SetCreator` is `public`, only runs on the owner and only when no
creator is set yet (i.e. genuinely at placement), and it captures a
lambda for `m_playerHistory.FindIndex` — a real body with a closure, so
**not a plausible inline; safe patch target** (a postfix here is the
placement signal). `GetCreator` is a one-line field read → inline
candidate. `m_creator` is a private field.

```csharp
// WearNTear
private float m_createTime;

public void OnPlaced()
{
    m_createTime = -1f;
    m_clearCachedSupport = true;
}
```

Confirmed: `OnPlaced()` sets `m_createTime = -1f` (and flags
`m_clearCachedSupport`). `OnPlaced` is `public` but only two field
assignments — a **strong inline candidate; do not patch it**. Detect
placement via the `Piece.SetCreator` postfix and read `m_createTime` (a
private `float`, reflection needed) on the next frame to confirm the
piece is newly placed (`== -1f` before the first weathering update
overwrites it). `m_createTime` is set to `Time.time` in `Awake` and back
to `-1f` only by `OnPlaced`, so `-1f` is the "placed this frame" marker.

## Prefab identity

```csharp
// ZNetScene
public GameObject GetPrefab(int hash)
{
    if (m_namedPrefabs.TryGetValue(hash, out var value))
    {
        return value;
    }
    return null;
}

public GameObject GetPrefab(string name)
{
    return GetPrefab(name.GetStableHashCode());
}
```

`GetPrefab(int)` is a single `TryGetValue` → **inline candidate**, call
only. `ZNetScene.instance` is likewise a getter. `m_namedPrefabs` is
keyed by `prefab.name.GetStableHashCode()`. Use this to turn a portal
ZDO's `GetPrefab()` hash back into the prefab (e.g. to read its light /
model), or to resolve `"portal"` / `"portal_wood"` by name.

### `Game.m_portalPrefab` does **not** exist

There is no `Game.m_portalPrefab`. Vanilla has:

```csharp
// Game
public List<GameObject> m_portalPrefabs;

public List<int> PortalPrefabHash { get; private set; } = new List<int>();

// in Game.Awake:
foreach (GameObject portalPrefab in m_portalPrefabs)
{
    PortalPrefabHash.Add(portalPrefab.name.GetStableHashCode());
}
```

So the portal identity check is `Game.instance.PortalPrefabHash.Contains(
zdo.GetPrefab())` — that is exactly how `ZDOMan` decides what goes in
`m_portalObjects`. `m_portalPrefabs` is a Unity-serialized `List<
GameObject>` (its contents are asset references, not in the DLL);
`PortalPrefabHash` is the runtime list of their name hashes and is the
correct, forward-compatible way to ask "is this ZDO a portal?" —
covering both stone and wood without hardcoding names.

## `GetStableHashCode`

From `assembly_utils.dll` (`StringExtensionMethods`), since every ZDO
key / prefab name / RPC name is hashed through it:

```csharp
public static int GetStableHashCode(this string str)
{
    int num = 5381;
    int num2 = num;
    for (int i = 0; i < str.Length && str[i] != 0; i += 2)
    {
        num = ((num << 5) + num) ^ str[i];
        if (i == str.Length - 1 || str[i + 1] == '\0')
        {
            break;
        }
        num2 = ((num2 << 5) + num2) ^ str[i + 1];
    }
    return num + num2 * 1566083941;
}
```

Stable across runs, so a hash computed once (e.g. `"tag"
.GetStableHashCode()`) is a valid literal key.

## Things I could not verify

- **The prefab names `"portal"` (stone) and `"portal_wood"` (wood).**
  These are Unity asset names, not string literals in the assemblies —
  a binary grep of `assembly_valheim.dll` for `portal_wood` finds
  nothing, and no decompiled body references either name. They are
  well-known from the community but are *not* confirmable from the DLL.
  Do not hardcode them; identify portals through `Game.instance
  .PortalPrefabHash.Contains(zdo.GetPrefab())` (verified above), which
  is name-agnostic. If you must map a specific prefab, resolve it at
  runtime via `ZNetScene.instance.GetPrefab(name)` and confirm against a
  live game.
- **The portal light / emission colour.** `TeleportWorld` exposes
  `m_colorUnconnected` and `m_colorTargetfound` (`[ColorUsage(true,
  true)] Color`, default `Color.white` in source) and drives
  `m_model.material.SetColor("_EmissionColor", ...)` in `Update`, but
  the actual shipped colours are Unity-serialized inspector values that
  override the `Color.white` field initialisers and are **not in the
  DLLs**. Read them off a live `TeleportWorld` instance if the mod needs
  the real hues.
- **Exact IL sizes / whether the JIT actually inlines a given
  candidate.** The "inline candidate" flags above are eyeballed from
  body shape (one-line delegates, single dictionary lookups, two-field
  setters), not measured against the 32-byte heuristic or a JIT dump.
  Treat every flagged member as call-only and never a Harmony target;
  that is the safe reading whether or not the JIT inlines it on a given
  run.
- **`ZDOExtraData.SetConnection` / `UpdateConnection` / `GetConnectionZDOID`
  internals.** `ZDO`'s connection methods delegate to `ZDOExtraData`;
  the storage/behaviour of the connection table itself (how a Portal
  connection is persisted and whether `Update` vs `Set` differ beyond
  one-sided replacement) was read only by call site, not decompiled this
  session.
- **Whether writing the connection without also writing a matching tag
  survives vanilla's periodic reconnect.** `Game`'s server-side connect
  loop (`ConnectPortals` / `FindRandomUnconnectedPortal`) pairs
  *unconnected* portals **by shared `s_tag`**. It only acts on portals
  whose Portal connection `IsNone()`, so a portal the mod has already
  connected is skipped — but this was read statically, not exercised on
  a live server; confirm at runtime that a mod-set connection with a
  non-matching (or absent) tag is not later overwritten by vanilla's
  auto-connect.
