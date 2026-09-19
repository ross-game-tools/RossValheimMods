using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using RossQoL.Game.Items;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// Pressing Use on a raised skeleton does nothing at all.
    ///
    /// Tameable.Interact is the single door into both halves of what a player
    /// can do to a tame by hand: the petting branch, and -- when the prefab's
    /// m_commandable is set, or a mod sets it for the length of the call, as
    /// RossQoL's own Tames/FollowCommand does -- Tameable.Command, which is
    /// what toggles follow and stay. Refusing the whole call therefore closes
    /// both, and does so no matter which of them would have run.
    ///
    /// Not patched on Command or RPC_Command: the spawning staff commands the
    /// new skeleton to follow through exactly that path
    /// (SpawnAbility.m_commandOnSpawn), and so does Tameable's own recovery
    /// after a reload, so blocking it would leave a summon that never follows
    /// anyone -- and a summon following nobody is the very state this feature
    /// exists to prevent.
    ///
    /// Interact is a long method with several branches and is not an inlining
    /// candidate, so the prefix is reached.
    /// </summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
    internal static class NoSummonCommandsPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Tameable), nameof(Tameable.Interact), NoSummonCommandsFeature.FeatureName);

        /// <summary>
        /// Returning false skips vanilla; the false result reports "nothing
        /// happened", which is what the game does for any object that offers
        /// no interaction.
        /// </summary>
        private static bool Prefix(Tameable __instance, ref bool __result)
        {
            if (NoSummonCommandsFeature.Instance?.IsActive != true) return true;

            try
            {
                if (__instance == null) return true;
                if (!SummonedSkeleton.Is(__instance.gameObject)) return true;

                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"NoSummonCommands: could not check a creature, leaving the interaction alone: {ex}");
                return true;
            }
        }
    }
}
