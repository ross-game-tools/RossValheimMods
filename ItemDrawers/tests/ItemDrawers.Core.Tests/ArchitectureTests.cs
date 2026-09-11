using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ItemDrawers.Core.Tests
{
    public class ArchitectureTests
    {
        private static readonly string[] Forbidden =
            { "UnityEngine", "Assembly-CSharp", "assembly_valheim", "BepInEx", "Jotunn", "0Harmony" };

        // The core is testable precisely because it knows nothing about the
        // game. If this ever fails, the design has drifted, not the test.
        [Fact]
        public void Core_references_no_game_or_engine_assemblies()
        {
            var core = typeof(DrawerProportions).Assembly;
            var referenced = core.GetReferencedAssemblies().Select(a => a.Name).ToArray();

            var violations = referenced
                .Where(name => Forbidden.Any(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            Assert.Empty(violations);
        }

        // GetReferencedAssemblies() only reflects assemblies whose types are
        // actually used by compiled IL - a PackageReference or
        // ProjectReference added "for later", before any code consumes it,
        // would pass the test above silently. This inspects the csproj's
        // declared references directly, independent of whether code uses
        // them yet, so that blind spot is covered too.
        [Fact]
        public void Core_csproj_declares_no_game_or_engine_references()
        {
            var csprojPath = FindCoreCsproj();
            Assert.True(File.Exists(csprojPath), $"Could not locate ItemDrawers.Core.csproj to inspect (looked at: {csprojPath}).");

            var text = File.ReadAllText(csprojPath);

            var violations = Forbidden
                .Where(f => text.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            Assert.Empty(violations);
        }

        // Walks up from the test assembly's run location to find the repo's
        // "ItemDrawers" directory (identified by ItemDrawers.sln living in
        // it), then locates src/ItemDrawers.Core/ItemDrawers.Core.csproj
        // from there. A relative path from bin/Debug/net8.0/... would break
        // the moment the build layout changes; this survives that.
        private static string FindCoreCsproj()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ItemDrawers.sln")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                // Fail loudly with a path that makes clear the search failed,
                // rather than silently reporting "no violations" for a file
                // we never actually opened.
                return Path.Combine(AppContext.BaseDirectory, "<ItemDrawers.sln not found while walking up from here>");
            }

            return Path.Combine(dir.FullName, "src", "ItemDrawers.Core", "ItemDrawers.Core.csproj");
        }
    }
}
