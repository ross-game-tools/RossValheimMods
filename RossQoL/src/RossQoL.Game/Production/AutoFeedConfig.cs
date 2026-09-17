using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// AutoFeed's settings beyond its toggle. Read at use on every feed, so
    /// live reloads apply without a restart.
    /// </summary>
    public static class AutoFeedConfig
    {
        public static ConfigEntry<bool> FeedSmelters;
        public static ConfigEntry<bool> FeedKilns;
        public static ConfigEntry<bool> FeedBlastFurnaces;
        public static ConfigEntry<bool> FeedWindmills;
        public static ConfigEntry<bool> FeedSpinningWheels;
        public static ConfigEntry<bool> FeedOvens;
        public static ConfigEntry<bool> FeedShieldGenerators;
        public static ConfigEntry<bool> FeedFermenters;

        public static ConfigEntry<float> FeedRadius;
        public static ConfigEntry<float> FeedInterval;

        public static ConfigEntry<int> MinimumLeftBehind;
        public static ConfigEntry<string> MinimumPerItem;
        public static ConfigEntry<string> KilnFuel;
        public static ConfigEntry<string> MaxOutput;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            FeedSmelters = config.Bind(section, "FeedSmelters", true,
                ConfigText.Description("Smelters take ore and fuel from nearby containers.",
                    scope, requiresRestart: false));

            FeedKilns = config.Bind(section, "FeedKilns", true,
                ConfigText.Description("Charcoal kilns take wood from nearby containers. See KilnFuel.",
                    scope, requiresRestart: false));

            FeedBlastFurnaces = config.Bind(section, "FeedBlastFurnaces", true,
                ConfigText.Description("Blast furnaces take ore and fuel from nearby containers.",
                    scope, requiresRestart: false));

            FeedWindmills = config.Bind(section, "FeedWindmills", true,
                ConfigText.Description("Windmills take barley from nearby containers.",
                    scope, requiresRestart: false));

            FeedSpinningWheels = config.Bind(section, "FeedSpinningWheels", true,
                ConfigText.Description("Spinning wheels take flax from nearby containers.",
                    scope, requiresRestart: false));

            FeedOvens = config.Bind(section, "FeedOvens", true,
                ConfigText.Description("Ovens take fuel from nearby containers. Food is still put in by hand.",
                    scope, requiresRestart: false));

            FeedShieldGenerators = config.Bind(section, "FeedShieldGenerators", true,
                ConfigText.Description("Shield generators take fuel from nearby containers.",
                    scope, requiresRestart: false));

            FeedFermenters = config.Bind(section, "FeedFermenters", true,
                ConfigText.Description("Empty fermenters take a mead base from nearby containers and start it.",
                    scope, requiresRestart: false));

            FeedRadius = config.Bind(section, "FeedRadius", 40f,
                ConfigText.Description(
                    "How far from a producer, in metres, to look for containers to take from. Measured in three "
                    + "dimensions. Fires use this too.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 100f)));

            FeedInterval = config.Bind(section, "FeedInterval", 1f,
                ConfigText.Description(
                    "Seconds between feed attempts for each producer. One item moves per attempt, so this is "
                    + "also how fast a producer fills. Fires use this too.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 3600f)));

            MinimumLeftBehind = config.Bind(section, "MinimumLeftBehind", 0,
                ConfigText.Description(
                    "How many of an item to leave across the containers near a producer rather than feed it in. "
                    + "Counted as a total, not per container: 50 wood means 50 in the area, however many chests "
                    + "it is spread over. Applies to every item without its own entry in MinimumPerItem, and to "
                    + "fires too.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<int>(0, 9999)));

            MinimumPerItem = config.Bind(section, "MinimumPerItem", "",
                ConfigText.Description(
                    "Per-item amounts to leave across the containers near a producer, overriding "
                    + "MinimumLeftBehind. Counted as a total, not per container. Prefab names with a number, "
                    + "comma-separated, e.g. \"Wood:50, Barley:20\".",
                    scope, requiresRestart: false));

            KilnFuel = config.Bind(section, "KilnFuel", "Wood",
                ConfigText.Description(
                    "What a charcoal kiln may be fed, comma-separated prefab names, e.g. \"Wood, FineWood\". "
                    + "Empty allows anything the kiln accepts. Only limits what this mod feeds it; "
                    + "you can always add fuel by hand.",
                    scope, requiresRestart: false));

            MaxOutput = config.Bind(section, "MaxOutput", "",
                ConfigText.Description(
                    "Stop feeding a producer once this many of what it makes are in nearby containers. "
                    + "Prefab names with a number, comma-separated, e.g. \"Coal:200, BarleyFlour:500\". "
                    + "Smelted metals are never capped, so ore is always processed.",
                    scope, requiresRestart: false));
        }
    }
}
