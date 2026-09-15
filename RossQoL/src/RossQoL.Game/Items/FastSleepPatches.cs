using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// EnvMan.SkipToMorning sets the speed the world clock runs at while a
    /// night is skipped, as (time to morning) / 12, so every sleep takes
    /// twelve seconds of real time on the server. The same distance is
    /// covered in SleepSkipSeconds instead.
    ///
    /// Only the server runs the skip (EnvMan.UpdateTimeSkip), and clients
    /// follow its clock, so this changes nothing on a client.
    /// </summary>
    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SkipToMorning))]
    internal static class FastSleepSkipPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(EnvMan), nameof(EnvMan.SkipToMorning), FastSleepFeature.FeatureName);

        private static void Postfix(EnvMan __instance)
        {
            if (FastSleepFeature.Instance?.IsActive != true) return;

            try
            {
                double seconds = WorldConfig.SleepSkipSeconds?.Value ?? 2f;
                if (seconds <= 0d) seconds = 0.1d;

                double remaining = __instance.m_skipToTime - ZNet.instance.GetTimeSeconds();
                if (remaining <= 0d) return;

                __instance.m_timeSkipSpeed = remaining / seconds;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"FastSleep: could not shorten the night skip: {ex}");
            }
        }
    }

    /// <summary>
    /// Hud.GetFadeDuration returns Game.m_fadeTimeSleep (three seconds) while
    /// the player is sleeping, for both the fade to black and the fade back.
    /// A shorter one is used instead. Death and everything else keep
    /// vanilla's timing.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "GetFadeDuration")]
    internal static class FastSleepFadePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Hud), "GetFadeDuration", FastSleepFeature.FeatureName);

        private static void Postfix(Player player, ref float __result)
        {
            if (FastSleepFeature.Instance?.IsActive != true) return;
            if (player == null || player.IsDead() || !player.IsSleeping()) return;

            float fade = WorldConfig.SleepFadeSeconds?.Value ?? 0.5f;
            __result = fade > 0f ? fade : 0.01f;
        }
    }
}
