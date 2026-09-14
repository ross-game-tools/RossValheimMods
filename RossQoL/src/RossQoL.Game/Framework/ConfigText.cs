using BepInEx.Configuration;
using RossQoL.Core.Framework;

namespace RossQoL.Game.Framework
{
    internal static class ConfigText
    {
        /// <summary>
        /// Every entry says whose value it is. Synced entries are admin-only,
        /// which is what makes Jotunn push the server's value to clients.
        /// </summary>
        public static ConfigDescription Description(string text, FeatureScope scope, bool requiresRestart) =>
            new ConfigDescription(
                FeatureRules.Describe(text, scope, requiresRestart),
                null,
                new ConfigurationManagerAttributes { IsAdminOnly = scope == FeatureScope.Synced });
    }
}
