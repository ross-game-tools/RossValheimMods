# ItemDrawers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Valheim 1.0 mod adding wall-mountable drawers that each hold a large quantity of one item, display it on the front, and can be read and drained by container-aware mods — with a hundred of them in a wall costing near-zero frame time.

**Architecture:** Two layers. `ItemDrawers.Core` is plain C# with no Unity or Valheim references and holds everything worth testing — the deposit/withdraw state machine, procedural mesh generation, icon atlas packing, and the spatial grid. `ItemDrawers.Game` is a thin BepInEx/Harmony adapter that binds that core to Valheim and is verified by playing the game. Drawer state is two ZDO fields, never an inventory; other mods see a `Container` whose inventory is synthesised on demand from those two fields.

**Tech Stack:** C# targeting `netstandard2.1`, BepInEx 5.4.2350, HarmonyX, Jotunn 2.30.0, xunit for the core tests, `BepInEx.AssemblyPublicizer.MSBuild` for Valheim references.

**Spec:** `ItemDrawers/docs/superpowers/specs/2026-09-09-item-drawers-design.md`

**Proportions:** `ItemDrawers/docs/drawer-spec.md`

**Execution order** (amended after a pre-flight conflict scan; task numbering
below is unchanged): 1-7, then 13 (DrawerConfig only), then 9's atlas and
renderer classes, then **8 and 10 together as one unit**, then 11, 12, the
rest of 13, and 14-16. DrawerComponent and DrawerManager are mutually
referential by design, so neither compiles alone; the config and renderer
moves are plain forward references that reordering resolves.

## Global Constraints

Every task's requirements implicitly include this section. Values are copied verbatim from the spec — do not adjust them while implementing.

- **Target framework:** `netstandard2.1` for both shipped assemblies. Test project targets `net8.0`.
- **Dependencies:** BepInEx 5.4.2350, Jotunn 2.30.0. No other runtime dependency ships. Valheim assemblies are referenced through `BepInEx.AssemblyPublicizer.MSBuild` and are **never committed** to the repository.
- **`ItemDrawers.Core` must not reference `UnityEngine`, `Assembly-CSharp`, `BepInEx`, or `Jotunn`.** This is enforced by a test in Task 2 and is the property that makes the core testable. If a task tempts you to break it, the design is wrong — stop and raise it.
- **ZDO keys:** exactly two. `Prefab` (string, `""` means unassigned) and `Amount` (int). Nothing else is persisted per drawer.
- **Capacities:** Wood 1,000 / Stone 2,000 / Black Marble 10,000. Configurable; these are defaults.
- **Recipes:** Wood = 10 Fine Wood. Stone = 5 Fine Wood + 10 Stone. Black Marble = 5 Fine Wood + 10 Black Marble. All three built from the Hammer, Furniture tab, at a **Workbench**.
- **Prefab names:** `rid_drawer_wood`, `rid_drawer_stone`, `rid_drawer_blackmarble`. These are written into save files and are therefore permanent — changing one after release orphans every drawer players have built. Do not rename them casually.
- **Plugin GUID:** `com.rossdwest.itemdrawers`. **Plugin name:** `ItemDrawers`.
- **Accepted items:** only items whose `m_maxStackSize > 1`. This excludes equipment, whose quality and durability a single integer cannot carry.
- **Controls** (makail's original scheme, unchanged):
  | Input | Effect |
  |---|---|
  | Use item from hotbar | Assign that item to an empty drawer |
  | Interact | Take one stack |
  | Alt + Interact | Take one item |
  | Alt + Interact at zero | Clear the drawer's item type |
  | Shift + Interact | Deposit every matching item in your inventory |
- **Mutation rule:** claim ZDO ownership, *then* write. Never write to a ZDO you do not own. Never RPC a mutation and assume it landed.
- **Performance target:** a 10×10 wall renders in a single-digit number of draw calls, and its frame cost is within noise of the same scene without it — including while OttoFuel and NoVikingLeftBehind are actively scanning. Measured in Task 13, not assumed.
- **Rendering prohibition:** no `Canvas`, no `TextMeshProUGUI`, no per-drawer `Update()`, no per-drawer `InvokeRepeating`. These are what made the old mods lag. `DrawerManager` owns the only tick.
- **Every drawer prefab MUST set these four fields.** The Unity editor sets them on
  vanilla prefabs; a component created in code does not, and each one silently
  masks the next. Verified in game on 2026-09-10:
  | Field | Required value | If omitted |
  |---|---|---|
  | `Piece` component | present | Jotunn `IsValid()` rejects the prefab |
  | `Piece.m_icon` | a real Sprite | `IsValid()` rejects it; an icon is mandatory |
  | `Piece.m_enabled` | `true` | Never enters `m_availablePieces`; no tab, never a known recipe |
  | `Piece.m_usage` | `Furniture \| Storage` | Invisible in every 1.0 tab except "show all" |
  `m_category` no longer drives the build menu on 1.0 — `m_usage` does. Set
  `m_category` anyway for compatibility, but never rely on it for visibility.
  `Furniture \| Storage` is what `piece_chest_wood` uses, confirmed at runtime.

---

## File Structure

```
ItemDrawers/
  ItemDrawers.sln
  src/
    ItemDrawers.Core/
      ItemDrawers.Core.csproj
      DrawerProportions.cs      approved geometry constants
      MeshData.cs               plain float arrays, no Unity types
      DrawerMeshBuilder.cs      chamfered-box generation
      DrawerSnapshot.cs         (item, amount) value type
      DrawerOutcome.cs          result of an action
      DrawerState.cs            deposit/withdraw/clear rules  <- the important one
      AtlasRect.cs              packing result types
      IconAtlasPacker.cs        shelf packer
      SpatialGrid.cs            cell hash for auto-pickup
    ItemDrawers.Game/
      ItemDrawers.Game.csproj
      DrawerPlugin.cs           BepInEx entry, config, Harmony bootstrap
      DrawerConfig.cs           config binding + server sync
      DrawerTier.cs             tier enum, capacities, prefab names, recipes
      DrawerPieces.cs           prefab construction + piece/recipe registration
      DrawerComponent.cs        MonoBehaviour: ZDO state, interaction
      DrawerRenderer.cs         shared mesh, atlas material, count text
      DrawerIconAtlas.cs        builds the runtime atlas from ObjectDB
      DrawerManager.cs          the single tick; visuals; auto-pickup
      ContainerBridge.cs        Harmony patches: GetInventory/Save/Load
      ItemDrawersAPI.cs         public static surface for other mods
      DebugCommands.cs          spawn-wall command, perf readout
  tests/
    ItemDrawers.Core.Tests/
      ItemDrawers.Core.Tests.csproj
      DrawerStateTests.cs
      DrawerMeshBuilderTests.cs
      IconAtlasPackerTests.cs
      SpatialGridTests.cs
      ArchitectureTests.cs      asserts Core references no game assemblies
  thunderstore/
    manifest.json
    icon.png
    README.md
    CHANGELOG.md
```

---

## Task 1: Toolchain, solution, and the piece-registration spike

This task exists to retire the spec's biggest risk (§14) before any effort is invested behind it. The spec asserts that Valheim 1.0's `SoftReferenceableAssets` system is what killed the old drawer mods. That is inference from the file layout, not a reproduction. **If a placeholder cube will not appear in the Hammer's furniture tab, everything downstream is built on a wrong diagnosis** — so we find out first.

**Files:**
- Create: `ItemDrawers/ItemDrawers.sln`
- Create: `ItemDrawers/src/ItemDrawers.Game/ItemDrawers.Game.csproj`
- Create: `ItemDrawers/src/ItemDrawers.Game/DrawerPlugin.cs`
- Create: `ItemDrawers/Directory.Build.props`

**Interfaces:**
- Consumes: nothing.
- Produces: a loadable BepInEx plugin with GUID `com.rossdwest.itemdrawers`; a known-good build command; a verified answer to whether Jotunn piece registration works on Valheim 1.0.

- [ ] **Step 1: Install a .NET SDK**

The machine has runtimes only — `dotnet --list-sdks` is empty. Install one:

```bash
winget install --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
```

Then open a **new** shell (the installer edits PATH) and verify:

```bash
dotnet --list-sdks
```

Expected: at least one line, e.g. `8.0.xxx [C:\Program Files\dotnet\sdk]`.

- [ ] **Step 2: Create the solution and game project**

```bash
cd ItemDrawers
dotnet new sln -n ItemDrawers
mkdir -p src/ItemDrawers.Game
```

`src/ItemDrawers.Game/ItemDrawers.Game.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>ItemDrawers</AssemblyName>
    <RootNamespace>ItemDrawers.Game</RootNamespace>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
    <Version>0.1.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Analyzers" Version="1.*" PrivateAssets="all" />
    <PackageReference Include="BepInEx.Core" Version="5.*" PrivateAssets="all" />
    <PackageReference Include="BepInEx.AssemblyPublicizer.MSBuild" Version="0.4.*" PrivateAssets="all" />
    <PackageReference Include="JotunnLib" Version="2.30.0" PrivateAssets="all" />
    <PackageReference Include="UnityEngine.Modules" Version="2022.3.*" IncludeAssets="compile" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="assembly_valheim">
      <HintPath>$(VALHEIM_INSTALL)\valheim_Data\Managed\assembly_valheim.dll</HintPath>
      <Publicize>true</Publicize>
      <Private>false</Private>
    </Reference>
    <Reference Include="assembly_utils">
      <HintPath>$(VALHEIM_INSTALL)\valheim_Data\Managed\assembly_utils.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Unity.TextMeshPro">
      <HintPath>$(VALHEIM_INSTALL)\valheim_Data\Managed\Unity.TextMeshPro.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

**Verify the assembly names before building.** The spec assumed `assembly_valheim.dll` from KG's 2023-era reference set; Valheim 1.0's `Managed` folder listing showed `Assembly-CSharp.dll`. List the folder and use whichever actually exists:

```bash
ls "$VALHEIM_INSTALL/valheim_Data/Managed" | grep -iE "assembly|valheim"
```

If it is `Assembly-CSharp.dll`, change the `Include` and `HintPath` accordingly. Record what you found in the commit message — later tasks depend on it.

`ItemDrawers/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <VALHEIM_INSTALL Condition="'$(VALHEIM_INSTALL)' == ''">C:\Program Files (x86)\Steam\steamapps\common\Valheim</VALHEIM_INSTALL>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write the minimal plugin**

`src/ItemDrawers.Game/DrawerPlugin.cs`:

```csharp
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ItemDrawers.Game
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class DrawerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.rossdwest.itemdrawers";
        public const string PluginName = "ItemDrawers";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            PrefabManager.OnVanillaPrefabsAvailable += RegisterSpikePiece;
            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        // Deliberately a bare cube. This task is asking one question:
        // does piece registration still work on Valheim 1.0?
        private void RegisterSpikePiece()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterSpikePiece;

            var go = new GameObject("rid_drawer_spike");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(go.transform, false);

            var piece = new CustomPiece(go, fixReference: false, new PieceConfig
            {
                Name = "Drawer (spike)",
                Description = "Verifying 1.0 piece registration",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Furniture,
                Requirements = new[] { new RequirementConfig("FineWood", 1, 0, true) }
            });

            PieceManager.Instance.AddPiece(piece);
            Log.LogInfo("Spike piece registered");
        }
    }
}
```

- [ ] **Step 4: Build**

```bash
cd ItemDrawers
dotnet sln add src/ItemDrawers.Game/ItemDrawers.Game.csproj
dotnet build -c Release
```

Expected: build succeeds, producing `src/ItemDrawers.Game/bin/Release/netstandard2.1/ItemDrawers.dll`.

If the Jotunn or UnityEngine package references fail to resolve, that is information — Jotunn's NuGet package name and Unity reference strategy may differ from what is written here. Fix it against Jotunn's current documentation and record the correction in the commit message.

- [ ] **Step 5: Verify in game — this is the actual point of the task**

Copy the built DLL into the r2modman profile and launch:

```bash
cp src/ItemDrawers.Game/bin/Release/netstandard2.1/ItemDrawers.dll \
  "/c/Users/ross/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/dev/BepInEx/plugins/"
```

Launch Valheim through r2modman, load a world, equip the Hammer, open the Furniture tab.

Expected: a "Drawer (spike)" entry appears and places a white cube.

Check `BepInEx/LogOutput.log` for `ItemDrawers 0.1.0 loaded` and `Spike piece registered`.

**If the piece does not appear**, stop and report before continuing. The likely causes, in order: Jotunn incompatibility with this Valheim build; the `OnVanillaPrefabsAvailable` hook firing before soft-referenced assets resolve; or the assembly reference being wrong. Whichever it is, the spec's §14 diagnosis needs revising and later tasks may need reshaping.

- [ ] **Step 6: Commit**

```bash
git add ItemDrawers/ItemDrawers.sln ItemDrawers/Directory.Build.props ItemDrawers/src ItemDrawers/.gitignore
git commit -m "Prove piece registration works on Valheim 1.0

Bare cube in the Hammer furniture tab, which is the only question this
commit is asking. The spec's central risk was that 1.0's soft-reference
asset system broke the registration path every old drawer mod used;
this settles it before anything is built on top."
```

---

## Task 2: Core project and the architecture guard

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Core/ItemDrawers.Core.csproj`
- Create: `ItemDrawers/tests/ItemDrawers.Core.Tests/ItemDrawers.Core.Tests.csproj`
- Create: `ItemDrawers/tests/ItemDrawers.Core.Tests/ArchitectureTests.cs`

**Interfaces:**
- Consumes: the solution from Task 1.
- Produces: `dotnet test` runs green; the `ItemDrawers.Core` assembly, which every later core task adds to.

- [ ] **Step 1: Write the failing test**

`tests/ItemDrawers.Core.Tests/ArchitectureTests.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class ArchitectureTests
    {
        // The core is testable precisely because it knows nothing about the
        // game. If this ever fails, the design has drifted, not the test.
        [Fact]
        public void Core_references_no_game_or_engine_assemblies()
        {
            var forbidden = new[] { "UnityEngine", "Assembly-CSharp", "assembly_valheim", "BepInEx", "Jotunn", "0Harmony" };

            var core = typeof(DrawerProportions).Assembly;
            var referenced = core.GetReferencedAssemblies().Select(a => a.Name).ToArray();

            var violations = referenced
                .Where(name => forbidden.Any(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            Assert.Empty(violations);
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
cd ItemDrawers && dotnet test
```

Expected: compile error — `DrawerProportions` does not exist, and neither project exists yet.

- [ ] **Step 3: Create both projects and the type the test names**

`src/ItemDrawers.Core/ItemDrawers.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <RootNamespace>ItemDrawers.Core</RootNamespace>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
```

`src/ItemDrawers.Core/DrawerProportions.cs` — the values approved in `docs/drawer-spec.md`:

```csharp
namespace ItemDrawers.Core
{
    public enum HandleStyle { None, Bar, Knobs, Pull }

    /// <summary>Drawer geometry, in metres. Settled interactively; see docs/drawer-spec.md.</summary>
    public sealed class DrawerProportions
    {
        public float Width = 1.000f;
        public float Height = 1.000f;
        public float Depth = 1.000f;
        public float FrameThickness = 0.050f;
        public float RecessDepth = 0.030f;
        public float Bevel = 0.0060f;
        public float HandleWidth = 0.400f;
        public float HandleSection = 0.065f;
        public float HandleProud = 0.035f;
        public HandleStyle Handle = HandleStyle.Bar;

        /// <summary>How much of the frame opening the icon fills. Approved at 0.98.</summary>
        public float LabelScale = 0.98f;

        /// <summary>
        /// Label size is derived, not free: the frame opening minus handle
        /// clearance, times LabelScale. docs/drawer-spec.md records 0.691 for
        /// the approved proportions; this recomputes it so the two cannot drift.
        /// </summary>
        public float LabelSize
        {
            get
            {
                float openW = Width - 2f * FrameThickness;
                float openH = Height - 2f * FrameThickness;
                if (Handle == HandleStyle.Bar) openH -= HandleSection * 3.0f;
                float min = openW < openH ? openW : openH;
                float size = min * LabelScale;
                return size < 0.05f ? 0.05f : size;
            }
        }
    }
}
```

`tests/ItemDrawers.Core.Tests/ItemDrawers.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>ItemDrawers.Core.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ItemDrawers.Core\ItemDrawers.Core.csproj" />
  </ItemGroup>
</Project>
```

```bash
cd ItemDrawers
dotnet sln add src/ItemDrawers.Core/ItemDrawers.Core.csproj
dotnet sln add tests/ItemDrawers.Core.Tests/ItemDrawers.Core.Tests.csproj
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd ItemDrawers && dotnet test
```

Expected: 1 passed.

- [ ] **Step 5: Add a LabelSize regression test**

Append to `ArchitectureTests.cs` — actually create `tests/ItemDrawers.Core.Tests/DrawerProportionsTests.cs`:

```csharp
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class DrawerProportionsTests
    {
        [Fact]
        public void Approved_proportions_produce_the_recorded_label_size()
        {
            // docs/drawer-spec.md records 0.691 for these proportions.
            // If this fails, either the proportions changed or the derivation did.
            var p = new DrawerProportions();
            Assert.Equal(0.691f, p.LabelSize, 3);
        }

        [Fact]
        public void Label_never_collapses_below_the_floor()
        {
            var p = new DrawerProportions { FrameThickness = 0.49f };
            Assert.True(p.LabelSize >= 0.05f);
        }
    }
}
```

Run `dotnet test`. Expected: 3 passed. If the first assertion fails, the derivation in `LabelSize` does not match the tool that produced 0.691 — reconcile them before moving on rather than editing the expected value.

- [ ] **Step 6: Commit**

```bash
git add ItemDrawers/src/ItemDrawers.Core ItemDrawers/tests ItemDrawers/ItemDrawers.sln
git commit -m "Add pure core project with an architecture guard

The guard is the load-bearing test: the core is testable only for as
long as it knows nothing about Unity, Valheim, BepInEx or Jotunn, so
that property is asserted rather than hoped for."
```

---

## Task 3: DrawerState — the deposit and withdraw rules

The highest-value code in the project. Every item duplication or loss bug lives here, and none of it needs a game running.

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Core/DrawerSnapshot.cs`
- Create: `ItemDrawers/src/ItemDrawers.Core/DrawerOutcome.cs`
- Create: `ItemDrawers/src/ItemDrawers.Core/DrawerState.cs`
- Test: `ItemDrawers/tests/ItemDrawers.Core.Tests/DrawerStateTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces:
  - `readonly struct DrawerSnapshot { string ItemName; int Amount; bool IsAssigned; bool IsEmpty; }`
  - `readonly struct DrawerOutcome { bool Accepted; DrawerSnapshot Result; int MovedToDrawer; int MovedToPlayer; string Rejection; }`
  - `static class DrawerState` with `Deposit`, `WithdrawStack`, `WithdrawOne`, `Clear` — signatures in Step 3.

- [ ] **Step 1: Write the failing tests**

`tests/ItemDrawers.Core.Tests/DrawerStateTests.cs`:

```csharp
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class DrawerStateTests
    {
        private static DrawerSnapshot Empty => new DrawerSnapshot("", 0);
        private static DrawerSnapshot Wood(int n) => new DrawerSnapshot("Wood", n);

        // ---------- assignment ----------

        [Fact]
        public void Depositing_into_an_unassigned_drawer_assigns_the_item()
        {
            var r = DrawerState.Deposit(Empty, capacity: 1000, itemName: "Wood", offered: 20);

            Assert.True(r.Accepted);
            Assert.Equal("Wood", r.Result.ItemName);
            Assert.Equal(20, r.Result.Amount);
            Assert.Equal(20, r.MovedToDrawer);
        }

        [Fact]
        public void Depositing_a_different_item_is_refused_with_a_reason()
        {
            var r = DrawerState.Deposit(Wood(50), capacity: 1000, itemName: "Stone", offered: 10);

            Assert.False(r.Accepted);
            Assert.Equal(0, r.MovedToDrawer);
            Assert.Equal(Wood(50), r.Result);          // unchanged
            Assert.False(string.IsNullOrEmpty(r.Rejection));
        }

        // ---------- capacity ----------

        [Fact]
        public void Deposit_clamps_to_capacity_and_reports_what_actually_moved()
        {
            var r = DrawerState.Deposit(Wood(995), capacity: 1000, itemName: "Wood", offered: 20);

            Assert.True(r.Accepted);
            Assert.Equal(1000, r.Result.Amount);
            Assert.Equal(5, r.MovedToDrawer);          // the player keeps the other 15
        }

        [Fact]
        public void Depositing_into_a_full_drawer_moves_nothing_and_is_refused()
        {
            var r = DrawerState.Deposit(Wood(1000), capacity: 1000, itemName: "Wood", offered: 20);

            Assert.False(r.Accepted);
            Assert.Equal(0, r.MovedToDrawer);
            Assert.Equal(1000, r.Result.Amount);
        }

        [Fact]
        public void Deposit_of_zero_or_negative_is_refused()
        {
            Assert.False(DrawerState.Deposit(Wood(10), 1000, "Wood", 0).Accepted);
            Assert.False(DrawerState.Deposit(Wood(10), 1000, "Wood", -5).Accepted);
        }

        [Fact]
        public void Deposit_cannot_overflow_when_capacity_is_enormous()
        {
            var r = DrawerState.Deposit(Wood(int.MaxValue - 10), capacity: int.MaxValue,
                                        itemName: "Wood", offered: 1000);

            Assert.True(r.Result.Amount > 0);          // never wraps negative
            Assert.Equal(int.MaxValue, r.Result.Amount);
            Assert.Equal(10, r.MovedToDrawer);
        }

        // ---------- withdraw ----------

        [Fact]
        public void Withdraw_stack_takes_a_full_stack_when_there_is_one()
        {
            var r = DrawerState.WithdrawStack(Wood(120), maxStackSize: 50);

            Assert.True(r.Accepted);
            Assert.Equal(50, r.MovedToPlayer);
            Assert.Equal(70, r.Result.Amount);
        }

        [Fact]
        public void Withdraw_stack_takes_the_remainder_when_less_than_a_stack()
        {
            var r = DrawerState.WithdrawStack(Wood(7), maxStackSize: 50);

            Assert.True(r.Accepted);
            Assert.Equal(7, r.MovedToPlayer);
            Assert.Equal(0, r.Result.Amount);
        }

        [Fact]
        public void A_drained_drawer_keeps_its_item_type_and_its_place_in_the_wall()
        {
            var r = DrawerState.WithdrawStack(Wood(7), maxStackSize: 50);

            Assert.Equal("Wood", r.Result.ItemName);
            Assert.True(r.Result.IsAssigned);
            Assert.True(r.Result.IsEmpty);
        }

        [Fact]
        public void Withdrawing_from_an_empty_drawer_is_refused_and_changes_nothing()
        {
            var r = DrawerState.WithdrawStack(Wood(0), maxStackSize: 50);

            Assert.False(r.Accepted);
            Assert.Equal(0, r.MovedToPlayer);
            Assert.Equal("Wood", r.Result.ItemName);
        }

        [Fact]
        public void Withdraw_one_takes_exactly_one()
        {
            var r = DrawerState.WithdrawOne(Wood(3));

            Assert.True(r.Accepted);
            Assert.Equal(1, r.MovedToPlayer);
            Assert.Equal(2, r.Result.Amount);
        }

        [Fact]
        public void Withdraw_one_from_an_unassigned_drawer_is_refused()
        {
            Assert.False(DrawerState.WithdrawOne(Empty).Accepted);
        }

        // ---------- clear ----------

        [Fact]
        public void Clearing_an_empty_drawer_unassigns_it()
        {
            var r = DrawerState.Clear(Wood(0));

            Assert.True(r.Accepted);
            Assert.Equal("", r.Result.ItemName);
            Assert.False(r.Result.IsAssigned);
        }

        [Fact]
        public void Clearing_a_drawer_that_still_holds_items_is_refused()
        {
            // Guards against wiping a full drawer with a misclick.
            var r = DrawerState.Clear(Wood(1));

            Assert.False(r.Accepted);
            Assert.Equal("Wood", r.Result.ItemName);
            Assert.Equal(1, r.Result.Amount);
        }

        // ---------- external withdrawal (the Container bridge path) ----------

        [Fact]
        public void External_withdrawal_cannot_take_more_than_is_present()
        {
            var r = DrawerState.WithdrawExact(Wood(40), requested: 100);

            Assert.Equal(40, r.MovedToPlayer);
            Assert.Equal(0, r.Result.Amount);
        }

        [Fact]
        public void External_withdrawal_of_a_negative_amount_moves_nothing()
        {
            var r = DrawerState.WithdrawExact(Wood(40), requested: -3);

            Assert.Equal(0, r.MovedToPlayer);
            Assert.Equal(40, r.Result.Amount);
        }
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
cd ItemDrawers && dotnet test --filter DrawerStateTests
```

Expected: compile failure — `DrawerState`, `DrawerSnapshot`, `DrawerOutcome` do not exist.

- [ ] **Step 3: Write the implementation**

`src/ItemDrawers.Core/DrawerSnapshot.cs`:

```csharp
using System;

namespace ItemDrawers.Core
{
    /// <summary>A drawer's entire persistent state: one item name and one count.</summary>
    public readonly struct DrawerSnapshot : IEquatable<DrawerSnapshot>
    {
        public readonly string ItemName;
        public readonly int Amount;

        public DrawerSnapshot(string itemName, int amount)
        {
            ItemName = itemName ?? "";
            Amount = amount < 0 ? 0 : amount;
        }

        public bool IsAssigned => !string.IsNullOrEmpty(ItemName);
        public bool IsEmpty => Amount <= 0;

        public bool Equals(DrawerSnapshot other) =>
            string.Equals(ItemName, other.ItemName, StringComparison.Ordinal) && Amount == other.Amount;

        public override bool Equals(object obj) => obj is DrawerSnapshot s && Equals(s);
        public override int GetHashCode() => (ItemName?.GetHashCode() ?? 0) * 397 ^ Amount;
        public override string ToString() => IsAssigned ? $"{ItemName} x{Amount}" : "(empty drawer)";
    }
}
```

`src/ItemDrawers.Core/DrawerOutcome.cs`:

```csharp
namespace ItemDrawers.Core
{
    public readonly struct DrawerOutcome
    {
        public readonly bool Accepted;
        public readonly DrawerSnapshot Result;
        public readonly int MovedToDrawer;
        public readonly int MovedToPlayer;
        public readonly string Rejection;

        private DrawerOutcome(bool accepted, DrawerSnapshot result, int toDrawer, int toPlayer, string rejection)
        {
            Accepted = accepted;
            Result = result;
            MovedToDrawer = toDrawer;
            MovedToPlayer = toPlayer;
            Rejection = rejection;
        }

        public static DrawerOutcome Deposited(DrawerSnapshot result, int moved) =>
            new DrawerOutcome(true, result, moved, 0, null);

        public static DrawerOutcome Withdrew(DrawerSnapshot result, int moved) =>
            new DrawerOutcome(true, result, 0, moved, null);

        public static DrawerOutcome Changed(DrawerSnapshot result) =>
            new DrawerOutcome(true, result, 0, 0, null);

        public static DrawerOutcome Refused(DrawerSnapshot unchanged, string why) =>
            new DrawerOutcome(false, unchanged, 0, 0, why);
    }
}
```

`src/ItemDrawers.Core/DrawerState.cs`:

```csharp
namespace ItemDrawers.Core
{
    /// <summary>
    /// Every rule governing what a drawer will accept and release. Pure: no
    /// game, no network, no side effects. The caller is responsible for
    /// having claimed ZDO ownership before applying an outcome.
    /// </summary>
    public static class DrawerState
    {
        public const string WrongItem = "This drawer holds a different item";
        public const string DrawerFull = "This drawer is full";
        public const string DrawerEmpty = "This drawer is empty";
        public const string NotAssigned = "This drawer is empty";
        public const string NotEmptyYet = "Empty the drawer before clearing it";
        public const string NothingOffered = "Nothing to deposit";

        public static DrawerOutcome Deposit(DrawerSnapshot current, int capacity, string itemName, int offered)
        {
            if (offered <= 0) return DrawerOutcome.Refused(current, NothingOffered);
            if (string.IsNullOrEmpty(itemName)) return DrawerOutcome.Refused(current, NothingOffered);

            if (current.IsAssigned && current.ItemName != itemName)
                return DrawerOutcome.Refused(current, WrongItem);

            int room = capacity - current.Amount;
            if (room <= 0) return DrawerOutcome.Refused(current, DrawerFull);

            int moved = offered < room ? offered : room;
            return DrawerOutcome.Deposited(new DrawerSnapshot(itemName, current.Amount + moved), moved);
        }

        public static DrawerOutcome WithdrawStack(DrawerSnapshot current, int maxStackSize)
        {
            if (!current.IsAssigned) return DrawerOutcome.Refused(current, NotAssigned);
            if (current.IsEmpty) return DrawerOutcome.Refused(current, DrawerEmpty);

            int stack = maxStackSize < 1 ? 1 : maxStackSize;
            int moved = current.Amount < stack ? current.Amount : stack;
            return DrawerOutcome.Withdrew(new DrawerSnapshot(current.ItemName, current.Amount - moved), moved);
        }

        public static DrawerOutcome WithdrawOne(DrawerSnapshot current)
        {
            if (!current.IsAssigned) return DrawerOutcome.Refused(current, NotAssigned);
            if (current.IsEmpty) return DrawerOutcome.Refused(current, DrawerEmpty);

            return DrawerOutcome.Withdrew(new DrawerSnapshot(current.ItemName, current.Amount - 1), 1);
        }

        /// <summary>
        /// Withdrawal on behalf of another mod through the Container bridge.
        /// Silently clamps rather than refusing, because the caller has
        /// already committed to taking what it can get.
        /// </summary>
        public static DrawerOutcome WithdrawExact(DrawerSnapshot current, int requested)
        {
            if (requested <= 0 || !current.IsAssigned || current.IsEmpty)
                return DrawerOutcome.Withdrew(current, 0);

            int moved = requested < current.Amount ? requested : current.Amount;
            return DrawerOutcome.Withdrew(new DrawerSnapshot(current.ItemName, current.Amount - moved), moved);
        }

        public static DrawerOutcome Clear(DrawerSnapshot current)
        {
            if (!current.IsEmpty) return DrawerOutcome.Refused(current, NotEmptyYet);
            return DrawerOutcome.Changed(new DrawerSnapshot("", 0));
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd ItemDrawers && dotnet test --filter DrawerStateTests
```

Expected: 16 passed.

Note the overflow test: `capacity - current.Amount` with `capacity = int.MaxValue` and a large amount stays positive, so `room` is correct and no wrap occurs. If that test fails, the arithmetic needs widening to `long` internally — fix it rather than relaxing the test.

- [ ] **Step 5: Commit**

```bash
git add ItemDrawers/src/ItemDrawers.Core ItemDrawers/tests
git commit -m "Add drawer deposit/withdraw rules under test

This is where item duplication and loss bugs live, so it is the part
that runs without a game. Notable rules: a drained drawer keeps its
item type so it holds its place in a wall, and clearing is refused
unless the drawer is already empty so a full one survives a misclick."
```

---

## Task 4: DrawerMeshBuilder — procedural chamfered geometry

Ports the generator validated in the Drawer Forge preview into C#, emitting plain float arrays so it can be tested without Unity.

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Core/MeshData.cs`
- Create: `ItemDrawers/src/ItemDrawers.Core/DrawerMeshBuilder.cs`
- Test: `ItemDrawers/tests/ItemDrawers.Core.Tests/DrawerMeshBuilderTests.cs`

**Interfaces:**
- Consumes: `DrawerProportions`, `HandleStyle` from Task 2.
- Produces:
  - `sealed class MeshData { float[] Vertices; float[] Normals; float[] Uvs; int VertexCount; int TriangleCount; }`
  - `static class DrawerMeshBuilder { static MeshData Build(DrawerProportions p); static MeshData ChamferBox(float cx, float cy, float cz, float w, float h, float d, float chamfer); }`

- [ ] **Step 1: Write the failing tests**

`tests/ItemDrawers.Core.Tests/DrawerMeshBuilderTests.cs`:

```csharp
using System;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class DrawerMeshBuilderTests
    {
        private static (float x, float y, float z) V(MeshData m, int i) =>
            (m.Vertices[i * 3], m.Vertices[i * 3 + 1], m.Vertices[i * 3 + 2]);

        private static (float x, float y, float z) N(MeshData m, int i) =>
            (m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);

        [Fact]
        public void Output_arrays_are_consistent_and_triangulated()
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions());

            Assert.Equal(0, m.VertexCount % 3);
            Assert.Equal(m.VertexCount * 3, m.Vertices.Length);
            Assert.Equal(m.VertexCount * 3, m.Normals.Length);
            Assert.Equal(m.VertexCount * 2, m.Uvs.Length);
            Assert.Equal(m.VertexCount / 3, m.TriangleCount);
        }

        [Fact]
        public void Every_normal_is_unit_length()
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions());

            for (int i = 0; i < m.VertexCount; i++)
            {
                var (x, y, z) = N(m, i);
                double len = Math.Sqrt(x * x + y * y + z * z);
                Assert.InRange(len, 0.999, 1.001);
            }
        }

        [Fact]
        public void No_vertex_or_normal_is_NaN()
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions());

            foreach (var f in m.Vertices) Assert.False(float.IsNaN(f) || float.IsInfinity(f));
            foreach (var f in m.Normals) Assert.False(float.IsNaN(f) || float.IsInfinity(f));
        }

        [Fact]
        public void Geometry_stays_inside_the_declared_footprint()
        {
            // A wall of drawers only tiles if nothing escapes the 1m cube,
            // except the handle, which is allowed to stand proud of the front.
            var p = new DrawerProportions();
            var m = DrawerMeshBuilder.Build(p);

            for (int i = 0; i < m.VertexCount; i++)
            {
                var (x, y, z) = V(m, i);
                Assert.InRange(x, -p.Width / 2 - 1e-4, p.Width / 2 + 1e-4);
                Assert.InRange(y, -p.Height / 2 - 1e-4, p.Height / 2 + 1e-4);
                Assert.InRange(z, -p.Depth / 2 - 1e-4, p.Depth / 2 + p.HandleProud + 1e-4);
            }
        }

        [Fact]
        public void A_chamfer_box_faces_outward_everywhere()
        {
            // For a convex box centred on the origin, every triangle's normal
            // must point away from the centre. This catches winding bugs,
            // which are otherwise invisible until the mesh renders inside out.
            var m = DrawerMeshBuilder.ChamferBox(0, 0, 0, 1f, 1f, 1f, 0.05f);

            for (int t = 0; t < m.TriangleCount; t++)
            {
                int i = t * 3;
                var (ax, ay, az) = V(m, i);
                var (bx, by, bz) = V(m, i + 1);
                var (cx, cy, cz) = V(m, i + 2);

                float mx = (ax + bx + cx) / 3f, my = (ay + by + cy) / 3f, mz = (az + bz + cz) / 3f;
                var (nx, ny, nz) = N(m, i);

                Assert.True(mx * nx + my * ny + mz * nz > 0f,
                    $"Triangle {t} is wound inward.");
            }
        }

        [Fact]
        public void Chamfering_cuts_inward_and_never_grows_the_box()
        {
            var sharp = DrawerMeshBuilder.ChamferBox(0, 0, 0, 1f, 1f, 1f, 0f);
            var beveled = DrawerMeshBuilder.ChamferBox(0, 0, 0, 1f, 1f, 1f, 0.1f);

            float ExtentX(MeshData m)
            {
                float max = 0f;
                for (int i = 0; i < m.VertexCount; i++) max = Math.Max(max, Math.Abs(m.Vertices[i * 3]));
                return max;
            }

            Assert.Equal(ExtentX(sharp), ExtentX(beveled), 4);
            Assert.Equal(0.5f, ExtentX(beveled), 4);
        }

        [Fact]
        public void A_zero_bevel_still_produces_valid_geometry()
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions { Bevel = 0f });

            Assert.True(m.TriangleCount > 0);
            foreach (var f in m.Normals) Assert.False(float.IsNaN(f));
        }

        [Theory]
        [InlineData(HandleStyle.None)]
        [InlineData(HandleStyle.Bar)]
        [InlineData(HandleStyle.Knobs)]
        [InlineData(HandleStyle.Pull)]
        public void Every_handle_style_builds(HandleStyle style)
        {
            var m = DrawerMeshBuilder.Build(new DrawerProportions { Handle = style });
            Assert.True(m.TriangleCount > 0);
        }

        [Fact]
        public void A_drawer_stays_cheap_enough_for_a_hundred_of_them()
        {
            // Budget, not a measurement: 100 drawers should be a few hundred
            // thousand triangles at worst. If this trips, something is
            // generating far more geometry than the design intends.
            var m = DrawerMeshBuilder.Build(new DrawerProportions());
            Assert.InRange(m.TriangleCount, 1, 3000);
        }
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
cd ItemDrawers && dotnet test --filter DrawerMeshBuilderTests
```

Expected: compile failure — `MeshData` and `DrawerMeshBuilder` do not exist.

- [ ] **Step 3: Write the implementation**

`src/ItemDrawers.Core/MeshData.cs`:

```csharp
using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Geometry as plain arrays. Deliberately not a UnityEngine.Mesh: the
    /// adapter converts it, which keeps generation testable.
    /// </summary>
    public sealed class MeshData
    {
        public float[] Vertices { get; }
        public float[] Normals { get; }
        public float[] Uvs { get; }

        public int VertexCount => Vertices.Length / 3;
        public int TriangleCount => VertexCount / 3;

        public MeshData(float[] vertices, float[] normals, float[] uvs)
        {
            Vertices = vertices;
            Normals = normals;
            Uvs = uvs;
        }

        internal sealed class Builder
        {
            private readonly List<float> _pos = new List<float>(4096);
            private readonly List<float> _nor = new List<float>(4096);
            private readonly List<float> _uv = new List<float>(2731);

            public void Triangle(float[] a, float[] b, float[] c, float uvScale)
            {
                float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];

                float nx = uy * vz - uz * vy;
                float ny = uz * vx - ux * vz;
                float nz = ux * vy - uy * vx;

                float len = (float)System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-9f) return;              // degenerate; drop it
                nx /= len; ny /= len; nz /= len;

                // Box-project UVs off the dominant axis so grain tiles at a
                // consistent world scale whichever face we are on.
                float ax = System.Math.Abs(nx), ay = System.Math.Abs(ny), az = System.Math.Abs(nz);
                int i0, i1;
                if (ax >= ay && ax >= az) { i0 = 2; i1 = 1; }
                else if (ay >= az) { i0 = 0; i1 = 2; }
                else { i0 = 0; i1 = 1; }

                foreach (var p in new[] { a, b, c })
                {
                    _pos.Add(p[0]); _pos.Add(p[1]); _pos.Add(p[2]);
                    _nor.Add(nx); _nor.Add(ny); _nor.Add(nz);
                    _uv.Add(p[i0] * uvScale); _uv.Add(p[i1] * uvScale);
                }
            }

            /// <summary>Winds the quad so its normal agrees with <paramref name="hint"/>.</summary>
            public void Quad(float[] a, float[] b, float[] c, float[] d, float[] hint, float uvScale)
            {
                float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;

                if (nx * hint[0] + ny * hint[1] + nz * hint[2] < 0f)
                {
                    var t = b; b = d; d = t;
                }
                Triangle(a, b, c, uvScale);
                Triangle(a, c, d, uvScale);
            }

            public void TriangleHinted(float[] a, float[] b, float[] c, float[] hint, float uvScale)
            {
                float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;

                if (nx * hint[0] + ny * hint[1] + nz * hint[2] < 0f)
                {
                    var t = b; b = c; c = t;
                }
                Triangle(a, b, c, uvScale);
            }

            public MeshData Build() => new MeshData(_pos.ToArray(), _nor.ToArray(), _uv.ToArray());
        }
    }
}
```

`src/ItemDrawers.Core/DrawerMeshBuilder.cs`:

```csharp
using System;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Builds the drawer from chamfered boxes. Sharp ninety-degree edges
    /// catch no specular and read as computer-generated, so every box is
    /// bevelled — that single detail is most of what makes it look like wood.
    /// </summary>
    public static class DrawerMeshBuilder
    {
        private const float UvScale = 1.0f;
        private static readonly int[] Signs = { -1, 1 };

        public static MeshData Build(DrawerProportions p)
        {
            var gb = new MeshData.Builder();

            float w = p.Width, h = p.Height, d = p.Depth;
            float frame = Math.Min(p.FrameThickness, Math.Min(w, h) / 2f - 0.02f);
            float recess = Math.Min(p.RecessDepth, d * 0.5f);
            float bevel = p.Bevel;

            // Carcass, pulled back by the recess. Its own front face becomes
            // the recessed panel, so the recess costs no extra geometry.
            AddChamferBox(gb, 0f, 0f, -recess / 2f, w, h, d - recess, bevel);

            // Frame planks standing proud around the opening.
            float fz = d / 2f - recess / 2f;
            AddChamferBox(gb, 0f, h / 2f - frame / 2f, fz, w, frame, recess, bevel);
            AddChamferBox(gb, 0f, -h / 2f + frame / 2f, fz, w, frame, recess, bevel);

            float stileHeight = h - 2f * frame;
            if (stileHeight > 0.01f)
            {
                AddChamferBox(gb, -w / 2f + frame / 2f, 0f, fz, frame, stileHeight, recess, bevel);
                AddChamferBox(gb, w / 2f - frame / 2f, 0f, fz, frame, stileHeight, recess, bevel);
            }

            AddHandle(gb, p, frame, recess);
            return gb.Build();
        }

        private static void AddHandle(MeshData.Builder gb, DrawerProportions p, float frame, float recess)
        {
            float panelZ = p.Depth / 2f - recess;
            float openH = p.Height - 2f * frame;

            switch (p.Handle)
            {
                case HandleStyle.Bar:
                {
                    float bw = Math.Min(p.HandleWidth, p.Width - 2f * frame - 0.02f);
                    float by = -openH / 2f + p.HandleSection * 1.4f;
                    AddChamferBox(gb, 0f, by, panelZ + p.HandleProud / 2f,
                                  bw, p.HandleSection, p.HandleProud,
                                  Math.Min(p.Bevel, p.HandleSection / 2.5f));
                    break;
                }
                case HandleStyle.Knobs:
                {
                    float kx = Math.Min(p.HandleWidth / 2f, p.Width / 2f - frame - p.HandleSection);
                    for (int s = -1; s <= 1; s += 2)
                        AddChamferBox(gb, kx * s, 0f, panelZ + p.HandleProud / 2f,
                                      p.HandleSection, p.HandleSection, p.HandleProud,
                                      p.HandleSection / 3f);
                    break;
                }
                case HandleStyle.Pull:
                {
                    float pw = Math.Min(p.HandleWidth, p.Width - 2f * frame);
                    AddChamferBox(gb, 0f, -p.Height / 2f + frame + p.HandleSection / 2f,
                                  p.Depth / 2f - p.HandleProud / 2f,
                                  pw, p.HandleSection, p.HandleProud, p.Bevel);
                    break;
                }
                case HandleStyle.None:
                default:
                    break;
            }
        }

        /// <summary>Standalone chamfered box, exposed for testing winding and extents.</summary>
        public static MeshData ChamferBox(float cx, float cy, float cz,
                                          float w, float h, float d, float chamfer)
        {
            var gb = new MeshData.Builder();
            AddChamferBox(gb, cx, cy, cz, w, h, d, chamfer);
            return gb.Build();
        }

        private static void AddChamferBox(MeshData.Builder gb, float cx, float cy, float cz,
                                          float w, float h, float d, float chamfer)
        {
            float hx = w / 2f, hy = h / 2f, hz = d / 2f;
            float c = Math.Max(0f, Math.Min(chamfer, Math.Min(hx, Math.Min(hy, hz)) * 0.98f));

            var extent = new[] { hx, hy, hz };
            var inner = new[] { hx - c, hy - c, hz - c };
            var centre = new[] { cx, cy, cz };

            // The corner point that lies on `axis`, in octant `s`.
            float[] Point(int axis, int[] s)
            {
                var v = new float[3];
                for (int i = 0; i < 3; i++)
                    v[i] = centre[i] + s[i] * (i == axis ? extent[i] : inner[i]);
                return v;
            }

            // Six faces, each inset by the chamfer on its two in-plane axes.
            var order = new[] { new[] { -1, -1 }, new[] { 1, -1 }, new[] { 1, 1 }, new[] { -1, 1 } };
            for (int axis = 0; axis < 3; axis++)
            {
                foreach (int s in Signs)
                {
                    int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                    var corner = new float[4][];
                    for (int q = 0; q < 4; q++)
                    {
                        var sg = new int[3];
                        sg[axis] = s; sg[a1] = order[q][0]; sg[a2] = order[q][1];
                        corner[q] = Point(axis, sg);
                    }
                    var hint = new float[3];
                    hint[axis] = s;
                    gb.Quad(corner[0], corner[1], corner[2], corner[3], hint, UvScale);
                }
            }

            // Twelve edge chamfers.
            for (int e = 0; e < 3; e++)
            {
                int b1 = (e + 1) % 3, b2 = (e + 2) % 3;
                foreach (int s1 in Signs)
                foreach (int s2 in Signs)
                {
                    var qa = new float[2][];
                    var qb = new float[2][];
                    for (int j = 0; j < 2; j++)
                    {
                        var g = new int[3];
                        g[e] = Signs[j]; g[b1] = s1; g[b2] = s2;
                        qa[j] = Point(b1, g);
                        qb[j] = Point(b2, g);
                    }
                    var hint = new float[3];
                    hint[b1] = s1; hint[b2] = s2;
                    gb.Quad(qa[0], qa[1], qb[1], qb[0], hint, UvScale);
                }
            }

            // Eight corner triangles.
            foreach (int sx in Signs)
            foreach (int sy in Signs)
            foreach (int sz in Signs)
            {
                var sg = new[] { sx, sy, sz };
                var hint = new float[] { sx, sy, sz };
                gb.TriangleHinted(Point(0, sg), Point(1, sg), Point(2, sg), hint, UvScale);
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd ItemDrawers && dotnet test --filter DrawerMeshBuilderTests
```

Expected: 12 passed (the theory counts as 4).

The winding test is the one that matters. If it fails, the `hint` vectors are wrong for some face or edge, and the mesh will render inside out in game — where it is much harder to diagnose.

- [ ] **Step 5: Commit**

```bash
git add ItemDrawers/src/ItemDrawers.Core ItemDrawers/tests
git commit -m "Generate drawer geometry procedurally from chamfered boxes

No asset bundle ships and nothing is hand-modelled. Tests assert
outward winding, unit normals and that the geometry stays inside the
1m cube so a wall of them tiles, which are all things that are painful
to diagnose once the mesh is in the game."
```

---

## Task 5: IconAtlasPacker

Every drawer's icon must come from one shared texture, because that is what lets a hundred labels batch into a single draw call.

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Core/AtlasRect.cs`
- Create: `ItemDrawers/src/ItemDrawers.Core/IconAtlasPacker.cs`
- Test: `ItemDrawers/tests/ItemDrawers.Core.Tests/IconAtlasPackerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `readonly struct AtlasRect { int X, Y, Width, Height; float U0, V0, U1, V1 (given an atlas size); }`
  - `sealed class AtlasLayout { int Width; int Height; IReadOnlyDictionary<string, AtlasRect> Rects; bool TryGetUv(string name, out float u0, out float v0, out float u1, out float v1); }`
  - `static class IconAtlasPacker { static AtlasLayout Pack(IReadOnlyList<IconSize> icons, int maxDimension = 4096); }`
  - `readonly struct IconSize { string Name; int Width; int Height; }`

- [ ] **Step 1: Write the failing tests**

`tests/ItemDrawers.Core.Tests/IconAtlasPackerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class IconAtlasPackerTests
    {
        private static List<IconSize> Uniform(int count, int size = 64) =>
            Enumerable.Range(0, count).Select(i => new IconSize("item" + i, size, size)).ToList();

        private static bool Overlaps(AtlasRect a, AtlasRect b) =>
            a.X < b.X + b.Width && b.X < a.X + a.Width &&
            a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

        [Fact]
        public void Every_icon_gets_a_rect()
        {
            var layout = IconAtlasPacker.Pack(Uniform(500));
            Assert.Equal(500, layout.Rects.Count);
        }

        [Fact]
        public void No_two_icons_overlap()
        {
            var layout = IconAtlasPacker.Pack(Uniform(500));
            var rects = layout.Rects.Values.ToArray();

            for (int i = 0; i < rects.Length; i++)
                for (int j = i + 1; j < rects.Length; j++)
                    Assert.False(Overlaps(rects[i], rects[j]), $"rect {i} overlaps rect {j}");
        }

        [Fact]
        public void Everything_lands_inside_the_atlas()
        {
            var layout = IconAtlasPacker.Pack(Uniform(500));

            foreach (var r in layout.Rects.Values)
            {
                Assert.InRange(r.X, 0, layout.Width - r.Width);
                Assert.InRange(r.Y, 0, layout.Height - r.Height);
            }
        }

        [Fact]
        public void Mixed_sizes_pack_without_overlapping()
        {
            var icons = new List<IconSize>();
            var rng = new Random(1234);
            for (int i = 0; i < 200; i++)
                icons.Add(new IconSize("i" + i, 16 + rng.Next(64), 16 + rng.Next(64)));

            var layout = IconAtlasPacker.Pack(icons);
            var rects = layout.Rects.Values.ToArray();

            for (int i = 0; i < rects.Length; i++)
                for (int j = i + 1; j < rects.Length; j++)
                    Assert.False(Overlaps(rects[i], rects[j]));
        }

        [Fact]
        public void Uv_coordinates_are_normalised_and_ordered()
        {
            var layout = IconAtlasPacker.Pack(Uniform(64));

            Assert.True(layout.TryGetUv("item7", out float u0, out float v0, out float u1, out float v1));
            Assert.InRange(u0, 0f, 1f);
            Assert.InRange(v0, 0f, 1f);
            Assert.True(u1 > u0);
            Assert.True(v1 > v0);
        }

        [Fact]
        public void An_unknown_item_reports_no_uv_rather_than_throwing()
        {
            var layout = IconAtlasPacker.Pack(Uniform(4));
            Assert.False(layout.TryGetUv("nope", out _, out _, out _, out _));
        }

        [Fact]
        public void Atlas_dimensions_are_powers_of_two()
        {
            var layout = IconAtlasPacker.Pack(Uniform(300));

            Assert.Equal(0, layout.Width & (layout.Width - 1));
            Assert.Equal(0, layout.Height & (layout.Height - 1));
        }

        [Fact]
        public void Too_much_to_fit_throws_rather_than_silently_dropping_icons()
        {
            // A silently truncated atlas would show wrong icons on drawers,
            // which is worse than failing loudly at startup.
            Assert.Throws<InvalidOperationException>(() =>
                IconAtlasPacker.Pack(Uniform(10_000, 256), maxDimension: 1024));
        }

        [Fact]
        public void An_empty_input_produces_an_empty_layout()
        {
            var layout = IconAtlasPacker.Pack(new List<IconSize>());
            Assert.Empty(layout.Rects);
        }
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
cd ItemDrawers && dotnet test --filter IconAtlasPackerTests
```

Expected: compile failure — the types do not exist.

- [ ] **Step 3: Write the implementation**

`src/ItemDrawers.Core/AtlasRect.cs`:

```csharp
using System.Collections.Generic;

namespace ItemDrawers.Core
{
    public readonly struct IconSize
    {
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;

        public IconSize(string name, int width, int height)
        {
            Name = name;
            Width = width;
            Height = height;
        }
    }

    public readonly struct AtlasRect
    {
        public readonly int X, Y, Width, Height;

        public AtlasRect(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }
    }

    public sealed class AtlasLayout
    {
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyDictionary<string, AtlasRect> Rects { get; }

        public AtlasLayout(int width, int height, IReadOnlyDictionary<string, AtlasRect> rects)
        {
            Width = width;
            Height = height;
            Rects = rects;
        }

        public bool TryGetUv(string name, out float u0, out float v0, out float u1, out float v1)
        {
            u0 = v0 = u1 = v1 = 0f;
            if (name == null || !Rects.TryGetValue(name, out var r)) return false;

            u0 = (float)r.X / Width;
            v0 = (float)r.Y / Height;
            u1 = (float)(r.X + r.Width) / Width;
            v1 = (float)(r.Y + r.Height) / Height;
            return true;
        }
    }
}
```

`src/ItemDrawers.Core/IconAtlasPacker.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Shelf packer. Item icons are near-uniform squares, so shelves waste
    /// very little and the algorithm stays short enough to be obviously
    /// correct — which matters more here than packing density.
    /// </summary>
    public static class IconAtlasPacker
    {
        private const int Padding = 2;   // keeps bilinear filtering from bleeding between icons

        public static AtlasLayout Pack(IReadOnlyList<IconSize> icons, int maxDimension = 4096)
        {
            if (icons == null) throw new ArgumentNullException(nameof(icons));
            if (icons.Count == 0)
                return new AtlasLayout(1, 1, new Dictionary<string, AtlasRect>());

            var sorted = new List<IconSize>(icons);
            sorted.Sort((a, b) => b.Height.CompareTo(a.Height));   // tallest first

            for (int size = 64; size <= maxDimension; size *= 2)
            {
                if (TryPackInto(sorted, size, out var rects))
                    return new AtlasLayout(size, size, rects);
            }

            throw new InvalidOperationException(
                $"Cannot fit {icons.Count} icons into a {maxDimension}x{maxDimension} atlas.");
        }

        private static bool TryPackInto(List<IconSize> sorted, int size,
                                        out Dictionary<string, AtlasRect> rects)
        {
            rects = new Dictionary<string, AtlasRect>(sorted.Count);

            int shelfY = 0, shelfHeight = 0, cursorX = 0;

            foreach (var icon in sorted)
            {
                int w = icon.Width + Padding;
                int h = icon.Height + Padding;

                if (w > size || h > size) return false;

                if (cursorX + w > size)
                {
                    shelfY += shelfHeight;
                    shelfHeight = 0;
                    cursorX = 0;
                }

                if (shelfY + h > size) return false;

                rects[icon.Name] = new AtlasRect(cursorX, shelfY, icon.Width, icon.Height);
                cursorX += w;
                if (h > shelfHeight) shelfHeight = h;
            }

            return true;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd ItemDrawers && dotnet test --filter IconAtlasPackerTests
```

Expected: 9 passed.

- [ ] **Step 5: Commit**

```bash
git add ItemDrawers/src/ItemDrawers.Core ItemDrawers/tests
git commit -m "Pack item icons into one atlas

One atlas means one material for every drawer label, which is what
lets a hundred of them batch. Overflow throws at startup rather than
silently dropping icons, since a drawer showing the wrong item is
worse than a loud failure."
```

---

## Task 6: SpatialGrid

Auto-pickup iterates dropped items and asks which drawers are near them. That query must not be a linear scan over every drawer in the world.

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Core/SpatialGrid.cs`
- Test: `ItemDrawers/tests/ItemDrawers.Core.Tests/SpatialGridTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `sealed class SpatialGrid<T>` with `SpatialGrid(float cellSize)`, `void Insert(T item, float x, float y, float z)`, `bool Remove(T item)`, `void Move(T item, float x, float y, float z)`, `void Query(float x, float y, float z, float radius, List<T> results)`, `int Count`.

- [ ] **Step 1: Write the failing tests**

`tests/ItemDrawers.Core.Tests/SpatialGridTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class SpatialGridTests
    {
        [Fact]
        public void Finds_an_item_inside_the_radius()
        {
            var grid = new SpatialGrid<string>(cellSize: 8f);
            grid.Insert("a", 1f, 0f, 1f);

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Equal(new[] { "a" }, hits);
        }

        [Fact]
        public void Excludes_an_item_outside_the_radius()
        {
            var grid = new SpatialGrid<string>(cellSize: 8f);
            grid.Insert("far", 100f, 0f, 0f);

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Empty(hits);
        }

        [Fact]
        public void Radius_is_spherical_not_cubic()
        {
            var grid = new SpatialGrid<string>(cellSize: 8f);
            grid.Insert("corner", 4f, 4f, 4f);   // ~6.93 away, outside a radius of 5

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Empty(hits);
        }

        [Fact]
        public void Agrees_with_brute_force_over_random_data()
        {
            // The grid is an optimisation. Its only job is to return exactly
            // what a linear scan would.
            var rng = new Random(20260909);
            var grid = new SpatialGrid<int>(cellSize: 10f);
            var points = new List<(int id, float x, float y, float z)>();

            for (int i = 0; i < 2000; i++)
            {
                float x = (float)(rng.NextDouble() * 400 - 200);
                float y = (float)(rng.NextDouble() * 60 - 30);
                float z = (float)(rng.NextDouble() * 400 - 200);
                points.Add((i, x, y, z));
                grid.Insert(i, x, y, z);
            }

            var hits = new List<int>();
            for (int q = 0; q < 100; q++)
            {
                float qx = (float)(rng.NextDouble() * 400 - 200);
                float qy = (float)(rng.NextDouble() * 60 - 30);
                float qz = (float)(rng.NextDouble() * 400 - 200);
                float r = (float)(rng.NextDouble() * 30 + 1);

                hits.Clear();
                grid.Query(qx, qy, qz, r, hits);

                var expected = points
                    .Where(p => (p.x - qx) * (p.x - qx) + (p.y - qy) * (p.y - qy) + (p.z - qz) * (p.z - qz) <= r * r)
                    .Select(p => p.id)
                    .OrderBy(i => i)
                    .ToArray();

                Assert.Equal(expected, hits.OrderBy(i => i).ToArray());
            }
        }

        [Fact]
        public void Removed_items_stop_being_found()
        {
            var grid = new SpatialGrid<string>(8f);
            grid.Insert("a", 0f, 0f, 0f);

            Assert.True(grid.Remove("a"));
            Assert.False(grid.Remove("a"));       // idempotent second call

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 50f, hits);
            Assert.Empty(hits);
            Assert.Equal(0, grid.Count);
        }

        [Fact]
        public void Moving_an_item_relocates_it_rather_than_duplicating_it()
        {
            var grid = new SpatialGrid<string>(8f);
            grid.Insert("a", 0f, 0f, 0f);
            grid.Move("a", 100f, 0f, 0f);

            var near = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, near);
            Assert.Empty(near);

            var far = new List<string>();
            grid.Query(100f, 0f, 0f, 5f, far);
            Assert.Equal(new[] { "a" }, far);
            Assert.Equal(1, grid.Count);
        }

        [Fact]
        public void Query_appends_without_clearing_the_caller_s_list()
        {
            // The manager reuses one list across many queries to avoid
            // allocating every frame, so this behaviour is depended upon.
            var grid = new SpatialGrid<string>(8f);
            grid.Insert("a", 0f, 0f, 0f);

            var hits = new List<string> { "pre-existing" };
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Equal(new[] { "pre-existing", "a" }, hits);
        }

        [Fact]
        public void Inserting_the_same_item_twice_keeps_one_entry()
        {
            var grid = new SpatialGrid<string>(8f);
            grid.Insert("a", 0f, 0f, 0f);
            grid.Insert("a", 1f, 0f, 0f);

            var hits = new List<string>();
            grid.Query(0f, 0f, 0f, 5f, hits);

            Assert.Single(hits);
            Assert.Equal(1, grid.Count);
        }
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
cd ItemDrawers && dotnet test --filter SpatialGridTests
```

Expected: compile failure — `SpatialGrid<T>` does not exist.

- [ ] **Step 3: Write the implementation**

`src/ItemDrawers.Core/SpatialGrid.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Uniform cell hash. Drawers are static once built, so insert and remove
    /// are rare and query is the operation that matters.
    /// </summary>
    public sealed class SpatialGrid<T>
    {
        private readonly struct Cell : IEquatable<Cell>
        {
            public readonly int X, Y, Z;
            public Cell(int x, int y, int z) { X = x; Y = y; Z = z; }

            public bool Equals(Cell o) => X == o.X && Y == o.Y && Z == o.Z;
            public override bool Equals(object o) => o is Cell c && Equals(c);
            public override int GetHashCode()
            {
                unchecked { return ((X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791)); }
            }
        }

        private readonly float _cellSize;
        private readonly Dictionary<Cell, List<T>> _cells = new Dictionary<Cell, List<T>>();
        private readonly Dictionary<T, (Cell cell, float x, float y, float z)> _index =
            new Dictionary<T, (Cell, float, float, float)>();

        public SpatialGrid(float cellSize)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            _cellSize = cellSize;
        }

        public int Count => _index.Count;

        private Cell CellOf(float x, float y, float z) => new Cell(
            (int)Math.Floor(x / _cellSize),
            (int)Math.Floor(y / _cellSize),
            (int)Math.Floor(z / _cellSize));

        public void Insert(T item, float x, float y, float z)
        {
            if (_index.ContainsKey(item)) { Move(item, x, y, z); return; }

            var cell = CellOf(x, y, z);
            if (!_cells.TryGetValue(cell, out var bucket))
            {
                bucket = new List<T>(4);
                _cells[cell] = bucket;
            }
            bucket.Add(item);
            _index[item] = (cell, x, y, z);
        }

        public bool Remove(T item)
        {
            if (!_index.TryGetValue(item, out var entry)) return false;

            if (_cells.TryGetValue(entry.cell, out var bucket))
            {
                bucket.Remove(item);
                if (bucket.Count == 0) _cells.Remove(entry.cell);
            }
            _index.Remove(item);
            return true;
        }

        public void Move(T item, float x, float y, float z)
        {
            Remove(item);
            Insert(item, x, y, z);
        }

        /// <summary>Appends every item within <paramref name="radius"/>. Does not clear the list.</summary>
        public void Query(float x, float y, float z, float radius, List<T> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (radius <= 0f) return;

            // Symmetric and rounded up. A floor-based range is asymmetric and
            // silently skips the far cell: at radius 5 with cellSize 8 it
            // yields -1..0, missing an item one cell over and well inside
            // the radius.
            int range = (int)Math.Ceiling(radius / _cellSize);
            var origin = CellOf(x, y, z);
            float r2 = radius * radius;

            for (int dx = -range; dx <= range; dx++)
            for (int dy = -range; dy <= range; dy++)
            for (int dz = -range; dz <= range; dz++)
            {
                var cell = new Cell(origin.X + dx, origin.Y + dy, origin.Z + dz);
                if (!_cells.TryGetValue(cell, out var bucket)) continue;

                for (int i = 0; i < bucket.Count; i++)
                {
                    var item = bucket[i];
                    var p = _index[item];
                    float ex = p.x - x, ey = p.y - y, ez = p.z - z;
                    if (ex * ex + ey * ey + ez * ez <= r2) results.Add(item);
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd ItemDrawers && dotnet test --filter SpatialGridTests
```

Expected: 8 passed. The brute-force agreement test is the important one — if it fails, the cell range computed in `Query` is too narrow and drawers near cell boundaries will silently miss items.

- [ ] **Step 5: Run the whole suite**

```bash
cd ItemDrawers && dotnet test
```

Expected: all green. The core is now complete.

- [ ] **Step 6: Commit**

```bash
git add ItemDrawers/src/ItemDrawers.Core ItemDrawers/tests
git commit -m "Add spatial grid for auto-pickup queries

Verified against brute force on random data, because the grid's only
job is to return exactly what a linear scan would while costing less."
```

---

## Task 7: Real drawer geometry in game

Replaces the spike cube with the generated mesh wearing vanilla materials. First point at which the thing looks like a drawer.

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Game/DrawerTier.cs`
- Create: `ItemDrawers/src/ItemDrawers.Game/DrawerPieces.cs`
- Modify: `ItemDrawers/src/ItemDrawers.Game/DrawerPlugin.cs`
- Modify: `ItemDrawers/src/ItemDrawers.Game/ItemDrawers.Game.csproj` (add the Core project reference)

**Interfaces:**
- Consumes: `DrawerMeshBuilder.Build`, `MeshData`, `DrawerProportions` from Tasks 2 and 4.
- Produces:
  - `enum DrawerTier { Wood, Stone, BlackMarble }`
  - `static class DrawerTiers { static string PrefabName(DrawerTier t); static int DefaultCapacity(DrawerTier t); static string DisplayName(DrawerTier t); static IEnumerable<DrawerTier> All; }`
  - `static class DrawerPieces { static void RegisterAll(); static GameObject BuildPrefab(DrawerTier tier, Mesh sharedMesh); }`
  - `static UnityEngine.Mesh MeshDataExtensions.ToUnityMesh(this MeshData data, string name)`

- [ ] **Step 1: Add the project reference**

In `ItemDrawers.Game.csproj`, inside a new `ItemGroup`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\ItemDrawers.Core\ItemDrawers.Core.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Write the tier table**

`src/ItemDrawers.Game/DrawerTier.cs`:

```csharp
using System.Collections.Generic;

namespace ItemDrawers.Game
{
    public enum DrawerTier { Wood, Stone, BlackMarble }

    public static class DrawerTiers
    {
        public static readonly DrawerTier[] All =
            { DrawerTier.Wood, DrawerTier.Stone, DrawerTier.BlackMarble };

        /// <summary>
        /// Written into save files, therefore permanent. Renaming one of
        /// these after release orphans every drawer players have built.
        /// </summary>
        public static string PrefabName(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return "rid_drawer_wood";
                case DrawerTier.Stone: return "rid_drawer_stone";
                case DrawerTier.BlackMarble: return "rid_drawer_blackmarble";
                default: return "rid_drawer_wood";
            }
        }

        public static string DisplayName(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return "Item Drawer";
                case DrawerTier.Stone: return "Stone Item Drawer";
                case DrawerTier.BlackMarble: return "Black Marble Item Drawer";
                default: return "Item Drawer";
            }
        }

        public static int DefaultCapacity(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return 1000;
                case DrawerTier.Stone: return 2000;
                case DrawerTier.BlackMarble: return 10000;
                default: return 1000;
            }
        }

        /// <summary>Build cost, as (item prefab name, amount) pairs.</summary>
        public static IReadOnlyList<(string Item, int Amount)> Recipe(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood:
                    return new[] { ("FineWood", 10) };
                case DrawerTier.Stone:
                    return new[] { ("FineWood", 5), ("Stone", 10) };
                case DrawerTier.BlackMarble:
                    return new[] { ("FineWood", 5), ("BlackMarble", 10) };
                default:
                    return new[] { ("FineWood", 10) };
            }
        }

        /// <summary>
        /// Vanilla prefab whose material this tier borrows, so drawers
        /// inherit the game's wear, wet and snow shading for free.
        /// </summary>
        public static string MaterialDonor(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return "piece_chest_wood";
                case DrawerTier.Stone: return "piece_chest_grausten";
                case DrawerTier.BlackMarble: return "blackmarble_post01";
                default: return "piece_chest_wood";
            }
        }
    }
}
```

**Verify the three donor prefab names before relying on them.** `piece_chest_wood` and `piece_chest_grausten` were confirmed present in the 1.0 soft-reference manifest; `blackmarble_post01` was not checked. Search the manifest and substitute a real one if it is absent:

```bash
grep -aoE "blackmarble[A-Za-z0-9_]*" \
  "/c/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/StreamingAssets/SoftRef/manifest_extended" \
  | sort -u | head -20
```

- [ ] **Step 3: Write the prefab builder**

`src/ItemDrawers.Game/DrawerPieces.cs`:

```csharp
using System.Linq;
using ItemDrawers.Core;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ItemDrawers.Game
{
    internal static class MeshDataExtensions
    {
        public static Mesh ToUnityMesh(this MeshData data, string name)
        {
            var vertices = new Vector3[data.VertexCount];
            var normals = new Vector3[data.VertexCount];
            var uvs = new Vector2[data.VertexCount];
            var triangles = new int[data.VertexCount];

            for (int i = 0; i < data.VertexCount; i++)
            {
                vertices[i] = new Vector3(data.Vertices[i * 3], data.Vertices[i * 3 + 1], data.Vertices[i * 3 + 2]);
                normals[i] = new Vector3(data.Normals[i * 3], data.Normals[i * 3 + 1], data.Normals[i * 3 + 2]);
                uvs[i] = new Vector2(data.Uvs[i * 2], data.Uvs[i * 2 + 1]);
                triangles[i] = i;                      // non-indexed: one vertex per corner
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: false);
            return mesh;
        }
    }

    public static class DrawerPieces
    {
        private static Mesh _sharedMesh;

        public static void RegisterAll()
        {
            var proportions = new DrawerProportions();
            _sharedMesh = DrawerMeshBuilder.Build(proportions).ToUnityMesh("rid_drawer");

            foreach (var tier in DrawerTiers.All)
            {
                var prefab = BuildPrefab(tier, _sharedMesh);
                if (prefab == null) continue;

                var config = new PieceConfig
                {
                    Name = DrawerTiers.DisplayName(tier),
                    Description = "Holds a great many of a single item.",
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Furniture,
                    CraftingStation = CraftingStations.Workbench,
                    Requirements = DrawerTiers.Recipe(tier)
                        .Select(r => new RequirementConfig(r.Item, r.Amount, 0, true))
                        .ToArray()
                };

                PieceManager.Instance.AddPiece(new CustomPiece(prefab, fixReference: false, config));
                DrawerPlugin.Log.LogInfo($"Registered {DrawerTiers.PrefabName(tier)}");
            }
        }

        public static GameObject BuildPrefab(DrawerTier tier, Mesh sharedMesh)
        {
            var donorName = DrawerTiers.MaterialDonor(tier);
            var donor = PrefabManager.Instance.GetPrefab(donorName);
            if (donor == null)
            {
                DrawerPlugin.Log.LogError(
                    $"Material donor '{donorName}' not found; cannot build {tier} drawer.");
                return null;
            }

            var donorRenderer = donor.GetComponentInChildren<MeshRenderer>();
            if (donorRenderer == null)
            {
                DrawerPlugin.Log.LogError($"Donor '{donorName}' has no MeshRenderer.");
                return null;
            }

            var go = new GameObject(DrawerTiers.PrefabName(tier));
            Object.DontDestroyOnLoad(go);

            var body = new GameObject("body");
            body.transform.SetParent(go.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = sharedMesh;

            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = donorRenderer.sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(1f, 1f, 1f);

            go.AddComponent<ZNetView>();
            // Piece, WearNTear and the drawer behaviour are added in Task 8.

            return go;
        }
    }
}
```

- [ ] **Step 4: Wire it into the plugin**

In `DrawerPlugin.cs`, replace the `RegisterSpikePiece` method and its subscription with:

```csharp
            PrefabManager.OnVanillaPrefabsAvailable += OnPrefabsReady;
```

```csharp
        private void OnPrefabsReady()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnPrefabsReady;
            DrawerPieces.RegisterAll();
        }
```

Delete the spike method entirely — it has served its purpose.

- [ ] **Step 5: Build, deploy, and look at it**

```bash
cd ItemDrawers && dotnet build -c Release
cp src/ItemDrawers.Game/bin/Release/netstandard2.1/ItemDrawers.dll \
  "/c/Users/ross/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/dev/BepInEx/plugins/"
```

Launch, load a world, open the Hammer's Furniture tab.

Expected: three drawer entries. Place one of each. Check:
- The mesh is not inside out (you can see the front, not the inside of the back).
- The recessed panel and bar handle are visible.
- Each tier wears a visibly different material.
- Placing two side by side leaves no gap and no overlap — they tile.

If the mesh renders inside out despite Task 4's winding test passing, the cause is the handedness difference between the test's assumption and Unity's clockwise-front convention. Fix it by reversing triangle order in `ToUnityMesh`, not by changing the core.

- [ ] **Step 6: Commit**

```bash
git add ItemDrawers/src
git commit -m "Build real drawer prefabs from generated geometry

Three tiers sharing one mesh, each borrowing a vanilla material so the
drawers inherit the game's wear, wet and snow shading rather than
carrying their own."
```

---

## Task 8: DrawerComponent — ZDO state and player interaction

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Game/DrawerComponent.cs`
- Modify: `ItemDrawers/src/ItemDrawers.Game/DrawerPieces.cs` (attach the component, `Piece`, `WearNTear`)

**Interfaces:**
- Consumes: `DrawerState`, `DrawerSnapshot`, `DrawerOutcome` from Task 3; `DrawerTiers` from Task 7.
- Produces: `class DrawerComponent : Container, Interactable, Hoverable` exposing
  `DrawerSnapshot Snapshot { get; }`, `int Capacity { get; }`, `DrawerTier Tier { get; }`,
  `bool TryWithdrawExternally(int requested, out int taken)`,
  `bool TryDepositExternally(string itemName, int amount, out int accepted)`,
  and `static readonly List<DrawerComponent> All`.

- [ ] **Step 1: Write the component**

`src/ItemDrawers.Game/DrawerComponent.cs`:

```csharp
using System.Collections.Generic;
using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Derives from Container so that container-aware mods find drawers
    /// without knowing this mod exists, and re-declares Interactable and
    /// Hoverable so player interaction reaches this class rather than
    /// Container's — re-implementing an interface in a derived type
    /// replaces the interface mapping.
    /// </summary>
    public class DrawerComponent : Container, Interactable, Hoverable
    {
        public static readonly List<DrawerComponent> All = new List<DrawerComponent>();

        private const string KeyPrefab = "Prefab";
        private const string KeyAmount = "Amount";

        private ZNetView _view;
        internal DrawerTier Tier { get; private set; }

        public DrawerSnapshot Snapshot => _view != null && _view.IsValid()
            ? new DrawerSnapshot(_view.GetZDO().GetString(KeyPrefab, ""), _view.GetZDO().GetInt(KeyAmount, 0))
            : new DrawerSnapshot("", 0);

        public int Capacity => DrawerConfig.CapacityFor(Tier);

        private new void Awake()
        {
            _view = GetComponent<ZNetView>();
            if (_view == null || !_view.IsValid()) return;

            Tier = TierFromPrefabName(gameObject.name);

            // Container.Awake is not relied upon: the mirror inventory this
            // class hands to other mods is constructed by ContainerBridge.
            All.Add(this);
            DrawerManager.Instance?.Register(this);
        }

        private void OnDestroy()
        {
            All.Remove(this);
            DrawerManager.Instance?.Unregister(this);
        }

        private static DrawerTier TierFromPrefabName(string name)
        {
            foreach (var tier in DrawerTiers.All)
                if (name.StartsWith(DrawerTiers.PrefabName(tier)))
                    return tier;
            return DrawerTier.Wood;
        }

        /// <summary>
        /// The one place drawer state is written. Claims ZDO ownership first,
        /// because writing to a ZDO you do not own is how items get duplicated.
        /// </summary>
        private bool Commit(DrawerSnapshot next)
        {
            if (_view == null || !_view.IsValid()) return false;

            _view.ClaimOwnership();
            var zdo = _view.GetZDO();
            zdo.Set(KeyPrefab, next.ItemName);
            zdo.Set(KeyAmount, next.Amount);

            DrawerManager.Instance?.MarkDirty(this);
            return true;
        }

        // ---------- player interaction ----------

        public new bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            var player = user as Player;
            if (player == null) return false;

            var current = Snapshot;

            if (alt)
            {
                var outcome = current.IsEmpty
                    ? DrawerState.Clear(current)
                    : DrawerState.WithdrawOne(current);
                return Apply(player, outcome);
            }

            if (ZInput.GetButton("Sneak") || Input.GetKey(KeyCode.LeftShift))
                return DepositEverythingMatching(player, current);

            int maxStack = ItemFacts.MaxStackSize(current.ItemName);
            return Apply(player, DrawerState.WithdrawStack(current, maxStack));
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            var player = user as Player;
            if (player == null || item == null) return false;

            if (item.m_shared.m_maxStackSize <= 1)
            {
                player.Message(MessageHud.MessageType.Center,
                    "Drawers only hold stackable items");
                return true;
            }

            string itemName = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
            var outcome = DrawerState.Deposit(Snapshot, Capacity, itemName, item.m_stack);
            if (!outcome.Accepted)
            {
                player.Message(MessageHud.MessageType.Center, outcome.Rejection);
                return true;
            }

            if (!Commit(outcome.Result)) return true;
            player.GetInventory().RemoveItem(item, outcome.MovedToDrawer);
            return true;
        }

        private bool DepositEverythingMatching(Player player, DrawerSnapshot current)
        {
            if (!current.IsAssigned)
            {
                player.Message(MessageHud.MessageType.Center, DrawerState.NotAssigned);
                return true;
            }

            int total = 0;
            var inventory = player.GetInventory();
            foreach (var item in new List<ItemDrop.ItemData>(inventory.GetAllItems()))
            {
                string name = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
                if (name != current.ItemName) continue;
                total += item.m_stack;
            }

            if (total <= 0)
            {
                player.Message(MessageHud.MessageType.Center, "You have none of those");
                return true;
            }

            var outcome = DrawerState.Deposit(current, Capacity, current.ItemName, total);
            if (!outcome.Accepted)
            {
                player.Message(MessageHud.MessageType.Center, outcome.Rejection);
                return true;
            }

            if (!Commit(outcome.Result)) return true;
            inventory.RemoveItem(current.ItemName, outcome.MovedToDrawer);
            player.Message(MessageHud.MessageType.TopLeft,
                $"Stored {outcome.MovedToDrawer} {current.ItemName}");
            return true;
        }

        private bool Apply(Player player, DrawerOutcome outcome)
        {
            if (!outcome.Accepted)
            {
                player.Message(MessageHud.MessageType.Center, outcome.Rejection);
                return true;
            }

            if (!Commit(outcome.Result)) return true;

            if (outcome.MovedToPlayer > 0)
                ItemFacts.GiveToPlayer(player, outcome.Result.ItemName, outcome.MovedToPlayer);

            return true;
        }

        // ---------- external access, used by ContainerBridge ----------

        public bool TryWithdrawExternally(int requested, out int taken)
        {
            var outcome = DrawerState.WithdrawExact(Snapshot, requested);
            taken = outcome.MovedToPlayer;
            if (taken <= 0) return false;
            return Commit(outcome.Result);
        }

        public bool TryDepositExternally(string itemName, int amount, out int accepted)
        {
            var outcome = DrawerState.Deposit(Snapshot, Capacity, itemName, amount);
            accepted = outcome.MovedToDrawer;
            if (!outcome.Accepted || accepted <= 0) return false;
            return Commit(outcome.Result);
        }

        // ---------- hover ----------

        public new string GetHoverText()
        {
            var s = Snapshot;
            if (!s.IsAssigned)
                return Localization.instance.Localize(
                    $"{DrawerTiers.DisplayName(Tier)}\n[<color=yellow><b>1-8</b></color>] Store an item");

            string label = ItemFacts.LocalizedName(s.ItemName);
            return Localization.instance.Localize(
                $"{label}  <color=orange>{s.Amount}</color>/{Capacity}\n" +
                "[<color=yellow><b>E</b></color>] Take stack   " +
                "[<color=yellow><b>Alt+E</b></color>] Take one   " +
                "[<color=yellow><b>Shift+E</b></color>] Store all");
        }

        public new string GetHoverName() => DrawerTiers.DisplayName(Tier);
    }
}
```

- [ ] **Step 2: Write the item lookup helper**

`src/ItemDrawers.Game/ItemFacts.cs`:

```csharp
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>Thin wrapper over ObjectDB so the rest of the code needn't repeat null checks.</summary>
    internal static class ItemFacts
    {
        public static GameObject Prefab(string itemName) =>
            string.IsNullOrEmpty(itemName) || ObjectDB.instance == null
                ? null
                : ObjectDB.instance.GetItemPrefab(itemName);

        public static ItemDrop Drop(string itemName)
        {
            var prefab = Prefab(itemName);
            return prefab == null ? null : prefab.GetComponent<ItemDrop>();
        }

        public static bool IsStorable(string itemName)
        {
            var drop = Drop(itemName);
            return drop != null && drop.m_itemData.m_shared.m_maxStackSize > 1;
        }

        public static int MaxStackSize(string itemName)
        {
            var drop = Drop(itemName);
            return drop == null ? 1 : drop.m_itemData.m_shared.m_maxStackSize;
        }

        public static string LocalizedName(string itemName)
        {
            var drop = Drop(itemName);
            return drop == null
                ? itemName
                : Localization.instance.Localize(drop.m_itemData.m_shared.m_name);
        }

        public static Sprite Icon(string itemName)
        {
            var drop = Drop(itemName);
            return drop == null ? null : drop.m_itemData.GetIcon();
        }

        /// <summary>
        /// Gives items to a player, spilling to the ground if the inventory
        /// is full. Never silently deletes: a drawer that eats your iron is
        /// worse than one that drops it at your feet.
        /// </summary>
        public static void GiveToPlayer(Player player, string itemName, int amount)
        {
            var prefab = Prefab(itemName);
            if (prefab == null || amount <= 0) return;

            int stackSize = MaxStackSize(itemName);
            var inventory = player.GetInventory();

            while (amount > 0)
            {
                int chunk = amount < stackSize ? amount : stackSize;

                if (!inventory.AddItem(prefab.name, chunk, 1, 0, 0L, ""))
                {
                    var dropped = Object.Instantiate(
                        prefab,
                        player.transform.position + player.transform.forward + Vector3.up,
                        Quaternion.identity);
                    var drop = dropped.GetComponent<ItemDrop>();
                    if (drop != null)
                    {
                        drop.m_itemData.m_stack = chunk;
                        drop.Save();
                    }
                }
                amount -= chunk;
            }
        }
    }
}
```

- [ ] **Step 3: Attach the component to the prefab**

In `DrawerPieces.BuildPrefab`, replace the `go.AddComponent<ZNetView>();` line and the comment beneath it with:

```csharp
            go.AddComponent<ZNetView>();
            go.AddComponent<DrawerComponent>();

            var piece = go.AddComponent<Piece>();
            piece.m_name = DrawerTiers.DisplayName(tier);
            piece.m_category = Piece.PieceCategory.Furniture;
            piece.m_groundPiece = false;
            piece.m_allowAltGroundPlacement = true;
            piece.m_groundOnly = false;
            piece.m_cultivatedGroundOnly = false;
            piece.m_targetNonPlayerBuilt = false;
            piece.m_comfort = 0;

            var wear = go.AddComponent<WearNTear>();
            wear.m_health = tier == DrawerTier.Wood ? 200f : 400f;
            wear.m_noSupportWear = false;
            wear.m_noRoofWear = tier != DrawerTier.Wood;
            wear.m_destroyedEffect = donor.GetComponent<WearNTear>()?.m_destroyedEffect;
            wear.m_hitEffect = donor.GetComponent<WearNTear>()?.m_hitEffect;
```

- [ ] **Step 4: Build and verify in game**

```bash
cd ItemDrawers && dotnet build -c Release && cp src/ItemDrawers.Game/bin/Release/netstandard2.1/ItemDrawers.dll "/c/Users/ross/AppData/Roaming/r2modmanPlus-local/Valheim/profiles/dev/BepInEx/plugins/"
```

Walk the full control scheme against a placed wood drawer. Each line is a separate check:

- Hover an unassigned drawer → hover text invites you to store an item.
- Select wood on the hotbar, press the hotbar key → the drawer takes it and the hover text shows `Wood 20/1000`.
- Press E → you receive one stack, the count drops.
- Alt+E → you receive exactly one.
- Drain it to zero, then Alt+E → the drawer unassigns and hover text resets.
- Refill it, then Alt+E with items still inside → refused, count unchanged.
- Shift+E with wood in your inventory → everything transfers, capped at 1000.
- Try to store a sword → refused with the stackable-items message.
- Fill to exactly 1000, try to add more → refused, nothing lost from your inventory.

**The last one matters most.** If items vanish from your inventory without arriving in the drawer, stop and fix it before continuing — that is the bug class this whole design exists to avoid.

- [ ] **Step 5: Commit**

```bash
git add ItemDrawers/src
git commit -m "Add drawer state and the original control scheme

All five of makail's controls, resolved through the tested core rules.
Every write claims ZDO ownership first, and withdrawals spill to the
ground rather than vanishing when a player's inventory is full."
```

---

## Task 9: Label rendering — icon atlas and count

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Game/DrawerIconAtlas.cs`
- Create: `ItemDrawers/src/ItemDrawers.Game/DrawerRenderer.cs`
- Modify: `ItemDrawers/src/ItemDrawers.Game/DrawerPieces.cs`
- Modify: `ItemDrawers/src/ItemDrawers.Game/DrawerComponent.cs`

**Interfaces:**
- Consumes: `IconAtlasPacker`, `AtlasLayout`, `IconSize` from Task 5; `DrawerProportions.LabelSize` from Task 2.
- Produces:
  - `static class DrawerIconAtlas { static void Build(); static bool TryGetUv(string item, out Vector2 min, out Vector2 max); static Material SharedMaterial; static bool IsBuilt; }`
  - `class DrawerRenderer : MonoBehaviour` exposing `void Show(string itemName, int amount)`, `void Hide()`, `void SetLabelVisible(bool visible)`.

- [ ] **Step 1: Build the atlas from ObjectDB**

`src/ItemDrawers.Game/DrawerIconAtlas.cs`:

```csharp
using System.Collections.Generic;
using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// One texture holding every storable item's icon. This is what lets a
    /// hundred drawer labels share a single material and batch into one
    /// draw call — the old mods gave each drawer its own Canvas instead,
    /// which is where their frame cost went.
    /// </summary>
    public static class DrawerIconAtlas
    {
        private static AtlasLayout _layout;
        private static Texture2D _texture;

        public static Material SharedMaterial { get; private set; }
        public static bool IsBuilt => _layout != null && SharedMaterial != null;

        public static void Build()
        {
            if (IsBuilt) return;
            if (ObjectDB.instance == null)
            {
                DrawerPlugin.Log.LogWarning("ObjectDB not ready; icon atlas deferred.");
                return;
            }

            var sizes = new List<IconSize>();
            var sprites = new Dictionary<string, Sprite>();

            foreach (var prefab in ObjectDB.instance.m_items)
            {
                var drop = prefab == null ? null : prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;
                if (drop.m_itemData.m_shared.m_maxStackSize <= 1) continue;

                var icon = drop.m_itemData.GetIcon();
                if (icon == null || icon.texture == null) continue;
                if (sprites.ContainsKey(prefab.name)) continue;

                sprites[prefab.name] = icon;
                sizes.Add(new IconSize(prefab.name,
                    (int)icon.rect.width, (int)icon.rect.height));
            }

            _layout = IconAtlasPacker.Pack(sizes);

            _texture = new Texture2D(_layout.Width, _layout.Height, TextureFormat.RGBA32, mipChain: true)
            {
                name = "rid_icon_atlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var clear = new Color32[_layout.Width * _layout.Height];
            _texture.SetPixels32(clear);

            foreach (var pair in _layout.Rects)
            {
                if (!sprites.TryGetValue(pair.Key, out var sprite)) continue;
                if (!TryReadSprite(sprite, out var pixels, out int w, out int h)) continue;

                _texture.SetPixels32(pair.Value.X, pair.Value.Y, w, h, pixels);
            }

            _texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);

            SharedMaterial = new Material(Shader.Find("Unlit/Transparent"))
            {
                name = "rid_icon_material",
                mainTexture = _texture
            };

            DrawerPlugin.Log.LogInfo(
                $"Icon atlas built: {_layout.Rects.Count} icons in {_layout.Width}x{_layout.Height}");
        }

        /// <summary>
        /// Item icons usually live on a non-readable atlas texture, so they
        /// are blitted through a temporary RenderTexture rather than read
        /// directly, which would throw.
        /// </summary>
        private static bool TryReadSprite(Sprite sprite, out Color32[] pixels, out int width, out int height)
        {
            pixels = null;
            width = (int)sprite.rect.width;
            height = (int)sprite.rect.height;
            if (width <= 0 || height <= 0) return false;

            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            try
            {
                Graphics.Blit(sprite.texture, rt);
                RenderTexture.active = rt;

                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(sprite.rect.x, sprite.rect.y, width, height), 0, 0);
                readable.Apply();
                pixels = readable.GetPixels32();
                Object.Destroy(readable);
                return true;
            }
            catch (System.Exception e)
            {
                DrawerPlugin.Log.LogWarning($"Could not read icon for atlas: {e.Message}");
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static bool TryGetUv(string item, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.one;
            if (_layout == null) return false;
            if (!_layout.TryGetUv(item, out float u0, out float v0, out float u1, out float v1)) return false;

            min = new Vector2(u0, v0);
            max = new Vector2(u1, v1);
            return true;
        }
    }
}
```

- [ ] **Step 2: Write the renderer**

`src/ItemDrawers.Game/DrawerRenderer.cs`:

```csharp
using ItemDrawers.Core;
using TMPro;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// A drawer's face. Four vertices for the icon whose UVs address the
    /// shared atlas, plus a 3D TextMeshPro for the count. No Canvas, and no
    /// per-drawer material, so every drawer in a wall batches together.
    /// </summary>
    public class DrawerRenderer : MonoBehaviour
    {
        private MeshFilter _iconFilter;
        private MeshRenderer _iconRenderer;
        private TextMeshPro _countText;
        private Mesh _quad;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector2[] _uvs = new Vector2[4];

        private string _shownItem;
        private int _shownAmount = -1;

        public static DrawerRenderer Attach(GameObject drawer, DrawerProportions p)
        {
            var root = new GameObject("label");
            root.transform.SetParent(drawer.transform, false);

            float size = p.LabelSize;
            float y = p.Handle == HandleStyle.Bar ? p.HandleSection * 1.1f : 0f;
            float z = p.Depth / 2f - p.RecessDepth + 0.004f;
            root.transform.localPosition = new Vector3(0f, y, z);

            var renderer = root.AddComponent<DrawerRenderer>();
            renderer.Initialise(size);
            return renderer;
        }

        private void Initialise(float size)
        {
            float h = size / 2f;
            _corners[0] = new Vector3(-h, -h, 0f);
            _corners[1] = new Vector3(-h, h, 0f);
            _corners[2] = new Vector3(h, h, 0f);
            _corners[3] = new Vector3(h, -h, 0f);

            _quad = new Mesh { name = "rid_label_quad" };
            _quad.SetVertices(_corners);
            _quad.SetUVs(0, _uvs);
            _quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            _quad.RecalculateNormals();
            _quad.RecalculateBounds();
            _quad.MarkDynamic();

            var iconGo = new GameObject("icon");
            iconGo.transform.SetParent(transform, false);
            _iconFilter = iconGo.AddComponent<MeshFilter>();
            _iconFilter.sharedMesh = _quad;
            _iconRenderer = iconGo.AddComponent<MeshRenderer>();
            _iconRenderer.sharedMaterial = DrawerIconAtlas.SharedMaterial;
            _iconRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _iconRenderer.receiveShadows = false;

            var textGo = new GameObject("count");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, -size * 0.62f, 0f);
            _countText = textGo.AddComponent<TextMeshPro>();
            _countText.alignment = TextAlignmentOptions.Center;
            _countText.fontSize = size * 9f;
            _countText.color = new Color32(246, 236, 214, 255);
            _countText.outlineWidth = 0.22f;
            _countText.outlineColor = new Color32(12, 9, 6, 235);
            _countText.enableWordWrapping = false;
            _countText.raycastTarget = false;

            var rect = textGo.GetComponent<RectTransform>();
            if (rect != null) rect.sizeDelta = new Vector2(size * 2f, size * 0.6f);

            Hide();
        }

        public void Show(string itemName, int amount)
        {
            if (itemName == _shownItem && amount == _shownAmount) return;   // nothing changed

            if (string.IsNullOrEmpty(itemName) || !DrawerIconAtlas.TryGetUv(itemName, out var min, out var max))
            {
                Hide();
                _shownItem = itemName;
                _shownAmount = amount;
                return;
            }

            if (itemName != _shownItem)
            {
                // Repointing four UVs is the whole cost of changing a
                // drawer's item. No new material, no atlas rebuild.
                _uvs[0] = new Vector2(min.x, min.y);
                _uvs[1] = new Vector2(min.x, max.y);
                _uvs[2] = new Vector2(max.x, max.y);
                _uvs[3] = new Vector2(max.x, min.y);
                _quad.SetUVs(0, _uvs);
            }

            _iconRenderer.enabled = true;
            _countText.enabled = amount > 0;
            if (amount > 0) _countText.SetText("{0}", amount);

            _shownItem = itemName;
            _shownAmount = amount;
        }

        public void Hide()
        {
            if (_iconRenderer != null) _iconRenderer.enabled = false;
            if (_countText != null) _countText.enabled = false;
        }

        /// <summary>Distance culling, driven by DrawerManager. A wall of unreadable text is wasted work.</summary>
        public void SetLabelVisible(bool visible)
        {
            if (_iconRenderer == null || _countText == null) return;
            if (!visible)
            {
                _iconRenderer.enabled = false;
                _countText.enabled = false;
                return;
            }
            _iconRenderer.enabled = !string.IsNullOrEmpty(_shownItem);
            _countText.enabled = _shownAmount > 0;
        }

        private void OnDestroy()
        {
            if (_quad != null) Destroy(_quad);
        }
    }
}
```

- [ ] **Step 3: Wire the atlas and renderer in**

In `DrawerPlugin.OnPrefabsReady`, before `DrawerPieces.RegisterAll()`:

```csharp
            DrawerIconAtlas.Build();
```

In `DrawerPieces.BuildPrefab`, after `go.AddComponent<DrawerComponent>();`:

```csharp
            DrawerRenderer.Attach(go, new DrawerProportions());
```

In `DrawerComponent`, add a field and resolve it in `Awake`:

```csharp
        internal DrawerRenderer Face { get; private set; }
```

```csharp
            Face = GetComponentInChildren<DrawerRenderer>();
            RefreshFace();
```

and add:

```csharp
        internal void RefreshFace()
        {
            if (Face == null) return;
            var s = Snapshot;
            Face.Show(s.ItemName, s.Amount);
        }
```

Call `RefreshFace()` at the end of `Commit`, after `MarkDirty`.

- [ ] **Step 4: Build and verify in game**

Expected:
- A drawer holding wood shows the wood icon and the number on its front.
- Depositing changes the number immediately.
- Emptying to zero hides the number but keeps the icon.
- Clearing hides both.
- Ten drawers with ten different items each show the correct icon — this is the check that the atlas UVs are not off by one cell.

- [ ] **Step 5: Commit**

```bash
git add ItemDrawers/src
git commit -m "Render drawer faces from a shared icon atlas

Icons are four vertices whose UVs address one atlas texture and counts
are 3D TextMeshPro, so every label in a wall shares a material and
batches. Changing a drawer's item rewrites four UVs and allocates
nothing."
```

---

## Task 10: DrawerManager — the single tick

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Game/DrawerManager.cs`
- Modify: `ItemDrawers/src/ItemDrawers.Game/DrawerPlugin.cs`

**Interfaces:**
- Consumes: `SpatialGrid<T>` from Task 6; `DrawerComponent` from Task 8.
- Produces: `class DrawerManager : MonoBehaviour` exposing `static DrawerManager Instance`, `void Register(DrawerComponent d)`, `void Unregister(DrawerComponent d)`, `void MarkDirty(DrawerComponent d)`, `void QueryNear(Vector3 pos, float radius, List<DrawerComponent> results)`.

- [ ] **Step 1: Write the manager**

`src/ItemDrawers.Game/DrawerManager.cs`:

```csharp
using System.Collections.Generic;
using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// The only ticking object in this mod. Individual drawers have no
    /// Update and no InvokeRepeating: a hundred of them in a wall must cost
    /// what one costs, which is why every periodic concern lives here.
    /// </summary>
    public class DrawerManager : MonoBehaviour
    {
        public static DrawerManager Instance { get; private set; }

        private readonly SpatialGrid<DrawerComponent> _grid = new SpatialGrid<DrawerComponent>(8f);
        private readonly List<DrawerComponent> _all = new List<DrawerComponent>();
        private readonly HashSet<DrawerComponent> _dirty = new HashSet<DrawerComponent>();
        private readonly List<DrawerComponent> _scratch = new List<DrawerComponent>(64);

        private float _cullTimer;
        private int _syncCursor;

        public static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("RidDrawerManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DrawerManager>();
        }

        public void Register(DrawerComponent drawer)
        {
            if (drawer == null || _all.Contains(drawer)) return;
            _all.Add(drawer);
            var p = drawer.transform.position;
            _grid.Insert(drawer, p.x, p.y, p.z);
        }

        public void Unregister(DrawerComponent drawer)
        {
            if (drawer == null) return;
            _all.Remove(drawer);
            _dirty.Remove(drawer);
            _grid.Remove(drawer);
        }

        public void MarkDirty(DrawerComponent drawer)
        {
            if (drawer != null) _dirty.Add(drawer);
        }

        public void QueryNear(Vector3 position, float radius, List<DrawerComponent> results)
        {
            _grid.Query(position.x, position.y, position.z, radius, results);
        }

        private void Update()
        {
            if (_dirty.Count > 0) FlushDirty();

            SyncSomeFaces();

            _cullTimer -= Time.deltaTime;
            if (_cullTimer <= 0f)
            {
                _cullTimer = 0.5f;
                UpdateLabelVisibility();
            }
        }

        /// <summary>Drawers this client changed itself: refresh immediately.</summary>
        private void FlushDirty()
        {
            foreach (var drawer in _dirty)
                if (drawer != null) drawer.RefreshFace();
            _dirty.Clear();
        }

        /// <summary>
        /// Drawers another client changed: their ZDO values arrive by
        /// replication with no local event, so a slice of the list is
        /// re-read each frame. Show() returns immediately when nothing
        /// changed, so this is a cheap comparison, not a rebuild.
        /// </summary>
        private void SyncSomeFaces()
        {
            if (_all.Count == 0) return;

            int slice = Mathf.Max(1, _all.Count / 30);   // whole list about twice a second
            for (int i = 0; i < slice; i++)
            {
                if (_syncCursor >= _all.Count) _syncCursor = 0;
                var drawer = _all[_syncCursor++];
                if (drawer != null) drawer.RefreshFace();
            }
        }

        private void UpdateLabelVisibility()
        {
            var camera = Camera.main;
            if (camera == null) return;

            var eye = camera.transform.position;
            float radius = DrawerConfig.LabelDistance.Value;
            float radiusSq = radius * radius;

            for (int i = 0; i < _all.Count; i++)
            {
                var drawer = _all[i];
                if (drawer == null) continue;
                drawer.Face?.SetLabelVisible(
                    (drawer.transform.position - eye).sqrMagnitude <= radiusSq);
            }
        }
    }
}
```

- [ ] **Step 2: Create the manager on plugin load**

In `DrawerPlugin.Awake`, after the Harmony patch call:

```csharp
            DrawerManager.Create();
```

- [ ] **Step 3: Build and verify in game**

- Place several drawers, fill them, walk away past 20 m: labels disappear.
- Walk back: labels return with correct icons and counts.
- Confirm no drawer defines `Update` — search the source:

```bash
grep -n "void Update\|InvokeRepeating\|Canvas\|TextMeshProUGUI" ItemDrawers/src/ItemDrawers.Game/*.cs
```

Expected: `Update` appears only in `DrawerManager.cs`; the other three terms appear nowhere. This is a Global Constraint — if any of them appear elsewhere, remove them.

- [ ] **Step 4: Commit**

```bash
git add ItemDrawers/src
git commit -m "Centralise all periodic work in one manager

No drawer has an Update or an InvokeRepeating. Faces changed locally
refresh immediately; faces changed by other clients are re-read a
slice at a time, and labels beyond reading distance switch off."
```

---

## Task 11: Auto-pickup

**Files:**
- Modify: `ItemDrawers/src/ItemDrawers.Game/DrawerManager.cs`

**Interfaces:**
- Consumes: `QueryNear` from Task 10; `TryDepositExternally` from Task 8.
- Produces: no new public surface; auto-pickup is internal to the manager.

- [ ] **Step 1: Confirm how to enumerate dropped items**

The spec (§14) flags this as unverified. Check what Valheim 1.0 offers:

```bash
grep -rn "s_instances\|m_instances" ItemDrawers/src 2>/dev/null
```

Inspect `ItemDrop` in a decompiler, or test at runtime, for a static instance list. If one exists, use it. **If none exists**, fall back to a single `Physics.OverlapSphere` around the local player from the manager, on a timer — still one query for the whole world, not one per drawer. Record which path you took in the commit message.

- [ ] **Step 2: Add the pickup pass**

Add to `DrawerManager`:

```csharp
        private float _pickupTimer;
        private readonly List<DrawerComponent> _pickupScratch = new List<DrawerComponent>(16);
        private readonly Collider[] _overlap = new Collider[64];
```

In `Update`, after the cull block:

```csharp
            _pickupTimer -= Time.deltaTime;
            if (_pickupTimer <= 0f)
            {
                _pickupTimer = DrawerConfig.PickupInterval.Value;
                if (DrawerConfig.AutoPickupEnabled.Value) RunAutoPickup();
            }
```

And the pass itself:

```csharp
        /// <summary>
        /// Iterates dropped items and asks which drawers are near them --
        /// never the reverse. Cost scales with items on the ground, which is
        /// normally zero, rather than with the number of drawers.
        /// </summary>
        private void RunAutoPickup()
        {
            var player = Player.m_localPlayer;
            if (player == null || _all.Count == 0) return;

            float radius = DrawerConfig.PickupRadius.Value;

            int found = Physics.OverlapSphereNonAlloc(
                player.transform.position,
                DrawerConfig.PickupScanRange.Value,
                _overlap,
                LayerMask.GetMask("item"));

            for (int i = 0; i < found; i++)
            {
                var drop = _overlap[i] == null ? null : _overlap[i].GetComponentInParent<ItemDrop>();
                if (drop == null || drop.m_nview == null || !drop.m_nview.IsValid()) continue;

                // Only the owning client absorbs an item, so two clients
                // cannot pick up the same drop twice.
                if (!drop.m_nview.IsOwner()) continue;
                if (drop.m_itemData == null || drop.m_itemData.m_shared.m_maxStackSize <= 1) continue;

                string itemName = drop.m_itemData.m_dropPrefab != null
                    ? drop.m_itemData.m_dropPrefab.name
                    : drop.name.Replace("(Clone)", "");

                _pickupScratch.Clear();
                QueryNear(drop.transform.position, radius, _pickupScratch);

                for (int d = 0; d < _pickupScratch.Count; d++)
                {
                    var drawer = _pickupScratch[d];
                    if (drawer == null) continue;

                    var snapshot = drawer.Snapshot;
                    if (!snapshot.IsAssigned || snapshot.ItemName != itemName) continue;

                    if (!drawer.TryDepositExternally(itemName, drop.m_itemData.m_stack, out int accepted))
                        continue;
                    if (accepted <= 0) continue;

                    drop.m_itemData.m_stack -= accepted;
                    if (drop.m_itemData.m_stack <= 0)
                    {
                        drop.m_nview.ClaimOwnership();
                        ZNetScene.instance.Destroy(drop.gameObject);
                    }
                    else
                    {
                        drop.Save();
                    }
                    break;                       // this drop is handled
                }
            }
        }
```

- [ ] **Step 3: Build and verify in game**

- Assign a drawer to Wood, drop wood on the floor within the radius → it is absorbed within a second.
- Drop stone next to a Wood drawer → it stays on the ground.
- Fill the drawer to capacity, drop more wood → it stays on the ground, nothing is destroyed.
- Drop a sword → ignored.
- With no items on the ground anywhere, confirm no measurable cost: this is re-checked properly in Task 13.

**The capacity case is the one to be careful about.** If a full drawer destroys the dropped item without storing it, that is item loss — fix before continuing.

- [ ] **Step 4: Commit**

```bash
git add ItemDrawers/src
git commit -m "Absorb matching dropped items into drawers

The loop runs over dropped items and asks which drawers are near them,
never the reverse, so a wall of a hundred drawers over a clean floor
costs nothing. Only the client owning a drop may absorb it."
```

---

## Task 12: The Container bridge

The crux. Makes drawers legible to OttoFuel and NoVikingLeftBehind without either mod knowing this one exists.

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Game/ContainerBridge.cs`
- Create: `ItemDrawers/src/ItemDrawers.Game/ItemDrawersAPI.cs`
- Modify: `ItemDrawers/src/ItemDrawers.Game/DrawerComponent.cs`

**Interfaces:**
- Consumes: `DrawerComponent`, `DrawerState.WithdrawExact` from Tasks 3 and 8.
- Produces:
  - `internal Inventory DrawerComponent.MirrorInventory` and `internal void RefreshMirror()`
  - `static class ItemDrawersAPI` with `static IReadOnlyList<DrawerComponent> AllDrawers`, `static int CountItem(Vector3 near, float radius, string itemName)`, `static int Withdraw(Vector3 near, float radius, string itemName, int amount)`.

- [ ] **Step 1: Add the mirror to DrawerComponent**

```csharp
        private Inventory _mirror;
        private DrawerSnapshot _mirrored = new DrawerSnapshot("", -1);
        private bool _applyingMirror;

        internal Inventory MirrorInventory
        {
            get
            {
                if (_mirror == null)
                {
                    _mirror = new Inventory("drawer", null, 1, 1);
                    _mirror.m_onChanged += OnMirrorChanged;
                }
                return _mirror;
            }
        }

        /// <summary>
        /// Brings the mirror in line with the ZDO, but only when something
        /// actually changed. One ItemData carries the entire count -- four
        /// thousand coal is one stack of four thousand, not eighty stacks of
        /// fifty. Materialising real stacks would mean twenty thousand
        /// objects across a wall of a hundred marble drawers, rebuilt on
        /// every scan, which is exactly the cost this mod exists to avoid.
        /// </summary>
        internal void RefreshMirror()
        {
            var current = Snapshot;
            if (current.Equals(_mirrored)) return;

            _applyingMirror = true;
            try
            {
                var inventory = MirrorInventory;
                inventory.RemoveAll();

                if (current.IsAssigned && !current.IsEmpty)
                {
                    var prefab = ItemFacts.Prefab(current.ItemName);
                    if (prefab != null)
                    {
                        var drop = prefab.GetComponent<ItemDrop>();
                        if (drop != null)
                        {
                            var data = drop.m_itemData.Clone();
                            data.m_stack = current.Amount;
                            data.m_dropPrefab = prefab;
                            data.m_gridPos = new Vector2i(0, 0);
                            inventory.AddItem(data);
                        }
                    }
                }
                _mirrored = current;
            }
            finally
            {
                _applyingMirror = false;
            }
        }

        /// <summary>
        /// Fires when another mod removed from the mirror. The delta is
        /// always valid because Inventory.RemoveItem cannot take more than
        /// is present.
        /// </summary>
        private void OnMirrorChanged()
        {
            if (_applyingMirror) return;

            int mirrored = 0;
            foreach (var item in _mirror.GetAllItems()) mirrored += item.m_stack;

            var current = Snapshot;
            int delta = current.Amount - mirrored;
            if (delta <= 0) { _mirrored = current; return; }

            var outcome = DrawerState.WithdrawExact(current, delta);
            Commit(outcome.Result);
            _mirrored = outcome.Result;
        }
```

- [ ] **Step 2: Write the Harmony patches**

`src/ItemDrawers.Game/ContainerBridge.cs`:

```csharp
using HarmonyLib;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Three patches on Container, each guarded by a type check so vanilla
    /// containers are untouched.
    /// </summary>
    internal static class ContainerBridge
    {
        /// <summary>
        /// Container.GetInventory is not virtual, so a subclass cannot
        /// override it. This is the lazy refresh point.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.GetInventory))]
        private static class GetInventoryPatch
        {
            private static void Prefix(Container __instance)
            {
                if (__instance is DrawerComponent drawer) drawer.RefreshMirror();
            }

            private static void Postfix(Container __instance, ref Inventory __result)
            {
                if (__instance is DrawerComponent drawer) __result = drawer.MirrorInventory;
            }
        }

        /// <summary>
        /// Without this, every autosave serialises the mirror -- a ten
        /// thousand item stack -- into the ZDO's items field, bloating the
        /// save and shadowing the two fields that are actually authoritative.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Save))]
        private static class SavePatch
        {
            private static bool Prefix(Container __instance) => !(__instance is DrawerComponent);
        }

        /// <summary>Counterpart to SavePatch: vanilla would overwrite drawer state from that field.</summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Load))]
        private static class LoadPatch
        {
            private static bool Prefix(Container __instance) => !(__instance is DrawerComponent);
        }
    }
}
```

- [ ] **Step 3: Write the public API**

`src/ItemDrawers.Game/ItemDrawersAPI.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Deliberate integration point for other mods, so they need not infer
    /// behaviour from the Container surface. This is what we would have
    /// wanted from the original ItemDrawers.
    /// </summary>
    public static class ItemDrawersAPI
    {
        public static IReadOnlyList<DrawerComponent> AllDrawers => DrawerComponent.All;

        public static int CountItem(Vector3 near, float radius, string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || DrawerManager.Instance == null) return 0;

            var found = new List<DrawerComponent>();
            DrawerManager.Instance.QueryNear(near, radius, found);

            int total = 0;
            foreach (var drawer in found)
            {
                var s = drawer.Snapshot;
                if (s.ItemName == itemName) total += s.Amount;
            }
            return total;
        }

        /// <summary>Takes up to <paramref name="amount"/>; returns how much was actually taken.</summary>
        public static int Withdraw(Vector3 near, float radius, string itemName, int amount)
        {
            if (string.IsNullOrEmpty(itemName) || amount <= 0 || DrawerManager.Instance == null) return 0;

            var found = new List<DrawerComponent>();
            DrawerManager.Instance.QueryNear(near, radius, found);

            int taken = 0;
            foreach (var drawer in found)
            {
                if (taken >= amount) break;
                if (drawer.Snapshot.ItemName != itemName) continue;

                if (drawer.TryWithdrawExternally(amount - taken, out int got)) taken += got;
            }
            return taken;
        }
    }
}
```

- [ ] **Step 4: Verify against the real mods in game**

With OttoFuel and NoVikingLeftBehind active in the profile:

- Place a drawer, fill it with coal, put a smelter within OttoFuel's range with ore in it → OttoFuel draws coal from the drawer and the drawer's count drops.
- Fill a drawer with wood, stand at a workbench with no wood in your inventory, attempt to build → NVLB counts the drawer's wood and consumes it on build.
- Drain a drawer to zero via OttoFuel → the drawer keeps its item type and shows zero, and does not go negative.
- Save, quit, reload → drawer counts are exactly as left.
- **Inspect the save file size before and after building twenty full drawers.** If it grows by more than a few kilobytes, the `Save` patch is not taking effect.

- [ ] **Step 5: Commit**

```bash
git add ItemDrawers/src
git commit -m "Make drawers legible to container-aware mods

Drawers derive from Container and synthesise a one-stack mirror
inventory on demand, so OttoFuel and NoVikingLeftBehind can read and
drain them without knowing this mod exists. Save and Load are
suppressed for drawers, since vanilla would otherwise serialise the
mirror into the ZDO and shadow the two fields that are authoritative."
```

---

## Task 13: Configuration and server sync

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Game/DrawerConfig.cs`
- Modify: `ItemDrawers/src/ItemDrawers.Game/DrawerPlugin.cs`

**Interfaces:**
- Consumes: `DrawerTiers.DefaultCapacity` from Task 7.
- Produces: `static class DrawerConfig` with `static void Bind(ConfigFile file)`, `static int CapacityFor(DrawerTier tier)`, and the entries `WoodCapacity`, `StoneCapacity`, `BlackMarbleCapacity`, `AutoPickupEnabled`, `PickupRadius`, `PickupScanRange`, `PickupInterval`, `LabelDistance` — all referenced by earlier tasks, so the names must match exactly.

- [ ] **Step 1: Write the config**

`src/ItemDrawers.Game/DrawerConfig.cs`:

```csharp
using BepInEx.Configuration;

namespace ItemDrawers.Game
{
    public static class DrawerConfig
    {
        public static ConfigEntry<int> WoodCapacity;
        public static ConfigEntry<int> StoneCapacity;
        public static ConfigEntry<int> BlackMarbleCapacity;
        public static ConfigEntry<bool> AutoPickupEnabled;
        public static ConfigEntry<float> PickupRadius;
        public static ConfigEntry<float> PickupScanRange;
        public static ConfigEntry<float> PickupInterval;
        public static ConfigEntry<float> LabelDistance;

        public static void Bind(ConfigFile config)
        {
            WoodCapacity = config.Bind("Capacity", "Wood", 1000,
                "How many items a wood drawer holds.");
            StoneCapacity = config.Bind("Capacity", "Stone", 2000,
                "How many items a stone drawer holds.");
            BlackMarbleCapacity = config.Bind("Capacity", "BlackMarble", 10000,
                "How many items a black marble drawer holds.");

            AutoPickupEnabled = config.Bind("Pickup", "Enabled", true,
                "Drawers absorb matching items dropped nearby.");
            PickupRadius = config.Bind("Pickup", "Radius", 4f,
                "How far from a drawer a dropped item is absorbed, in metres.");
            PickupScanRange = config.Bind("Pickup", "ScanRange", 40f,
                "How far around you dropped items are considered at all. " +
                "One query covers every drawer, so this is cheap.");
            PickupInterval = config.Bind("Pickup", "Interval", 0.5f,
                "Seconds between pickup passes.");

            LabelDistance = config.Bind("Display", "LabelDistance", 20f,
                "Beyond this distance drawer icons and counts switch off. " +
                "A wall of text nobody can read is wasted work.");
        }

        public static int CapacityFor(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return WoodCapacity?.Value ?? DrawerTiers.DefaultCapacity(tier);
                case DrawerTier.Stone: return StoneCapacity?.Value ?? DrawerTiers.DefaultCapacity(tier);
                case DrawerTier.BlackMarble: return BlackMarbleCapacity?.Value ?? DrawerTiers.DefaultCapacity(tier);
                default: return DrawerTiers.DefaultCapacity(tier);
            }
        }
    }
}
```

- [ ] **Step 2: Bind on load and enable server sync**

In `DrawerPlugin.Awake`, before `DrawerManager.Create()`:

```csharp
            DrawerConfig.Bind(Config);
```

Then enable Jotunn's config sync so a dedicated server's values govern clients. On each `config.Bind` call, pass a `ConfigurationManagerAttributes { IsAdminOnly = true }` description as the fourth argument — consult Jotunn's current configuration documentation for the exact type name, which has changed between Jotunn versions, and adjust `Bind` accordingly.

Verify sync by setting `WoodCapacity` to 5000 on the server only and confirming a client sees 5000 in the drawer's hover text.

- [ ] **Step 3: Build and verify**

- Change `LabelDistance` to 5 in the config file, restart → labels vanish much sooner.
- Change `WoodCapacity` to 50 → an existing full drawer shows `1000/50` and refuses deposits. Capacity is read live rather than stored, which is intentional; confirm no items are destroyed by the reduction.

- [ ] **Step 4: Commit**

```bash
git add ItemDrawers/src
git commit -m "Bind configuration and sync it from the server

Capacity is read live rather than stored on each drawer, so changing a
tier's capacity applies to drawers already built. Lowering it strands
the excess rather than destroying it."
```

---

## Task 14: Measure the 100-drawer wall

The acceptance criterion the whole design exists to satisfy. Measured, not assumed.

**Files:**
- Create: `ItemDrawers/src/ItemDrawers.Game/DebugCommands.cs`
- Modify: `ItemDrawers/src/ItemDrawers.Game/DrawerPlugin.cs`
- Create: `ItemDrawers/docs/performance.md`

**Interfaces:**
- Consumes: `DrawerTiers`, `DrawerComponent` from Tasks 7 and 8.
- Produces: console commands `rid_wall <cols> <rows> [tier]` and `rid_stats`.

- [ ] **Step 1: Write the debug commands**

`src/ItemDrawers.Game/DebugCommands.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace ItemDrawers.Game
{
    internal static class DebugCommands
    {
        private static readonly string[] SampleItems =
            { "Wood", "Stone", "Coal", "Iron", "Resin", "Flint" };

        public static void Register()
        {
            new Terminal.ConsoleCommand("rid_wall",
                "rid_wall <cols> <rows> [wood|stone|blackmarble] - build a test wall of drawers",
                args =>
                {
                    if (Player.m_localPlayer == null) return;

                    int cols = args.Length > 1 ? int.Parse(args[1]) : 10;
                    int rows = args.Length > 2 ? int.Parse(args[2]) : 10;
                    var tier = DrawerTier.Wood;
                    if (args.Length > 3)
                    {
                        if (args[3].Equals("stone", System.StringComparison.OrdinalIgnoreCase)) tier = DrawerTier.Stone;
                        else if (args[3].StartsWith("black", System.StringComparison.OrdinalIgnoreCase)) tier = DrawerTier.BlackMarble;
                    }

                    var prefab = ZNetScene.instance.GetPrefab(DrawerTiers.PrefabName(tier));
                    if (prefab == null) { args.Context.AddString("Drawer prefab not found"); return; }

                    var player = Player.m_localPlayer.transform;
                    var origin = player.position + player.forward * 6f;
                    var right = player.right;
                    int built = 0;

                    for (int y = 0; y < rows; y++)
                    for (int x = 0; x < cols; x++)
                    {
                        var pos = origin
                                  + right * ((x - (cols - 1) / 2f) * 1.0f)
                                  + Vector3.up * (y * 1.0f + 0.5f);

                        var go = Object.Instantiate(prefab, pos,
                            Quaternion.LookRotation(-player.forward, Vector3.up));

                        var drawer = go.GetComponent<DrawerComponent>();
                        if (drawer != null)
                        {
                            string item = SampleItems[(x + y * cols) % SampleItems.Length];
                            drawer.TryDepositExternally(item, 250 + (x * 7 + y * 13) % 500, out _);
                        }
                        built++;
                    }

                    args.Context.AddString($"Built {built} drawers ({cols}x{rows}, {tier})");
                }, isCheat: true);

            new Terminal.ConsoleCommand("rid_stats",
                "rid_stats - report drawer counts and rendering cost",
                args =>
                {
                    int loaded = DrawerComponent.All.Count;
                    int assigned = 0, total = 0;
                    foreach (var d in DrawerComponent.All)
                    {
                        var s = d.Snapshot;
                        if (s.IsAssigned) assigned++;
                        total += s.Amount;
                    }

                    args.Context.AddString($"drawers loaded : {loaded}");
                    args.Context.AddString($"assigned       : {assigned}");
                    args.Context.AddString($"items held     : {total}");
                    args.Context.AddString($"atlas built    : {DrawerIconAtlas.IsBuilt}");
                }, isCheat: true);
        }
    }
}
```

Register it in `DrawerPlugin.Awake`, after `DrawerManager.Create()`:

```csharp
            DebugCommands.Register();
```

- [ ] **Step 2: Take a baseline**

In game, enable the developer console (`devcommands`), find a quiet flat area, and record the frame time with nothing built. Use Valheim's own frame counter or an external overlay; record the exact method used so the two measurements are comparable.

Write the number into `ItemDrawers/docs/performance.md` under a "Baseline" heading, along with the machine, resolution and graphics settings.

- [ ] **Step 3: Measure the wall**

```
rid_wall 10 10
rid_stats
```

Record, each as its own line in `performance.md`:

- Frame time with the wall filling the screen.
- Frame time with the wall behind you (culling working).
- Draw calls with the wall visible, from a frame debugger or overlay if available; if not, note that it was not measurable and say so rather than guessing.
- Frame time with the wall visible while OttoFuel and NoVikingLeftBehind are both active and scanning — stand at a workbench beside a smelter so both are doing real work.

- [ ] **Step 4: Judge against the criterion**

The Global Constraint is: frame cost within noise of the baseline, and single-digit draw calls.

If it passes, write "PASS" with the numbers in `performance.md` and move on.

**If it fails, do not adjust the target.** Diagnose in this order, since each is a known way the design can be undone:
1. Are labels sharing one material? Two drawers with different items must not have different materials — check `DrawerIconAtlas.SharedMaterial` is what every `DrawerRenderer` uses.
2. Is `SyncSomeFaces` doing real work every frame? `Show()` must return immediately when nothing changed.
3. Is `RunAutoPickup` running with an empty floor? It should find nothing and cost nothing.
4. Is anything calling `GetInventory` per frame? The `RefreshMirror` guard must make repeat calls free.

- [ ] **Step 5: Commit**

```bash
git add ItemDrawers/src ItemDrawers/docs/performance.md
git commit -m "Add wall-building debug command and record measurements

A hundred drawers in a wall is the normal way this mod gets used and
the reason it was rewritten, so the cost is measured and written down
rather than asserted."
```

---

## Task 15: Multiplayer verification

No new code unless something fails. This task exists because the spec's hardest requirement cannot be checked single-player, and because makail's changelog shows the original shipped these bugs twice.

**Files:**
- Create: `ItemDrawers/docs/multiplayer-verification.md`
- Modify: whatever the testing exposes.

- [ ] **Step 1: Stand up a dedicated server**

Use the Valheim dedicated server tool with the same BepInEx profile — the mod DLL, Jotunn, OttoFuel and NoVikingLeftBehind must all be present server-side. Connect two clients.

- [ ] **Step 2: Work through the scenarios**

Record pass or fail for each in `multiplayer-verification.md`. **Every one is a conservation check: count the items before and after and confirm the totals match.**

1. Both players withdraw a stack from the same full drawer at the same moment. Total received plus remaining equals the starting amount.
2. One player deposits while the other withdraws, repeatedly, for thirty seconds. Totals conserved.
3. Player A empties a drawer to zero; Player B, looking at the same drawer, sees the count reach zero without a manual refresh.
4. Player A clears a drawer's item type; Player B's view updates.
5. OttoFuel on client A drains a coal drawer while player B withdraws from it. Totals conserved.
6. Player B builds using NVLB's craft-from-containers against a drawer that player A is simultaneously filling. Totals conserved.
7. Restart the server. Every drawer's item and count is exactly as left.
8. Player A walks far enough away that the drawer unloads, then returns. State intact.
9. Two players stand at opposite ends of a 100-drawer wall and both use `rid_stats`. Both report the same totals.

- [ ] **Step 3: Fix anything that fails**

A conservation failure means the ownership rule is being violated somewhere — some path is writing without `ClaimOwnership`, or reading a stale `Snapshot` and writing back a computed value. Find the write, not the symptom.

- [ ] **Step 4: Commit**

```bash
git add ItemDrawers/docs/multiplayer-verification.md ItemDrawers/src
git commit -m "Verify drawer state under concurrent multiplayer access

Nine scenarios, each a conservation check. The original ItemDrawers
shipped sync bugs twice by its own changelog, so this is the failure
mode the ownership rule was designed against."
```

---

## Task 16: Thunderstore packaging

**Files:**
- Create: `ItemDrawers/thunderstore/manifest.json`
- Create: `ItemDrawers/thunderstore/icon.png` (256×256)
- Create: `ItemDrawers/thunderstore/README.md`
- Create: `ItemDrawers/thunderstore/CHANGELOG.md`
- Create: `LICENSE` (repository root)
- Modify: `README.md`

- [ ] **Step 1: Choose and add a licence**

The geometry is generated from arithmetic and the code is ours, so nothing is encumbered. MIT is the convention in the Valheim modding community and is what makes a fork possible if this mod is ever abandoned — which is precisely the situation that created this project.

Create `LICENSE` at the repository root with the standard MIT text, copyright `2026 Ross West`.

- [ ] **Step 2: Write the manifest**

`ItemDrawers/thunderstore/manifest.json`:

```json
{
  "name": "ItemDrawers",
  "version_number": "1.0.0",
  "website_url": "https://github.com/ross-game-tools/RossValheimMods",
  "description": "Drawers that each hold a lot of one item and show it on the front. Built for walls of a hundred.",
  "dependencies": [
    "denikson-BepInExPack_Valheim-5.4.2350",
    "ValheimModding-Jotunn-2.30.0"
  ]
}
```

The Thunderstore `name` must be unique within your author namespace, not globally, so `ItemDrawers` is available even though makail's exists. `description` is capped at 250 characters.

- [ ] **Step 3: Write the package README**

`ItemDrawers/thunderstore/README.md` — reuse the content of `ItemDrawers/README.md`, and add:

- The control table from the Global Constraints.
- The three tiers with capacities and recipes.
- A compatibility section naming OttoFuel and NoVikingLeftBehind as tested, and stating that any container-aware mod should work because drawers present as containers.
- A note that this is a clean-room rewrite, not a fork of makail's or KG's mod, and that **drawers from those mods are not converted** — worlds using them keep them as-is and will show unknown pieces if the old mod is removed.
- The measured numbers from `docs/performance.md`. Users choosing between drawer mods care about exactly this.

- [ ] **Step 4: Make the icon**

256×256 PNG. Render the drawer, or draw a simple front-facing drawer with an item icon on it. It must be exactly 256×256 or Thunderstore rejects the upload.

- [ ] **Step 5: Write the changelog**

`ItemDrawers/thunderstore/CHANGELOG.md`:

```markdown
# Changelog

## 1.0.0

First release. A clean-room rewrite for Valheim 1.0 — not a fork of any
earlier drawer mod.

- Three tiers: wood (1,000), stone (2,000) and black marble (10,000).
- The original control scheme: hotbar to assign, interact to take a
  stack, alt to take one, alt at zero to clear, shift to store all.
- Drawers absorb matching items dropped nearby.
- Readable and drainable by container-aware mods, tested against
  OttoFuel and NoVikingLeftBehind.
- Built for walls: no per-drawer Canvas and no per-drawer tick, so a
  hundred of them cost what a handful of draw calls cost.
```

- [ ] **Step 6: Build the package**

```bash
cd ItemDrawers
dotnet build -c Release
mkdir -p thunderstore/build/plugins
cp src/ItemDrawers.Game/bin/Release/netstandard2.1/ItemDrawers.dll thunderstore/build/plugins/
cp thunderstore/manifest.json thunderstore/icon.png thunderstore/README.md thunderstore/CHANGELOG.md thunderstore/build/
cd thunderstore/build && zip -r ../ItemDrawers-1.0.0.zip .
```

- [ ] **Step 7: Verify the package installs clean**

Create a **fresh** r2modman profile with only BepInEx and Jotunn. Install the zip locally. Launch. Build one of each drawer tier and store something.

This catches the commonest packaging bug: the mod working only because something else in the development profile was providing a dependency.

- [ ] **Step 8: Update the repository README**

Change the ItemDrawers row's state from "Design approved, not yet implemented" to released, and link the Thunderstore page once it exists.

- [ ] **Step 9: Commit**

```bash
git add LICENSE README.md ItemDrawers/thunderstore
git commit -m "Package for Thunderstore under MIT

MIT specifically because this project exists as a consequence of three
abandoned drawer mods, two of which shipped without a licence and so
cannot legally be revived. Verified installing into a clean profile
with only BepInEx and Jotunn."
```

---

## Self-Review

**Spec coverage.** Every section maps to a task: §1 risk → Task 1. §2 requirements → Tasks 8 (controls), 11 (auto-pickup), 7/13 (tiers and capacities), 12 (OttoFuel and NVLB), 15 (dedicated server), 14 (wall performance), 16 (Thunderstore). §3 module shape → the File Structure section. §4 data model → Task 8. §5 geometry → Tasks 2 and 4. §6 rendering → Tasks 9 and 10. §7 interaction → Task 8. §8 Container bridge → Task 12. §9 ownership → Tasks 8 and 15. §10 auto-pickup → Task 11. §11 tiers and config → Tasks 7 and 13. §12 testing → Tasks 3–6 for units, 14 and 15 for the game. §13 toolchain → Task 1. §14 risks → each is verified in the task that depends on it: soft references in Task 1, `Container.Awake` in Task 8, the `GetInventory` patch in Task 12, oversized stacks in Task 12, `ItemDrop` enumeration in Task 11 Step 1.

**Two spec items were deliberately not given tasks.** §2's "Won't" list — per-drawer colour and migration from old drawers — is out of scope by decision, and §15 records no blocking open questions.

**Type consistency.** `DrawerConfig`'s entry names are referenced in Tasks 8, 10 and 11 before Task 13 defines them; the Interfaces block in Task 13 lists them so an executor working out of order has the names. `DrawerComponent.Face`, `RefreshFace` and `Commit` are introduced in Task 8 and used in Tasks 9, 10 and 12. `DrawerTiers.PrefabName` is used in Tasks 8, 14 and 16. `MeshData.Builder` is internal to the core and used only by `DrawerMeshBuilder`.

**Known unverifiable content.** Task 1 (assembly names, Jotunn package reference), Task 7 (`blackmarble_post01` donor), Task 11 (dropped-item enumeration) and Task 13 (Jotunn's admin-only config attribute type) contain Valheim and Jotunn API details that could not be checked without a build. Each carries an explicit verification step rather than being presented as settled. This is the plan's main weakness and the reason Task 1 comes first.
