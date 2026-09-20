using HarmonyLib;
using RossPortals.Game.Framework;
using RossPortals.Game.Portals;

namespace RossPortals.Game.Patches
{
    /// <summary>
    /// Replaces the server-side portal pairing pass. Vanilla re-pairs by tag;
    /// we instead rebuild each portal's live connection from its stored
    /// destination (remapping ids that changed since the last session), which is
    /// also where a save previously managed by XPortal gets imported onto our
    /// keys. See <see cref="PortalZdo.RestoreConnections"/>.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ConnectPortals))]
    internal static class ZDOMan_ConnectPortals
    {
        private static bool Prepare() => ValheimCompat.RequireMethod(typeof(ZDOMan), nameof(ZDOMan.ConnectPortals), "portal destinations");

        private static bool Prefix()
        {
            PortalZdo.RestoreConnections();
            return false; // never run vanilla's tag pairing
        }
    }
}
