using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RossQoL.Core.Items;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// Widens the butcher knife's tamed-only target filter to include summons.
    ///
    /// <c>Attack.DoMeleeAttack</c> and <c>DoAreaAttack</c> skip a target when
    /// <c>m_weapon.m_shared.m_tamedOnly &amp;&amp; !character.IsTamed()</c> --
    /// so the butcher knife (the one weapon that ships with <c>m_tamedOnly</c>)
    /// passes through anything untamed, and a summon is untamed. The transpiler
    /// replaces every <c>Character.IsTamed()</c> call in those two methods with
    /// <see cref="IsButcherable(Character)"/>, which answers true for a summon
    /// as well as a tame.
    ///
    /// This only ever changes the outcome for a summon: in that condition the
    /// other <c>IsTamed()</c> is the attacker's own (the player swinging, never
    /// a summon), a real tame already answered true, and a non-tamed-only weapon
    /// never reaches the summon term -- so tames, players and every normal
    /// weapon are untouched. The single difference is that a tamed-only swing no
    /// longer skips a summon.
    ///
    /// Matching is by name and declaring type, not a resolved <c>MethodInfo</c>
    /// reference: Valheim runs on Mono, where the operand a transpiler sees is
    /// not reference-equal to <c>AccessTools.Method(...)</c>, so a
    /// <c>==</c> comparison silently matches nothing (the mistake that shipped
    /// dead in 0.28.0). Both IsTamed overloads are handled -- vanilla calls the
    /// parameterless one, but the private <c>IsTamed(float)</c> is covered too
    /// so a future inline cannot quietly reopen the same hole.
    /// </summary>
    [HarmonyPatch]
    internal static class ButcherSummonsPatch
    {
        private static readonly MethodInfo Replace0 =
            AccessTools.Method(typeof(ButcherSummonsPatch), nameof(IsButcherable), new[] { typeof(Character) });

        private static readonly MethodInfo Replace1 =
            AccessTools.Method(typeof(ButcherSummonsPatch), nameof(IsButcherable), new[] { typeof(Character), typeof(float) });

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Attack), "DoMeleeAttack", ButcherSummonsFeature.FeatureName)
            & ValheimCompat.RequireMethod(typeof(Attack), "DoAreaAttack", ButcherSummonsFeature.FeatureName)
            & ValheimCompat.RequireMethod(typeof(Character), nameof(Character.IsTamed), ButcherSummonsFeature.FeatureName);

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Attack), "DoMeleeAttack");
            yield return AccessTools.Method(typeof(Attack), "DoAreaAttack");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            int replaced = 0;

            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Callvirt && code[i].opcode != OpCodes.Call) continue;
                if (!(code[i].operand is MethodInfo m)) continue;
                if (m.Name != "IsTamed" || m.DeclaringType != typeof(Character)) continue;

                // Same stack shape either way: the Character receiver becomes the
                // first argument, and a plain Call to the static replacement drops
                // in. The float overload keeps its extra time argument.
                var parameters = m.GetParameters();
                if (parameters.Length == 0)
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = Replace0;
                    replaced++;
                }
                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(float))
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = Replace1;
                    replaced++;
                }
            }

            // A missing call means the filter changed shape: leave THIS method to
            // vanilla (the knife keeps cutting only tames) rather than throwing,
            // which would abort the whole patch class and drop the other method
            // with it.
            if (replaced == 0)
                RossQoLPlugin.Log.LogWarning(
                    $"ButcherSummons: no Character.IsTamed() call found in {__originalMethod?.Name}; "
                    + "leaving that swing to vanilla.");

            return code;
        }

        /// <summary>
        /// Stands in for <c>Character.IsTamed()</c> at the butcher knife's target
        /// filter: a tame, or -- while the feature is on -- one of RossQoL's
        /// summons. Public because the rewritten IL calls it from inside
        /// Valheim's own method body.
        /// </summary>
        public static bool IsButcherable(Character character)
        {
            if (character == null) return false;
            if (character.IsTamed()) return true;
            if (ButcherSummonsFeature.Instance?.IsActive != true) return false;
            return SummonKinds.IsSummon(character.name);
        }

        /// <summary>Overload for the private <c>IsTamed(float)</c> call site; the time is vanilla's own and unused here.</summary>
        public static bool IsButcherable(Character character, float time) => IsButcherable(character);
    }
}
