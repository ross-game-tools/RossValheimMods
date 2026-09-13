using UnityEngine;

namespace RossPortalTames.Game
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
        public static bool TryMove(ZDOID id, Vector3 destination)
        {
            if (id == ZDOID.None) return false;

            var man = ZDOMan.instance;
            if (man == null) return false;

            // Null means the creature no longer exists -- killed while the
            // player was in transit, most likely. Nothing to move.
            var zdo = man.GetZDO(id);
            if (zdo == null) return false;

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
            if (!zdo.IsOwner()) return false;

            zdo.SetPosition(destination);
            return true;
        }
    }
}
