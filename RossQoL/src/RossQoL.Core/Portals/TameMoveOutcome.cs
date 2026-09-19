namespace RossQoL.Core.Portals
{
    /// <summary>
    /// Why one captured tame did, or did not, arrive.
    ///
    /// The move itself has several independent ways to fail and, until this
    /// existed, all of them looked identical from a log: "0 of 1 tame(s)
    /// arrived". They are not remotely the same problem -- a missing ZDO means
    /// the creature was destroyed while the player was in transit, while a
    /// refused ownership claim means it still exists and something else holds
    /// it. Naming them separately is what turns one portal trip into an
    /// answer.
    /// </summary>
    public enum TameMoveOutcome
    {
        /// <summary>The position was written. The creature arrived.</summary>
        Moved,

        /// <summary>The captured id was blank, so there was never anything to find.</summary>
        NoId,

        /// <summary>Networking was not available to look the creature up at all.</summary>
        NoZdoMan,

        /// <summary>
        /// The creature no longer exists: its ZDO has been destroyed. Killed
        /// in transit, or despawned by the game -- a summon whose unsummon
        /// rules fired while the player was away is the known case.
        /// </summary>
        ZdoMissing,

        /// <summary>
        /// The creature exists, but this client could not take ownership of
        /// it, so a position written here would be discarded.
        /// </summary>
        OwnershipRefused,
    }

    /// <summary>
    /// The pure half of <c>TameMover.Move</c>: given what the game answered at
    /// each step, which outcome is that. Separated so the decision is testable
    /// without a running game, and so the order of the checks -- which is what
    /// makes a diagnosis unambiguous -- is pinned by tests rather than by the
    /// shape of a method body that happens to return early.
    /// </summary>
    public static class TameMoveOutcomes
    {
        public static TameMoveOutcome Classify(
            bool hasId, bool networkReady, bool zdoFound, bool ownershipHeld)
        {
            // Deliberately ordered from "we never even looked" outward, so an
            // earlier failure is always reported in preference to a later
            // one. A blank id cannot meaningfully also be "not found".
            if (!hasId) return TameMoveOutcome.NoId;
            if (!networkReady) return TameMoveOutcome.NoZdoMan;
            if (!zdoFound) return TameMoveOutcome.ZdoMissing;
            if (!ownershipHeld) return TameMoveOutcome.OwnershipRefused;

            return TameMoveOutcome.Moved;
        }

        /// <summary>Whether an outcome means the creature actually arrived.</summary>
        public static bool Arrived(TameMoveOutcome outcome) => outcome == TameMoveOutcome.Moved;
    }
}
