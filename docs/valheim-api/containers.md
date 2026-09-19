# Containers: ownership and how inventory writes reach the ZDO

What this covers: how `Container` persists its inventory to its `ZDO`,
whether a non-owner's write to a container "sticks," what `ZNetView
.ClaimOwnership()` / `ZDO.SetOwner()` actually do on the wire, and
whether `ZDOMan` enforces ownership when it receives a ZDO update from
a peer. Written to settle whether claiming ownership before removing
items from a nearby container is a safe, normal idiom or a hazard.

Produced on 2026-09-18 by decompiling `assembly_valheim.dll` with
ilspycmd 8.2 (`8.2.0.7535-95108c96`). Game version from the assembly:
`Version.CurrentVersion = new GameVersion(1, 0, 14)`. `Container.cs`
read in full (509 lines); `ZNetView.cs` in full (373 lines); `ZDO.cs`
and `ZDOMan.cs` read in relevant part (ownership, `Set`, and the
`RPC_ZDOData` receive path).

## `Container.Save()` is gated on ownership, but that gate is easy to route around

```csharp
private void OnContainerChanged()
{
    if (!m_loading && IsOwner())
    {
        Save();
    }
}

private void Save()
{
    ZPackage zPackage = new ZPackage();
    m_inventory.Save(zPackage);
    byte[] array = zPackage.GetArray();
    m_nview.GetZDO().Set(ZDOVars.s_items, array);
    m_lastRevision = m_nview.GetZDO().DataRevision;
}
```

`Inventory.Changed()` fires `OnContainerChanged`, and vanilla only
calls `Save()` (which writes the serialized inventory into the ZDO)
when `IsOwner()` is true. This is the vanilla contract: a container's
inventory only ever reaches its ZDO through the owning peer. A
non-owner that mutates `Container`'s `Inventory` in place and never
calls `Save()` itself has a purely client-local change that vanishes
on the next `Load()` (triggered whenever `ZDO.DataRevision` moves past
`m_lastRevision`, e.g. from the real owner's next legitimate write).

`Save()` here is a **private** instance method. A caller outside
`Container` cannot invoke it without reflection (`BindingFlags
.NonPublic`) or unless it call `ZDO.Set(ZDOVars.s_items, bytes)`
directly, bypassing `Container` entirely. Either route sidesteps the
`IsOwner()` gate above — vanilla's own ownership check is on the
`Container` convenience method, not on the ZDO write itself.

## `ZDO.Set()` never checks ownership

```csharp
public void Set(int hash, byte[] bytes)
{
    if (ZDOExtraData.Set(m_uid, hash, bytes))
    {
        IncreaseDataRevision();
    }
}

private void IncreaseDataRevision()
{
    DataRevision++;
    if (!ZNet.instance.IsServer())
    {
        ZDOMan.instance.ClientChanged(m_uid);
    }
    ZDOMan.instance.SetDirtySector(this);
}
```

Any peer — owner or not — can call `ZDO.Set(...)` on any ZDO it has a
local copy of. The write always applies locally and always bumps
`DataRevision`, and on a client (non-`ZNet.instance.IsServer()`) it is
queued via `ClientChanged` for outbound sync regardless of ownership.
There is no `IsOwner()` check inside `ZDO.Set()` itself.

## The receiving side accepts a write by revision number, not by sender identity

`ZDOMan.RPC_ZDOData` is what a peer runs when it receives a batch of
ZDO updates from another peer (client→server or server→client):

```csharp
ZDOID zDOID = pkg.ReadZDOID();
...
ushort num3 = pkg.ReadUShort();      // incoming OwnerRevision
uint num4 = pkg.ReadUInt();          // incoming DataRevision
long ownerInternal = pkg.ReadLong(); // incoming owner uid, as the SENDER sees it
...
ZDO zDO = GetZDO(zDOID);
if (zDO != null)
{
    if (num4 <= zDO.DataRevision)
    {
        if (num3 > zDO.OwnerRevision)
        {
            zDO.SetOwnerInternal(ownerInternal);
            zDO.OwnerRevision = num3;
            ...
        }
        continue; // data payload NOT applied
    }
}
else { zDO = CreateNewZDO(zDOID, vector); ... }
zDO.OwnerRevision = num3;
zDO.DataRevision = num4;
zDO.SetOwnerInternal(ownerInternal);
zDO.InternalSetPosition(vector);
...
zDO.Deserialize(pkg2);   // the container's item bytes land here
```

The only gate on accepting a ZDO update — including its serialized
inventory bytes — is `num4 > zDO.DataRevision`, i.e. "is the incoming
revision newer than what I already have." **There is no check that
the sender is the ZDO's current owner.** A peer that has never owned a
given container can send it an update and have that update accepted
and rebroadcast, as long as its local `DataRevision` for that ZDO is
ahead of what the recipient currently holds. `GetOwner()`/`IsOwner()`
are advisory to every peer's own logic (vanilla's `Container` honors
it via the `OnContainerChanged` gate above; other code does not have
to), not enforced by `ZDOMan` on receipt.

## `ZNetView.ClaimOwnership()` / `ZDO.SetOwner()` are unilateral, not negotiated

```csharp
// ZNetView
public void ClaimOwnership()
{
    if (!IsOwner())
    {
        m_zdo.SetOwner(ZDOMan.GetSessionID());
    }
}

// ZDO
public void SetOwner(long uid)
{
    if (ZDOExtraData.GetOwner(m_uid) != uid)
    {
        SetOwnerInternal(uid);
        IncreaseOwnerRevision(); // bumps OwnerRevision, propagates like DataRevision
    }
}
```

Claiming ownership is a **local** write to this peer's own `ZDO`
instance, synchronous, with no round trip to the previous owner and no
server-side arbitration. It propagates to other peers the same way
any other ZDO change does — through the revision-number race described
above. Two peers claiming (or writing to) the same container in the
same sync window resolve by whichever update's revision number wins
the `RPC_ZDOData` comparison on each recipient; the loser's write is
silently dropped on that recipient, without an error either side can
observe.

## What this settles

- **A non-owner client CAN legitimately remove items from a container
  and have it stick**, and this is exactly the mechanism vanilla
  itself does not prevent: `ZDO.Set()` has no ownership check, and
  `RPC_ZDOData` accepts by revision number, not by sender identity.
  Claiming ownership first (`ZNetView.ClaimOwnership()` /
  `ZDO.SetOwner()`) is the documented, low-level idiom for "I intend to
  be the one whose writes to this ZDO win from here on," and vanilla
  systems that write to objects they don't already own (e.g. picking
  up or interacting with something owned by another peer) use the same
  call before writing.
- Claiming ownership is **not a lock**. It is optimistic and
  last-write-wins: if the previous owner (or a third peer) writes to
  the *same* ZDO in the same sync window — including if the container
  is open in someone else's inventory screen at that exact moment —
  the two writes race on `DataRevision`/`OwnerRevision` comparison on
  every recipient independently, and the losing write is discarded
  with no error raised to either peer. The narrower the write (claim
  immediately before the single `RemoveItem` + `Save()`, release
  nothing, don't hold it open) the narrower this window is, but it is
  never fully closed.
- A write made **without** claiming ownership first is the same kind
  of race but with the odds inverted: the container's legitimate owner
  need not be doing anything unusual — its own idle re-sync of an
  unrelated field, or simply the next time it opens the container,
  carries a `DataRevision` that was never advanced past the removal,
  so the removal can be silently reverted (items reappear) or, if the
  removal's `DataRevision` bump does win the race, it sticks despite
  never having gone through `Container`'s own `IsOwner()`-gated
  `Save()` path at all — vanilla's own container code never produces
  writes like this, so its behavior under that condition is genuinely
  undefined rather than merely unlikely.

## Claim-then-verify: what the verify actually proves

`ZNetView.ClaimOwnership()` → `ZDO.SetOwner()` → `SetOwnerInternal` is a
**synchronous local write**, so `nview.IsOwner()` read on the very next
line already reflects it. That makes `if (!IsOwner()) ClaimOwnership();
if (!IsOwner()) give up;` a meaningful guard, but only about the local
side: a false from the second `IsOwner()` means the claim could not even
be recorded locally (no `ZDO`, an invalid `ZNetView`, a peer id this
client does not have), which is exactly the case where a subsequent
write would be wasted. It proves **nothing** about remote peers — there
is no round trip in that window, so a concurrent claim or write
elsewhere is invisible to it and is still resolved later by the
revision-number race above.

The practical consequence for a write that must not silently under- or
over-apply: claim immediately before the single `RemoveItem` + `Save()`,
confirm `IsOwner()`, and treat a failed confirm as "this container pays
nothing" rather than writing anyway. `RossQoL.Game.Crafting.ChestCrafting
.Take` is the worked example.

## Things I could not verify

- Runtime frequency of the race window in practice — this is a static
  read of the revision-comparison logic, not a measurement of how
  often two peers actually write the same container's ZDO inside one
  sync interval on a live dedicated server.
- Whether `ZDOExtraData.Set` (the underlying byte-array store) does
  anything version- or type-specific beyond the equality check implied
  by its `bool` return (used to skip `IncreaseDataRevision` when the
  bytes are unchanged) — not decompiled this session.
- The exact `ZDOPeer.ShouldSend` predicate that decides whether a
  locally-changed ZDO makes it into the next outbound sync — read only
  by name/callsite (`zdo.DataRevision > value.m_dataRevision` when
  `OwnerRevision` hasn't regressed), not the full method body.
