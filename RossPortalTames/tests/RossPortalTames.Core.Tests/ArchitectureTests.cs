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
