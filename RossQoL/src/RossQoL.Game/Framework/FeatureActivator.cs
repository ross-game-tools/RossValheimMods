using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Core.Framework;
using UnityEngine;

namespace RossQoL.Game.Framework
{
    internal static class FeatureActivator
    {
        // A patch class can legitimately be listed by more than one feature
        // in a category -- several features reading the same recorded
        // state, say. Patching it once per feature would run its
        // prefix/postfix once per feature too, so it is applied once here
        // and each patch body decides for itself whether its feature is
        // active.
        //
        // This mirrors Harmony's own patched-or-not state, so whatever
        // unpatches Harmony's side (RossQoLPlugin.OnDestroy) must also call
        // Reset() below -- otherwise a reload within the same process (the
        // HotReload path anticipates a plugin unload/reload) finds every
        // shared class already in this set and never re-patches it, with
        // nothing but an info log to show for it.
        private static readonly HashSet<Type> AppliedPatchClasses = new HashSet<Type>();

        /// <summary>
        /// Forgets every patch class this activator has applied. Call
        /// alongside whatever undoes the actual Harmony patches, so this
        /// set can never drift out of sync with what is really patched.
        /// </summary>
        internal static void Reset() => AppliedPatchClasses.Clear();

        public static void Activate(Feature feature, Harmony harmony, GameObject host)
        {
            var log = RossQoLPlugin.Log;

            var missing = ValheimCompat.FindMissing(feature.RequiredMembers);
            if (missing.Count > 0)
            {
                log.LogError(
                    $"{feature.Name} is disabled: this Valheim version has changed members it depends on. "
                    + "This is almost certainly a Valheim update, not a conflict with another mod. Missing:");
                foreach (var m in missing) log.LogError($"    {m}");
            }

            if (!FeatureRules.ShouldPatch(feature.Scope, feature.IsActive, missing.Count == 0))
            {
                if (missing.Count == 0) log.LogInfo($"{feature.Name} is off; not patched.");
                return;
            }

            // Class by class, never PatchAll: one throwing class must not abort
            // the loop and leave later classes silently unpatched. This also
            // keeps a broken mod patching the same method from taking us down.
            int failed = 0;
            foreach (var type in feature.PatchClasses)
            {
                if (!AppliedPatchClasses.Add(type))
                {
                    log.LogInfo($"{feature.Name}: patch class {type.Name} already applied by another feature; shared.");
                    continue;
                }

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception ex)
                {
                    failed++;
                    log.LogError($"{feature.Name}: patch class {type.Name} failed and was skipped: {ex}");
                }
            }

            // One feature's OnActivated must not abort activation of every
            // feature that comes after it in the loop.
            bool activationFailed = false;
            try
            {
                feature.OnActivated(host);
            }
            catch (Exception ex)
            {
                activationFailed = true;
                log.LogError($"{feature.Name}: OnActivated failed: {ex}");
            }

            log.LogInfo(failed == 0 && !activationFailed
                ? $"{feature.Name} patched."
                : $"{feature.Name} patched with {failed} failed patch class(es)"
                  + (activationFailed ? " and a failed activation." : "."));
        }
    }
}
