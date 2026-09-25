using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// MultiplePowers' keys. Slot 1 is vanilla's own selected power, assigned by
    /// using a guardian stone normally and fired by vanilla's own key -- neither
    /// is ours. These cover the second slot: the key that fires it, and the
    /// modifier held while using a stone to send that power to the second slot
    /// instead of the first. Bound Client so each player picks their own keys
    /// and the server does not sync them; read at use, so live reloads apply
    /// without a restart.
    /// </summary>
    public static class MultiplePowersConfig
    {
        public static ConfigEntry<KeyCode> SecondPowerKey { get; private set; }
        public static ConfigEntry<KeyCode> AssignSecondPowerModifier { get; private set; }

        internal static void Bind(ConfigFile config, string section)
        {
            SecondPowerKey = config.Bind(section, "SecondPowerKey", KeyCode.H,
                ConfigText.Description(
                    "Key that fires your second guardian power. Your first power still fires on "
                    + "the vanilla guardian-power key. None leaves the second power unbound. "
                    + "Your own setting, not the server's.",
                    FeatureScope.Client, requiresRestart: false));

            AssignSecondPowerModifier = config.Bind(section, "AssignSecondPowerModifier", KeyCode.LeftShift,
                ConfigText.Description(
                    "Hold this while using a guardian stone to set that power as your SECOND power "
                    + "instead of your first. Using a stone normally still sets your first power. "
                    + "None disables the second slot's assignment. Your own setting, not the server's.",
                    FeatureScope.Client, requiresRestart: false));
        }
    }
}
