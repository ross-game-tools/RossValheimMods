using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// BenchRange's settings beyond its toggle. Read at use, so live reloads
    /// apply without a restart.
    /// </summary>
    public static class BenchRangeConfig
    {
        // Never "BenchRange": that is the feature's own toggle, already bound
        // as a bool in this section, and binding a float over it throws.
        public static ConfigEntry<float> BuildRange;
        public static ConfigEntry<string> BuildRangePerType;
        public static ConfigEntry<float> ExtensionRange;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            BuildRange = config.Bind(section, "BuildRange", 40f,
                ConfigText.Description(
                    "How far from a crafting station you can build, in metres. Vanilla is 10. Each station's "
                    + "extensions still add their own bonus on top, as they always did.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 100f)));

            BuildRangePerType = config.Bind(section, "BuildRangePerType", "",
                ConfigText.Description(
                    "Per-station ranges overriding BuildRange, in metres. Prefab names with a number, "
                    + "comma-separated, e.g. \"piece_workbench:30, forge:15\".",
                    scope, requiresRestart: false));

            ExtensionRange = config.Bind(section, "ExtensionRange", 10f,
                ConfigText.Description(
                    "How far an attachment -- a chopping block, anvils, a tanning rack -- may sit from its "
                    + "station, in metres. Vanilla is 5. Applies to attachments as they load, so it takes a "
                    + "reload to change what is already around you.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 50f)));
        }
    }
}
