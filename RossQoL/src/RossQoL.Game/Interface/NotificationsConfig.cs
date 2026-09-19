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
                    "Show how much progress a skill gained towards its next level, e.g. \"Woodcutting +11%\". "
                    + "Off keeps item pickups and level-ups and drops only these lines.",
                    scope, requiresRestart: false));
        }
    }
}
