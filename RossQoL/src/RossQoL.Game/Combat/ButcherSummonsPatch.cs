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
    /// <see cref="IsButcherable"/>, which answers true for a summon as well as a
    /// tame.
    ///
    /// This only ever changes the outcome for a summon: in that condition the
    /// other <c>IsTamed()</c> is the attacker's own (the player swinging, never
    /// a summon), a real tame already answered true, and a non-tamed-only weapon
    /// never reaches the summon term at all -- so tames, players and every
    /// normal weapon are untouched. The single difference is that a tamed-only
    /// swing no longer skips a summon.
    ///
    /// A <c>call</c> to the static replacement is stack-identical to the
    /// <c>callvirt</c> it replaces: the <see cref="Character"/> receiver simply
    /// becomes the method's one argument.
    /// </summary>
    [HarmonyPatch]
    internal static class ButcherSummonsPatch
    {
        private static readonly MethodInfo IsTamedMethod =
            AccessTools.Method(typeof(Character), nameof(Character.IsTamed), Type.EmptyTypes);

        private static readonly MethodInfo Replacement =
            AccessTools.Method(typeof(ButcherSummonsPatch), nameof(IsButcherable));

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
                if ((code[i].opcode == OpCodes.Callvirt || code[i].opcode == OpCodes.Call)
                    && code[i].operand is MethodInfo called && called == IsTamedMethod)
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = Replacement;
                    replaced++;
                }
            }

            if (replaced == 0)
                throw new InvalidOperationException(
                    $"ButcherSummons: no Character.IsTamed() call found in {__originalMethod?.Name}; the "
                    + "butcher knife's target filter has changed. It keeps cutting only tames.");

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
    }
}
