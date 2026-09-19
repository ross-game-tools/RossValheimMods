using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using RossQoL.Game.Items;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// With the interaction gone, the prompt that advertises it has to go too,
    /// or a raised skeleton reads as broken: it would still say to press Use,
    /// and pressing Use would do nothing.
    ///
    /// Vanilla's tamed branch of Tameable.GetHoverText is the name, the
    /// "( tame, happy )" status, and then two prompt lines -- pet, and rename.
    /// This rebuilds the first two and stops, which is what the game already
    /// shows for a creature with nothing to offer. Rename goes with the rest:
    /// it is the same Use press on the same creature, and a name on something
    /// that exists for a few minutes and cannot be told to wait for you is not
    /// worth a prompt that half works.
    ///
    /// Only the tamed branch is replaced. If a summon ever reads as untamed --
    /// see docs/valheim-api/summons.md on how unreliable that flag is -- vanilla
    /// shows the wild/tameness line, which carries no prompt and needs no help.
    ///
    /// GetHoverText is long, branchy and calls into Localization, so it is not
    /// an inlining candidate and the postfix is reached.
    /// </summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
    internal static class NoSummonCommandsHoverPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Tameable), nameof(Tameable.GetHoverText), NoSummonCommandsFeature.FeatureName);

        private static void Postfix(Tameable __instance, ref string __result)
        {
            if (NoSummonCommandsFeature.Instance?.IsActive != true) return;

            try
            {
                if (__instance == null) return;

                // Vanilla returns an empty string when the creature's ZDO is
                // not valid yet. That is not the tamed branch, and replacing it
                // would put a name on something the game is not describing.
                if (string.IsNullOrEmpty(__result)) return;

                if (!SummonedSkeleton.Is(__instance.gameObject)) return;
                if (!__instance.IsTamed()) return;

                string text = __instance.GetName();

                // Same condition vanilla uses before appending the status: the
                // status words describe the creature, so a Tameable with no
                // Character has nothing to say.
                var character = __instance.m_character;
                if (character != null)
                    text += Localization.instance.Localize(" ( $hud_tame, " + __instance.GetStatusString() + " )");

                __result = text;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"NoSummonCommands: could not rewrite a hover text, leaving it alone: {ex}");
            }
        }
    }
}
