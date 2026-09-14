using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RossQoL.Game.Portals;
using UnityEngine;

namespace RossQoL.Game
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
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

            ValheimCompat.Verify();

            try
            {
                foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0) continue;

                    try
                    {
                        _harmony.CreateClassProcessor(type).Patch();
                    }
                    catch (Exception ex)
                    {
                        Log.LogError($"Patch class {type.Name} failed and was skipped: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Patching aborted: {ex}");
            }

            PortalTamesConfig.Bind(Config);

            var host = new GameObject(PluginName + "Manager");
            host.AddComponent<PortalTamesManager>();
            DontDestroyOnLoad(host);
            host.transform.SetParent(gameObject.transform);

            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
