using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using RossPortals.Game.Framework;
using RossPortals.Game.Portals;
using RossPortals.Game.UI;
using UnityEngine;

namespace RossPortals.Game
{
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // We replace XPortal outright — both patch the same portal hover/interact
    // path, so running them together would be two mods fighting over one UI.
    [BepInIncompatibility(ModInfo.XPortalGuid)]
    // The portal list, its RPC protocol and the ZDO destination format only
    // agree when every peer runs the same rules.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class RossPortalsPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(ModInfo.Guid);

            // Before any patching, so a Valheim update that moved something
            // leads the log with WHICH member rather than a downstream symptom.
            ValheimCompat.Verify();

            PatchEachClassIndividually();

            PortalConfig.Bind(Config);

            // One host object for the whole mod; the scheduler lives on it.
            var host = new GameObject(ModInfo.Name + "Manager");
            DontDestroyOnLoad(host);
            host.transform.SetParent(gameObject.transform);
            host.AddComponent<Scheduler>();

            // The panel is client-only. Construct it now (it registers itself as
            // Instance) but it builds its GUI lazily on first open, once
            // GUIManager is ready.
            if (!GUIManager.IsHeadless())
                _ = new PortalConfigPanel();

            // Earliest client-side point at which the world (and its portals)
            // exists; never fires on a dedicated server, which instead answers
            // sync requests. Ask the server for the portal list here.
            MinimapManager.OnVanillaMapDataLoaded += PortalManager.RequestInitialSync;

            Log.LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded");
        }

        private void Update()
        {
            if (!Env.IsHeadless) PortalConfigPanel.Instance?.HandleInput();
        }

        private void OnDestroy()
        {
            MinimapManager.OnVanillaMapDataLoaded -= PortalManager.RequestInitialSync;
            PortalConfigPanel.Instance?.Dispose();
            _harmony?.UnpatchSelf();
        }

        // Patch every [HarmonyPatch] class in this assembly one at a time, each
        // in its own try/catch, and log a one-line summary of what applied.
        // PatchAll() runs them in a single loop with no per-class guard: one
        // class throwing during patching can abort the loop before later classes
        // are reached, silently leaving them unpatched. A patch failing (a future
        // Valheim rename) must degrade only its own feature, never take the rest
        // of the mod down with it — and the summary line makes "did our patch
        // apply?" answerable from the log alone.
        private void PatchEachClassIndividually()
        {
            var summary = new List<string>();
            try
            {
                foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
                {
                    if (type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0) continue;

                    try
                    {
                        var patched = _harmony.CreateClassProcessor(type).Patch();
                        if (patched == null || patched.Count == 0)
                            summary.Add($"{type.Name}=SKIPPED(Prepare() false or no target)");
                        else
                            foreach (var method in patched)
                                summary.Add($"{method.DeclaringType?.Name}.{method.Name}=OK(via {type.Name})");
                    }
                    catch (Exception ex)
                    {
                        summary.Add($"{type.Name}=FAILED({ex.GetType().Name})");
                        Log.LogError($"Harmony patch '{type.FullName}' failed to apply; continuing without it: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                summary.Add($"enumeration=FAILED({ex.GetType().Name})");
                Log.LogError($"Harmony patching failed unexpectedly; continuing without the affected patch(es): {ex}");
            }

            Log.LogInfo($"{ModInfo.Name} Harmony patches: {string.Join("; ", summary)}");
        }
    }
}
