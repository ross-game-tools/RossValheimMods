using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Startup
{
    /// <summary>
    /// A postfix, so the join data is exactly what vanilla just acted on.
    /// JoinServer can return early (PlayFab login popup, incompatible server)
    /// and be called again; a stale stash is harmless because only a real
    /// spawn commits it.
    /// </summary>
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.JoinServer))]
    internal static class JoinServerRecordingPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(FejdStartup), nameof(FejdStartup.JoinServer), ContinueButtonFeature.FeatureName);

        private static void Postfix(FejdStartup __instance)
        {
            if (ContinueButtonFeature.Instance?.IsActive != true) return;
            SessionRecorder.ServerJoinRequested(__instance.GetServerToJoin());
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnWorldStart))]
    internal static class WorldStartRecordingPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(FejdStartup), nameof(FejdStartup.OnWorldStart), ContinueButtonFeature.FeatureName);

        private static void Prefix() => SessionRecorder.LocalWorldStartRequested();
    }
}
