using HarmonyLib;
using RossPortals.Game.Portals;

namespace RossPortals.Game.Patches
{
    /// <summary>
    /// Detect a freshly-placed portal. We hook <c>Piece.SetCreator</c> (called
    /// during <c>Player.PlacePiece</c>) rather than <c>WearNTear.OnPlaced</c>:
    /// OnPlaced is tiny and gets inlined, and a patch on it silently stops
    /// working once anything patches PlacePiece (a known HarmonyX interaction).
    /// SetCreator is a safe target and fires at placement.
    /// </summary>
    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class Piece_SetCreator
    {
        private static void Postfix(Piece __instance)
        {
            if (__instance == null || __instance.m_name == null || !__instance.m_name.Contains("$piece_portal"))
                return;

            var nview = __instance.m_nview;
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null) return;

            PortalManager.OnPortalPlaced(zdo.m_uid, zdo.GetPosition());
        }
    }
}
