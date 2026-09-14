using System.Collections.Generic;

namespace RossQoL.Core.Framework
{
    /// <summary>
    /// Whose config value a feature obeys.
    /// Client: the player's own. Synced: a connected server's, via Jotunn's
    /// admin-only config sync, because it changes shared-world rules.
    /// </summary>
    public enum FeatureScope
    {
        Client,
        Synced,
    }

    /// <summary>
    /// The rules deciding when a feature runs and when it is patched, kept
    /// here so they are tested without a game.
    /// </summary>
    public static class FeatureRules
    {
        public static bool IsActive(bool categoryEnabled, bool featureEnabled) =>
            categoryEnabled && featureEnabled;

        /// <summary>
        /// A category's master switch takes the strictest scope of its
        /// features. If it stayed personal while a feature under it was
        /// server-controlled, a client could turn that feature off by turning
        /// off its section.
        /// </summary>
        public static FeatureScope CategoryScope(IEnumerable<FeatureScope> featureScopes)
        {
            if (featureScopes == null) return FeatureScope.Client;

            foreach (var scope in featureScopes)
                if (scope == FeatureScope.Synced) return FeatureScope.Synced;

            return FeatureScope.Client;
        }

        /// <summary>
        /// Client features are patched only when on at startup, so a disabled
        /// one touches nothing and cannot conflict with another mod. Synced
        /// features are always patched and check their state on every call,
        /// because a server can switch them on after the client has loaded.
        /// Missing Valheim members override both: patching against a changed
        /// game is how a mod breaks in confusing ways.
        /// </summary>
        public static bool ShouldPatch(FeatureScope scope, bool activeAtStartup, bool requiredMembersPresent)
        {
            if (!requiredMembersPresent) return false;
            return scope == FeatureScope.Synced || activeAtStartup;
        }

        public static string Describe(string text, FeatureScope scope, bool requiresRestart)
        {
            string owner = scope == FeatureScope.Synced
                ? " Server-controlled when connected."
                : " Personal setting.";

            return text + owner + (requiresRestart ? " Requires restart." : "");
        }
    }
}
