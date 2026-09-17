using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Meals
{
    /// <summary>
    /// Tells you a meal is nearly out while there is still time to do
    /// something about it, rather than only once it has gone.
    ///
    /// Client scope: it is a message to the player reading it, and changes
    /// nothing about the world.
    /// </summary>
    internal sealed class FoodWarningFeature : Feature
    {
        public const string FeatureName = "Food/ExpiryWarning";

        public static FoodWarningFeature Instance { get; private set; }

        public FoodWarningFeature() => Instance = this;

        public override string Key => "ExpiryWarning";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Warns you WarningSeconds before a food runs out, so a meal can be topped up before it lapses. "
            + "Vanilla only tells you once it has already gone.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(FoodWarningPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Player", "UpdateFood", "where a meal's remaining time ticks down"),
            new CompatMember("Player", "m_foods", "the meals you are living on"),
            new CompatMember("Character", "Message", "telling you about it"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            FoodConfig.BindWarning(config, section, Scope);
    }

    /// <summary>
    /// Watches the countdown vanilla already keeps and speaks once as each
    /// meal crosses the threshold.
    ///
    /// Warned meals are remembered by the food object itself, so a meal warns
    /// once however often this runs, and eating the same dish again -- a new
    /// object -- warns again in its turn.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateFood")]
    internal static class FoodWarningPatch
    {
        private static readonly HashSet<Player.Food> Warned = new HashSet<Player.Food>();
        private static readonly List<Player.Food> Gone = new List<Player.Food>();

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), "UpdateFood", FoodWarningFeature.FeatureName);

        private static void Postfix(Player __instance)
        {
            if (FoodWarningFeature.Instance?.IsActive != true) return;
            if (__instance == null || __instance != Player.m_localPlayer) return;

            try
            {
                var foods = __instance.m_foods;
                if (foods == null) return;

                Forget(foods);

                float threshold = FoodConfig.WarningSeconds?.Value ?? 60f;
                foreach (var food in foods)
                {
                    var shared = food?.m_item?.m_shared;
                    if (shared == null) continue;
                    if (food.m_time > threshold || Warned.Contains(food)) continue;

                    Warned.Add(food);
                    __instance.Message(
                        MessageHud.MessageType.Center,
                        Localization.instance.Localize(shared.m_name) + ": "
                        + Mathf.CeilToInt(Mathf.Max(0f, food.m_time)) + "s left");
                }
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"ExpiryWarning: could not check your meals: {ex}");
            }
        }

        /// <summary>Drops meals that have been eaten through, so the set cannot grow forever.</summary>
        private static void Forget(List<Player.Food> foods)
        {
            if (Warned.Count == 0) return;

            Gone.Clear();
            foreach (var warned in Warned)
                if (!foods.Contains(warned)) Gone.Add(warned);

            foreach (var food in Gone) Warned.Remove(food);
            Gone.Clear();
        }
    }
}
