using System.Collections.Generic;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// The tames captured for one teleport, and when they were captured.
    ///
    /// ZDOIDs rather than GameObjects or components, because by the time this
    /// is acted on the creature objects no longer exist: the departure zone
    /// unloads while the player is in transit and Unity destroys them. The ZDOs
    /// survive, and a ZDOID is how you find one again.
    /// </summary>
    internal sealed class PendingArrival
    {
        public IReadOnlyList<ZDOID> Tames { get; }
        public float CapturedAt { get; }

        public PendingArrival(IReadOnlyList<ZDOID> tames, float capturedAt)
        {
            Tames = tames;
            CapturedAt = capturedAt;
        }
    }
}
