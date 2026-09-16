using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Interface
{
    /// <summary>
    /// Hold a key to look around without turning your character. Releasing it
    /// snaps the camera back behind you.
    ///
    /// Client scope: it only moves this player's camera; nothing about the
    /// character, aim or the world changes.
    /// </summary>
    internal sealed class PanCameraFeature : Feature
    {
        public const string FeatureName = "Interface/PanCamera";

        public static PanCameraFeature Instance { get; private set; }

        public PanCameraFeature() => Instance = this;

        public override string Key => "PanCamera";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Hold PanKey (left Alt by default) to look around with the mouse without turning your character. "
            + "Releasing it snaps the camera back.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(PanCameraInputPatch),
            typeof(PanCameraPositionPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("PlayerController", "LateUpdate", "where the mouse turns the character"),
            new CompatMember("PlayerController", "m_character", "telling the character the mouse did not move"),
            new CompatMember("PlayerController", "m_mouseSens", "matching your mouse sensitivity while panning"),
            new CompatMember("GameCamera", "GetCameraPosition", "where the camera is placed"),
            new CompatMember("GameCamera", "GetOffsetedEyePos", "where the camera orbits"),
            new CompatMember("GameCamera", "CollideRay2", "keeping the panned camera out of walls"),
            new CompatMember("GameCamera", "m_distance", "how far the camera sits from you"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            PanCameraConfig.Bind(config, section, Scope);
    }
}
