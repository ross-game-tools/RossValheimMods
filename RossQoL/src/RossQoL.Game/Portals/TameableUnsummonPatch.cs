using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// Holds vanilla's unsummon rules off a creature for exactly as long as it
    /// is being carried through a portal. See <see cref="SummonUnsummonGuard"/>
    /// for the full mechanism and what the exemption risks.
    ///
    /// Patched on <c>Tameable.Update</c> rather than on <c>UpdateSummon</c> or
    /// <c>UnSummon</c>, which would each be a narrower and more obvious target.
    /// Both of those are tiny private methods and so are candidates for Mono's
    /// inliner, which silently bypasses a Harmony patch -- this repository has
    /// been bitten by exactly that before. <c>Update</c> is invoked by the
    /// Unity runtime rather than from managed code, so it is never inlined
    /// away, and suppressing it suppresses <c>UpdateSummon</c> with it.
    /// </summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Update))]
    internal static class TameableUnsummonPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Tameable), nameof(Tameable.Update), "Portals/TamesFollow");

        private static bool Prefix(Tameable __instance)
        {
            // This runs once per frame per tame in the scene. An exception
            // escaping would stop every tame in the world from updating, so it
            // falls through to vanilla on any trouble.
            try
            {
                var view = __instance.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) return true;

                var zdo = view.GetZDO();
                if (zdo == null) return true;

                return !SummonUnsummonGuard.IsGuarded(zdo.m_uid);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Portals/TamesFollow: unsummon guard skipped: {ex}");
                return true;
            }
        }
    }
}
