using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// Character.OnDeath, on the dying creature's owner, spawns the corpse,
    /// calls Ragdoll.Setup (which stores the loot list in the corpse's ZDO)
    /// and then disables CharacterDrop, so the corpse is the only thing that
    /// would ever drop this loot. Vanilla drops it from Ragdoll.DestroyNow
    /// when the corpse fades.
    ///
    /// Here the stored loot is dropped straight away with vanilla's own
    /// SpawnLoot, at the position DestroyNow would use, and the stored count
    /// is then zeroed. When the corpse fades, DestroyNow finds nothing to
    /// drop. Mods that drop the corpse's loot early themselves read that same
    /// count and find nothing either.
    /// </summary>
    [HarmonyPatch(typeof(Ragdoll), nameof(Ragdoll.Setup))]
    internal static class InstantLootPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Ragdoll), nameof(Ragdoll.Setup), InstantLootFeature.FeatureName);

        private static void Postfix(Ragdoll __instance)
        {
            if (InstantLootFeature.Instance?.IsActive != true) return;

            // An exception escaping here would abort Character.OnDeath midway.
            try
            {
                var nview = __instance.m_nview;
                if (!nview || !nview.IsValid() || !nview.IsOwner()) return;

                var zdo = nview.GetZDO();
                if (zdo.GetInt(ZDOVars.s_drops) <= 0) return;

                var at = __instance.m_lootSpawnJoint
                    ? __instance.m_lootSpawnJoint.transform.position
                    : __instance.GetAverageBodyPosition();

                try
                {
                    __instance.SpawnLoot(at);
                }
                finally
                {
                    // Even if dropping failed partway: a second, full drop
                    // when the corpse fades would duplicate what did drop.
                    zdo.Set(ZDOVars.s_drops, 0);
                }
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"InstantLoot: dropping loot at death failed: {ex}");
            }
        }
    }
}
