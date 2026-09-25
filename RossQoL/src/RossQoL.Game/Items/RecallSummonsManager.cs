using System.Collections.Generic;
using RossQoL.Core.Portals;
using RossQoL.Game.Portals;
using UnityEngine;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Brings every skeleton the Dead Raiser raised, and that is still
    /// following this player, back to their side.
    ///
    /// A summoned skeleton is, at the engine level, an ordinary tame that was
    /// auto-commanded to follow its caster at spawn -- see
    /// docs/valheim-api/summons.md. That is why this reuses
    /// <see cref="PortalTamesManager"/>'s own "is this following me" test
    /// rather than inventing a second one, and <see cref="TameMover"/> for
    /// the move itself: a summon and a portal-carried tame are the same kind
    /// of object once spawned, so the same primitives apply unchanged.
    /// </summary>
    internal static class RecallSummonsManager
    {
        /// <summary>How far from the arrival point to search for a clear spot for each skeleton.</summary>
        private const float SearchDistance = 6f;

        /// <summary>
        /// Moves every following, summoned skeleton to the player, spread
        /// around them rather than stacked. Returns how many were moved;
        /// zero is not a failure -- it means there was nothing to bring
        /// back, which is the common case once a fight is already over.
        /// </summary>
        public static int Recall(Player player)
        {
            var ids = FindFollowingSkeletons(player);
            if (ids.Count == 0) return 0;

            var arrival = player.transform.position;
            var facing = player.transform.forward;
            var arrivalVec = PortalTamesManager.ToVec3(arrival);

            var spots = ArrivalPlacement.Compute(
                arrivalVec, PortalTamesManager.ToVec3(facing), ids.Count, SearchDistance, PortalTamesManager.IsFree);

            int moved = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                var target = PortalTamesManager.ToVector3(spots[i]);

                // As in PortalTamesManager.PlaceArrivals: the fallback spot IS
                // the player's own position, already known-good, so only a
                // spot the search actually found needs its height snapped.
                if (!spots[i].Equals(arrivalVec)) target.y = PortalTamesManager.GroundHeight(target);

                if (TameMover.TryMove(ids[i], target)) moved++;
            }

            return moved;
        }

        /// <summary>
        /// Every currently-loaded creature following this player, filtered
        /// to summoned skeletons. Uses the identical "following me" test as
        /// <see cref="PortalTamesManager.CaptureDeparture"/>: a tame another
        /// client owns follows this player only as far as the ZDO's saved
        /// follow name, since <c>Tameable.Command</c> (the RPC that sets a
        /// live <c>MonsterAI</c> follow target) is routed to the tame's
        /// owner, not to whoever is asking.
        /// </summary>
        private static List<ZDOID> FindFollowingSkeletons(Player player)
        {
            var ids = new List<ZDOID>();

            var characters = Character.GetAllCharacters();
            if (characters == null || characters.Count == 0) return ids;

            foreach (var character in characters)
            {
                if (character == null) continue;
                if (!SummonedMinion.Is(character)) continue;

                var view = character.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;

                var ai = character.GetComponent<MonsterAI>();
                bool followingMe = ai != null
                    && (ai.GetFollowTarget() == player.gameObject
                        || view.GetZDO().GetString(ZDOVars.s_follow) == player.GetPlayerName());
                if (!followingMe) continue;

                ids.Add(view.GetZDO().m_uid);
            }

            return ids;
        }
    }
}
