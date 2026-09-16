using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// A windmill banks its flour in the ZDO (s_spawnOre / s_spawnAmount)
    /// and only spawns it when the stack fills, the grain runs out, or a
    /// player empties it by hand. The postfix moves the banked flour into a
    /// nearby container on the harvest interval, which is the automation;
    /// the Spawn prefix catches the stacks vanilla does drop, so they land
    /// in a container too.
    ///
    /// Flour moves whole or not at all: a part-placed stack would leave
    /// vanilla's banked amount and the container disagreeing, and losing
    /// flour is worse than leaving it in the windmill.
    ///
    /// Only windmills. Kilns, smelters, blast furnaces and spinning wheels
    /// keep dropping their output on the ground.
    /// </summary>
    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    internal static class WindmillHarvestPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Smelter), "UpdateSmelter", AutoHarvestFeature.FeatureName);

        private static void Postfix(Smelter __instance)
        {
            if (AutoHarvestFeature.Instance?.IsActive != true) return;
            if (__instance.m_windmill == null) return;

            // An exception escaping here would stop this windmill's update.
            try
            {
                if (!HarvestGate.ShouldRun(__instance, __instance.m_nview, ProductionConfig.HarvestWindmills)) return;
                Harvest(__instance);
                HarvestGate.Succeeded(__instance);
            }
            catch (Exception ex)
            {
                HarvestGate.LogFailure(__instance, __instance.m_nview, ex);
            }
        }

        private static void Harvest(Smelter windmill)
        {
            // As Smelter.OnEmpty (the switch a player uses): no ward access,
            // no harvest.
            if (!PrivateArea.CheckAccess(windmill.transform.position, 0f, flash: false)) return;

            var zdo = windmill.m_nview.GetZDO();
            int amount = zdo.GetInt(ZDOVars.s_spawnAmount);
            if (amount <= 0) return;

            string ore = zdo.GetString(ZDOVars.s_spawnOre);
            var conversion = windmill.GetItemConversion(ore);
            if (conversion == null || conversion.m_to == null) return;

            int placed = Harvester.PlaceWholeStack(
                conversion.m_to, amount, IsCheated(zdo), windmill.transform.position);
            if (placed < amount) return;

            // As vanilla's SpawnProcessed clears it once the stack is out.
            zdo.Set(ZDOVars.s_spawnOre, "");
            zdo.Set(ZDOVars.s_spawnAmount, 0);
        }

        /// <summary>As Smelter.Spawn decides it.</summary>
        internal static bool IsCheated(ZDO zdo) =>
            (zdo.GetBool(ZDOVars.s_cheatedQueued) || zdo.GetBool(ZDOVars.s_cheated))
            && !PlayerProfile.s_bypassCheatChecks;
    }

    /// <summary>
    /// The stacks vanilla spawns anyway: a full stack, the grain running
    /// out, or a player emptying the windmill by hand. Placing them in a
    /// container beats dropping them at the output point. Falls through to
    /// vanilla whenever the whole stack does not fit.
    /// </summary>
    [HarmonyPatch(typeof(Smelter), "Spawn")]
    internal static class WindmillSpawnPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Smelter), "Spawn", AutoHarvestFeature.FeatureName);

        private static bool Prefix(Smelter __instance, string ore, int stack)
        {
            if (AutoHarvestFeature.Instance?.IsActive != true) return true;
            if (ProductionConfig.HarvestWindmills?.Value != true) return true;

            try
            {
                if (__instance.m_windmill == null || stack <= 0) return true;

                var nview = __instance.m_nview;
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return true;
                if (Player.m_localPlayer == null) return true;
                if (!PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false)) return true;

                var conversion = __instance.GetItemConversion(ore);
                if (conversion == null || conversion.m_to == null) return true;

                int placed = Harvester.PlaceWholeStack(
                    conversion.m_to, stack, WindmillHarvestPatch.IsCheated(nview.GetZDO()),
                    __instance.transform.position);

                // Nothing placed: vanilla drops it, as it would without us.
                return placed < stack;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"AutoHarvest: emptying a windmill failed, its flour was dropped instead: {ex}");
                return true;
            }
        }
    }
}
