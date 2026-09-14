using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Startup
{
    /// <summary>
    /// Game.Start queues the intro when the profile's m_firstSpawn is set.
    /// Clearing the queue here means Game.UpdateRespawn takes vanilla's normal
    /// path, SpawnPlayer(point, false): start location, spawn stat, and
    /// m_firstSpawn cleared. Blocking the Valkyrie itself instead leaves the
    /// player stuck in intro mode; clearing the flag any later is too late.
    /// </summary>
    [HarmonyPatch(typeof(global::Game), nameof(global::Game.Start))]
    internal static class ValkyrieIntroPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(global::Game), nameof(global::Game.Start), SkipValkyrieFeature.FeatureName);

        private static void Postfix(global::Game __instance)
        {
            if (SkipValkyrieFeature.Instance?.IsActive != true) return;
            if (!__instance.m_queuedIntro) return;

            __instance.m_queuedIntro = false;
            RossQoLPlugin.Log.LogInfo("SkipValkyrie: intro skipped for a new character.");
        }
    }
}
