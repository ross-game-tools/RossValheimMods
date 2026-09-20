using HarmonyLib;
using RossPortals.Game.Portals;

namespace RossPortals.Game.Patches
{
    /// <summary>
    /// A portal's Interact calls <c>TextInput.RequestText</c> to pop the vanilla
    /// "set tag" box. We intercept that one call — when the requester is a
    /// portal — and open our own configuration panel instead. Any other caller
    /// (signs, etc.) is left alone.
    /// </summary>
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.RequestText))]
    internal static class TextInput_RequestText
    {
        private static bool Prefix(TextReceiver sign)
        {
            if (sign is TeleportWorld teleportWorld)
            {
                var nview = teleportWorld.m_nview;
                var zdo = nview != null ? nview.GetZDO() : null;
                if (zdo != null) PortalManager.OnPortalInteract(zdo.m_uid);
                return false; // suppress the vanilla tag dialog
            }
            return true;
        }
    }
}
