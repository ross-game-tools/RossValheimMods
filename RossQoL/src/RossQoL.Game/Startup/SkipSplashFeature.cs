using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Startup
{
    internal sealed class SkipSplashFeature : Feature
    {
        public const string FeatureName = "Startup/SkipSplash";

        public static SkipSplashFeature Instance { get; private set; }

        public SkipSplashFeature() => Instance = this;

        public override string Key => "SkipSplash";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description => "Skips the logos at launch and the main menu intro video.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(SceneLoaderLogosPatch),
            typeof(MenuIntroVideoPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("SceneLoader", "Awake", "skipping the launch logos"),
            new CompatMember("SceneLoader", "_showLogos", "skipping the launch logos"),
            new CompatMember("FejdStartup", "Start", "skipping the menu intro video"),
            new CompatMember("CinematicsManager", "m_introOnStartup", "skipping the menu intro video"),
        };

        /// <summary>
        /// The boot scene may already have run SceneLoader.Awake by the time
        /// BepInEx loads plugins, in which case the Awake patch never fires.
        /// The logo coroutine starts later, from Start, so an existing loader
        /// can still be told not to show them.
        /// </summary>
        public override void OnActivated(GameObject host)
        {
            foreach (var loader in UnityEngine.Object.FindObjectsByType<SceneLoader>(FindObjectsSortMode.None))
            {
                loader._showLogos = false;
                RossQoLPlugin.Log.LogInfo("SkipSplash: launch logo skip requested (loader already awake).");
            }
        }
    }
}
