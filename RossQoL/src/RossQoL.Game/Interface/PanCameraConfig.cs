using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Interface
{
    /// <summary>PanCamera's settings beyond its toggle. Read while panning, so live reloads apply.</summary>
    public static class PanCameraConfig
    {
        public static ConfigEntry<KeyboardShortcut> PanKey;
        public static ConfigEntry<float> PanMaxPitch;

        internal static void Bind(ConfigFile config, string section, FeatureScope scope)
        {
            PanKey = config.Bind(section, "PanKey", new KeyboardShortcut(KeyCode.LeftAlt),
                ConfigText.Description(
                    "Hold this key to look around without turning your character.",
                    scope, requiresRestart: false));

            PanMaxPitch = config.Bind(section, "PanMaxPitch", 70f,
                ConfigText.Description(
                    "How far up or down, in degrees, panning can look.",
                    scope, requiresRestart: false, range: new AcceptableValueRange<float>(10f, 89f)));
        }
    }
}
