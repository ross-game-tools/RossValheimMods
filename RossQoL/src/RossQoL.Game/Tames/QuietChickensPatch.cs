using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// A bird's noise reaches this client two different ways: the idle sounds
    /// are spawned effects, created by whichever client owns the bird and
    /// handed to everyone else to play, while footsteps and hits play from a
    /// ZSFX on the bird itself. Silencing the timer that spawns the first kind
    /// would leave every bird another player owns as loud as before.
    ///
    /// Both kinds pass through ZSFX.Play on the machine that hears them, so
    /// that is where this stops them, and ownership never enters into it. The
    /// game names its chicken audio prefabs "sfx_chicken_..." (and
    /// "sfx_chick_hurt"), which is the whole family and nothing else.
    ///
    /// The looping sounds start once when a bird loads, so switching the
    /// feature off returns the noise for birds that load afterwards rather
    /// than for the ones already standing around you.
    /// </summary>
    [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
    internal static class QuietChickensPatch
    {
        /// <summary>What the game names every chicken and chick audio prefab.</summary>
        private const string ChickenPrefabs = "sfx_chick";

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ZSFX), nameof(ZSFX.Play), QuietChickensFeature.FeatureName);

        private static bool Prefix(ZSFX __instance)
        {
            if (QuietChickensFeature.Instance?.IsActive != true) return true;

            try
            {
                if (__instance == null) return true;

                return !__instance.gameObject.name.StartsWith(ChickenPrefabs, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"QuietChickens: could not check a sound, leaving it alone: {ex}");
                return true;
            }
        }
    }
}
