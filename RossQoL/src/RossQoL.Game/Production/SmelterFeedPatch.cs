using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RossQoL.Core.Production;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Smelters, kilns, blast furnaces, windmills and spinning wheels all run
    /// on Smelter.UpdateSmelter, which ticks every second on every peer and
    /// does its work on the owner. Feeding rides the same tick.
    ///
    /// Ore and fuel are handed over with vanilla's own RPCs, one unit at a
    /// time as a player at the switch would, so the queue limits, the cheat
    /// flag and the effects are the game's.
    /// </summary>
    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    internal static class SmelterFeedPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Smelter), "UpdateSmelter", AutoFeedFeature.FeatureName);

        private static void Postfix(Smelter __instance)
        {
            if (AutoFeedFeature.Instance?.IsActive != true) return;

            // An exception escaping here would stop this smelter's update.
            try
            {
                var kind = SmelterKinds.Of(__instance);
                if (!FeedGate.ShouldRun(__instance, __instance.m_nview, AutoFeedFeature.Instance, kind.Enabled)) return;

                SmelterFeeder.Feed(__instance, kind);
                FeedGate.Succeeded(__instance);
            }
            catch (Exception ex)
            {
                FeedGate.LogFailure(__instance, ex);
            }
        }

    }

    /// <summary>
    /// The feeding itself, out of the patch class: Harmony's analyser reads
    /// every method beside a patch as a patch of its own and warns about
    /// parameters it thinks are vanilla's.
    /// </summary>
    internal static class SmelterFeeder
    {
        public static void Feed(Smelter smelter, SmelterKind kind)
        {
            FeedOre(smelter, kind);
            FeedFuel(smelter);
        }

        private static void FeedOre(Smelter smelter, SmelterKind kind)
        {
            if (smelter.m_maxOre <= 0) return;

            int room = smelter.m_maxOre - smelter.GetQueueSize();
            if (room <= 0) return;

            var origin = smelter.transform.position;
            foreach (var conversion in smelter.m_conversion)
            {
                if (conversion?.m_from == null || conversion.m_to == null) continue;

                // A kiln burns whatever its recipe allows; KilnFuel narrows
                // that to what the player is willing to spend on charcoal.
                if (kind.LimitFuel && !FeedRules.Allowed(KilnFuels(), conversion.m_from.gameObject.name)) continue;

                // Ore into metal is never capped: a cap there would leave ore
                // sitting in chests, which is not what a cap is for.
                if (!kind.Smelts
                    && FeedRules.AtCap(
                        ContainerSource.Caps(),
                        conversion.m_to.gameObject.name,
                        ContainerSource.CountNearby(origin, conversion.m_to.m_itemData.m_shared.m_name)))
                    continue;

                // One at a time, as a player at the switch does it: the feed
                // runs every second, and an item that is refused or lost in a
                // race costs one item rather than a chest's worth.
                if (ContainerSource.Take(origin, conversion.m_from, 1, out bool cheated) <= 0) continue;

                smelter.m_nview.InvokeRPC("RPC_AddOre", conversion.m_from.gameObject.name, cheated);
                return;
            }
        }

        private static void FeedFuel(Smelter smelter)
        {
            if (smelter.m_maxFuel <= 0 || smelter.m_fuelItem == null) return;

            if (smelter.m_maxFuel - Mathf.CeilToInt(smelter.GetFuel()) <= 0) return;

            if (ContainerSource.Take(smelter.transform.position, smelter.m_fuelItem, 1, out _) > 0)
                smelter.m_nview.InvokeRPC("RPC_AddFuel");
        }

        private static string _kilnFuelText;
        private static HashSet<string> _kilnFuels = new HashSet<string>(FeedRules.NameComparer);

        private static HashSet<string> KilnFuels()
        {
            string text = AutoFeedConfig.KilnFuel?.Value ?? string.Empty;
            if (text != _kilnFuelText)
            {
                _kilnFuels = FeedRules.ParseNames(text);
                _kilnFuelText = text;
            }
            return _kilnFuels;
        }
    }

    internal readonly struct SmelterKind
    {
        public SmelterKind(ConfigEntry<bool> enabled, bool smelts, bool limitFuel)
        {
            Enabled = enabled;
            Smelts = smelts;
            LimitFuel = limitFuel;
        }

        /// <summary>The setting that turns this kind of station on and off.</summary>
        public ConfigEntry<bool> Enabled { get; }

        /// <summary>True for the stations that turn ore into metal, which are never output-capped.</summary>
        public bool Smelts { get; }

        /// <summary>True for the kiln, whose input the KilnFuel setting narrows.</summary>
        public bool LimitFuel { get; }
    }

    /// <summary>
    /// Which kind of station a Smelter is. By component and prefab name, as
    /// vanilla itself tells them apart: the windmill has a Windmill, and the
    /// rest are named.
    /// </summary>
    internal static class SmelterKinds
    {
        public static SmelterKind Of(Smelter smelter)
        {
            if (smelter.m_windmill != null)
                return new SmelterKind(AutoFeedConfig.FeedWindmills, smelts: false, limitFuel: false);

            string name = smelter.name;
            if (name.IndexOf("charcoal_kiln", StringComparison.OrdinalIgnoreCase) >= 0)
                return new SmelterKind(AutoFeedConfig.FeedKilns, smelts: false, limitFuel: true);

            if (name.IndexOf("spinningwheel", StringComparison.OrdinalIgnoreCase) >= 0)
                return new SmelterKind(AutoFeedConfig.FeedSpinningWheels, smelts: false, limitFuel: false);

            if (name.IndexOf("blastfurnace", StringComparison.OrdinalIgnoreCase) >= 0)
                return new SmelterKind(AutoFeedConfig.FeedBlastFurnaces, smelts: true, limitFuel: false);

            return new SmelterKind(AutoFeedConfig.FeedSmelters, smelts: true, limitFuel: false);
        }
    }
}
