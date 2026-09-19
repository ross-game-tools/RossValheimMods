using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Gives the Dead Raiser staff (<c>StaffSkeleton</c>) a real secondary
    /// attack. Vanilla ships it with <c>m_secondaryAttack.m_attackAnimation
    /// == ""</c>, which is vanilla's own way of saying "this item has no
    /// secondary attack" (<c>ItemDrop.ItemData.HaveSecondaryAttack</c>) --
    /// so today, middle-click with this staff does nothing at all.
    ///
    /// This does not decide what the recall DOES -- see
    /// <see cref="RecallSummonsAttackPatch"/>, which intercepts the input
    /// before vanilla's own attack ever runs and never lets this Attack
    /// object actually resolve. This patch exists so the staff's data is no
    /// longer "no secondary attack": <c>HaveSecondaryAttack()</c> now
    /// answers true, and anything vanilla that reads the secondary attack's
    /// animation or cost (a tooltip, a keybind hint) sees a real, harmless
    /// entry instead of a blank one.
    ///
    /// Applied on ObjectDB.Awake and ObjectDB.CopyOtherDB, exactly like
    /// StackableMeadBasesPatch: the main menu builds one ObjectDB and
    /// loading a world copies another over it, and a change made only to
    /// the first would be back to vanilla in game.
    /// </summary>
    [HarmonyPatch]
    internal static class RecallSummonsItemPatch
    {
        private const string StaffPrefabName = "StaffSkeleton";

        /// <summary>
        /// The staff's own primary attack animation. Reused rather than
        /// inventing a new one, because it is known to exist for this staff
        /// -- an animation name that does not exist for the equipped item
        /// simply plays nothing, so guessing here would be silently wrong.
        ///
        /// Internal rather than private: <see cref="RecallSummonsAttackPatch"/>
        /// plays this same trigger for the recall's cast animation, so both
        /// features share one name instead of two copies drifting apart.
        /// </summary>
        internal const string RecallAnimation = "staff_summon";

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ObjectDB), nameof(ObjectDB.Awake), RecallSummonsFeature.FeatureName)
            & ValheimCompat.RequireMethod(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB), RecallSummonsFeature.FeatureName);

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ObjectDB), nameof(ObjectDB.Awake));
            yield return AccessTools.Method(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB));
        }

        private static void Postfix(ObjectDB __instance)
        {
            // An exception escaping here would leave the game with no items.
            try
            {
                Apply(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"RecallSummons: leaving the Dead Raiser's secondary attack blank: {ex}");
            }
        }

        private static void Apply(ObjectDB db)
        {
            if (db?.m_items == null) return;

            foreach (var prefab in db.m_items)
            {
                if (prefab == null) continue;
                if (!string.Equals(prefab.name, StaffPrefabName, StringComparison.Ordinal)) continue;

                var drop = prefab.GetComponent<ItemDrop>();
                var shared = drop != null ? drop.m_itemData?.m_shared : null;
                if (shared == null) return;

                // Idempotent: a blank m_attackAnimation is vanilla's marker
                // for "no secondary attack", so once this has been filled in
                // once, a non-empty animation is proof it was already
                // applied -- re-running (e.g. CopyOtherDB firing again on
                // the same items, or a hot reload) must not clone-of-a-clone
                // an already-patched item.
                if (shared.m_secondaryAttack != null
                    && !string.IsNullOrEmpty(shared.m_secondaryAttack.m_attackAnimation))
                {
                    return;
                }

                // The empty Attack object vanilla ships is not a usable one
                // to start from -- fields like the effect lists and layer
                // masks are only ever initialised inside Attack.Start, which
                // never runs for a blank attack. Cloning the primary attack
                // is the cleanest way to get a fully-formed Attack (every
                // field the staff's real attack already carries), which is
                // then stripped of everything offensive: no resource cost,
                // no projectile, no hit resolution at all.
                var recall = shared.m_attack.Clone();
                recall.m_attackAnimation = RecallAnimation;
                recall.m_attackType = Attack.AttackType.None;
                recall.m_attackEitr = 0f;
                recall.m_attackStamina = 0f;
                recall.m_attackHealth = 0f;
                recall.m_attackHealthPercentage = 0f;
                recall.m_attackProjectile = null;
                recall.m_spawnOnHit = null;
                recall.m_spawnOnTrigger = null;

                shared.m_secondaryAttack = recall;

                RossQoLPlugin.Log.LogInfo("RecallSummons: the Dead Raiser now has a working secondary attack.");
                return;
            }
        }
    }
}
