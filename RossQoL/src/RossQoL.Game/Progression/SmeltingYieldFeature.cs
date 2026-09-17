using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RossQoL.Core.Progression;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// Beat a biome's boss and its ore smelts richer: one ore becomes
    /// SmeltingMultiplier bars instead of one, for the same fuel.
    ///
    /// The ore decides, by the same table the teleport unlocks use: copper
    /// and tin answer to the Elder, scrap iron to Bonemass, silver to Moder,
    /// black metal to Yagluth. Anything not on that list smelts as it always
    /// did, so coal, flour and the rest are untouched.
    ///
    /// Synced scope: it puts extra metal into a shared world.
    /// </summary>
    internal sealed class SmeltingYieldFeature : Feature
    {
        public const string FeatureName = "Progression/SmeltingYield";

        public static SmeltingYieldFeature Instance { get; private set; }

        public SmeltingYieldFeature() => Instance = this;

        public override string Key => "SmeltingYield";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Ore from a biome whose boss you have killed smelts into SmeltingMultiplier bars instead of one, "
            + "for the same ore and fuel. Only the metals tied to a boss are affected; coal, flour and "
            + "everything else is unchanged.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(SmeltingYieldPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Smelter", "Spawn", "where a smelter hands over what it made"),
            new CompatMember("ZoneSystem", "GetGlobalKey", "reading which bosses are dead"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            ProgressionConfig.BindSmelting(config, section, Scope);
    }

    /// <summary>
    /// Smelter.Spawn is handed the ore's prefab name and how many bars to
    /// make. Multiplying that count before vanilla acts on it leaves
    /// everything else alone: the ore was already spent, the effects still
    /// play, and a windmill or kiln is untouched because their inputs are not
    /// on the boss table.
    /// </summary>
    [HarmonyPatch(typeof(Smelter), "Spawn")]
    internal static class SmeltingYieldPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Smelter), "Spawn", SmeltingYieldFeature.FeatureName);

        private static void Prefix(string ore, ref int stack)
        {
            if (SmeltingYieldFeature.Instance?.IsActive != true || stack <= 0) return;

            try
            {
                if (!TeleportUnlocks.IsUnlocked(ore, HasKey)) return;

                int multiplier = ProgressionConfig.SmeltingMultiplier?.Value ?? 2;
                if (multiplier <= 1) return;

                stack *= multiplier;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"SmeltingYield: leaving this batch at vanilla size: {ex}");
            }
        }

        private static bool HasKey(string key) =>
            ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
    }
}
