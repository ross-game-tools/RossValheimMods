# RossPortalTames Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tames that are following you come through the portal with you.

**Architecture:** Two projects. `RossPortalTames.Core` is pure C# with no engine
references and holds the two rule sets worth testing — which tames qualify, and
where they land. `RossPortalTames.Game` is a BepInEx plugin: one Harmony patch on
`TeleportWorld.Teleport` captures eligible tames at departure, and one
MonoBehaviour re-asserts ZDO ownership and writes each new position after the
player arrives. Nothing is ever despawned or recreated.

**Tech Stack:** C#, netstandard2.1, BepInEx 5.4.2350, HarmonyX, xunit (net8.0 test
project). No Jotunn.

**Spec:** `RossPortalTames/docs/superpowers/specs/2026-09-13-portal-tames-design.md`

## Global Constraints

- Target framework `netstandard2.1` for both `src` projects; `net8.0` for tests.
- `RossPortalTames.Core` MUST NOT reference UnityEngine, Valheim assemblies, or
  BepInEx. An architecture test enforces this (Task 1).
- No Jotunn dependency anywhere. BepInEx only.
- Valheim assemblies are referenced from `$(VALHEIM_INSTALL)` and never committed.
- Plugin GUID is `com.rossdwest.portaltames`. Written into BepInEx's config
  filename, therefore permanent.
- The mod never destroys or recreates a creature. The only write is
  `ZDO.SetPosition` on an existing ZDO.
- Every Harmony patch class has a `Prepare()` that logs what it skipped rather
  than returning a bare `false`.
- Leave no build warnings.
- Config defaults: `Enabled` true, `FollowRadius` 20f, `SearchDistance` 6f.

---

## File Structure

```
RossPortalTames/
  Directory.Build.props                  VALHEIM_INSTALL default + MSB3277 downgrade
  Directory.Build.local.props.sample     machine-specific override template
  NuGet.config                           copied from ItemDrawers
  RossPortalTames.sln
  deploy.sh                              build + copy to an r2modman profile
  package.sh                             build + zip for Thunderstore
  README.md                              mod-level readme
  docs/superpowers/specs/2026-09-13-portal-tames-design.md   (exists)
  src/
    RossPortalTames.Core/
      RossPortalTames.Core.csproj
      Vec3.cs                 engine-free 3D vector
      TameCandidate.cs        one creature's decision inputs
      TameEligibility.cs      which candidates qualify
      ArrivalPlacement.cs     where each one lands
    RossPortalTames.Game/
      RossPortalTames.Game.csproj
      PortalTamesPlugin.cs    BepInPlugin entry, Harmony setup
      PortalTamesConfig.cs    three config entries
      ValheimCompat.cs        startup check + Prepare helper
      PortalPatch.cs          TeleportWorld.Teleport postfix
      PendingArrival.cs       captured ZDOIDs + capture time
      PortalTamesManager.cs   the only Update; arrival detection
      TameMover.cs            re-assert ownership, write position
  tests/
    RossPortalTames.Core.Tests/
      RossPortalTames.Core.Tests.csproj
      ArchitectureTests.cs
      TameEligibilityTests.cs
      ArrivalPlacementTests.cs
  thunderstore/
    manifest.json
    README.md
    CHANGELOG.md
```

---

### Task 1: Solution scaffolding and the Core/engine boundary

**Files:**
- Create: `RossPortalTames/Directory.Build.props`
- Create: `RossPortalTames/Directory.Build.local.props.sample`
- Create: `RossPortalTames/NuGet.config`
- Create: `RossPortalTames/RossPortalTames.sln`
- Create: `RossPortalTames/src/RossPortalTames.Core/RossPortalTames.Core.csproj`
- Create: `RossPortalTames/src/RossPortalTames.Core/Vec3.cs`
- Create: `RossPortalTames/tests/RossPortalTames.Core.Tests/RossPortalTames.Core.Tests.csproj`
- Test: `RossPortalTames/tests/RossPortalTames.Core.Tests/ArchitectureTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `RossPortalTames.Core.Vec3` — `readonly struct` with
  `float X, Y, Z`, constructor `Vec3(float x, float y, float z)`, and
  `static float DistanceSquared(Vec3 a, Vec3 b)`,
  `static Vec3 operator +(Vec3, Vec3)`, `static Vec3 operator *(Vec3, float)`,
  `Vec3 WithY(float y)`, `static readonly Vec3 Zero`.

- [ ] **Step 1: Create the Core project file**

`RossPortalTames/src/RossPortalTames.Core/RossPortalTames.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <RootNamespace>RossPortalTames.Core</RootNamespace>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Write Vec3**

`RossPortalTames/src/RossPortalTames.Core/Vec3.cs`:

```csharp
using System;

namespace RossPortalTames.Core
{
    /// <summary>
    /// A 3D point, engine-free.
    ///
    /// Core exists to hold the rules that are worth testing without a running
    /// game, which means it cannot reference UnityEngine and therefore cannot
    /// use Vector3. This is the whole of the geometry Core needs; the Game
    /// layer converts at its own boundary.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vec3 WithY(float y) => new Vec3(X, y, Z);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        /// <summary>
        /// Squared distance, because every caller here compares against a
        /// radius and a square root would be thrown away.
        /// </summary>
        public static float DistanceSquared(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }
}
```

- [ ] **Step 3: Create the test project file**

`RossPortalTames/tests/RossPortalTames.Core.Tests/RossPortalTames.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>RossPortalTames.Core.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RossPortalTames.Core\RossPortalTames.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Write the failing architecture test**

`RossPortalTames/tests/RossPortalTames.Core.Tests/ArchitectureTests.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace RossPortalTames.Core.Tests
{
    public class ArchitectureTests
    {
        // Core's value is that these rules can be tested without a running
        // game. A single stray `using UnityEngine;` destroys that quietly --
        // the project still compiles on a developer machine that has the
        // assemblies, and only fails for whoever does not. This test is the
        // thing that keeps the boundary real rather than aspirational.
        [Theory]
        [InlineData("UnityEngine")]
        [InlineData("assembly_valheim")]
        [InlineData("assembly_utils")]
        [InlineData("BepInEx")]
        [InlineData("0Harmony")]
        [InlineData("Jotunn")]
        public void Core_does_not_reference(string forbidden)
        {
            var core = typeof(Vec3).Assembly;
            var referenced = core.GetReferencedAssemblies().Select(a => a.Name).ToArray();

            Assert.DoesNotContain(referenced, name =>
                name.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }
}
```

- [ ] **Step 5: Create the solution and add all three projects**

```bash
cd RossPortalTames
dotnet new sln -n RossPortalTames
dotnet sln add src/RossPortalTames.Core/RossPortalTames.Core.csproj
dotnet sln add tests/RossPortalTames.Core.Tests/RossPortalTames.Core.Tests.csproj
```

(The Game project is added in Task 4.)

- [ ] **Step 6: Copy the build configuration from ItemDrawers**

Copy `ItemDrawers/NuGet.config` to `RossPortalTames/NuGet.config` unchanged.

Copy `ItemDrawers/Directory.Build.local.props.sample` to
`RossPortalTames/Directory.Build.local.props.sample`, then edit it to drop any
`JOTUNN_INSTALL` line — this mod has no Jotunn dependency.

Create `RossPortalTames/Directory.Build.props`:

```xml
<Project>
  <!--
    Local, machine-specific overrides. Copy Directory.Build.local.props.sample to
    Directory.Build.local.props (gitignored) and edit it, or set VALHEIM_INSTALL
    as an environment variable instead — either works.
  -->
  <Import Project="Directory.Build.local.props" Condition="Exists('Directory.Build.local.props')" />

  <PropertyGroup>
    <VALHEIM_INSTALL Condition="'$(VALHEIM_INSTALL)' == ''">C:\Program Files (x86)\Steam\steamapps\common\Valheim</VALHEIM_INSTALL>
  </PropertyGroup>

  <PropertyGroup>
    <!--
      MSB3277 fires on every build of the Game project and none of it is
      actionable. Valheim ships its own copies of System.* assemblies at
      versions differing from the netstandard2.1 reference assemblies; MSBuild
      reports the conflict and resolves it correctly, and at runtime neither
      copy is ours. Downgraded to a message rather than suppressed, so it still
      shows in a detailed log. MSBuild warnings do not respond to NoWarn, which
      is C#-compiler only; MSBuildWarningsAsMessages is the equivalent.
    -->
    <MSBuildWarningsAsMessages>$(MSBuildWarningsAsMessages);MSB3277</MSBuildWarningsAsMessages>
  </PropertyGroup>
</Project>
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test RossPortalTames/tests/RossPortalTames.Core.Tests -c Release --nologo`
Expected: PASS, 6 tests (one per `InlineData`).

- [ ] **Step 8: Commit**

```bash
git add RossPortalTames/
git commit -m "feat: RossPortalTames scaffolding and the Core engine-free boundary"
```

---

### Task 2: Tame eligibility

**Files:**
- Create: `RossPortalTames/src/RossPortalTames.Core/TameCandidate.cs`
- Create: `RossPortalTames/src/RossPortalTames.Core/TameEligibility.cs`
- Test: `RossPortalTames/tests/RossPortalTames.Core.Tests/TameEligibilityTests.cs`

**Interfaces:**
- Consumes: `Vec3` from Task 1.
- Produces:
  - `readonly struct TameCandidate` with constructor
    `TameCandidate(Vec3 position, bool isTamed, bool isFollowingPlayer, bool isBusy)`
    and matching properties `Position`, `IsTamed`, `IsFollowingPlayer`, `IsBusy`.
  - `static class TameEligibility` with
    `static bool Qualifies(TameCandidate candidate, Vec3 playerPosition, float radius)`
    and
    `static List<int> SelectIndices(IReadOnlyList<TameCandidate> candidates, Vec3 playerPosition, float radius)`.

- [ ] **Step 1: Write the failing tests**

`RossPortalTames/tests/RossPortalTames.Core.Tests/TameEligibilityTests.cs`:

```csharp
using System.Collections.Generic;
using Xunit;

namespace RossPortalTames.Core.Tests
{
    public class TameEligibilityTests
    {
        private static readonly Vec3 Player = Vec3.Zero;
        private const float Radius = 20f;

        private static TameCandidate Eligible(float distance) =>
            new TameCandidate(new Vec3(distance, 0f, 0f), isTamed: true, isFollowingPlayer: true, isBusy: false);

        [Fact]
        public void A_tamed_follower_in_range_qualifies()
        {
            Assert.True(TameEligibility.Qualifies(Eligible(5f), Player, Radius));
        }

        [Fact]
        public void A_wild_creature_never_qualifies()
        {
            var wild = new TameCandidate(new Vec3(5f, 0f, 0f), isTamed: false, isFollowingPlayer: true, isBusy: false);
            Assert.False(TameEligibility.Qualifies(wild, Player, Radius));
        }

        [Fact]
        public void A_tame_that_is_not_following_me_never_qualifies()
        {
            // Covers both "following nobody" and "following another player":
            // the Game layer resolves the follow target and passes the answer
            // as a bool, so there is exactly one case here.
            var idle = new TameCandidate(new Vec3(5f, 0f, 0f), isTamed: true, isFollowingPlayer: false, isBusy: false);
            Assert.False(TameEligibility.Qualifies(idle, Player, Radius));
        }

        [Fact]
        public void A_busy_tame_never_qualifies()
        {
            // isBusy is ridden or otherwise attached. A half-attached creature
            // is exactly the state worth not moving.
            var ridden = new TameCandidate(new Vec3(5f, 0f, 0f), isTamed: true, isFollowingPlayer: true, isBusy: true);
            Assert.False(TameEligibility.Qualifies(ridden, Player, Radius));
        }

        [Fact]
        public void A_tame_beyond_the_radius_does_not_qualify()
        {
            Assert.False(TameEligibility.Qualifies(Eligible(Radius + 1f), Player, Radius));
        }

        [Fact]
        public void The_radius_is_inclusive_at_its_exact_edge()
        {
            // Pinned deliberately: a tame standing exactly at the configured
            // distance should come, so that setting the radius to 20 means
            // "within 20 metres" rather than "within 19.99".
            Assert.True(TameEligibility.Qualifies(Eligible(Radius), Player, Radius));
        }

        [Fact]
        public void Distance_is_measured_in_three_dimensions()
        {
            // A tame directly above or below -- on a roof, or under a floor --
            // is as far away as one across the ground, and a 2D test would
            // wrongly include something 30m up a cliff.
            var high = new TameCandidate(new Vec3(0f, Radius + 1f, 0f), isTamed: true, isFollowingPlayer: true, isBusy: false);
            Assert.False(TameEligibility.Qualifies(high, Player, Radius));
        }

        [Fact]
        public void SelectIndices_returns_the_positions_of_qualifying_candidates()
        {
            var candidates = new List<TameCandidate>
            {
                Eligible(1f),                                                                           // 0 yes
                new TameCandidate(new Vec3(2f, 0f, 0f), true, false, false),                            // 1 no
                Eligible(3f),                                                                           // 2 yes
                new TameCandidate(new Vec3(100f, 0f, 0f), true, true, false),                           // 3 no
            };

            Assert.Equal(new[] { 0, 2 }, TameEligibility.SelectIndices(candidates, Player, Radius));
        }

        [Fact]
        public void SelectIndices_returns_empty_rather_than_null_when_nothing_qualifies()
        {
            var candidates = new List<TameCandidate> { new TameCandidate(Vec3.Zero, false, false, false) };
            Assert.Empty(TameEligibility.SelectIndices(candidates, Player, Radius));
        }

        [Fact]
        public void A_zero_or_negative_radius_selects_nothing()
        {
            // Guards a config edit of 0 or -1 from being read as "no limit".
            Assert.False(TameEligibility.Qualifies(Eligible(0f), Player, 0f));
            Assert.False(TameEligibility.Qualifies(Eligible(0f), Player, -5f));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RossPortalTames/tests/RossPortalTames.Core.Tests -c Release --nologo`
Expected: FAIL — `TameCandidate` and `TameEligibility` do not exist.

- [ ] **Step 3: Write TameCandidate**

`RossPortalTames/src/RossPortalTames.Core/TameCandidate.cs`:

```csharp
namespace RossPortalTames.Core
{
    /// <summary>
    /// Everything Core needs to decide whether one creature comes along.
    ///
    /// Deliberately flags rather than engine objects: the Game layer has
    /// already asked Valheim whether the creature is tamed, who it is
    /// following, and whether it is ridden, so those questions are answered
    /// once, at the boundary, instead of being re-asked inside rules that
    /// then could not be tested.
    /// </summary>
    public readonly struct TameCandidate
    {
        public Vec3 Position { get; }
        public bool IsTamed { get; }

        /// <summary>Following THIS player specifically, not merely following something.</summary>
        public bool IsFollowingPlayer { get; }

        /// <summary>Ridden, saddled-and-mounted, or otherwise attached.</summary>
        public bool IsBusy { get; }

        public TameCandidate(Vec3 position, bool isTamed, bool isFollowingPlayer, bool isBusy)
        {
            Position = position;
            IsTamed = isTamed;
            IsFollowingPlayer = isFollowingPlayer;
            IsBusy = isBusy;
        }
    }
}
```

- [ ] **Step 4: Write TameEligibility**

`RossPortalTames/src/RossPortalTames.Core/TameEligibility.cs`:

```csharp
using System.Collections.Generic;

namespace RossPortalTames.Core
{
    /// <summary>
    /// Which following tames come through the portal.
    /// </summary>
    public static class TameEligibility
    {
        public static bool Qualifies(TameCandidate candidate, Vec3 playerPosition, float radius)
        {
            // A non-positive radius means nothing comes, rather than
            // everything. A config edit to 0 reads naturally as "off", and
            // reading it as "unlimited" would teleport a player's entire
            // tamed population across the world.
            if (radius <= 0f) return false;

            if (!candidate.IsTamed) return false;
            if (!candidate.IsFollowingPlayer) return false;
            if (candidate.IsBusy) return false;

            // Inclusive at the edge, and in three dimensions: a tame 30m
            // straight up a cliff is not nearby, however close it looks on a
            // map.
            return Vec3.DistanceSquared(candidate.Position, playerPosition) <= radius * radius;
        }

        /// <summary>
        /// Indices rather than the candidates themselves, so Core never has to
        /// know about ZDOIDs or GameObjects: the caller keeps its own parallel
        /// list and maps the answers back.
        /// </summary>
        public static List<int> SelectIndices(
            IReadOnlyList<TameCandidate> candidates, Vec3 playerPosition, float radius)
        {
            var selected = new List<int>();
            if (candidates == null) return selected;

            for (int i = 0; i < candidates.Count; i++)
                if (Qualifies(candidates[i], playerPosition, radius))
                    selected.Add(i);

            return selected;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test RossPortalTames/tests/RossPortalTames.Core.Tests -c Release --nologo`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add RossPortalTames/src/RossPortalTames.Core RossPortalTames/tests
git commit -m "feat: tame eligibility rules"
```

---

### Task 3: Arrival placement

**Files:**
- Create: `RossPortalTames/src/RossPortalTames.Core/ArrivalPlacement.cs`
- Test: `RossPortalTames/tests/RossPortalTames.Core.Tests/ArrivalPlacementTests.cs`

**Interfaces:**
- Consumes: `Vec3` from Task 1.
- Produces: `static class ArrivalPlacement` with
  `static Vec3[] Compute(Vec3 arrival, Vec3 facing, int count, float searchDistance, Func<Vec3, bool> isFree)`.
  Returns exactly `count` positions, in order. A position equal to `arrival`
  means "nowhere better was found".

- [ ] **Step 1: Write the failing tests**

`RossPortalTames/tests/RossPortalTames.Core.Tests/ArrivalPlacementTests.cs`:

```csharp
using System;
using System.Linq;
using Xunit;

namespace RossPortalTames.Core.Tests
{
    public class ArrivalPlacementTests
    {
        private static readonly Vec3 Arrival = Vec3.Zero;
        private static readonly Vec3 North = new Vec3(0f, 0f, 1f);
        private const float Search = 6f;

        private static bool OpenWorld(Vec3 p) => true;

        [Fact]
        public void Returns_one_position_per_tame()
        {
            var placed = ArrivalPlacement.Compute(Arrival, North, 5, Search, OpenWorld);
            Assert.Equal(5, placed.Length);
        }

        [Fact]
        public void Returns_empty_for_no_tames()
        {
            Assert.Empty(ArrivalPlacement.Compute(Arrival, North, 0, Search, OpenWorld));
        }

        [Fact]
        public void Placements_are_within_the_search_distance()
        {
            var placed = ArrivalPlacement.Compute(Arrival, North, 8, Search, OpenWorld);
            foreach (var p in placed)
                Assert.True(Vec3.DistanceSquared(p, Arrival) <= Search * Search + 0.01f,
                    $"{p} is beyond the {Search}m search distance");
        }

        [Fact]
        public void Placements_do_not_collide_in_an_open_world()
        {
            var placed = ArrivalPlacement.Compute(Arrival, North, 8, Search, OpenWorld);
            Assert.Equal(placed.Length, placed.Distinct().Count());
        }

        [Fact]
        public void A_wall_pushes_every_placement_to_the_free_side()
        {
            // THE case this exists for. Portals are commonly built against a
            // wall, and a ring placement -- the obvious first design -- puts
            // half the pack inside the geometry. Here everything with X > 0 is
            // solid rock.
            bool IsFree(Vec3 p) => p.X <= 0f;

            var placed = ArrivalPlacement.Compute(Arrival, North, 6, Search, IsFree);

            foreach (var p in placed)
                Assert.True(IsFree(p) || p.Equals(Arrival),
                    $"{p} was placed inside the wall");
        }

        [Fact]
        public void Falls_back_to_the_arrival_point_when_the_world_is_entirely_blocked()
        {
            // The player's own arrival position is by definition somewhere a
            // body can stand, so it is the one safe answer when the search
            // finds nothing. Overlapping creatures separate themselves within
            // a second; a creature inside a wall does not.
            var placed = ArrivalPlacement.Compute(Arrival, North, 3, Search, _ => false);

            Assert.Equal(3, placed.Length);
            Assert.All(placed, p => Assert.Equal(Arrival, p));
        }

        [Fact]
        public void Prefers_the_direction_the_player_faces()
        {
            // Valheim puts the player in front of the destination portal, so
            // "where the player faces" is "away from the portal" -- the side
            // with room.
            var placed = ArrivalPlacement.Compute(Arrival, North, 1, Search, OpenWorld);
            Assert.True(placed[0].Z > 0f, $"expected a placement north of the player, got {placed[0]}");
        }

        [Fact]
        public void Is_deterministic()
        {
            // Two clients computing the same arrival must not disagree, and a
            // flaky test here would be untraceable.
            var a = ArrivalPlacement.Compute(Arrival, North, 5, Search, OpenWorld);
            var b = ArrivalPlacement.Compute(Arrival, North, 5, Search, OpenWorld);
            Assert.Equal(a, b);
        }

        [Fact]
        public void A_zero_facing_does_not_produce_invalid_positions()
        {
            // Guards against a player whose forward vector is degenerate;
            // NaN here would write a corrupt position into a ZDO.
            var placed = ArrivalPlacement.Compute(Arrival, Vec3.Zero, 4, Search, OpenWorld);

            Assert.Equal(4, placed.Length);
            foreach (var p in placed)
            {
                Assert.False(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z), $"{p} contains NaN");
                Assert.False(float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z), $"{p} is infinite");
            }
        }

        [Fact]
        public void Null_predicate_is_treated_as_an_open_world()
        {
            var placed = ArrivalPlacement.Compute(Arrival, North, 2, Search, null);
            Assert.Equal(2, placed.Length);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RossPortalTames/tests/RossPortalTames.Core.Tests -c Release --nologo`
Expected: FAIL — `ArrivalPlacement` does not exist.

- [ ] **Step 3: Write ArrivalPlacement**

`RossPortalTames/src/RossPortalTames.Core/ArrivalPlacement.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace RossPortalTames.Core
{
    /// <summary>
    /// Where each arriving tame is put down.
    ///
    /// A ring around the player was the obvious design and is wrong: portals
    /// are commonly built against a wall inside a small hut, and a ring puts
    /// half the pack inside the geometry. This searches instead, and validates
    /// every candidate against the world through a predicate the caller
    /// supplies -- which is also what makes the wall case directly testable.
    /// </summary>
    public static class ArrivalPlacement
    {
        // Distance between successive rings, and the angles tried within each.
        // The angle order fans outward from straight ahead so the first free
        // spot found is the one most nearly in front of the player.
        private const float RingStep = 1.5f;
        private static readonly float[] AngleOffsetsDegrees =
            { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f, 130f, -130f, 160f, -160f, 180f };

        public static Vec3[] Compute(
            Vec3 arrival, Vec3 facing, int count, float searchDistance, Func<Vec3, bool> isFree)
        {
            if (count <= 0) return Array.Empty<Vec3>();

            // A null predicate means the caller has no way to ask the world;
            // treat that as open rather than as blocked, so a missing
            // integration degrades to "they land near you" instead of "they
            // all pile on your head".
            if (isFree == null) isFree = _ => true;

            var (fx, fz) = Normalise(facing);
            var placed = new Vec3[count];
            var taken = new List<Vec3>(count);

            for (int i = 0; i < count; i++)
            {
                placed[i] = FindSpot(arrival, fx, fz, searchDistance, isFree, taken);
                taken.Add(placed[i]);
            }

            return placed;
        }

        private static Vec3 FindSpot(
            Vec3 arrival, float fx, float fz, float searchDistance,
            Func<Vec3, bool> isFree, List<Vec3> taken)
        {
            for (float radius = RingStep; radius <= searchDistance + 0.001f; radius += RingStep)
            {
                foreach (float degrees in AngleOffsetsDegrees)
                {
                    var candidate = Rotate(arrival, fx, fz, radius, degrees);

                    if (!isFree(candidate)) continue;
                    if (IsTaken(taken, candidate)) continue;

                    return candidate;
                }
            }

            // Nothing free within range. The player's own arrival position is
            // the one point guaranteed to be somewhere a body can stand.
            return arrival;
        }

        private static bool IsTaken(List<Vec3> taken, Vec3 candidate)
        {
            const float MinSeparation = 1.0f;
            for (int i = 0; i < taken.Count; i++)
                if (Vec3.DistanceSquared(taken[i], candidate) < MinSeparation * MinSeparation)
                    return true;
            return false;
        }

        private static Vec3 Rotate(Vec3 origin, float fx, float fz, float radius, float degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            double cos = Math.Cos(radians), sin = Math.Sin(radians);

            float dx = (float)(fx * cos - fz * sin);
            float dz = (float)(fx * sin + fz * cos);

            // Y is left at the arrival height. The Game layer snaps it to the
            // ground, which is knowledge Core does not have.
            return new Vec3(origin.X + dx * radius, origin.Y, origin.Z + dz * radius);
        }

        /// <summary>
        /// Horizontal facing as a unit vector. A degenerate facing (straight
        /// up, or an uninitialised zero) would divide by zero and write NaN
        /// into a ZDO position, so it falls back to a fixed direction.
        /// </summary>
        private static (float X, float Z) Normalise(Vec3 facing)
        {
            float length = (float)Math.Sqrt(facing.X * facing.X + facing.Z * facing.Z);
            if (length < 0.0001f) return (0f, 1f);
            return (facing.X / length, facing.Z / length);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RossPortalTames/tests/RossPortalTames.Core.Tests -c Release --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add RossPortalTames/src/RossPortalTames.Core RossPortalTames/tests
git commit -m "feat: arrival placement with a validated candidate search"
```

---

### Task 4: Game project, plugin entry, config, and the compatibility check

**Files:**
- Create: `RossPortalTames/src/RossPortalTames.Game/RossPortalTames.Game.csproj`
- Create: `RossPortalTames/src/RossPortalTames.Game/PortalTamesPlugin.cs`
- Create: `RossPortalTames/src/RossPortalTames.Game/PortalTamesConfig.cs`
- Create: `RossPortalTames/src/RossPortalTames.Game/ValheimCompat.cs`
- Modify: `RossPortalTames/RossPortalTames.sln` (add the Game project)

**Interfaces:**
- Consumes: nothing from Core yet.
- Produces:
  - `PortalTamesPlugin.Log` — `static ManualLogSource`.
  - `PortalTamesPlugin.PluginGuid` = `"com.rossdwest.portaltames"`.
  - `PortalTamesConfig.Bind(ConfigFile)`, and entries
    `Enabled` (`ConfigEntry<bool>`), `FollowRadius` (`ConfigEntry<float>`),
    `SearchDistance` (`ConfigEntry<float>`).
  - `ValheimCompat.Verify()` and
    `ValheimCompat.RequireMethod(Type type, string method)` returning `bool`.

- [ ] **Step 1: Create the Game project file**

`RossPortalTames/src/RossPortalTames.Game/RossPortalTames.Game.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>RossPortalTames</AssemblyName>
    <RootNamespace>RossPortalTames.Game</RootNamespace>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\RossPortalTames.Core\RossPortalTames.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="BepInEx.Analyzers" Version="1.*" PrivateAssets="all" />
    <PackageReference Include="BepInEx.Core" Version="5.*" PrivateAssets="all" />
    <PackageReference Include="BepInEx.AssemblyPublicizer.MSBuild" Version="0.4.*" PrivateAssets="all" />
  </ItemGroup>

  <!--
    UnityEngine.Modules on nuget.org stops at 2021.3.33 and Valheim 1.0 runs
    Unity 6000.0.75f1, so the module DLLs shipped next to the game are
    referenced directly. They are never committed.
  -->
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
    <Reference Include="UnityEngine">
      <HintPath>$(VALHEIM_INSTALL)\valheim_Data\Managed\UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(VALHEIM_INSTALL)\valheim_Data\Managed\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.PhysicsModule">
      <HintPath>$(VALHEIM_INSTALL)\valheim_Data\Managed\UnityEngine.PhysicsModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the Game project to the solution**

```bash
cd RossPortalTames
dotnet sln add src/RossPortalTames.Game/RossPortalTames.Game.csproj
```

- [ ] **Step 3: Write the config**

`RossPortalTames/src/RossPortalTames.Game/PortalTamesConfig.cs`:

```csharp
using BepInEx.Configuration;

namespace RossPortalTames.Game
{
    /// <summary>
    /// Three knobs, all local to this client.
    ///
    /// Not server-synced, and that is a consequence of having no Jotunn
    /// dependency rather than an oversight. It is acceptable because every
    /// setting here only affects which of YOUR OWN tames follow YOU: there is
    /// nothing a client can grant itself that it could not already do by
    /// walking its wolves to the portal.
    /// </summary>
    public static class PortalTamesConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> FollowRadius;
        public static ConfigEntry<float> SearchDistance;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Tames following you come through portals with you.");

            FollowRadius = config.Bind("General", "FollowRadius", 20f,
                "How close a following tame must be, in metres, to come along. "
                + "Measured in three dimensions, so a tame on a roof is as far as one across the ground. "
                + "Set to 0 to bring nothing.");

            SearchDistance = config.Bind("General", "SearchDistance", 6f,
                "How far from your arrival point to look for a clear spot to place each tame. "
                + "Anything that cannot be placed within this distance is put at your own position "
                + "instead, where creatures separate themselves naturally. Lower it for tight portal huts.");
        }
    }
}
```

- [ ] **Step 4: Write the compatibility check**

`RossPortalTames/src/RossPortalTames.Game/ValheimCompat.cs`:

```csharp
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RossPortalTames.Game
{
    /// <summary>
    /// One startup check over the Valheim members this mod reaches for by NAME
    /// rather than by compiler-checked reference.
    ///
    /// Those fail differently. A publicized field read is bound at compile
    /// time: if Valheim removes it, the plugin fails to load with the member's
    /// name in the message, which is self-diagnosing. A Harmony patch whose
    /// target method has been renamed fails QUIETLY -- the patch is skipped and
    /// the mod simply stops working, with nothing pointing at a game update as
    /// the cause. This exists so the log says which member disappeared, on the
    /// first line, before anything else goes wrong.
    /// </summary>
    internal static class ValheimCompat
    {
        private static readonly (string Type, string Member, string Why)[] Required =
        {
            ("TeleportWorld", "Teleport",
                "patched to notice when you use a portal, which is the only trigger this mod has"),
        };

        public static void Verify()
        {
            var missing = new List<string>();

            foreach (var (type, member, why) in Required)
            {
                var t = AccessTools.TypeByName(type);
                if (t == null)
                {
                    missing.Add($"{type} (whole type) -- {why}");
                    continue;
                }

                if (AccessTools.Field(t, member) == null && AccessTools.Method(t, member) == null)
                    missing.Add($"{type}.{member} -- {why}");
            }

            if (missing.Count == 0)
            {
                PortalTamesPlugin.Log.LogInfo($"Valheim compatibility check passed ({Required.Length} members).");
                return;
            }

            PortalTamesPlugin.Log.LogError(
                "Valheim compatibility check FAILED -- this game version has changed members this mod "
                + "depends on, and tames will not follow you through portals. This is almost certainly a "
                + "Valheim update, not a conflict with another mod. Missing:");
            foreach (var m in missing) PortalTamesPlugin.Log.LogError($"    {m}");
        }

        /// <summary>
        /// Harmony Prepare helper. A Prepare that just returns
        /// `AccessTools.Method(...) != null` skips its patch in total silence,
        /// which for this mod means "nothing happens and nobody knows why".
        /// </summary>
        public static bool RequireMethod(Type type, string method)
        {
            if (AccessTools.Method(type, method) != null) return true;

            PortalTamesPlugin.Log.LogError(
                $"{type.Name}.{method} not found -- skipping that patch. Tames will not follow you "
                + "through portals. See the compatibility check above.");
            return false;
        }
    }
}
```

- [ ] **Step 5: Write the plugin entry point**

`RossPortalTames/src/RossPortalTames.Game/PortalTamesPlugin.cs`:

```csharp
using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RossPortalTames.Game
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class PortalTamesPlugin : BaseUnityPlugin
    {
        // Written into BepInEx's config filename, therefore permanent:
        // changing it silently resets everyone's settings to defaults.
        public const string PluginGuid = "com.rossdwest.portaltames";
        public const string PluginName = "RossPortalTames";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);

            // Before any patching, so that if a Valheim update has moved
            // something out from under us the log leads with WHICH member
            // rather than with whatever secondary symptom surfaces first.
            ValheimCompat.Verify();

            // Each [HarmonyPatch] class is applied individually, in its own
            // try/catch. Harmony's PatchAll iterates every patch class in one
            // loop with no per-class handling: one class throwing can abort
            // that loop before later classes are reached, leaving them
            // unpatched with no error anyone would connect to the cause.
            try
            {
                foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0) continue;

                    try
                    {
                        _harmony.CreateClassProcessor(type).Patch();
                    }
                    catch (Exception ex)
                    {
                        Log.LogError($"Patch class {type.Name} failed and was skipped: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Patching aborted: {ex}");
            }

            PortalTamesConfig.Bind(Config);

            // One manager object for the whole mod, carrying the only Update.
            var host = new GameObject(PluginName + "Manager");
            host.AddComponent<PortalTamesManager>();
            DontDestroyOnLoad(host);
            host.transform.SetParent(gameObject.transform);

            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
```

- [ ] **Step 6: Build**

Run: `dotnet build RossPortalTames/RossPortalTames.sln -c Release --nologo`
Expected: FAIL — `PortalTamesManager` does not exist yet. This is the one
deliberate forward reference in the plan; Task 5 creates it. Do not stub it.

- [ ] **Step 7: Commit**

```bash
git add RossPortalTames/src/RossPortalTames.Game RossPortalTames/RossPortalTames.sln
git commit -m "feat: plugin entry, config, and compatibility check"
```

---

### Task 5: Capture eligible tames when a portal is used

**Files:**
- Create: `RossPortalTames/src/RossPortalTames.Game/PendingArrival.cs`
- Create: `RossPortalTames/src/RossPortalTames.Game/PortalPatch.cs`
- Create: `RossPortalTames/src/RossPortalTames.Game/PortalTamesManager.cs`

**Interfaces:**
- Consumes: `TameCandidate`, `TameEligibility`, `Vec3` (Tasks 1-2);
  `PortalTamesConfig`, `ValheimCompat`, `PortalTamesPlugin.Log` (Task 4).
- Produces:
  - `sealed class PendingArrival` with `IReadOnlyList<ZDOID> Tames` and
    `float CapturedAt`.
  - `PortalTamesManager.Instance` — `static PortalTamesManager`.
  - `PortalTamesManager.CaptureDeparture()` — scans, claims, and stores the
    pending list.

- [ ] **Step 1: Write PendingArrival**

`RossPortalTames/src/RossPortalTames.Game/PendingArrival.cs`:

```csharp
using System.Collections.Generic;

namespace RossPortalTames.Game
{
    /// <summary>
    /// The tames captured for one teleport, and when they were captured.
    ///
    /// ZDOIDs rather than GameObjects or components, because by the time this
    /// is acted on the creature objects no longer exist: the departure zone
    /// unloads while the player is in transit and Unity destroys them. The ZDOs
    /// survive, and a ZDOID is how you find one again.
    /// </summary>
    internal sealed class PendingArrival
    {
        public IReadOnlyList<ZDOID> Tames { get; }
        public float CapturedAt { get; }

        public PendingArrival(IReadOnlyList<ZDOID> tames, float capturedAt)
        {
            Tames = tames;
            CapturedAt = capturedAt;
        }
    }
}
```

- [ ] **Step 2: Write the portal patch**

`RossPortalTames/src/RossPortalTames.Game/PortalPatch.cs`:

```csharp
using HarmonyLib;

namespace RossPortalTames.Game
{
    /// <summary>
    /// Notices that the local player has used a portal.
    ///
    /// A POSTFIX with no injected parameters, which is deliberate on both
    /// counts. Postfix, because vanilla's Teleport decides whether a teleport
    /// actually happens -- a portal with no destination (XPortal's unconfigured
    /// state, among others) simply does not teleport anyone, and running before
    /// that decision would capture tames for a journey that never occurs.
    ///
    /// No parameters, because injecting `Player player` couples this patch to
    /// the exact parameter name in Valheim's signature, which a game update can
    /// change without removing the method -- the quiet kind of break. Asking
    /// Player.m_localPlayer whether IT is now teleporting answers the only
    /// question that matters (did MY player just go through?) and is immune to
    /// that.
    /// </summary>
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class PortalPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(TeleportWorld), nameof(TeleportWorld.Teleport));

        private static void Postfix()
        {
            if (!PortalTamesConfig.Enabled.Value) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            // The teleport is in progress exactly when it was accepted. If the
            // portal refused -- no destination, or vanilla declined for any
            // other reason -- there is nothing to follow.
            if (!player.IsTeleporting()) return;

            PortalTamesManager.Instance?.CaptureDeparture();
        }
    }
}
```

- [ ] **Step 3: Write the manager's capture half**

`RossPortalTames/src/RossPortalTames.Game/PortalTamesManager.cs`:

```csharp
using System.Collections.Generic;
using RossPortalTames.Core;
using UnityEngine;

namespace RossPortalTames.Game
{
    /// <summary>
    /// The only ticking object in this mod. Holds the pending capture and
    /// performs the arrival placement.
    /// </summary>
    internal class PortalTamesManager : MonoBehaviour
    {
        public static PortalTamesManager Instance { get; private set; }

        /// <summary>
        /// How long a capture stays valid. A teleport that includes loading a
        /// distant zone can take a while, so this is generous -- but not
        /// unbounded, because an arrival that never registers must not move
        /// creatures to a stale destination minutes later.
        /// </summary>
        private const float PendingExpirySeconds = 30f;

        private PendingArrival _pending;
        private bool _wasTeleporting;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Records which tames are coming, and takes ownership of each while
        /// they are still loaded and there is still a live ZNetView to claim
        /// through.
        /// </summary>
        public void CaptureDeparture()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            var characters = Character.GetAllCharacters();
            if (characters == null || characters.Count == 0) return;

            var candidates = new List<TameCandidate>(characters.Count);
            var views = new List<ZNetView>(characters.Count);

            foreach (var character in characters)
            {
                if (character == null) continue;

                var view = character.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;

                var ai = character.GetComponent<MonsterAI>();
                bool followingMe = ai != null && ai.GetFollowTarget() == player.gameObject;

                candidates.Add(new TameCandidate(
                    position: ToVec3(character.transform.position),
                    isTamed: character.IsTamed(),
                    isFollowingPlayer: followingMe,
                    isBusy: character.IsAttached() || character.IsRiding()));
                views.Add(view);
            }

            var chosen = TameEligibility.SelectIndices(
                candidates, ToVec3(player.transform.position), PortalTamesConfig.FollowRadius.Value);

            if (chosen.Count == 0)
            {
                _pending = null;
                return;
            }

            var ids = new List<ZDOID>(chosen.Count);
            foreach (int index in chosen)
            {
                var view = views[index];

                // Claim now, while the object is loaded. This is only a head
                // start: ownership is re-asserted at arrival because
                // ZDOMan.ReleaseNearbyZDOS can take it back while the player is
                // far away in transit. See TameMover.
                if (!view.IsOwner()) view.ClaimOwnership();

                ids.Add(view.GetZDO().m_uid);
            }

            _pending = new PendingArrival(ids, Time.time);
            _wasTeleporting = true;

            PortalTamesPlugin.Log.LogInfo($"Portal: bringing {ids.Count} tame(s).");
        }

        internal static Vec3 ToVec3(Vector3 v) => new Vec3(v.x, v.y, v.z);

        internal static Vector3 ToVector3(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build RossPortalTames/RossPortalTames.sln -c Release --nologo`
Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add RossPortalTames/src/RossPortalTames.Game
git commit -m "feat: capture eligible tames when the local player uses a portal"
```

---

### Task 6: Move the tames on arrival

**Files:**
- Create: `RossPortalTames/src/RossPortalTames.Game/TameMover.cs`
- Modify: `RossPortalTames/src/RossPortalTames.Game/PortalTamesManager.cs`
  (add `Update` and the arrival half)

**Interfaces:**
- Consumes: `PendingArrival` (Task 5), `ArrivalPlacement` (Task 3).
- Produces: `static bool TameMover.TryMove(ZDOID id, Vector3 destination)`.

- [ ] **Step 1: Write TameMover**

`RossPortalTames/src/RossPortalTames.Game/TameMover.cs`:

```csharp
using UnityEngine;

namespace RossPortalTames.Game
{
    /// <summary>
    /// The entire write side of this mod: re-assert ownership, set a position.
    ///
    /// Nothing is destroyed, nothing is recreated, no creature state is copied.
    /// That is why every failure here is harmless -- a tame that cannot be
    /// moved simply stays where it was, alive and still yours. A despawn and
    /// respawn would have to carry level, name, tameness, health and pregnancy
    /// by hand, and anything missed would be lost silently.
    /// </summary>
    internal static class TameMover
    {
        public static bool TryMove(ZDOID id, Vector3 destination)
        {
            if (id == ZDOID.None) return false;

            var man = ZDOMan.instance;
            if (man == null) return false;

            // Null means the creature no longer exists -- killed while the
            // player was in transit, most likely. Nothing to move.
            var zdo = man.GetZDO(id);
            if (zdo == null) return false;

            // Re-assert ownership immediately before the write, with nothing
            // in between. The claim taken at departure is NOT enough:
            // ZDOMan.ReleaseNearbyZDOS reassigns ownership by proximity every
            // couple of seconds, and the player is far from these creatures for
            // the whole of the teleport. A write into a ZDO this client no
            // longer owns is lost or overwritten by whoever picked it up --
            // intermittently, and more often on a busy server.
            //
            // Both calls work on a bare ZDO with no live GameObject, which is
            // what makes this possible at all: the creature objects were
            // destroyed when their zone unloaded.
            zdo.SetOwner(ZDOMan.GetSessionID());
            if (!zdo.IsOwner()) return false;

            zdo.SetPosition(destination);
            return true;
        }
    }
}
```

- [ ] **Step 2: Add the arrival half to the manager**

Add to `PortalTamesManager`, after `CaptureDeparture`:

```csharp
        private void Update()
        {
            if (_pending == null) return;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                // Logged out, or died into a loading screen. Drop the capture:
                // nothing has been moved, and the tames are untouched.
                _pending = null;
                _wasTeleporting = false;
                return;
            }

            if (Time.time - _pending.CapturedAt > PendingExpirySeconds)
            {
                PortalTamesPlugin.Log.LogWarning(
                    $"Portal: arrival never registered within {PendingExpirySeconds:F0}s; "
                    + $"leaving {_pending.Tames.Count} tame(s) where they are.");
                _pending = null;
                _wasTeleporting = false;
                return;
            }

            bool teleporting = player.IsTeleporting();

            // The transition matters, not the state: acting while still
            // teleporting would place tames at a position the player is about
            // to leave.
            if (_wasTeleporting && !teleporting)
            {
                PlaceArrivals(player);
                _pending = null;
            }

            _wasTeleporting = teleporting;
        }

        private void PlaceArrivals(Player player)
        {
            var arrival = player.transform.position;
            var facing = player.transform.forward;

            var spots = ArrivalPlacement.Compute(
                ToVec3(arrival),
                ToVec3(facing),
                _pending.Tames.Count,
                PortalTamesConfig.SearchDistance.Value,
                IsFree);

            int moved = 0;
            for (int i = 0; i < _pending.Tames.Count; i++)
            {
                var target = ToVector3(spots[i]);
                target.y = GroundHeight(target);

                if (TameMover.TryMove(_pending.Tames[i], target)) moved++;
            }

            PortalTamesPlugin.Log.LogInfo($"Portal: {moved} of {_pending.Tames.Count} tame(s) arrived.");
        }

        /// <summary>
        /// Whether a creature could stand at this point. Backed by the real
        /// world here; the tests supply their own predicate, which is what
        /// makes the wall case assertable without a running game.
        /// </summary>
        private static bool IsFree(Vec3 point)
        {
            var zones = ZoneSystem.instance;
            if (zones == null) return true;

            var world = ToVector3(point);
            return !zones.IsBlocked(world);
        }

        private static float GroundHeight(Vector3 point)
        {
            var zones = ZoneSystem.instance;
            if (zones == null) return point.y;

            return zones.GetSolidHeight(point, out float height) ? height : point.y;
        }
```

- [ ] **Step 3: Build**

Run: `dotnet build RossPortalTames/RossPortalTames.sln -c Release --nologo`
Expected: PASS, 0 warnings, 0 errors.

If `ZoneSystem.GetSolidHeight` does not have a `(Vector3, out float)` overload
in this Valheim build, check the available overloads and use the one that
returns a height for a world point; do not guess. The four overloads were
confirmed present by assembly inspection but their signatures were not.

- [ ] **Step 4: Run the Core tests**

Run: `dotnet test RossPortalTames/tests/RossPortalTames.Core.Tests -c Release --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add RossPortalTames/src/RossPortalTames.Game
git commit -m "feat: place arriving tames and re-assert ownership before writing"
```

---

### Task 7: Deployment, packaging, and documentation

**Files:**
- Create: `RossPortalTames/deploy.sh`
- Create: `RossPortalTames/package.sh`
- Create: `RossPortalTames/thunderstore/manifest.json`
- Create: `RossPortalTames/thunderstore/README.md`
- Create: `RossPortalTames/thunderstore/CHANGELOG.md`
- Create: `RossPortalTames/README.md`
- Modify: `README.md` (repo root — add the mod to the table)
- Modify: `.gitignore` (ignore this mod's packaging output)

**Interfaces:**
- Consumes: the built `RossPortalTames.dll` and `RossPortalTames.Core.dll`.
- Produces: `RossPortalTames/RossPortalTames-<version>.zip`.

- [ ] **Step 1: Write deploy.sh**

Adapt `ItemDrawers/deploy.sh`, changing the assembly glob to
`RossPortalTames*.dll` and the destination folder to `RossPortalTames`. Keep
its two load-bearing behaviours: copy EVERY produced assembly (the plugin
depends on `RossPortalTames.Core.dll`, and shipping one without the other
installs cleanly and then does nothing), and fail loudly with `LOCKED` when
Valheim is holding a file rather than reporting a silent success.

Per the standing rule, deploy targets the `dev` profile only. Never copy into
the `Default` profile; r2modman owns that.

- [ ] **Step 2: Write package.sh**

Adapt `ItemDrawers/package.sh`. Keep:
- the version-agreement check between `manifest.json` and `PluginVersion`,
  which refuses to build when they disagree;
- the both-assemblies-present check;
- the `zip`-or-Python fallback, writing forward-slash entry names.

- [ ] **Step 3: Write the Thunderstore manifest**

`RossPortalTames/thunderstore/manifest.json`:

```json
{
  "name": "RossPortalTames",
  "version_number": "0.1.0",
  "website_url": "https://github.com/ross-game-tools/RossValheimMods",
  "description": "Tames that are following you come through the portal with you",
  "dependencies": [
    "denikson-BepInExPack_Valheim-5.4.2350"
  ]
}
```

Note there is no Jotunn dependency.

- [ ] **Step 4: Write the package README and changelog**

`RossPortalTames/thunderstore/README.md` — what it does, the three config
settings and what they mean, and that it is client-side only with no server
install required.

`RossPortalTames/thunderstore/CHANGELOG.md` — a `0.1.0` entry.

- [ ] **Step 5: Add the mod to the repo root README table**

Add a row to the table in `README.md`:

```markdown
| [RossPortalTames](RossPortalTames/) | Tames following you come through portals with you. Client-side only; no Jotunn. | Implemented |
```

- [ ] **Step 6: Ignore the packaging output**

Append to `.gitignore`:

```
RossPortalTames/thunderstore/build/
RossPortalTames/*RossPortalTames-*.zip
```

- [ ] **Step 7: Verify the package builds**

Run: `bash RossPortalTames/package.sh`
Expected: version agreement reported, 0 warnings, a zip containing
`manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md` and
`plugins/RossPortalTames.dll` + `plugins/RossPortalTames.Core.dll`, with
forward-slash paths.

`icon.png` (256x256) must exist before packaging succeeds. Generate it with a
script under `RossPortalTames/tools/`, the way `ItemDrawers/tools/make-icon.py`
does, so the asset has readable source.

- [ ] **Step 8: Commit**

```bash
git add RossPortalTames README.md .gitignore
git commit -m "feat: deployment, Thunderstore packaging, and documentation"
```

---

## Manual verification

Core tests cover the two pure rule sets. Everything below needs a running game,
in the `dev` profile, which has XPortal installed.

- [ ] Wolves following, portal in the open — all arrive near you.
- [ ] Portal with a wall directly behind it — nothing is placed inside geometry.
- [ ] A following tame outside `FollowRadius` — stays put.
- [ ] A tame killed while you are in transit — no error, the others arrive.
- [ ] An XPortal portal with no target set — no teleport, no tames moved, no log noise.
- [ ] A ridden lox — excluded.
- [ ] `Enabled = false` — nothing follows.

**Dedicated server, before any release.** This is the assumption most worth
disproving early, and the design changes materially if it fails:

- [ ] A moved tame is still at its new location after the destination zone
      unloads and reloads.
- [ ] Two clients: each player's own tames follow only that player.
