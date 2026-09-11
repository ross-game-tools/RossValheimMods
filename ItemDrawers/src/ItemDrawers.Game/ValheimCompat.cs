using System.Collections.Generic;
using HarmonyLib;

namespace ItemDrawers.Game
{
    /// <summary>
    /// One startup check over every Valheim member this mod reaches for by
    /// NAME rather than by compiler-checked reference.
    ///
    /// Those two categories fail very differently, and only one of them is
    /// this class's problem. A publicized field read (Piece.m_usage,
    /// ZNetView.m_persistent) is bound at compile time: if Valheim removes
    /// it, the plugin fails to load with a MissingFieldException naming the
    /// member, which is loud and self-diagnosing. A name-based lookup --
    /// AccessTools.FieldRefAccess with a string, or a Harmony patch whose
    /// target method has been renamed -- fails quietly instead: the patch is
    /// skipped or the lookup throws deep inside a component's Awake, long
    /// after anyone could connect it to a game update.
    ///
    /// That quiet mode is not hypothetical. A Valheim update mid-development
    /// broke a different mod in this profile in exactly this way, and the
    /// only reason it was diagnosed quickly is that its symptoms happened to
    /// be dramatic. This check exists so that when it is this mod's turn,
    /// the log says which member disappeared, on the first line, before
    /// anything else goes wrong.
    /// </summary>
    internal static class ValheimCompat
    {
        /// <summary>
        /// Reported by Verify so the failure is visible once, up front,
        /// rather than as a cascade of secondary symptoms.
        /// </summary>
        private static readonly (string Type, string Member, string Why)[] Required =
        {
            ("Container", "m_nview",
                "DrawerComponent.Awake assigns it directly so OttoFuel/NVLB's Container.Awake postfixes see a live view"),
            ("Container", "Awake",
                "patched with a skip-prefix so vanilla's body never runs on a drawer"),
            ("Container", "GetInventory",
                "patched to substitute the drawer's 1x1 mirror inventory"),
            ("Container", "Save", "patched so vanilla never writes its own inventory over a drawer's ZDO"),
            ("Container", "Load", "patched so vanilla never reads an inventory a drawer does not have"),
            ("Container", "CanBeRemoved", "patched so a drawer holding items can still be removed"),
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

                // Field or method: the check does not care which, only that
                // the name still resolves to something.
                if (AccessTools.Field(t, member) == null && AccessTools.Method(t, member) == null)
                {
                    missing.Add($"{type}.{member} -- {why}");
                }
            }

            if (missing.Count == 0)
            {
                DrawerPlugin.Log.LogInfo(
                    $"Valheim compatibility check passed ({Required.Length} members).");
                return;
            }

            // Error, not warning: every entry above is load-bearing. A drawer
            // with any of these missing is not a degraded drawer, it is a
            // Container subclass with vanilla behaviour half-applied, which
            // is how items get eaten.
            DrawerPlugin.Log.LogError(
                "Valheim compatibility check FAILED -- this game version has changed members this mod "
                + "depends on, and drawers will not work correctly. This is almost certainly a Valheim "
                + "update, not a conflict with another mod. Missing:");
            foreach (var m in missing) DrawerPlugin.Log.LogError($"    {m}");
        }

        /// <summary>
        /// Harmony Prepare helper: same "does this still exist" test, but
        /// reported per patch.
        ///
        /// A Prepare that just returns `AccessTools.Method(...) != null`
        /// skips the patch in total silence, and for these patches a skipped
        /// one is WORSE than a failed one -- skipping the Container.Awake
        /// prefix, for instance, lets vanilla's Awake body run on a drawer,
        /// building an inventory the drawer does not use and registering
        /// RPCs it does not expect. Better to say so.
        /// </summary>
        public static bool RequireMethod(System.Type type, string method)
        {
            if (AccessTools.Method(type, method) != null) return true;

            DrawerPlugin.Log.LogError(
                $"{type.Name}.{method} not found -- skipping that patch. Drawers will misbehave. "
                + "See the compatibility check above.");
            return false;
        }
    }
}
