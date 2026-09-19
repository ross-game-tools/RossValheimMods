using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Scales the factor vanilla passes into <see cref="Skills.LowerAllSkills"/>
    /// -- the single call site both a hard death and
    /// <see cref="GlobalKeys.DeathSkillsReset"/> avoid go through -- rather
    /// than touching <see cref="Game.m_skillReductionRate"/>, which is
    /// public static, shared with vanilla's own world-modifier rate, and
    /// gets overwritten whenever a global key changes.
    /// </summary>
    [HarmonyPatch(typeof(Skills), nameof(Skills.LowerAllSkills))]
    internal static class SkillLossPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Skills), nameof(Skills.LowerAllSkills), SkillLossFeature.FeatureName);

        private static void Prefix(ref float factor)
        {
            if (SkillLossFeature.Instance?.IsActive != true) return;

            try
            {
                float multiplier = DeathConfig.SkillLossMultiplier?.Value ?? 1f;
                if (multiplier < 0f) return;
                factor *= multiplier;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"SkillLoss: leaving this death's skill loss at vanilla: {ex}");
            }
        }
    }
}
