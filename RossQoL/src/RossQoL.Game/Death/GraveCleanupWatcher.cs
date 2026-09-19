using System;
using System.Collections.Generic;
using RossQoL.Core.Death;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Forgets graves that have been emptied. Owned by the category rather
    /// than by any one feature: a server switching the corpse run buff on as
    /// a world rule must not depend on each client leaving a cosmetic marker
    /// switched on for the buff to ever end, and a looted grave must stop
    /// being remembered whether or not anything is drawing it.
    ///
    /// The grave is looked up by WHERE it is and WHOSE it is, never by the
    /// tombstone's ZDOID. A ZDO's id does not survive a save and load --
    /// `ZDO.Load` re-keys every loaded ZDO (`m_uid.SetID(++ZDOID.m_loadID)`),
    /// so an id written down in one session matches nothing in the next and
    /// `ZDOMan.GetZDO` can only ever return null for it. That is exactly the
    /// bug this replaced: the lookup failed forever after a relog, so no
    /// sighting was ever taken, and the record could never be forgotten --
    /// a marker and a Just Died buff that outlived the looted grave. See
    /// docs/valheim-api/death-and-respawn.md §12.
    ///
    /// Added to the mod's shared host object by every feature that needs a
    /// grave record, once -- see <see cref="EnsureOn"/>.
    /// </summary>
    internal sealed class GraveCleanupWatcher : MonoBehaviour
    {
        private const float CheckSeconds = 1f;

        /// <summary>
        /// How close the player has to be for a missing tombstone to mean
        /// "looted" rather than "this zone simply is not loaded". 64m is
        /// comfortably inside the zone a nearby player already has loaded.
        /// </summary>
        private const float ZoneLoadedRange = 64f;

        /// <summary>
        /// How long after arriving in a world before a grave may be forgotten.
        /// Belt and braces alongside the sighting rule: the very first tick in
        /// a new world lands while zones are still streaming, and there is
        /// nothing to gain from judging a grave that early.
        /// </summary>
        private const float ArmSeconds = 10f;

        /// <summary>
        /// The Valheim members this watcher reaches by name. Spliced into the
        /// RequiredMembers of every feature that causes it to be added, so a
        /// rename here disables exactly the features that depend on it.
        /// </summary>
        internal static IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("TombStone", "GetOwnerName", "finding the tombstone still standing over a remembered grave"),
            new CompatMember("ZNetView", "IsValid", "ignoring tombstones whose networking is not up yet"),
            new CompatMember("ZNetView", "GetZDO", "reading which player a tombstone belongs to"),
            new CompatMember("ZDOVars", "s_owner", "matching a tombstone to the player who left it"),
            new CompatMember("Game", "GetPlayerProfile", "matching a tombstone to its owner"),
            new CompatMember("PlayerProfile", "GetPlayerID", "matching a tombstone to its owner"),
        };

        /// <summary>
        /// The one watcher on the shared host, so the death hook can record a
        /// sighting for the grave it has just written down.
        /// </summary>
        private static GraveCleanupWatcher _instance;

        /// <summary>
        /// Graves whose tombstone has been seen STANDING at least once in this
        /// world-session. Without this, a grave whose zone simply has not
        /// streamed in yet -- the ordinary case right after a respawn near
        /// your own grave -- looks identical to a looted one, and would be
        /// forgotten before its tombstone ever had a chance to spawn, costing
        /// the player the record of where their gear is.
        ///
        /// Keyed by the record's own stored id, which is a name for the
        /// record rather than a live handle on anything.
        ///
        /// This component lives on the mod's DontDestroyOnLoad host and is
        /// never rebuilt, so the per-world part is not automatic the way it
        /// was when this lived on a HUD component: <see cref="GraveSightings"/>
        /// holds the sightings against the world they were made in and drops
        /// them on any change, and <see cref="Tick"/> reports the current
        /// world on every pass -- including 0 at the main menu, which is what
        /// makes leaving and rejoining the same world re-arm the rule.
        /// </summary>
        private readonly GraveSightings _sightings = new GraveSightings();

        private readonly List<GraveRecord> _inRange = new List<GraveRecord>();

        private readonly List<GraveRecord> _looted = new List<GraveRecord>();

        private float _nextCheck;

        /// <summary>Time.unscaledTime from which graves may be forgotten again; set on every world change.</summary>
        private float _armAt;

        /// <summary>
        /// Adds the watcher to the shared host if it is not already there.
        /// Several features ask for it and each one's OnActivated runs, so
        /// the guard is what keeps a single watcher rather than one per
        /// feature.
        /// </summary>
        internal static void EnsureOn(GameObject host)
        {
            // Explicit Unity-aware null check, not `?.`: a destroyed
            // GameObject still passes a reference-null test.
            if (host == null) return;
            if (host.GetComponent<GraveCleanupWatcher>() != null) return;

            host.AddComponent<GraveCleanupWatcher>();
        }

        /// <summary>
        /// Records that a grave was standing the moment it was written down.
        /// The death hook is looking straight at the tombstone it recorded,
        /// which is the strongest sighting there is, and taking it here means
        /// a grave looted moments later -- before any 1s pass ever caught the
        /// tombstone standing -- is still clearable in that same session.
        /// </summary>
        internal static void NoteJustBuried(uint recordId)
        {
            var watcher = _instance;
            if (watcher == null) return;

            if (watcher._sightings.EnterWorld(DeathState.WorldId)) watcher._armAt = Time.unscaledTime + ArmSeconds;
            watcher._sightings.Observe(recordId);
        }

        private void Awake() => _instance = this;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            // Nothing here may throw past Update -- an exception escaping a
            // MonoBehaviour callback would stop this component ticking for
            // the rest of the session, and looted graves would then be
            // remembered forever.
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Death: the looted-grave check failed: {ex}");
            }
        }

        private void Tick()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + CheckSeconds;

            // Before every other test, including the ones that return early:
            // a world this watcher never reported would never be noticed as a
            // change, and the trip through the main menu (world 0) is exactly
            // what re-arms the sighting rule for a rejoin of the same world.
            if (_sightings.EnterWorld(DeathState.WorldId)) _armAt = Time.unscaledTime + ArmSeconds;

            // Nothing left that cares where a grave is: leave the store
            // exactly as it is rather than quietly emptying it.
            if (!DeathCategory.NeedsGraveRecord) return;
            if (Player.m_localPlayer == null) return;
            if (global::Game.instance == null) return;

            var position = Player.m_localPlayer.transform.position;

            // Which graves are close enough to be judged at all. Gathered
            // first because finding every tombstone in the scene is the
            // expensive half, and the usual pass has nothing in range.
            _inRange.Clear();
            foreach (var grave in DeathState.Graves())
            {
                var at = new Vector3(grave.X, grave.Y, grave.Z);
                if (Vector3.Distance(position, at) <= ZoneLoadedRange) _inRange.Add(grave);
            }

            if (_inRange.Count == 0) return;

            long playerId = global::Game.instance.GetPlayerProfile().GetPlayerID();
            var tombstones = UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None);

            // Sightings are always worth taking; only the forgetting waits for
            // the new world to have had a moment to stream in.
            bool mayForget = Time.unscaledTime >= _armAt;

            _looted.Clear();
            foreach (var grave in _inRange)
            {
                var verdict = GraveClearRule.Decide(
                    inRange: true,
                    tombstoneStanding: IsStanding(tombstones, playerId, grave),
                    seenThisSession: _sightings.HasSeen(grave.ZdoId),
                    armed: mayForget);

                if (verdict == GraveVerdict.Observe) _sightings.Observe(grave.ZdoId);
                else if (verdict == GraveVerdict.Forget) _looted.Add(grave);
            }

            // Forgotten after the walk, never during it: Forget rewrites the
            // stored string the list above was parsed from.
            foreach (var grave in _looted) DeathState.Forget(grave);
            _looted.Clear();
            _inRange.Clear();
        }

        /// <summary>
        /// True when one of this player's tombstones is standing at the
        /// recorded spot. Matched by owner as well as position so another
        /// player's grave in the same place is never read as this one.
        /// </summary>
        private static bool IsStanding(TombStone[] tombstones, long playerId, GraveRecord grave)
        {
            foreach (var candidate in tombstones)
            {
                // Explicit Unity-aware null checks, not `?.`: a destroyed
                // component still passes a reference-null test, and the
                // tombstone destroyed on this very frame is the case that
                // matters most here.
                if (candidate == null) continue;

                var nview = candidate.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;
                if (nview.GetZDO().GetLong(ZDOVars.s_owner, 0L) != playerId) continue;

                var at = candidate.transform.position;
                if (GraveClearRule.Matches(grave.X, grave.Y, grave.Z, at.x, at.y, at.z)) return true;
            }

            return false;
        }
    }
}
