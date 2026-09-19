using RossQoL.Core.Portals;
using UnityEngine;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// The entire write side of this mod: re-assert ownership, set a position.
    ///
    /// Nothing is destroyed, nothing is recreated, no creature state is copied.
    /// That is why every failure here is harmless -- a tame that cannot be
    /// moved simply stays where it was, alive and still yours. A despawn and
    /// respawn would have to carry level, name, tameness, health and pregnancy
    /// by hand, and anything missed would be lost silently.
    /// </summary>
    internal static class TameMover
    {
        /// <summary>
        /// Convenience wrapper for callers that only care whether the creature
        /// arrived. <see cref="Move"/> is the one that says why it did not.
        /// </summary>
        public static bool TryMove(ZDOID id, Vector3 destination) =>
            TameMoveOutcomes.Arrived(Move(id, destination));

        /// <summary>
        /// Moves one creature, and names which step failed if it did not move.
        ///
        /// The outcome is not decoration: "0 of 1 arrived" was indistinguishable
        /// between a creature that had been destroyed and one this client could
        /// not claim, which are opposite problems with opposite fixes.
        /// </summary>
        public static TameMoveOutcome Move(ZDOID id, Vector3 destination)
        {
            if (id == ZDOID.None)
                return TameMoveOutcomes.Classify(false, false, false, false);

            var man = ZDOMan.instance;
            if (man == null)
                return TameMoveOutcomes.Classify(true, false, false, false);

            // Null means the creature no longer exists -- its ZDO has been
            // destroyed. Killed while the player was in transit, or despawned
            // by the game: vanilla's summon-unsummon path (Tameable.UnSummon ->
            // RPC_UnSummon -> ZNetScene.Destroy -> ZDOMan.DestroyZDO) is the
            // known way for a captured summon to vanish mid-hop, which is what
            // SummonUnsummonGuard exists to prevent.
            var zdo = man.GetZDO(id);
            if (zdo == null)
                return TameMoveOutcomes.Classify(true, true, false, false);

            // Re-assert ownership immediately before the write, with nothing
            // in between. The claim taken at departure is NOT enough:
            // ZDOMan.ReleaseNearbyZDOS reassigns ownership by proximity every
            // couple of seconds, and the player is far from these creatures for
            // the whole of the teleport. A write into a ZDO this client no
            // longer owns is lost or overwritten by whoever picked it up --
            // intermittently, and more often on a busy server.
            //
            // Both calls work on a bare ZDO with no live GameObject, which is
            // what makes this possible at all: the creature objects were
            // destroyed when their zone unloaded.
            zdo.SetOwner(ZDOMan.GetSessionID());
            if (!zdo.IsOwner())
                return TameMoveOutcomes.Classify(true, true, true, false);

            zdo.SetPosition(destination);

            // The design assumes the creature's zone unloads during the
            // teleport, destroying its GameObject and leaving only the ZDO
            // to write into. That holds for a long-distance hop but not for
            // a portal-hub jump or any destination inside the area already
            // loaded on this client: the GameObject survives, and
            // ZSyncTransform.OwnerSync writes transform.position back into
            // the ZDO every frame for as long as this client owns it --
            // which, thanks to the SetOwner call just above, is now. Left
            // alone, that overwrites the position we just set on the very
            // next frame, so the move silently fails whenever the creature
            // stayed loaded.
            //
            // ZNetScene.FindInstance(ZDOID) returns the live GameObject for
            // a ZDOID if this client still has one instantiated, or null
            // otherwise. When it is non-null we move its transform too --
            // a position write, same as the ZDO write above, not a destroy,
            // a recreate, or a state copy.
            // Moving the transform ALONE is not enough, and this is the
            // part that is easy to get wrong. ZSyncTransform.GetPosition --
            // the method deciding what gets pushed back into the ZDO on the
            // next sync -- is:
            //
            //     if (!m_body) return transform.position;
            //     return m_body.position;
            //
            // Every creature has a Rigidbody (Character.Awake always assigns
            // m_body), so for a creature that stayed loaded the sync ignores
            // transform.position entirely and reads the Rigidbody's. Setting
            // only the transform therefore either gets reverted on the next
            // frame, or leaves the ZDO claiming one position while the
            // physically simulated animal stands somewhere else.
            //
            // Valheim sets both together wherever it relocates a Character
            // itself -- see Character.UnderWorldCheck, which does exactly
            // `transform.position = pos; m_body.position = pos;`. We follow
            // that, then SyncTransforms so the physics engine's own copy
            // agrees before anything else reads it this frame.
            var scene = ZNetScene.instance;
            if (scene != null)
            {
                var go = scene.FindInstance(id);
                if (go != null)
                {
                    go.transform.position = destination;

                    var body = go.GetComponent<Rigidbody>();
                    if (body != null) body.position = destination;

                    Physics.SyncTransforms();
                }
            }

            return TameMoveOutcome.Moved;
        }
    }
}
