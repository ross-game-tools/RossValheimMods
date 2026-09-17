using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// ParticleMist is the singleton that makes the Mistlands what they are:
    /// every tick it emits mist particles around the player wherever a Mister
    /// volume covers the ground, and the wisplight's Demister carves holes in
    /// what it emits.
    ///
    /// With the Queen dead the prefix skips that update, so nothing new is
    /// emitted, and clears the particles already in the air once. The Mister
    /// volumes are untouched, so nothing about the world changes: switch the
    /// feature off, or load a world where she still lives, and the next tick
    /// fills the air again.
    ///
    /// Not MistEmitter, which is a different thing entirely -- EnvMan enables
    /// one on certain environment prefabs -- and never runs in the Mistlands.
    /// </summary>
    [HarmonyPatch(typeof(ParticleMist), "Update")]
    internal static class ClearMistPatch
    {
        /// <summary>Whether the particles have been cleared for the current state.</summary>
        private static bool _cleared;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ParticleMist), "Update", ClearMistFeature.FeatureName);

        private static bool Prefix(ParticleMist __instance)
        {
            try
            {
                if (!ShouldClear())
                {
                    _cleared = false;
                    return true;
                }

                if (!_cleared)
                {
                    if (__instance.m_ps != null) __instance.m_ps.Clear();
                    _cleared = true;
                    RossQoLPlugin.Log.LogInfo("ClearMist: the Queen is dead; the mist is cleared.");
                }

                return false;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"ClearMist: leaving the mist to vanilla: {ex}");
                return true;
            }
        }

        internal static bool ShouldClear()
        {
            if (ClearMistFeature.Instance?.IsActive != true) return false;

            var zones = ZoneSystem.instance;
            return zones != null && zones.GetGlobalKey(ClearMistFeature.QueenKey);
        }
    }

    /// <summary>
    /// EnvMan.SetEnv runs when the weather changes, which is the natural
    /// moment to decide whether the Mistlands mist should exist at all.
    /// Switching the whole ParticleMist object off costs nothing per frame --
    /// its Update never runs -- and switching it back on is what restores the
    /// mist if the feature is turned off or the key is removed.
    /// </summary>
    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv))]
    internal static class ClearMistEnvPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(EnvMan), nameof(EnvMan.SetEnv), ClearMistFeature.FeatureName);

        private static void Postfix()
        {
            // An exception escaping here would break the weather.
            try
            {
                var mist = ParticleMist.instance;
                if (mist == null) return;

                bool wanted = !ClearMistPatch.ShouldClear();
                if (mist.gameObject.activeSelf != wanted) mist.gameObject.SetActive(wanted);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"ClearMist: leaving the mist object alone: {ex}");
            }
        }
    }
}
