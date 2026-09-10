# Item Drawers for Valheim 1.0 — Design

Status: approved 2026-09-09
Supersedes: nothing. New project.

## 1. Why

Valheim 1.0 broke every item drawer mod, and none of them are coming back:

| Mod | Version | State |
|---|---|---|
| `makail/ItemDrawers` | 0.5.8 | Unmaintained. No source, no licence. 462 mods depend on it. |
| `KGvalheim/ItemDrawers` | 1.2.0 | Deprecated. Source public at `WernerCD/Valheim_ItemDrawers_KG`, no licence file. |
| `OdinPlus/ItemDrawers_Remake` | 0.0.6 | Deprecated. Source at `sbtoonz/item_drawers`. |

The likely cause of the breakage is that Valheim 1.0 moved its assets behind
`SoftReferenceableAssets` — 796 bundles, 4.1 GB, resolved through a soft-reference
manifest rather than sitting resident in `ZNetScene` at startup. Mods that grab
vanilla prefabs out of `ZNetScene` during `Awake` no longer find them. Anything we
build must load assets the 1.0 way. **This assumption is load-bearing and must be
verified first** — see §14.

We are writing a replacement rather than patching a corpse because two of the three
have no licence, and because the interesting requirements (below) are not what any
of them were designed for.

## 2. Requirements

Decided during brainstorming. These are settled, not open.

**Must**
- A drawer stores a large quantity of one item type and shows the item and the count
  on its front face.
- makail's control scheme, unchanged, because users know it:
  - Use item from hotbar → assign that item to an empty drawer
  - Interact → take one stack
  - Alt + Interact → take one item
  - Alt + Interact at zero → clear the drawer's item type
  - Shift + Interact → deposit every matching item in your inventory
- Auto-pickup: a drawer vacuums matching dropped items within a configurable radius.
- Three tiers — wood, stone, black marble — holding **1,000 / 2,000 / 10,000**.
- **OttoFuel can withdraw from drawers** to feed smelters, kilns, fireplaces.
- **NoVikingLeftBehind can craft from drawers**, consuming their contents.
- Correct on a **dedicated server with multiple players**. No duplication, no loss.
- A wall of 100 drawers costs near-zero frame time. This is a measured acceptance
  criterion, not an aspiration — see §12.
- Publishable to Thunderstore: clean-room assets, a licence, config sync.

**Won't** (this version)
- Per-drawer colour or per-drawer pickup range UI. KG had these; cut as YAGNI.
- Migration from makail or KG drawers. Fresh worlds only. The conversion is
  understood (KG's `ConvertMakailDrawers` shows the old ZDO format) and can be added
  later if users ask.
- Items with quality or durability. Drawers accept only items with
  `m_maxStackSize > 1`, which excludes equipment. Storing a sword would mean storing
  its quality, and a drawer holds one number.

## 3. Shape of the system

Two layers, because Valheim has no test harness and we want the interesting logic
under test.

```
ItemDrawers.Core          — pure C#, no UnityEngine, no Valheim. Unit tested.
  DrawerMeshBuilder       — proportions -> float[] vertices/normals/uvs
  IconAtlasPacker         — N icon sizes -> atlas rects
  DrawerState             — deposit/withdraw/clear rules, capacity clamping
  SpatialGrid             — cell hash for auto-pickup queries

ItemDrawers.Game          — thin Valheim/Unity adapter. Verified in game.
  DrawerPlugin            — BepInEx entry, config, Harmony
  DrawerPieces            — prefab + piece + recipe registration (Jotunn)
  DrawerComponent         — MonoBehaviour, ZDO owner, interaction
  DrawerRenderer          — shared mesh, atlas material, TextMeshPro count
  DrawerManager           — the single tick; auto-pickup; visual refresh
  ContainerBridge         — Harmony patches making drawers legible to other mods
```

`DrawerState` is the piece worth isolating: given a current item, a current amount, a
capacity and an action, it returns the new amount and what the player receives. That
is where duplication bugs would live, and it needs no game running to test.

## 4. Data model

A drawer's entire persistent state is two ZDO fields:

| Key | Type | Meaning |
|---|---|---|
| `Prefab` | string | Item prefab name. Empty string = drawer unassigned. |
| `Amount` | int | How many. Never exceeds the tier capacity. |

Nothing else is stored. A drawer holding 10 items and one holding 10,000 are
identical in memory, network traffic and save size. This is why capacity is cheap and
why 100 drawers cost what 100 chests cost.

Capacity is **not** stored — it is a property of the tier, read from config, so
raising a tier's capacity in config applies to drawers already built.

## 5. Geometry

Generated procedurally in C# from chamfered boxes. No asset bundle ships. The
proportions were settled interactively (see `ItemDrawers/docs/drawer-spec.md`) and are:

```csharp
Width = 1.000f;  Height = 1.000f;  Depth = 1.000f;
FrameThickness = 0.050f;  RecessDepth = 0.030f;  Bevel = 0.0060f;
HandleWidth = 0.400f;  HandleSection = 0.065f;  HandleProud = 0.035f;
LabelSize = 0.691f;  Handle = HandleStyle.Bar;
```

Construction, in order: a carcass box inset by `RecessDepth` at the front, so the
carcass's own front face *is* the recessed panel and the recess costs no extra
geometry; four frame planks standing proud around it; a bar handle across the lower
panel. Every box is chamfered by `Bevel` — small, but sharp 90° edges catch no
specular and read as computer-generated.

`LabelSize` is derived from the frame opening minus handle clearance. It is written
down as a constant for the record, but the builder recomputes it; if the proportions
change, the constant is stale and the computed value wins.

Materials are **vanilla Valheim materials**, not ours, so drawers inherit the game's
wear, wetness, moss and snow shading and sit correctly in the world under its
lighting. The three tiers are a material swap over one mesh, not three meshes.

One mesh instance per tier is built at startup and shared by every drawer.

## 6. Rendering, and the 100-drawer wall

The old mods gave every drawer its own world-space `Canvas` holding a UGUI `Image`
and a `TextMeshProUGUI`. Canvases do not batch across each other, so 100 drawers cost
~200 draw calls plus 100 entries in `Canvas.SendWillRenderCanvases` every frame. That
is the lag. We use no Canvas at all.

**Icon.** At startup, every item icon in `ObjectDB` is packed into one texture atlas
(`IconAtlasPacker` decides the rects; the adapter blits). Each drawer's label is a
four-vertex quad whose **UVs** address its item's cell in the atlas. All labels
therefore share a single unlit material and batch together. Assigning a different
item to a drawer rewrites four UVs in place — no allocation, no new material, no
atlas rebuild.

**Count.** 3D `TextMeshPro` (`MeshRenderer`-based), never `TextMeshProUGUI`.
`Unity.TextMeshPro.dll` ships with the game, so the 3D variant is available. One
shared font atlas material, so counts batch too.

**Ticking.** No drawer has an `Update` or an `InvokeRepeating`. `DrawerManager` owns
the only tick. Per frame it does nothing unless something is dirty.

**Distance.** Icon and count renderers are disabled beyond a configurable radius
(default 20 m). A wall of text nobody can read is wasted work.

Target: a 10×10 wall renders in a **single-digit number of draw calls** — one for the
bodies, one for the labels, one for the counts.

## 7. Interaction

`DrawerComponent` re-implements `Interactable` and `Hoverable` while deriving from
`Container` (§8). Re-declaring an interface in a derived class replaces the interface
mapping, so `((Interactable)drawer).Interact(...)` reaches our code and never
`Container`'s — the drawer does not open a chest window. KG's fork does exactly this
and shipped it to 200k+ users, so the technique is proven on this class.

Rules, all resolved in `DrawerState` and all clamped to capacity:

- Depositing into an unassigned drawer assigns its item type.
- Depositing a non-matching item is refused with a hover message, not silently eaten.
- Withdrawing from an empty drawer leaves the item type assigned, so a drained drawer
  keeps its label and its place in the wall.
- Clearing requires Alt + Interact **at zero**, so a full drawer cannot be cleared by
  a misclick.

## 8. The Container bridge

This is the crux of the project. Both required integrations discover storage by
finding `Container` components and reading `Inventory` objects. A drawer has no
inventory — it has an int.

**The drawer derives from `Container`**, so every container-aware mod finds it with no
knowledge of us. Behind that, `GetInventory()` returns a **mirror**: a real
`Inventory` holding exactly **one** `ItemData` whose `m_stack` is the entire count.
Four thousand coal is one stack of 4,000, not eighty stacks of fifty.

This works because `Inventory.CountItems` and `Inventory.RemoveItem` walk stacks
arithmetically, and nothing ever displays this inventory in a UI where an oversized
stack would look wrong. The alternative — materialising 200 real stacks per black marble
drawer — is 20,000 `ItemData` objects across a wall of 100, rebuilt on every scan.
That is the naive implementation and it is the one that would reintroduce the lag.

**Laziness.** The mirror is not maintained continuously. It refreshes only when the
ZDO's amount has changed since the mirror was last built, and the refresh mutates the
existing `ItemData.m_stack` in place rather than allocating. OttoFuel scanning your
whole wall becomes roughly 100 integer comparisons.

**Three Harmony patches on `Container`**, all guarded by a type check so vanilla
containers are unaffected:

- `GetInventory` postfix → refresh this drawer's mirror before returning it. Needed
  because `Container.GetInventory` is not virtual, so a subclass cannot override it.
- `Save` prefix → skip. Vanilla would serialise the mirror inventory into the ZDO's
  `items` field, writing a 10,000-item stack into the save on every autosave.
- `Load` prefix → skip. Vanilla would overwrite our state from that field.

Missing the `Save`/`Load` pair would bloat every save file and silently corrupt drawer
state. They are not optional.

**Write-back.** The mirror's `m_onChanged` fires when another mod removes from it. We
diff the mirror's total against the ZDO amount and apply the delta. Because removal
went through `Inventory.RemoveItem`, which cannot remove more than is present, the
delta is always valid.

**Public API.** A small static class (`ItemDrawersAPI`) lets future mods enumerate
drawers and query or withdraw deliberately, rather than inferring our behaviour from
the `Container` surface. Cheap to provide, and it is what we would have wanted from
makail.

**Fallback held in reserve.** If testing shows OttoFuel or NVLB does something the
generic bridge cannot satisfy, we add a targeted shim for that one case. We do not
adopt per-mod patching as a strategy — it would break on their next release and would
do nothing for the hundreds of container mods a Thunderstore audience will have.

## 9. Ownership and multiplayer

The rule: **claim ZDO ownership, then mutate.** Never write to a ZDO you do not own,
never RPC a mutation and assume it landed.

ZDO ownership is exclusive, so claiming it serialises concurrent mutations — two
players grabbing from the same drawer, or a player depositing while OttoFuel withdraws
from another client. The second actor sees post-first state rather than stale state.
This is how vanilla chests already behave, so it is well-trodden ground.

makail's 0.5.5 changelog reads "Fix network sync issue", and 0.5.4 "Fix initial load
of items in drawers". Both are the signature of getting this wrong. Designing for it
from the start is the point.

Visual state is derived, never authoritative: clients read `Prefab` and `Amount` off
the ZDO and update icon UVs and count text when the values change. A client showing a
stale count is a cosmetic bug that self-corrects; a client *writing* a stale count is
item duplication.

## 10. Auto-pickup

Naive: every drawer periodically queries for nearby dropped items. KG does this on a
2.5–3 s per-drawer timer, which at 100 drawers is ~35 `OverlapSphere` calls per second
forever, whether or not anything was ever dropped.

Ours inverts the loop. `DrawerManager` iterates **dropped items**, not drawers, and
matches each against a `SpatialGrid` of drawer positions. Cost scales with items on
the ground, which is normally zero. A wall of 100 drawers over a clean floor costs
nothing.

Only the client that owns a dropped item's ZDO may absorb it, so the same item cannot
be picked up twice by two clients.

## 11. Tiers, recipes, config

| Tier | Capacity | Recipe | Station |
|---|---|---|---|
| Wood | 1,000 | 10 Fine Wood | Workbench |
| Stone | 2,000 | 5 Fine Wood, 10 Stone | Workbench |
| Black Marble | 10,000 | 5 Fine Wood, 10 Black Marble | Workbench |

Costs are decided. Wood matches makail's original exactly; the two upper tiers
share a shape — half the fine wood, ten of the tier material — so the wood is
framing and the tier material is the visible face.

Built from the Hammer, Furniture tab. **All three use the Workbench**, which is a
call rather than a requirement: vanilla ties raw stone and black marble *building*
pieces to the Stonecutter, so following that convention would mean gating the two
upper tiers behind one. Treating a drawer as wood-framed furniture regardless of its
facing keeps a stone drawer buildable wherever a wood one is, which matters when the
normal use is a hundred of them in a wall. Say the word and it becomes Stonecutter.

Config through BepInEx, synced from server to clients so a dedicated server's
capacities and pickup radius govern everyone. Jotunn provides the sync. Every number
in this section is configurable; none is compiled in.

## 12. Testing and acceptance

**Unit tested** (`ItemDrawers.Core`, no game required):
- `DrawerState` — deposit, withdraw stack, withdraw one, clear, capacity clamping,
  wrong-item refusal, and every boundary at 0 and at capacity.
- `DrawerMeshBuilder` — vertex counts, outward-facing normals, watertightness,
  bevel degenerating cleanly at zero.
- `IconAtlasPacker` — no overlaps, everything inside bounds.
- `SpatialGrid` — query correctness against brute force.

**Verified in game**, via a debug console command that spawns an N×N wall:
- 10×10 wall visible: frame time within noise of the same scene without it.
- Same wall with OttoFuel and NVLB actively scanning: no measurable regression.
- Draw calls for the wall in single digits.
- Deposit and withdraw from two clients against a dedicated server, concurrently,
  with the total conserved.
- OttoFuel drains a coal drawer into a smelter.
- NVLB crafts using material held only in drawers.
- Drawer contents survive a server restart.

The 100-drawer wall is the whole reason for this rewrite, so it gets measured rather
than assumed.

## 13. Toolchain

- **.NET SDK is not installed** on this machine — runtimes only. Installing one is
  step zero.
- Target `netstandard2.1`.
- Reference publicised Valheim assemblies via `BepInEx.AssemblyPublicizer.MSBuild`,
  so nothing is committed from the game install.
- Jotunn 2.30.0 as a dependency — already proven working on 1.0 in the target profile.
  It carries prefab registration, piece and recipe registration, localisation and
  config sync, all of which are exactly the surfaces 1.0's soft-reference asset system
  disturbed.
- Thunderstore package: manifest, icon, README, changelog, and a licence. The geometry
  is ours, generated from arithmetic, so there is nothing encumbered to ship.

## 14. Risks

**The soft-reference assumption (§1) is unverified.** The claim that 1.0's asset
system is what killed the old mods is inference from the file layout, not from a
reproduction. If it is wrong, the diagnosis is wrong, though most of this design is
unaffected — we would simply have less to work around. *Verify before writing
anything else: build a do-nothing plugin that registers one piece and confirm it
appears in the Hammer.*

**Deriving from `Container` inherits `Container.Awake`.** Unity's `Awake` is not
virtual, and the interaction between a base and derived private `Awake` is subtle. We
construct the mirror `Inventory` ourselves rather than relying on `Container.Awake`
having run. Verify early which `Awake` actually executes.

**Patching `Container.GetInventory` touches a method other mods call.** The patch is
a type check and an early return for non-drawers, but it is on a shared surface. If
it proves hot, the fallback is to refresh mirrors from `DrawerManager` on ZDO change
and drop the patch, accepting up to one frame of staleness.

**Oversized stacks are unusual.** A stack of 4,000 in an `Inventory` is legal but not
something vanilla produces. Some third mod may assume `m_stack <= m_maxStackSize`.
Nothing in the two required integrations should care, but it is the design's most
novel assumption and the first thing to suspect if a third mod misbehaves.

**`ItemDrop` enumeration.** §10 assumes a cheap way to enumerate dropped items
globally. Confirm the mechanism on 1.0 before building auto-pickup on it; if none
exists, fall back to one manager-owned periodic query — still one query, not a
hundred.

## 15. Open questions

None blocking. Recipes (§11) are a proposal and can be argued about during
implementation without changing anything structural.
