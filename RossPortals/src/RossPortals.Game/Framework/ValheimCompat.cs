using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace RossPortals.Game.Framework
{
    /// <summary>
    /// Checks the Valheim members this mod reaches for BY NAME rather than by
    /// compiler-checked reference — the ones a game update can move out from
    /// under a Harmony patch or a reflective read without any build error.
    ///
    /// A publicized member read fails at load with the member's name; a Harmony
    /// patch whose target was renamed fails QUIETLY (the patch is skipped and
    /// the feature just stops working). This exists so a game update produces
    /// one clear log line naming exactly what went missing, before any patch
    /// tries and fails.
    /// </summary>
    internal static class ValheimCompat
    {
        // Everything the portal engine touches by name. Kept together so the
        // startup log is a single, readable audit of our coupling to the game.
        private static readonly CompatMember[] Members =
        {
            new CompatMember("TeleportWorld", "GetHoverText", "replace portal hover text"),
            new CompatMember("TeleportWorld", "Teleport", "note recently-used destinations"),
            new CompatMember("TeleportWorld", "m_nview", "reach a portal's ZDO"),
            new CompatMember("TextInput", "RequestText", "open our panel instead of the tag box"),
            new CompatMember("ZDOMan", "GetPortalList", "enumerate every portal (server)"),
            new CompatMember("ZDOMan", "ConnectPortals", "rebuild connections from stored destinations"),
            new CompatMember("ZDOMan", "GetZDO", "resolve a portal by id"),
            new CompatMember("ZDOMan", "GetSessionID", "own a portal ZDO before writing it"),
            new CompatMember("ZDOMan", "ForceSendZDO", "push a just-placed portal to the server"),
            new CompatMember("ZDO", "GetConnectionZDOID", "read a portal's live destination"),
            new CompatMember("ZDO", "SetConnection", "set the destination vanilla teleport reads"),
            new CompatMember("ZDO", "GetString", "read the portal tag/name"),
            new CompatMember("ZDO", "GetZDOID", "read stored destination/previous id"),
            new CompatMember("ZDO", "SetOwner", "own a portal ZDO before writing it"),
            new CompatMember("ZDO", "GetPosition", "a portal's world position"),
            new CompatMember("ZDOVars", "s_tag", "the vanilla portal-name ZDO key"),
            new CompatMember("ZRoutedRpc", "InvokeRoutedRPC", "sync the portal list"),
            new CompatMember("ZRoutedRpc", "Everybody", "broadcast to all peers"),
            new CompatMember("Piece", "SetCreator", "detect a placed portal"),
            new CompatMember("Piece", "CanBeRemoved", "confirm a portal is actually being removed"),
            new CompatMember("WearNTear", "Destroy", "detect a removed portal"),
            new CompatMember("Game", "ConnectPortals", "suppress vanilla tag pairing"),
            new CompatMember("Game", "ConnectPortalsCoroutine", "suppress vanilla tag pairing"),
        };

        /// <summary>Logs a single audit line, plus an error naming anything that
        /// is missing. Never throws — a missing member degrades the relevant
        /// patch (which also guards itself in Prepare), it doesn't stop load.</summary>
        public static void Verify()
        {
            var missing = FindMissing(Members);
            if (missing.Count == 0)
            {
                RossPortalsPlugin.Log.LogInfo($"Valheim compatibility check: all {Members.Length} referenced members present.");
                return;
            }

            RossPortalsPlugin.Log.LogError(
                $"Valheim compatibility check: {missing.Count} of {Members.Length} referenced members are MISSING. "
                + "The affected features are disabled. This usually means a Valheim update moved something:");
            foreach (var line in missing) RossPortalsPlugin.Log.LogError($"  - {line}");
        }

        public static List<string> FindMissing(IEnumerable<CompatMember> members)
        {
            var missing = new List<string>();
            if (members == null) return missing;

            foreach (var m in members)
            {
                var type = AccessTools.TypeByName(m.Type);
                if (type == null)
                {
                    missing.Add($"{m.Type} (whole type) -- {m.Why}");
                    continue;
                }

                if (!HasMember(type, m.Member))
                    missing.Add($"{m.Type}.{m.Member} -- {m.Why}");
            }

            return missing;
        }

        // Plain reflection rather than AccessTools: AccessTools logs a warning on
        // every miss, and this asks "field? property? event? method?" in turn, so
        // each confirmed method would cost warnings. Walks base types itself,
        // which is the part AccessTools was doing — private members aren't
        // inherited by GetField/GetMethod.
        private static bool HasMember(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.GetField(name, AccessTools.all) != null) return true;
                if (t.GetProperty(name, AccessTools.all) != null) return true;
                if (t.GetEvent(name, AccessTools.all) != null) return true;

                try
                {
                    if (t.GetMethod(name, AccessTools.all) != null) return true;
                }
                catch (AmbiguousMatchException)
                {
                    // Overloaded: it exists, which is all this check asks.
                    return true;
                }
            }

            return false;
        }

        /// <summary>Harmony Prepare helper. A Prepare that just returns
        /// <c>AccessTools.Method(...) != null</c> skips its patch in total
        /// silence; this logs first.</summary>
        public static bool RequireMethod(Type type, string method, string featureName)
        {
            if (HasMethod(type, method)) return true;

            RossPortalsPlugin.Log.LogError(
                $"{type.Name}.{method} not found -- skipping that patch; {featureName} will not work. "
                + "See the compatibility check above.");
            return false;
        }

        private static bool HasMethod(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
                if (t.GetMethods(AccessTools.all).Any(m => m.Name == name))
                    return true;

            return false;
        }
    }
}
