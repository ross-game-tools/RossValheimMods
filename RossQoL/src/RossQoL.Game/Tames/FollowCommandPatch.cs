using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// Tameable.Interact on a tame calls Command (follow/stay) only when the
    /// prefab's m_commandable is set; otherwise it pets the creature. For the
    /// length of the call the flag is set on tames that have a MonsterAI (the
    /// component vanilla's follow uses), then restored, so switching the
    /// feature off takes effect on the next use and no prefab is changed.
    ///
    /// Following persists through vanilla's own saved follow target, which
    /// does not look at m_commandable.
    /// </summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
    internal static class FollowCommandPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Tameable), nameof(Tameable.Interact), FollowCommandFeature.FeatureName);

        private static void Prefix(Tameable __instance, ref bool __state)
        {
            __state = false;
            if (FollowCommandFeature.Instance?.IsActive != true) return;

            try
            {
                if (__instance.m_commandable || __instance.m_monsterAI == null || !__instance.IsTamed()) return;

                __instance.m_commandable = true;
                __state = true;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"FollowCommand: preparing a tame's command failed: {ex}");
            }
        }

        private static void Finalizer(Tameable __instance, bool __state)
        {
            if (__state && __instance) __instance.m_commandable = false;
        }
    }
}
