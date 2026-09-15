using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using RossQoL.Core.Framework;
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
        public const string PluginVersion = "0.4.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;
        private ConfigEntry<bool> _hotReloadEnabled;
        private ConfigHotReload _hotReload;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);

            _hotReloadEnabled = Config.Bind("General", "HotReload", true,
                ConfigText.Description(
                    "Apply edits to this file without a restart. On a server, any server-controlled "
                    + "settings changed this way are pushed to connected players. While connected to a "
                    + "server, local edits to server-controlled settings are ignored.",
                    FeatureScope.Client, requiresRestart: false));

            var categories = FeatureRegistry.Create();
            foreach (var category in categories) category.Bind(Config);

            // One host object for the whole mod; features add components to it.
            var host = new GameObject(PluginName + "Manager");
            DontDestroyOnLoad(host);
            host.transform.SetParent(gameObject.transform);

            foreach (var category in categories)
                foreach (var feature in category.Features)
                    FeatureActivator.Activate(feature, _harmony, host);

            // A failed watcher costs live reloading, never the plugin.
            try
            {
                _hotReload = new ConfigHotReload(Config, Log);
            }
            catch (Exception ex)
            {
                Log.LogError($"Config reload unavailable; edits apply after a restart: {ex}");
            }

            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void Update()
        {
            if (_hotReloadEnabled.Value) _hotReload?.Pump();
        }

        private void OnDestroy()
        {
            _hotReload?.Dispose();
            _harmony?.UnpatchSelf();
        }
    }
}
