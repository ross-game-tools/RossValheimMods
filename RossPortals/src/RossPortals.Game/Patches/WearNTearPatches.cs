using HarmonyLib;
using RossPortals.Game.Portals;

namespace RossPortals.Game.Patches
{
    /// <summary>
    /// Detect a portal being removed (hammer or damage) so it drops out of the
    /// list and anything that targeted it is cleared. <c>WearNTear.Destroy</c>
    /// is the vanilla teardown entry point and runs before the ZDO is gone, so
    /// the id is still readable in the prefix.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Destroy))]
    internal static class WearNTear_Destroy
    {
        private static void Prefix(WearNTear __instance)
        {
            var piece = __instance != null ? __instance.m_piece : null;
            if (piece == null || piece.m_name == null) return;
            if (!piece.m_name.Contains("$piece_portal") || !piece.CanBeRemoved()) return;

            var nview = piece.m_nview;
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null) return;

            PortalManager.OnPortalDestroyed(zdo.m_uid);
        }
    }
}
