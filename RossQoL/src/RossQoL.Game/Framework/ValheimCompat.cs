using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RossQoL.Game.Framework
{
    /// <summary>
    /// Checks the Valheim members each feature reaches for by NAME rather than
    /// by compiler-checked reference.
    ///
    /// Those fail differently. A publicized member read is bound at compile
    /// time: if Valheim removes it, the plugin fails to load with the member's
    /// name in the message. A Harmony patch whose target has been renamed
    /// fails QUIETLY -- the patch is skipped and the feature simply stops
    /// working. This exists so the log names the feature and the member first.
    /// </summary>
    internal static class ValheimCompat
    {
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

        /// <summary>
        /// Plain reflection rather than AccessTools: AccessTools writes a
        /// warning to the console every time a lookup misses, and this check
        /// misses on purpose -- it asks "field? property? event? method?" in
        /// turn, so every method it confirms cost two warnings. Reflection
        /// returns null quietly.
        ///
        /// Walks the base types itself, which is the part AccessTools was
        /// doing for us: private members are not inherited by GetField.
        /// </summary>
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

        /// <summary>
        /// Harmony Prepare helper. A Prepare that just returns
        /// `AccessTools.Method(...) != null` skips its patch in total silence.
        /// </summary>
        public static bool RequireMethod(Type type, string method, string featureName)
        {
            if (AccessTools.Method(type, method) != null) return true;

            RossQoLPlugin.Log.LogError(
                $"{type.Name}.{method} not found -- skipping that patch; {featureName} will not work. "
                + "See the compatibility check above.");
            return false;
        }
    }
}
