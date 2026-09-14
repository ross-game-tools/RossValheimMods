using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Startup
{
    internal sealed class ContinueButtonFeature : Feature
    {
        public const string FeatureName = "Startup/ContinueButton";

        public static ContinueButtonFeature Instance { get; private set; }

        public ContinueButtonFeature() => Instance = this;

        public override string Key => "ContinueButton";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Adds a Continue button to the main menu that resumes your last world or server "
            + "with the character you used. Local worlds resume private; server passwords are never stored.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(JoinServerRecordingPatch),
            typeof(WorldStartRecordingPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("FejdStartup", "JoinServer", "recording which server you joined"),
            new CompatMember("FejdStartup", "GetServerToJoin", "recording which server you joined"),
            new CompatMember("FejdStartup", "OnWorldStart", "recording and resuming local worlds"),
            new CompatMember("Game", "m_playerInitialSpawn", "knowing a session actually started"),
        };

        public override void OnActivated(GameObject host) => SessionRecorder.Subscribe();
    }
}
