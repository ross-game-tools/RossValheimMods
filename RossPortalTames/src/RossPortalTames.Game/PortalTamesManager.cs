using System.Collections.Generic;
using RossPortalTames.Core;
using UnityEngine;

namespace RossPortalTames.Game
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
                    isBusy: character.IsAttached() || character.IsRiding()));
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

            PortalTamesPlugin.Log.LogInfo($"Portal: bringing {ids.Count} tame(s).");
        }

        internal static Vec3 ToVec3(Vector3 v) => new Vec3(v.x, v.y, v.z);

        internal static Vector3 ToVector3(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
