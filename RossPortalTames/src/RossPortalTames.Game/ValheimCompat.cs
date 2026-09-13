using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RossPortalTames.Game
{
    /// <summary>
    /// One startup check over the Valheim members this mod reaches for by NAME
    /// rather than by compiler-checked reference.
    ///
    /// Those fail differently. A publicized field read is bound at compile
    /// time: if Valheim removes it, the plugin fails to load with the member's
    /// name in the message, which is self-diagnosing. A Harmony patch whose
    /// target method has been renamed fails QUIETLY -- the patch is skipped and
    /// the mod simply stops working, with nothing pointing at a game update as
    /// the cause. This exists so the log says which member disappeared, on the
    /// first line, before anything else goes wrong.
    /// </summary>
    internal static class ValheimCompat
    {
        private static readonly (string Type, string Member, string Why)[] Required =
        {
            ("TeleportWorld", "Teleport",
                "patched to notice when you use a portal, which is the only trigger this mod has"),
        };

        public static void Verify()
        {
            var missing = new List<string>();

            foreach (var (type, member, why) in Required)
            {
                var t = AccessTools.TypeByName(type);
                if (t == null)
                {
                    missing.Add($"{type} (whole type) -- {why}");
                    continue;
                }

                if (AccessTools.Field(t, member) == null && AccessTools.Method(t, member) == null)
                    missing.Add($"{type}.{member} -- {why}");
            }

            if (missing.Count == 0)
            {
                PortalTamesPlugin.Log.LogInfo($"Valheim compatibility check passed ({Required.Length} members).");
                return;
            }

            PortalTamesPlugin.Log.LogError(
                "Valheim compatibility check FAILED -- this game version has changed members this mod "
                + "depends on, and tames will not follow you through portals. This is almost certainly a "
                + "Valheim update, not a conflict with another mod. Missing:");
            foreach (var m in missing) PortalTamesPlugin.Log.LogError($"    {m}");
        }

        /// <summary>
        /// Harmony Prepare helper. A Prepare that just returns
        /// `AccessTools.Method(...) != null` skips its patch in total silence,
        /// which for this mod means "nothing happens and nobody knows why".
        /// </summary>
        public static bool RequireMethod(Type type, string method)
        {
            if (AccessTools.Method(type, method) != null) return true;

            PortalTamesPlugin.Log.LogError(
                $"{type.Name}.{method} not found -- skipping that patch. Tames will not follow you "
                + "through portals. See the compatibility check above.");
            return false;
        }
    }
}
