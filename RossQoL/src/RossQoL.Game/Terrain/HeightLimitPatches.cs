using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RossQoL.Core.Terrain;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Terrain
{
    /// <summary>
    /// Vanilla hard-codes the 8 metre limit as float constants:
    ///   ApplyToHeightmap: Mathf.Clamp(height, baseHeight - 8f, baseHeight + 8f)
    ///   LevelTerrain / RaiseTerrain: Mathf.Clamp(levelDelta, -8f, 8f)
    /// Each constant is swapped for a call returning the limit in effect, so
    /// switching the feature or changing a limit applies without a restart.
    /// </summary>
    internal static class HeightLimitIL
    {
        public static float Raise() => HeightLimits.Effective(IsActive, TerrainConfig.MaxRaise?.Value ?? HeightLimits.Vanilla);

        public static float Dig() => HeightLimits.Effective(IsActive, TerrainConfig.MaxDig?.Value ?? HeightLimits.Vanilla);

        public static float NegativeDig() => -Dig();

        private static bool IsActive => UnlimitedHeightFeature.Instance?.IsActive == true;

        /// <summary>
        /// Swaps every ±8 float constant in the method for a call, in order.
        /// The count and signs must match exactly: if Valheim changed the
        /// method, or another mod already rewrote these constants, this throws
        /// and the patch is skipped, rather than rewriting the wrong code.
        /// </summary>
        public static IEnumerable<CodeInstruction> Replace(
            IEnumerable<CodeInstruction> instructions, string where, params (float Value, string Method)[] expected)
        {
            var code = new List<CodeInstruction>(instructions);
            var hits = new List<int>();
            for (int i = 0; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldc_R4 && code[i].operand is float f
                    && Math.Abs(Math.Abs(f) - HeightLimits.Vanilla) < 0.0001f)
                    hits.Add(i);

            if (hits.Count != expected.Length)
                throw new InvalidOperationException(
                    $"{where}: expected {expected.Length} height limit constant(s), found {hits.Count}. "
                    + "The game or another mod has changed this method; its terrain limit is left as it is.");

            for (int k = 0; k < hits.Count; k++)
            {
                var instruction = code[hits[k]];
                if (Math.Abs((float)instruction.operand - expected[k].Value) > 0.0001f)
                    throw new InvalidOperationException(
                        $"{where}: height limit constant {k + 1} is {instruction.operand}, expected {expected[k].Value}. "
                        + "Its terrain limit is left as it is.");

                // Mutated in place so any labels and exception blocks stay attached.
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(HeightLimitIL), expected[k].Method);
            }

            return code;
        }
    }

    [HarmonyPatch(typeof(TerrainComp), nameof(TerrainComp.ApplyToHeightmap))]
    internal static class ApplyToHeightmapLimitPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(TerrainComp), nameof(TerrainComp.ApplyToHeightmap), UnlimitedHeightFeature.FeatureName);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            HeightLimitIL.Replace(instructions, "TerrainComp.ApplyToHeightmap",
                (8f, nameof(HeightLimitIL.Dig)),
                (8f, nameof(HeightLimitIL.Raise)));
    }

    [HarmonyPatch(typeof(TerrainComp), nameof(TerrainComp.LevelTerrain))]
    internal static class LevelTerrainLimitPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(TerrainComp), nameof(TerrainComp.LevelTerrain), UnlimitedHeightFeature.FeatureName);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            HeightLimitIL.Replace(instructions, "TerrainComp.LevelTerrain",
                (-8f, nameof(HeightLimitIL.NegativeDig)),
                (8f, nameof(HeightLimitIL.Raise)));
    }

    [HarmonyPatch(typeof(TerrainComp), nameof(TerrainComp.RaiseTerrain))]
    internal static class RaiseTerrainLimitPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(TerrainComp), nameof(TerrainComp.RaiseTerrain), UnlimitedHeightFeature.FeatureName);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            HeightLimitIL.Replace(instructions, "TerrainComp.RaiseTerrain",
                (-8f, nameof(HeightLimitIL.NegativeDig)),
                (8f, nameof(HeightLimitIL.Raise)));
    }
}
