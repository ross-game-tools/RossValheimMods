using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// BaseAI.DoIdleSound plays a creature's idle sound on a repeating timer;
    /// for a wolf that is the howl. It is skipped for tamed wolves, which
    /// are picked out by prefab name (Wolf, Wolf_cub and any Wolf* variant),
    /// so no other creature is quieted.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), "DoIdleSound")]
    internal static class QuietWolvesPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(BaseAI), "DoIdleSound", QuietWolvesFeature.FeatureName);

        private static bool Prefix(BaseAI __instance)
        {
            if (QuietWolvesFeature.Instance?.IsActive != true) return true;

            try
            {
                var character = __instance.m_character;
                if (character == null || !character.IsTamed()) return true;

                string prefab = Utils.GetPrefabName(__instance.gameObject);
                if (!prefab.StartsWith("Wolf", StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"QuietWolves: could not check a creature, leaving its sound alone: {ex}");
                return true;
            }
        }
    }
}
