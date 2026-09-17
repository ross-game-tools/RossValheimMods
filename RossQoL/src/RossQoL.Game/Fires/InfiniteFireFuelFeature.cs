using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Fires
{
    /// <summary>
    /// A fire filled to the top stops burning down, so a base that is kept
    /// stocked never gutters out and never drains the woodpile again.
    ///
    /// Synced scope: fuel is real world state that every player shares, so
    /// whether fires burn forever is the server's rule.
    /// </summary>
    internal sealed class InfiniteFireFuelFeature : Feature
    {
        public const string FeatureName = "Fires/InfiniteFuel";

        public static InfiniteFireFuelFeature Instance { get; private set; }

        public InfiniteFireFuelFeature() => Instance = this;

        public override string Key => "InfiniteFuel";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "A fire at maximum fuel stops burning down and stays lit without spending any more. Below maximum "
            + "it burns as it always did, so a fire still goes out if it is never filled.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(InfiniteFireFuelPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Fireplace", "UpdateFireplace", "the moment a fire burns fuel"),
            new CompatMember("Fireplace", "m_infiniteFuel", "vanilla's own switch for a fire that never burns down"),
            new CompatMember("Fireplace", "m_maxFuel", "how full is full"),
            new CompatMember("ZDOVars", "s_fuel", "how much fuel a fire holds"),
        };
    }

    /// <summary>
    /// Vanilla already has the switch: UpdateFireplace spends fuel only when
    /// m_infiniteFuel is off, and eternal flames ship with it on. The prefix
    /// turns it on while the fire is full and off again once it is not, so
    /// vanilla does the rest and nothing has to fake the burn.
    ///
    /// A fire that is infinite in its own right is left exactly alone: its
    /// original value is remembered the first time it is seen, and a fire
    /// that started infinite is never switched off.
    /// </summary>
    [HarmonyPatch(typeof(Fireplace), "UpdateFireplace")]
    internal static class InfiniteFireFuelPatch
    {
        // Keyed by the instance, so an entry dies with its fire.
        private static readonly ConditionalWeakTable<Fireplace, StrongBox<bool>> WasInfinite =
            new ConditionalWeakTable<Fireplace, StrongBox<bool>>();

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Fireplace), "UpdateFireplace", InfiniteFireFuelFeature.FeatureName);

        private static void Prefix(Fireplace __instance)
        {
            try
            {
                var nview = __instance.m_nview;
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

                if (Original(__instance)) return;

                if (InfiniteFireFuelFeature.Instance?.IsActive != true)
                {
                    // Switched off mid-session: hand the fire back to vanilla.
                    __instance.m_infiniteFuel = false;
                    return;
                }

                float fuel = nview.GetZDO().GetFloat(ZDOVars.s_fuel);
                __instance.m_infiniteFuel = Mathf.CeilToInt(fuel) >= Mathf.FloorToInt(__instance.m_maxFuel);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"InfiniteFuel: could not check a fire, leaving it to burn: {ex}");
            }
        }

        private static bool Original(Fireplace fireplace)
        {
            if (WasInfinite.TryGetValue(fireplace, out var seen)) return seen.Value;

            // First sight: whatever the prefab says, before this ever ran.
            WasInfinite.Add(fireplace, new StrongBox<bool>(fireplace.m_infiniteFuel));
            return fireplace.m_infiniteFuel;
        }
    }
}
