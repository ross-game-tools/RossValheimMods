using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// RecallSummons' settings beyond its toggle. Read live, so no restart.
    /// </summary>
    public static class RecallSummonsConfig
    {
        public static ConfigEntry<float> RecallCooldownSeconds;
        public static ConfigEntry<float> RecallCastSeconds;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            RecallCooldownSeconds = config.Bind(section, "RecallCooldownSeconds", 8f,
                ConfigText.Description(
                    "How many seconds must pass between uses of the recall attack. "
                    + "Set to 0 to allow it every time.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 60f)));

            RecallCastSeconds = config.Bind(section, "RecallCastSeconds", 0.5f,
                ConfigText.Description(
                    "How many seconds the recall takes to complete once the animation and sound start. "
                    + "Set to 0 for an instant recall with no cast time.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(0f, 3f)));
        }
    }
}
