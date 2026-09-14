using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Startup
{
    /// <summary>
    /// Only the logo fades are skipped: SceneLoader still waits for save data
    /// and the scene load exactly as before.
    /// </summary>
    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.Awake))]
    internal static class SceneLoaderLogosPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(SceneLoader), nameof(SceneLoader.Awake), SkipSplashFeature.FeatureName);

        private static void Postfix(SceneLoader __instance)
        {
            if (SkipSplashFeature.Instance?.IsActive != true) return;
            __instance._showLogos = false;
            RossQoLPlugin.Log.LogInfo("SkipSplash: launch logos skipped.");
        }
    }

    /// <summary>
    /// Turns the menu intro video off before FejdStartup.Start starts its
    /// coroutine. Blocking PlayIntroCinematic instead would also suppress the
    /// menu's FadeIn trigger.
    /// </summary>
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
    internal static class MenuIntroVideoPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(FejdStartup), nameof(FejdStartup.Start), SkipSplashFeature.FeatureName);

        private static void Prefix()
        {
            if (SkipSplashFeature.Instance?.IsActive != true) return;

            var cinematics = CinematicsManager.s_instance;
            if (cinematics == null || !cinematics.m_introOnStartup) return;

            cinematics.m_introOnStartup = false;
            RossQoLPlugin.Log.LogInfo("SkipSplash: menu intro video skipped.");
        }
    }
}
