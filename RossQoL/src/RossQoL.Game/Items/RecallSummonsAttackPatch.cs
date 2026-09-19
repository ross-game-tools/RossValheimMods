using System;
using HarmonyLib;
using RossQoL.Core.Items;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Where the Dead Raiser's secondary attack actually recalls your
    /// skeletons.
    ///
    /// Patched on <c>Humanoid.StartAttack(Character target, bool
    /// secondaryAttack)</c> rather than <c>Attack.Start</c>: the latter runs
    /// on a bare <c>Attack</c> clone that has no idea whether it is the
    /// primary or the secondary -- both now share the very same
    /// "staff_summon" animation name (see <see cref="RecallSummonsItemPatch"/>),
    /// so there is no way to tell them apart from inside <c>Attack.Start</c>
    /// itself. <c>Humanoid.StartAttack</c> is called with an explicit
    /// <c>secondaryAttack</c> flag and can look up the current weapon
    /// directly, which is exactly what is needed here.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class RecallSummonsAttackPatch
    {
        private const string StaffPrefabName = "StaffSkeleton";

        /// <summary>
        /// When the recall last fired, in <see cref="Time.time"/>. One
        /// process-wide timer rather than per-player: this mod only ever
        /// acts for the local player, so there is only ever one clock to
        /// keep.
        /// </summary>
        private static float? _lastRecallTime;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Humanoid), nameof(Humanoid.StartAttack), RecallSummonsFeature.FeatureName);

        private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
        {
            // An exception escaping here would break every attack, not just
            // this staff's -- Humanoid.StartAttack runs for every weapon.
            try
            {
                if (!secondaryAttack) return true;
                if (!(__instance is Player player) || player != Player.m_localPlayer) return true;

                var weapon = player.GetCurrentWeapon();
                if (weapon == null || weapon.m_dropPrefab == null) return true;
                if (!string.Equals(weapon.m_dropPrefab.name, StaffPrefabName, StringComparison.Ordinal)) return true;

                // From here this IS the Dead Raiser's secondary attack, and
                // it is always handled here rather than by vanilla's own
                // Attack.Start -- not just when the feature is active.
                // Attack.GetAttackEitr reads weapon.m_shared.m_attack
                // .m_attackEitr, the PRIMARY attack's own field, no matter
                // which Attack instance (primary or secondary) is the one
                // actually running. Letting vanilla resolve this secondary
                // attack at all would silently draw the summon spell's own
                // 100-eitr cost a second time. Driving the whole thing here
                // instead, and never calling the original method, is the
                // only way to guarantee no eitr, stamina or health is ever
                // spent by this attack.
                __result = TryRecall(player);
                return false;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"RecallSummons: leaving the secondary attack to vanilla: {ex}");
                return true;
            }
        }

        private static bool TryRecall(Player player)
        {
            if (RecallSummonsFeature.Instance?.IsActive != true) return false;

            // Casts are never queued: while one is already in flight, a
            // second middle-click does nothing, the same as pressing an
            // attack key mid-swing does nothing for a normal weapon.
            var runner = RecallCastRunner.Instance;
            if (runner != null && runner.IsPending) return false;

            float cooldown = RecallSummonsConfig.RecallCooldownSeconds?.Value ?? 8f;
            float now = Time.time;
            if (!RecallCooldown.CanFire(_lastRecallTime, now, cooldown)) return false;

            // The cooldown starts on the attempt, not on a successful
            // recall: pressing the button with nothing to bring back is
            // still "using" the attack, exactly like swinging a weapon at
            // empty air still resets its own cooldown. It also starts
            // immediately, not when the cast completes -- the cast time is
            // meant to feel like a spell being cast, not an extra delay
            // bolted onto the cooldown.
            _lastRecallTime = now;

            PlayCastAnimation(player);
            PlayCastSound(player);

            float castSeconds = RecallSummonsConfig.RecallCastSeconds?.Value ?? 0.5f;

            // Zero means instant, and must not schedule anything at all --
            // not even a same-frame pending state, so there is nothing for
            // Update to catch and nothing that could be cancelled.
            if (castSeconds <= 0f || runner == null)
            {
                FinishRecall(player);
            }
            else
            {
                runner.Begin(castSeconds);
            }

            return true;
        }

        /// <summary>
        /// Moves the skeletons. Called either immediately (zero cast time)
        /// or by <see cref="RecallCastRunner"/> once a nonzero cast
        /// completes -- both paths end here so there is exactly one place
        /// that does the actual recall and logs it.
        /// </summary>
        internal static void FinishRecall(Player player)
        {
            int moved = RecallSummonsManager.Recall(player);
            if (moved > 0) RossQoLPlugin.Log.LogInfo($"RecallSummons: {moved} skeleton(s) recalled.");
        }

        /// <summary>
        /// Plays the Dead Raiser's own primary-attack start effect -- the
        /// sound and visual flourish vanilla plays the instant the staff
        /// begins raising a skeleton (<c>Attack.Start</c> does
        /// <c>m_weapon.m_shared.m_startEffect.Create(...)</c>, read from the
        /// decompiled 1.0.14 source). Reusing it rather than inventing a new
        /// sound means the recall uses audio the player already associates
        /// with this exact staff, and sits correctly in the mix because it
        /// is one of vanilla's own effects.
        ///
        /// <c>m_startEffect</c> is a distinct field from
        /// <c>m_attackProjectile</c> (the thing that actually raises a
        /// skeleton, via that projectile's own <c>SpawnAbility</c>) -- it is
        /// vanilla's "cast flourish" list, played at the start of every
        /// attack alongside, not instead of, the projectile. Nothing about
        /// summoning a creature runs through <c>EffectList.Create</c>; that
        /// method only instantiates each listed prefab in place (see
        /// <c>EffectList.cs</c>), so calling it here cannot itself raise a
        /// skeleton no matter what the list contains.
        ///
        /// Played at the player's position via <c>EffectList.Create</c> --
        /// the same call vanilla's own <c>Attack.Start</c> makes for this
        /// exact effect -- rather than through <c>Attack.Start</c> itself,
        /// which is exactly what this feature exists to avoid (see the
        /// class summary).
        /// </summary>
        private static void PlayCastSound(Player player)
        {
            try
            {
                var weapon = player.GetCurrentWeapon();
                if (weapon == null || weapon.m_shared == null || weapon.m_shared.m_attack == null) return;

                var startEffect = weapon.m_shared.m_attack.m_startEffect;
                if (startEffect == null || !startEffect.HasEffects())
                {
                    RossQoLPlugin.Log.LogWarning(
                        "RecallSummons: the Dead Raiser's primary attack has no start effect to reuse; "
                        + "the recall will cast silently.");
                    return;
                }

                startEffect.Create(player.transform.position, player.transform.rotation);
            }
            catch (Exception ex)
            {
                // Exactly like PlayCastAnimation: the recall itself must not
                // be affected by a failed sound.
                RossQoLPlugin.Log.LogError($"RecallSummons: recall succeeded but the cast sound failed: {ex}");
            }
        }

        /// <summary>
        /// Plays the staff's own "staff_summon" animation so the recall
        /// looks like a cast, instead of firing with no visible hand
        /// movement at all.
        ///
        /// Deliberately just the animation trigger, not the rest of what
        /// <c>Attack.Start</c> would normally do (attack state, weapon
        /// cooldown, movement slowdown, attack-facing rotation): none of
        /// that is needed for a cast to look right, and going through
        /// <c>Attack.Start</c> at all is exactly what this feature exists to
        /// avoid -- <c>Attack.GetAttackEitr</c> would charge the staff's
        /// primary 100-eitr cost. <c>Character.GetZAnim().SetTrigger(name)</c>
        /// is vanilla's own trigger call (<c>Attack.Start</c> does
        /// <c>m_zanim.SetTrigger(m_attackAnimation)</c>, read from the
        /// decompiled 1.0.14 source), and <c>ZSyncAnimation.SetTrigger</c>
        /// itself sends a routed RPC to every peer
        /// (<c>m_nview.InvokeRPC(ZNetView.Everybody, "SetTrigger", name)</c>),
        /// so this replicates to other clients exactly like a real attack's
        /// animation does, with no separate sync code needed here.
        /// </summary>
        private static void PlayCastAnimation(Player player)
        {
            try
            {
                var zanim = player.GetZAnim();
                if (zanim == null) return;

                zanim.SetTrigger(RecallSummonsItemPatch.RecallAnimation);
            }
            catch (Exception ex)
            {
                // The recall itself must not be affected by a failed
                // animation -- swallow here rather than letting this
                // exception reach the outer catch in Prefix, which would
                // fall back to vanilla's Attack.Start and risk firing the
                // primary attack's 100-eitr cost a second time after the
                // recall has already happened.
                RossQoLPlugin.Log.LogError($"RecallSummons: recall succeeded but the cast animation failed: {ex}");
            }
        }
    }
}
