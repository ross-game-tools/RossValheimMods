using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RossQoL.Core.Tames;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// Replaces the one line of Tameable.UnsummonMaxInstances that decides
    /// which creature the summon cap despawns.
    ///
    /// Vanilla's method builds the list of creatures counted against the cap,
    /// sorts it oldest-first, and unsummons the first (count - limit) of them:
    ///
    ///     list.Sort((a, b) =&gt; b.GetTimeSinceSpawned().CompareTo(a.GetTimeSinceSpawned()));
    ///
    /// The transpiler swaps that one Sort call for a call of our own taking the
    /// same two stack operands -- the list and vanilla's own comparison -- so
    /// everything around it is vanilla's untouched code: the same list, built
    /// by the same two conditions, and the same number despawned off the front
    /// of it. Reimplementing the method in a prefix would have meant owning all
    /// of that, including the "max summons reached" message, for the sake of a
    /// sort.
    ///
    /// Patched on UnsummonMaxInstances and not on any of the small helpers it
    /// calls. Mono inlines tiny methods and a Harmony patch on one is silently
    /// bypassed, which has bitten this project before -- GetTimeSinceSpawned,
    /// GetHealth and GetMaxHealth are all exactly that shape, and are only ever
    /// called here, never patched. UnsummonMaxInstances itself is 365 bytes of
    /// IL (checked against a 1.0.15 disassembly), two orders of magnitude past
    /// any inlining threshold, and it contains a loop and a closure besides.
    /// </summary>
    [HarmonyPatch(typeof(Tameable), MethodName)]
    internal static class CullWoundedSummonsPatch
    {
        private const string MethodName = "UnsummonMaxInstances";

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Tameable), MethodName, CullWoundedSummonsFeature.FeatureName);

        /// <summary>
        /// The exact overload vanilla calls: List&lt;BaseAI&gt;.Sort(Comparison&lt;BaseAI&gt;).
        /// Matching on the resolved MethodInfo rather than on the name means a
        /// second, different Sort appearing in this method could never be
        /// mistaken for it.
        /// </summary>
        private static readonly MethodInfo VanillaSort =
            AccessTools.Method(typeof(List<BaseAI>), nameof(List<BaseAI>.Sort), new[] { typeof(Comparison<BaseAI>) });

        /// <summary>
        /// Rewrites the single Sort call. If the count is anything but one --
        /// the game changed the method, or another mod rewrote it first -- this
        /// throws rather than rewriting the wrong instruction, and Harmony
        /// leaves the method as vanilla wrote it.
        /// </summary>
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);

            if (VanillaSort == null)
                throw new InvalidOperationException(
                    "Tameable.UnsummonMaxInstances: List<BaseAI>.Sort(Comparison<BaseAI>) could not be resolved. "
                    + "The summon cap keeps vanilla's oldest-first order.");

            var hits = new List<int>();
            for (int i = 0; i < code.Count; i++)
                if ((code[i].opcode == OpCodes.Callvirt || code[i].opcode == OpCodes.Call)
                    && code[i].operand is MethodInfo called && called == VanillaSort)
                    hits.Add(i);

            if (hits.Count != 1)
                throw new InvalidOperationException(
                    $"Tameable.UnsummonMaxInstances: expected exactly 1 call to List<BaseAI>.Sort, found {hits.Count}. "
                    + "The game or another mod has changed this method; the summon cap keeps vanilla's "
                    + "oldest-first order.");

            // Mutated in place so any labels and exception blocks stay attached.
            var instruction = code[hits[0]];
            instruction.opcode = OpCodes.Call;
            instruction.operand = AccessTools.Method(typeof(CullWoundedSummonsPatch), nameof(SortWoundedFirst));

            return code;
        }

        /// <summary>
        /// Stands in for list.Sort(vanillaOrder), taking the same two operands
        /// off the stack and leaving nothing behind, so the rewritten call site
        /// is IL-identical to the one it replaces.
        ///
        /// Public because the rewritten IL calls it from inside Valheim's own
        /// method body.
        /// </summary>
        public static void SortWoundedFirst(List<BaseAI> list, Comparison<BaseAI> vanillaOrder)
        {
            if (list == null) return;

            try
            {
                if (CullWoundedSummonsFeature.Instance?.IsActive != true)
                {
                    SortVanilla(list, vanillaOrder);
                    return;
                }

                var candidates = new List<SummonCullCandidate>(list.Count);
                for (int i = 0; i < list.Count; i++)
                    candidates.Add(Describe(list[i]));

                var order = SummonCullOrder.Order(candidates);

                var reordered = new List<BaseAI>(list.Count);
                for (int i = 0; i < order.Count; i++)
                    reordered.Add(list[order[i]]);

                list.Clear();
                list.AddRange(reordered);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError(
                    $"CullWoundedSummons: could not order the summons by how hurt they are, "
                    + $"leaving the cap to despawn the oldest as vanilla does: {ex}");
                SortVanilla(list, vanillaOrder);
            }
        }

        /// <summary>
        /// Vanilla's own comparison, run as vanilla would have run it. Guarded
        /// because a failure here would otherwise escape into the middle of
        /// Valheim's method and take the whole summon cap down with it.
        /// </summary>
        private static void SortVanilla(List<BaseAI> list, Comparison<BaseAI> vanillaOrder)
        {
            if (list == null || vanillaOrder == null) return;

            try
            {
                list.Sort(vanillaOrder);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"CullWoundedSummons: vanilla's own summon ordering failed: {ex}");
            }
        }

        /// <summary>
        /// How hurt one counted creature is, and how long it has been alive.
        /// A creature with no Character component reports nothing, which
        /// SummonCullCandidate reads as unwounded -- so it is never singled out
        /// for being unmeasurable, and falls back to the age tie-break.
        /// </summary>
        private static SummonCullCandidate Describe(BaseAI ai)
        {
            if (ai == null) return new SummonCullCandidate(0f, 0f, 0d);

            double seconds = ai.GetTimeSinceSpawned().TotalSeconds;

            var character = ai.GetComponent<Character>();
            if (character == null) return new SummonCullCandidate(0f, 0f, seconds);

            return new SummonCullCandidate(character.GetHealth(), character.GetMaxHealth(), seconds);
        }
    }
}
