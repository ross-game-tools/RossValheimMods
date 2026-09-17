using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RossQoL.Core.Production;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Whether a producer is fed on this call: the owning feature on, its
    /// kind on, a local player, the producer owned by this client, ward
    /// access at the producer, and FeedInterval passed since its last
    /// attempt. Config is read here on every call.
    ///
    /// The owning feature is passed in because feeding is split across
    /// categories: Production feeds the workshop, Fires feeds the fires, and
    /// each is switched on and off on its own.
    ///
    /// The same shape as HarvestGate, kept separate so feeding and harvesting
    /// keep their own intervals and their own throttled logging.
    /// </summary>
    internal static class FeedGate
    {
        // Keyed by the instance itself, so an entry dies with its producer.
        private static readonly ConditionalWeakTable<MonoBehaviour, StrongBox<double>> LastAttempt =
            new ConditionalWeakTable<MonoBehaviour, StrongBox<double>>();

        /// <param name="kindEnabled">The per-kind setting, or null when the feature's own toggle is the only one.</param>
        public static bool ShouldRun(
            MonoBehaviour producer, ZNetView nview, Feature owner, ConfigEntry<bool> kindEnabled = null)
        {
            if (owner?.IsActive != true) return false;
            if (kindEnabled != null && !kindEnabled.Value) return false;
            if (Player.m_localPlayer == null) return false;
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return false;

            // No ward access, no feeding: the same anti-griefing rule
            // harvesting uses, so another player's base is left alone.
            if (!PrivateArea.CheckAccess(producer.transform.position, 0f, flash: false)) return false;

            double now = Time.time;
            float interval = AutoFeedConfig.FeedInterval?.Value ?? 5f;
            bool seen = LastAttempt.TryGetValue(producer, out var last);
            if (!HarvestMath.IsDue(seen ? last.Value : (double?)null, now, interval)) return false;

            if (seen) last.Value = now;
            else LastAttempt.Add(producer, new StrongBox<double>(now));
            return true;
        }

        // The exception type last logged per producer, so a failure that
        // repeats every interval is logged once, not once per attempt.
        private static readonly ConditionalWeakTable<MonoBehaviour, StrongBox<string>> LastFailure =
            new ConditionalWeakTable<MonoBehaviour, StrongBox<string>>();

        public static void LogFailure(MonoBehaviour producer, Exception ex)
        {
            string type = ex.GetType().FullName;
            if (producer != null)
            {
                if (LastFailure.TryGetValue(producer, out var last))
                {
                    if (last.Value == type) return;
                    last.Value = type;
                }
                else
                {
                    LastFailure.Add(producer, new StrongBox<string>(type));
                }
            }

            string name = producer ? producer.name : "a destroyed producer";
            RossQoLPlugin.Log.LogError(
                $"AutoFeed: feeding {name} failed and was skipped (repeats of this error are not logged): {ex}");
        }

        public static void Succeeded(MonoBehaviour producer) => LastFailure.Remove(producer);
    }
}
