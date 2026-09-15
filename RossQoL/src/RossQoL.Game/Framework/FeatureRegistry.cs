using System.Collections.Generic;
using RossQoL.Game.Combat;
using RossQoL.Game.Crafting;
using RossQoL.Game.Interface;
using RossQoL.Game.Portals;
using RossQoL.Game.Production;
using RossQoL.Game.Startup;
using RossQoL.Game.Tames;
using RossQoL.Game.Terrain;

namespace RossQoL.Game.Framework
{
    /// <summary>
    /// Every category and feature, listed explicitly so the whole mod is
    /// readable in one place. No reflection discovery.
    /// </summary>
    internal static class FeatureRegistry
    {
        public static IReadOnlyList<Category> Create() => new[]
        {
            new Category("Combat", "All combat and creature tweaks.",
                new InstantLootFeature()),
            new Category("Crafting", "All crafting station tweaks.",
                new CraftingSearchFeature()),
            new Category("Interface", "All HUD and interface tweaks.",
                new ClockFeature()),
            new Category("Portals", "All portal tweaks.",
                new TamesFollowFeature()),
            new Category("Production", "All production tweaks.",
                new AutoHarvestFeature()),
            new Category("Startup", "All startup and main menu tweaks.",
                new ContinueButtonFeature(),
                new SkipSplashFeature(),
                new SkipValkyrieFeature()),
            new Category("Tames", "All tame tweaks.",
                new FollowCommandFeature()),
            new Category("Terrain", "All terrain tweaks.",
                new UnlimitedHeightFeature()),
        };
    }
}
