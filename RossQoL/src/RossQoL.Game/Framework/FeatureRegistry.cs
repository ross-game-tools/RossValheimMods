using System.Collections.Generic;
using RossQoL.Game.Combat;
using RossQoL.Game.Crafting;
using RossQoL.Game.Fires;
using RossQoL.Game.Interface;
using RossQoL.Game.Portals;
using RossQoL.Game.Production;
using RossQoL.Game.Progression;
using RossQoL.Game.Startup;
using RossQoL.Game.Tames;
using RossQoL.Game.Items;
using RossQoL.Game.Meals;
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
                new CraftingSearchFeature(),
                new MultiCraftFeature(),
                new AutoRepairFeature(),
                new BenchRangeFeature(),
                new CraftFromChestsFeature()),
            new Category("Interface", "All HUD and interface tweaks.",
                new ClockFeature(),
                new ProductionTimersFeature(),
                new PanCameraFeature()),
            new Category("Fires", "All fire tweaks.",
                new FiresFeedFeature(),
                new InfiniteFireFuelFeature()),
            new Category("Food", "All food tweaks.",
                new NoFoodFalloffFeature(),
                new FoodWarningFeature()),
            new Category("Items", "All item tweaks.",
                new StackableMeadBasesFeature(),
                new WisplightCarryFeature()),
            new Category("Portals", "All portal tweaks.",
                new TamesFollowFeature(),
                new InstantPortalsFeature()),
            new Category("Production", "All production tweaks.",
                new AutoHarvestFeature(),
                new AutoFeedFeature()),
            new Category("Progression", "All boss progression tweaks.",
                new TeleportUnlocksFeature(),
                new ClearMistFeature(),
                new MiningPowerFeature(),
                new SmeltingYieldFeature()),
            new Category("Startup", "All startup and main menu tweaks.",
                new ContinueButtonFeature(),
                new SkipSplashFeature(),
                new SkipValkyrieFeature()),
            new Category("Tames", "All tame tweaks.",
                new FollowCommandFeature(),
                new FeedFromContainersFeature(),
                new SilentBirthsFeature(),
                new QuietWolvesFeature()),
            new Category("Terrain", "All terrain tweaks.",
                new UnlimitedHeightFeature()),
            new Category("World", "All world and item tweaks.",
                new FloatingItemsFeature(),
                new FastSleepFeature(),
                new AoeRepairFeature(),
                new ComfortRangeFeature()),
        };
    }
}
