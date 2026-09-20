using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// Settings for the progression rewards. Read at use, so live reloads
    /// apply without a restart.
    /// </summary>
    public static class ProgressionConfig
    {
        public static ConfigEntry<float> MiningMultiplier;
        public static ConfigEntry<int> SmeltingMultiplier;
        public static ConfigEntry<int> RespawnDays;
        public static ConfigEntry<bool> ProtectPlayerBuilds;

        internal static void BindMining(ConfigFile config, string section, FeatureScope scope)
        {
            MiningMultiplier = config.Bind(section, "MiningMultiplier", 2f,
                ConfigText.Description(
                    "How much harder you hit rock and ore in a biome whose boss you have killed. 2 means twice "
                    + "the damage, so half the swings.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(1f, 10f)));
        }

        internal static void BindDungeonRespawn(ConfigFile config, string section, FeatureScope scope)
        {
            RespawnDays = config.Bind(section, "RespawnDays", 24,
                ConfigText.Description(
                    "In-game days after your last visit before a dungeon is rebuilt as it was first found. "
                    + "A day is about half an hour of play.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<int>(1, 1000)));

            ProtectPlayerBuilds = config.Bind(section, "ProtectPlayerBuilds", true,
                ConfigText.Description(
                    "Leave a dungeon alone entirely once anything is built inside it. A rebuild destroys "
                    + "everything in the dungeon, a portal or a stash included, so by default a dungeon you "
                    + "have made your own stops respawning rather than being cleared out.",
                    scope, requiresRestart: false));
        }

        internal static void BindSmelting(ConfigFile config, string section, FeatureScope scope)
        {
            SmeltingMultiplier = config.Bind(section, "SmeltingMultiplier", 2,
                ConfigText.Description(
                    "How many bars one ore yields once the boss of the biome it comes from is dead, and how "
                    + "many refined eitr the Eitr Refinery yields once the Queen is dead. 2 doubles the output "
                    + "for the same inputs.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<int>(1, 10)));
        }
    }
}
