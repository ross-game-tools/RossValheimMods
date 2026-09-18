using System;
using UnityEngine;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// The day a dungeon was last visited, written by whoever owns it.
    ///
    /// The stamp lives on the dungeon's own ZDO, which is world state: it sits
    /// in the server's save beside the room data Valheim keeps there, and
    /// reaches every client that has the dungeon loaded. Reading it needs
    /// nothing. Writing it needs to be done by the owner, and on a dedicated
    /// server that is usually the server rather than any player.
    ///
    /// Claiming ownership just long enough to write does not work: the claim
    /// is provisional, and when the real owner asserts itself again its copy
    /// of the ZDO wins and the field is silently gone. Measured, not guessed --
    /// a client stamping this way logged the write on every sweep it owned the
    /// dungeon for, and the field read back empty a minute later, every time.
    ///
    /// So the write is asked for rather than taken: vanilla's own RPC, sent to
    /// the owner, exactly as adding fuel to someone else's fire does.
    /// </summary>
    internal static class DungeonVisits
    {
        private const string VisitRpc = "RossQoL_DungeonVisit";

        /// <summary>Our own ZDO field: the in-game day of the last visit.</summary>
        public static readonly int VisitDayKey = "rossqol_dungeonVisitDay".GetStableHashCode();

        /// <summary>
        /// Listens for visit stamps on one dungeon. Registered on every peer
        /// that loads it, the server included, because any of them may be the
        /// one holding the dungeon when a stamp arrives.
        /// </summary>
        public static void Listen(DungeonGenerator generator)
        {
            if (generator == null) return;

            try
            {
                var nview = generator.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return;

                nview.Register<int>(VisitRpc, (sender, day) => Receive(nview, day));
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"DungeonRespawn: could not listen for visits on a dungeon: {ex}");
            }
        }

        /// <summary>The day stamped on a dungeon, or null when it carries none. Needs no ownership.</summary>
        public static int? Read(ZNetView nview)
        {
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null) return null;

            return zdo.GetInt(VisitDayKey, out int day) ? day : (int?)null;
        }

        /// <summary>
        /// Stamps a dungeon with today. Written here when this client owns it,
        /// and asked of the owner when it does not.
        /// </summary>
        public static void Stamp(ZNetView nview, int today)
        {
            if (nview == null || !nview.IsValid()) return;

            try
            {
                if (nview.IsOwner())
                {
                    Receive(nview, today);
                    return;
                }

                // A dungeon nobody holds -- the common case in a single-player
                // world, where objects sit unowned until something needs them
                // -- is claimed and written here. An RPC would be sent to an
                // owner that does not exist and quietly go nowhere, which is
                // exactly how the stamp went missing before.
                var zdo = nview.GetZDO();
                if (zdo != null && !zdo.HasOwner())
                {
                    nview.ClaimOwnership();
                    if (nview.IsOwner())
                    {
                        Receive(nview, today);
                        return;
                    }
                }

                nview.InvokeRPC(VisitRpc, today);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"DungeonRespawn: could not stamp a dungeon's visit: {ex}");
            }
        }

        /// <summary>The write itself, only ever done by the owner.</summary>
        private static void Receive(ZNetView nview, int day)
        {
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

            var zdo = nview.GetZDO();
            if (zdo == null) return;

            if (zdo.GetInt(VisitDayKey, out int stamped) && stamped == day) return;

            zdo.Set(VisitDayKey, day);
        }
    }
}
