using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// The Eitr Refinery no longer hurts whoever stands near it while it
    /// runs. Its steam, light, sound and smelting are untouched -- only the
    /// two hidden emitters that would otherwise fire a damaging, knockback-
    /// dealing projectile out from it are stopped.
    ///
    /// Synced scope: the refinery only spawns those projectiles on whichever
    /// client currently owns its ZDO (docs/valheim-api/eitr-refinery.md), so
    /// this has to be off for everyone on a server, not just the one player
    /// standing next to it.
    /// </summary>
    internal sealed class SafeRefineryFeature : Feature
    {
        public const string FeatureName = "Production/SafeRefinery";

        public static SafeRefineryFeature Instance { get; private set; }

        public SafeRefineryFeature() => Instance = this;

        public override string Key => "SafeRefinery";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "The Eitr Refinery stops spitting the damaging, knocking-back projectile that otherwise fires out of "
            + "it while it runs; the steam, light, sound and smelting are unchanged. Whoever owns a given "
            + "refinery decides whether it hurts anyone standing near it, so everyone on a server needs this on "
            + "for it to be safe. A refinery already running when this is switched on keeps its old behaviour "
            + "until it is next turned off and back on (or the area is reloaded) -- new and restarted refineries "
            + "are safe immediately.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(SafeRefineryPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Radiator", "OnEnable", "the moment a refinery's emitter starts firing"),
        };
    }

    /// <summary>
    /// Stops a <c>Radiator</c>'s periodic projectile spawner from ever
    /// starting, but only for the two radiators that live under the Eitr
    /// Refinery -- every other radiator in the game (fires, lava, anything
    /// else built the same way) is left completely alone.
    ///
    /// <c>Radiator</c> is a small, reusable emitter with no built-in tie to
    /// the refinery (docs/valheim-api/eitr-refinery.md, section 5): the only
    /// thing that says "this one belongs to the refinery" is which prefab it
    /// is wired under, discovered at runtime by walking up to the owning
    /// <c>ZNetView</c> -- the same component <c>Radiator</c> itself walks up
    /// to decide who owns the spawn decision.
    /// </summary>
    [HarmonyPatch]
    internal static class SafeRefineryPatch
    {
        private const string RefineryPrefab = "eitrrefinery";

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Radiator), "OnEnable", SafeRefineryFeature.FeatureName);

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Radiator), "OnEnable");
        }

        /// <summary>
        /// Returning false skips vanilla's <c>StartCoroutine("UpdateLoop")</c>
        /// entirely, so the emitter never fires -- rather than letting it
        /// fire and then discarding what it spawns, which would still pay
        /// for the instantiate and let the projectile exist for a frame.
        /// </summary>
        private static bool Prefix(Radiator __instance)
        {
            if (SafeRefineryFeature.Instance?.IsActive != true) return true;

            try
            {
                if (__instance == null) return true;

                return !BelongsToRefinery(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"SafeRefinery: leaving this emitter at vanilla strength: {ex}");
                return true;
            }
        }

        private static bool BelongsToRefinery(Radiator radiator)
        {
            var nview = radiator.GetComponentInParent<ZNetView>();
            if (nview == null) return false;

            string prefab = Utils.GetPrefabName(nview.gameObject);
            return string.Equals(prefab, RefineryPrefab, StringComparison.OrdinalIgnoreCase);
        }
    }
}
