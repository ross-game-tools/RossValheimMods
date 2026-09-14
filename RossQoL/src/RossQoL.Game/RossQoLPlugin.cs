using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Synced features only mean anything if every peer runs the same rules.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class RossQoLPlugin : BaseUnityPlugin
    {
        // Written into BepInEx's config filename, therefore permanent:
        // changing it silently resets everyone's settings to defaults.
        public const string PluginGuid = "com.rossdwest.rossqol";
        public const string PluginName = "RossQoL";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);

            var categories = FeatureRegistry.Create();
            foreach (var category in categories) category.Bind(Config);

            // One host object for the whole mod; features add components to it.
            var host = new GameObject(PluginName + "Manager");
            DontDestroyOnLoad(host);
            host.transform.SetParent(gameObject.transform);

            foreach (var category in categories)
                foreach (var feature in category.Features)
                    FeatureActivator.Activate(feature, _harmony, host);

            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
