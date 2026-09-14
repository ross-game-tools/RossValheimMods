using System.Collections.Generic;
using RossQoL.Core.Portals;
using UnityEngine;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// The only ticking object in this mod. Holds the pending capture and
    /// performs the arrival placement.
    /// </summary>
    internal class PortalTamesManager : MonoBehaviour
    {
        public static PortalTamesManager Instance { get; private set; }

        /// <summary>
        /// How long a capture stays valid. A teleport that includes loading a
        /// distant zone can take a while, so this is generous -- but not
        /// unbounded, because an arrival that never registers must not move
        /// creatures to a stale destination minutes later.
        /// </summary>
        private const float PendingExpirySeconds = 30f;

        private PendingArrival _pending;

        /// <summary>
        /// Tracks the teleporting-&gt;not-teleporting transition in Update.
        /// It is the transition that matters, not the state: acting while
        /// still teleporting would place tames at a position the player is
        /// about to leave.
        /// </summary>
        private bool _wasTeleporting;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Records which tames are coming, and takes ownership of each while
        /// they are still loaded and there is still a live ZNetView to claim
        /// through.
        /// </summary>
        public void CaptureDeparture()
        {
            // Re-entrancy guard. Without this, a second capture that finds
            // nothing eligible falls through to `_pending = null` below and
            // silently discards a good capture already in flight.
            if (_pending != null) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            var characters = Character.GetAllCharacters();
            if (characters == null || characters.Count == 0) return;

            var candidates = new List<TameCandidate>(characters.Count);
            var views = new List<ZNetView>(characters.Count);

            foreach (var character in characters)
            {
                if (character == null) continue;

                var view = character.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;

                var ai = character.GetComponent<MonsterAI>();
                bool followingMe = ai != null && ai.GetFollowTarget() == player.gameObject;

                candidates.Add(new TameCandidate(
                    position: ToVec3(character.transform.position),
                    isTamed: character.IsTamed(),
                    isFollowingPlayer: followingMe,
                    isBusy: character.IsAttached() || IsMounted(view)));
                views.Add(view);
            }

            var chosen = TameEligibility.SelectIndices(
                candidates, ToVec3(player.transform.position), PortalTamesConfig.FollowRadius.Value);

            if (chosen.Count == 0)
            {
                _pending = null;
                return;
            }

            var ids = new List<ZDOID>(chosen.Count);
            foreach (int index in chosen)
            {
                var view = views[index];

                // Claim now, while the object is loaded. This is only a head
                // start: ownership is re-asserted at arrival because
                // ZDOMan.ReleaseNearbyZDOS can take it back while the player is
                // far away in transit. See TameMover.
                if (!view.IsOwner()) view.ClaimOwnership();

                ids.Add(view.GetZDO().m_uid);
            }

            _pending = new PendingArrival(ids, Time.time);
            _wasTeleporting = true;

            RossQoLPlugin.Log.LogInfo($"Portal: bringing {ids.Count} tame(s).");
        }

        private void Update()
        {
            if (_pending == null) return;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                // Logged out, or died into a loading screen. Drop the capture:
                // nothing has been moved, and the tames are untouched.
                _pending = null;
                _wasTeleporting = false;
                return;
            }

            if (Time.time - _pending.CapturedAt > PendingExpirySeconds)
            {
                RossQoLPlugin.Log.LogWarning(
                    $"Portal: arrival never registered within {PendingExpirySeconds:F0}s; "
                    + $"leaving {_pending.Tames.Count} tame(s) where they are.");
                _pending = null;
                _wasTeleporting = false;
                return;
            }

            bool teleporting = player.IsTeleporting();

            // The transition matters, not the state: acting while still
            // teleporting would place tames at a position the player is about
            // to leave.
            if (_wasTeleporting && !teleporting)
            {
                PlaceArrivals(player);
                _pending = null;
            }

            _wasTeleporting = teleporting;
        }

        private void PlaceArrivals(Player player)
        {
            var arrival = player.transform.position;
            var facing = player.transform.forward;

            var spots = ArrivalPlacement.Compute(
                ToVec3(arrival),
                ToVec3(facing),
                _pending.Tames.Count,
                PortalTamesConfig.SearchDistance.Value,
                IsFree);

            int moved = 0;
            for (int i = 0; i < _pending.Tames.Count; i++)
            {
                var target = ToVector3(spots[i]);

                // The fallback placement IS the player's own arrival
                // position -- a spot a body is already standing on, and
                // therefore known-good. Snapping it can only make it worse
                // (e.g. finding a solid a couple of metres below a raised
                // floor). Only searched candidates need their Y corrected.
                if (spots[i].Equals(ToVec3(arrival)))
                {
                    if (TameMover.TryMove(_pending.Tames[i], target)) moved++;
                    continue;
                }

                target.y = GroundHeight(target);

                if (TameMover.TryMove(_pending.Tames[i], target)) moved++;
            }

            RossQoLPlugin.Log.LogInfo($"Portal: {moved} of {_pending.Tames.Count} tame(s) arrived.");
        }

        /// <summary>
        /// Whether a creature is currently ridden.
        ///
        /// <see cref="Character.IsRiding"/> is virtual and returns false on
        /// the base class; the only override is on <c>Player</c>, where it
        /// reports whether the PLAYER is mounted, not whether the creature
        /// under them has a rider. The mount side instead has its rider
        /// written into its OWN ZDO: <c>Sadle.RPC_RequestControl</c> calls
        /// <c>m_nview.GetZDO().Set(ZDOVars.s_user, playerID)</c> on the
        /// creature's ZDO (<c>Sadle.Awake</c> resolves <c>m_nview</c> via
        /// <c>GetComponentInParent&lt;Character&gt;().GetComponent&lt;ZNetView&gt;()</c>,
        /// i.e. the creature's own view, not a separate saddle object), and
        /// clears it back to 0 on <c>RPC_ReleaseControl</c>/dismount. A
        /// non-zero <c>ZDOVars.s_user</c> is therefore exactly "someone is
        /// riding this creature right now" -- verified by decompiling
        /// <c>Sadle</c> and <c>ZDOVars</c> against assembly_valheim.dll.
        /// </summary>
        private static bool IsMounted(ZNetView view)
        {
            var zdo = view.GetZDO();
            return zdo != null && zdo.GetLong(ZDOVars.s_user, 0L) != 0L;
        }

        /// <summary>
        /// Whether a creature could stand at this point. Backed by the real
        /// world here; the tests supply their own predicate, which is what
        /// makes the wall case assertable without a running game.
        /// </summary>
        private static bool IsFree(Vec3 point)
        {
            var zones = ZoneSystem.instance;
            if (zones == null) return true;

            var world = ToVector3(point);
            return !zones.IsBlocked(world);
        }

        /// <summary>
        /// Snaps a candidate's Y to the solid directly below it.
        ///
        /// <c>ZoneSystem.GetSolidHeight(Vector3, out float)</c> defaults its
        /// <c>heightMargin</c> parameter to 1000, which raycasts DOWNWARD
        /// from <c>p.y + 1000</c> over <c>m_solidRayMask</c> (which includes
        /// "piece") and returns the height of the FIRST hit -- i.e. the
        /// topmost solid in the whole column. Inside a portal hut that is
        /// the roof, so every arriving tame with the default margin lands on
        /// top of the building.
        ///
        /// A candidate's Y is already the player's own arrival height (Core
        /// leaves Y untouched; see ArrivalPlacement.Rotate), which is by
        /// definition standing room. Starting the raycast only a couple of
        /// metres above that -- comfortably below a hut's roof, comfortably
        /// above uneven ground -- finds the floor under the candidate
        /// instead of the ceiling over the whole zone.
        /// </summary>
        private const int GroundSnapMarginMetres = 2;

        private static float GroundHeight(Vector3 point)
        {
            var zones = ZoneSystem.instance;
            if (zones == null) return point.y;

            return zones.GetSolidHeight(point, out float height, GroundSnapMarginMetres) ? height : point.y;
        }

        internal static Vec3 ToVec3(Vector3 v) => new Vec3(v.x, v.y, v.z);

        internal static Vector3 ToVector3(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
