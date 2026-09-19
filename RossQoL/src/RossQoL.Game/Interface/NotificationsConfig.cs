using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Interface
{
    /// <summary>Notifications' settings beyond its toggle. Read live, so no restart.</summary>
    public static class NotificationsConfig
    {
        public static ConfigEntry<bool> ShowSkillGain;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            ShowSkillGain = config.Bind(section, "NotificationsShowSkillGain", true,
                ConfigText.Description(
                    "Show what a skill gained and how far towards its next level that is, "
                    + "e.g. \"Woodcutting +12 (3%)\". "
                    + "Off keeps item pickups and level-ups and drops only these lines.",
                    scope, requiresRestart: false));
        }
    }
}
