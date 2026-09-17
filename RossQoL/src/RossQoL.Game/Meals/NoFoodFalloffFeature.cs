using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Meals
{
    /// <summary>
    /// A meal is worth what it says until the moment it runs out.
    ///
    /// Vanilla fades a food's health, stamina and eitr as its timer runs
    /// down, so most of a meal's life is spent giving less than the tooltip
    /// promises. Here the full value holds and then ends.
    ///
    /// Synced scope: it changes how much health and stamina players have in a
    /// shared world, so the server decides it.
    /// </summary>
    internal sealed class NoFoodFalloffFeature : Feature
    {
        public const string FeatureName = "Food/NoFalloff";

        public static NoFoodFalloffFeature Instance { get; private set; }

        public NoFoodFalloffFeature() => Instance = this;

        public override string Key => "NoFalloff";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Food gives its full health, stamina and eitr for its whole duration instead of fading as the "
            + "timer runs down. It still ends when it ends.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(NoFoodFalloffTotalPatch),
            typeof(NoFoodFalloffDisplayPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Player", "GetTotalFoodValue", "what a meal is worth right now"),
            new CompatMember("Player", "UpdateFood", "where a meal's worth is recalculated"),
            new CompatMember("Player", "m_foods", "the meals you are living on"),
            new CompatMember("Player", "m_baseHP", "your health before food"),
            new CompatMember("Player", "m_baseStamina", "your stamina before food"),
        };
    }

    /// <summary>
    /// Vanilla asks GetTotalFoodValue for the totals and hands them straight
    /// to SetMaxHealth, SetMaxStamina and SetMaxEitr. Those setters clamp what
    /// you currently have down to the new maximum -- SetMaxHealth calls
    /// SetHealth, the other two Clamp -- so a faded total does not merely
    /// shrink the bar, it takes the health and stamina with it, and putting
    /// the maximum back afterwards does not give them back.
    ///
    /// So the answer is corrected here, before vanilla ever sees it, rather
    /// than after: the totals are summed from what each meal is worth whole.
    /// </summary>
    [HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
    internal static class NoFoodFalloffTotalPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), "GetTotalFoodValue", NoFoodFalloffFeature.FeatureName);

        private static void Postfix(Player __instance, ref float hp, ref float stamina, ref float eitr)
        {
            if (NoFoodFalloffFeature.Instance?.IsActive != true || __instance == null) return;

            try
            {
                var foods = __instance.m_foods;
                if (foods == null || foods.Count == 0) return;

                hp = __instance.m_baseHP;
                stamina = __instance.m_baseStamina;
                eitr = 0f;

                foreach (var food in foods)
                {
                    var shared = food?.m_item?.m_shared;
                    if (shared == null) continue;

                    hp += shared.m_food;
                    stamina += shared.m_foodStamina;
                    eitr += shared.m_foodEitr;
                }
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"NoFalloff: leaving these totals to vanilla: {ex}");
            }
        }
    }

    /// <summary>
    /// The per-meal numbers the HUD reads are faded by the same tick. They do
    /// not decide anything now that the totals are answered above, but a food
    /// bar that disagrees with the health it grants is a lie, so they are put
    /// back to the item's own values after vanilla has written them.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateFood")]
    internal static class NoFoodFalloffDisplayPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), "UpdateFood", NoFoodFalloffFeature.FeatureName);

        private static void Postfix(Player __instance)
        {
            if (NoFoodFalloffFeature.Instance?.IsActive != true || __instance == null) return;

            try
            {
                var foods = __instance.m_foods;
                if (foods == null) return;

                foreach (var food in foods)
                {
                    var shared = food?.m_item?.m_shared;
                    if (shared == null) continue;

                    food.m_health = shared.m_food;
                    food.m_stamina = shared.m_foodStamina;
                    food.m_eitr = shared.m_foodEitr;
                }
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"NoFalloff: leaving the food bars to vanilla: {ex}");
            }
        }
    }
}
