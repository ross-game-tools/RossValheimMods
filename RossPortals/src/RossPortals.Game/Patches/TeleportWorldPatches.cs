using HarmonyLib;
using RossPortals.Game.Portals;

namespace RossPortals.Game.Patches
{
    /// <summary>
    /// Replace the portal's hover text with ours (name + chosen destination +
    /// "configure"). Returning false skips vanilla's tag/connection hover
    /// entirely.
    /// </summary>
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
    internal static class TeleportWorld_GetHoverText
    {
        private static bool Prefix(TeleportWorld __instance, ref string __result)
        {
            var nview = __instance.m_nview;
            if (Env.ShuttingDown || nview == null || nview.GetZDO() == null)
            {
                // Something's mid-teardown or malformed; say nothing rather than
                // throw inside the hover loop.
                __result = string.Empty;
                return false;
            }

            var zdo = nview.GetZDO();
            __result = PortalManager.BuildHoverText(zdo.m_uid, zdo.GetPosition());
            return false;
        }
    }

    /// <summary>
    /// Note the destination a player actually travelled to, for the Recent sort.
    /// Vanilla teleport is otherwise untouched — it reads the connection we set
    /// and does the jump itself.
    /// </summary>
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
    internal static class TeleportWorld_Teleport
    {
        private static void Postfix(TeleportWorld __instance)
        {
            var nview = __instance.m_nview;
            var zdo = nview != null ? nview.GetZDO() : null;
            if (zdo == null) return;

            var target = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
            PortalManager.RecordUsed(target);
        }
    }
}
