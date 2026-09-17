using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RossQoL.Core.Framework;
using RossQoL.Core.Progression;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// Beat a biome's boss and its rock gives way more easily: every swing at
    /// stone or ore there lands for MiningMultiplier times the damage.
    ///
    /// The ground decides, not the item: a copper vein in the Black Forest
    /// answers to the Elder, wherever the ore ends up afterwards. Trees are
    /// left alone -- this is about digging, not logging.
    ///
    /// Synced scope: how fast a shared world gives up its ore is the server's
    /// rule.
    /// </summary>
    internal sealed class MiningPowerFeature : Feature
    {
        public const string FeatureName = "Progression/MiningPower";

        public static MiningPowerFeature Instance { get; private set; }

        public MiningPowerFeature() => Instance = this;

        public override string Key => "MiningPower";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Rock and ore in a biome whose boss you have killed take MiningMultiplier times the damage, so the "
            + "Black Forest gives up its copper faster once the Elder is down, the swamp its scrap once "
            + "Bonemass is, and so on. Trees are unaffected.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(MiningPowerPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("MineRock5", "Damage", "where a swing at ore lands"),
            new CompatMember("MineRock", "Damage", "where a swing at older rock lands"),
            new CompatMember("Destructible", "Damage", "where a swing at a deposit lands"),
            new CompatMember("Heightmap", "FindBiome", "which biome the rock stands in"),
            new CompatMember("ZoneSystem", "GetGlobalKey", "reading which bosses are dead"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            ProgressionConfig.BindMining(config, section, Scope);
    }

    /// <summary>
    /// The three ways vanilla lets something be mined all take a HitData, so
    /// all three are patched with one prefix that scales the damage before
    /// vanilla reads it. Scaling the hit rather than the rock's health keeps
    /// every other rule -- tool tier, effects, drops -- exactly vanilla's.
    /// </summary>
    [HarmonyPatch]
    internal static class MiningPowerPatch
    {
        private static readonly (Type Type, string Method)[] Targets =
        {
            (typeof(MineRock5), "Damage"),
            (typeof(MineRock), "Damage"),
            (typeof(Destructible), "Damage"),
        };

        private static bool Prepare()
        {
            bool all = true;
            foreach (var (type, method) in Targets)
                all &= ValheimCompat.RequireMethod(type, method, MiningPowerFeature.FeatureName);

            return all;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var (type, method) in Targets)
                yield return AccessTools.Method(type, method, new[] { typeof(HitData) });
        }

        private static void Prefix(MonoBehaviour __instance, HitData hit)
        {
            if (MiningPowerFeature.Instance?.IsActive != true || hit == null || __instance == null) return;

            try
            {
                // Logging is not mining.
                if (__instance is IDestructible destructible
                    && destructible.GetDestructibleType() == DestructibleType.Tree) return;

                if (!BiomeBosses.IsCleared(BiomeAt(__instance.transform.position), HasKey)) return;

                float multiplier = ProgressionConfig.MiningMultiplier?.Value ?? 2f;
                if (multiplier <= 1f) return;

                hit.m_damage.Modify(multiplier);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"MiningPower: leaving this swing at vanilla strength: {ex}");
            }
        }

        private static string BiomeAt(Vector3 point) => Heightmap.FindBiome(point).ToString();

        private static bool HasKey(string key) =>
            ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
    }
}
