using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RossPortalTames.Game
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class PortalTamesPlugin : BaseUnityPlugin
    {
        // Written into BepInEx's config filename, therefore permanent:
        // changing it silently resets everyone's settings to defaults.
        public const string PluginGuid = "com.rossdwest.portaltames";
        public const string PluginName = "RossPortalTames";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);

            // Before any patching, so that if a Valheim update has moved
            // something out from under us the log leads with WHICH member
            // rather than with whatever secondary symptom surfaces first.
            ValheimCompat.Verify();

            // Each [HarmonyPatch] class is applied individually, in its own
            // try/catch. Harmony's PatchAll iterates every patch class in one
            // loop with no per-class handling: one class throwing can abort
            // that loop before later classes are reached, leaving them
            // unpatched with no error anyone would connect to the cause.
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

            // One manager object for the whole mod, carrying the only Update.
            var host = new GameObject(PluginName + "Manager");
            host.AddComponent<PortalTamesManager>();
            DontDestroyOnLoad(host);
            host.transform.SetParent(gameObject.transform);

            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
