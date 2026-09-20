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
    /// SmeltingMultiplier bars instead of one, for the same fuel. The Eitr
    /// Refinery is a <c>Smelter</c> too, so once the Queen is dead a batch of
    /// refined eitr is multiplied the same way.
    ///
    /// The ore decides, by the same table the teleport unlocks use: copper
    /// and tin answer to the Elder, scrap iron to Bonemass, silver to Moder,
    /// black metal to Yagluth. Refined eitr is decided by what the smelter
    /// produces, not what it eats, so it does not matter which of the
    /// refinery's two inputs (Sap or Soft tissue) Smelter.Spawn hands us.
    /// Anything else smelts as it always did, so coal, flour and the rest
    /// are untouched.
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
            + "for the same ore and fuel, and the Eitr Refinery yields that many refined eitr once the Queen "
            + "is dead. Only the metals tied to a boss and refined eitr are affected; coal, flour and "
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
    /// Smelter.Spawn is handed the input conversion's prefab name and how many
    /// items to make. Multiplying that count before vanilla acts on it leaves
    /// everything else alone: the ore was already spent, the effects still
    /// play, and a windmill or kiln is untouched because their conversions are
    /// not on the boss table.
    ///
    /// Two kinds of batch qualify. Metals are gated on the ore going in, by
    /// the same table the teleport unlocks use. Refined eitr is gated on what
    /// the smelter produces instead -- the Eitr Refinery is a <c>Smelter</c>
    /// whose conversion outputs the Eitr item -- so the Queen doubles it
    /// without us having to know which of the refinery's two inputs (Sap fuel
    /// or Soft tissue) <c>Spawn</c> was called with.
    /// </summary>
    [HarmonyPatch(typeof(Smelter), "Spawn")]
    internal static class SmeltingYieldPatch
    {
        /// <summary>Prefab name of the Eitr Refinery, whose Smelter produces refined eitr.</summary>
        private const string RefineryPrefab = "eitrrefinery";

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Smelter), "Spawn", SmeltingYieldFeature.FeatureName);

        private static void Prefix(Smelter __instance, string ore, ref int stack)
        {
            if (SmeltingYieldFeature.Instance?.IsActive != true || stack <= 0) return;

            try
            {
                if (!ShouldEnrich(__instance, ore)) return;

                int multiplier = ProgressionConfig.SmeltingMultiplier?.Value ?? 2;
                if (multiplier <= 1) return;

                stack *= multiplier;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"SmeltingYield: leaving this batch at vanilla size: {ex}");
            }
        }

        /// <summary>
        /// A metal whose biome boss is dead, or refined eitr from the Eitr
        /// Refinery once the Queen is.
        /// </summary>
        private static bool ShouldEnrich(Smelter smelter, string ore)
        {
            if (TeleportUnlocks.IsUnlocked(ore, HasKey)) return true;

            return IsEitrRefinery(smelter) && HasKey(TeleportUnlocks.Queen);
        }

        /// <summary>
        /// Whether this smelter is an Eitr Refinery, keyed on the owning
        /// prefab name the same way <c>SafeRefineryPatch</c> does -- the
        /// refinery's production output item name is asset data we cannot read
        /// from the DLL, but the prefab name is confirmed (docs/valheim-api/
        /// eitr-refinery.md).
        /// </summary>
        private static bool IsEitrRefinery(Smelter smelter)
        {
            var nview = smelter == null ? null : smelter.GetComponentInParent<ZNetView>();
            if (nview == null) return false;

            string prefab = Utils.GetPrefabName(nview.gameObject);
            return string.Equals(prefab, RefineryPrefab, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasKey(string key) =>
            ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
    }
}
