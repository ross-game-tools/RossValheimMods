using System.Collections.Generic;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Counters for diagnosing multiplayer withdraw behaviour, dumped by the
    /// rid_diag console command.
    ///
    /// Exists because the two symptoms reported -- withdrawals lagging, and
    /// "Try again" appearing often, both only when other players are nearby
    /// -- have several candidate causes that call for different fixes, and
    /// guessing between them costs a round trip through someone else's
    /// dedicated server each time. The counters distinguish them:
    ///
    ///   OwnershipChanges high, ClaimsMade high   -> peers trading ownership;
    ///                                               the claim cycle is the
    ///                                               problem, not latency.
    ///   RefusedUnsettled high, ClaimsMade ~0     -> ownership is churning for
    ///                                               a reason outside this mod
    ///                                               (ZDOMan proximity).
    ///   RequestsSent high, GrantLatency high     -> ordinary RPC round trips;
    ///                                               the drawer is simply owned
    ///                                               by someone else.
    ///
    /// Plain static fields and no allocation on the hot paths: this ships
    /// enabled, so it must cost nothing to leave in.
    /// </summary>
    internal static class DrawerDiagnostics
    {
        public static int RefusedUnsettled;
        public static int OwnershipChanges;
        public static int ClaimsMade;
        public static int RequestsSent;

        /// <summary>Drawers claimed because nobody owned them, rather than refusing the player.</summary>
        public static int UnownedClaims;
        public static int GrantsReceived;
        public static int GrantsGivenUp;

        private static double _grantLatencyTotalMs;
        private static double _grantLatencyMaxMs;

        /// <summary>Request id -> send time, so a grant can be timed against its own send.</summary>
        private static readonly Dictionary<long, float> _sentAt = new Dictionary<long, float>();

        public static void RequestSent(long id, float now)
        {
            RequestsSent++;
            _sentAt[id] = now;
        }

        public static void GrantReceived(long id, float now)
        {
            GrantsReceived++;
            if (!_sentAt.TryGetValue(id, out float sent)) return;
            _sentAt.Remove(id);

            double ms = (now - sent) * 1000.0;
            _grantLatencyTotalMs += ms;
            if (ms > _grantLatencyMaxMs) _grantLatencyMaxMs = ms;
        }

        /// <summary>Dropped so a request that never came back cannot leak an entry.</summary>
        public static void RequestClosed(long id) => _sentAt.Remove(id);

        public static double MeanGrantLatencyMs =>
            GrantsReceived > 0 ? _grantLatencyTotalMs / GrantsReceived : 0.0;

        public static double MaxGrantLatencyMs => _grantLatencyMaxMs;

        public static int OutstandingRequests => _sentAt.Count;

        /// <summary>How many refusals get a detailed log line before it goes quiet.</summary>
        private const int RefusalLogLimit = 15;

        private static int _refusalsLogged;

        /// <summary>
        /// Records why one refusal happened. The three values together name
        /// the cause: a previous owner that is another peer is a genuine
        /// handover and the wait is doing its job; 0 or this peer means the
        /// wait is being applied where there is no in-flight write to guard
        /// against.
        /// </summary>
        public static void LogRefusal(long previousOwner, long thisPeer, float ownerRevisionAge)
        {
            if (_refusalsLogged >= RefusalLogLimit) return;
            _refusalsLogged++;

            string who = previousOwner == 0L ? "nobody"
                : previousOwner == thisPeer ? "this client"
                : $"peer {previousOwner}";

            DrawerPlugin.Log.LogInfo(
                $"Withdraw refused ({RefusedUnsettled} so far): ownership came from {who} "
                + $"{ownerRevisionAge:F2}s ago."
                + (_refusalsLogged == RefusalLogLimit ? " Further refusals will not be logged; use rid_diag for totals." : ""));
        }

        public static void Reset()
        {
            RefusedUnsettled = 0;
            OwnershipChanges = 0;
            ClaimsMade = 0;
            RequestsSent = 0;
            UnownedClaims = 0;
            GrantsReceived = 0;
            GrantsGivenUp = 0;
            _grantLatencyTotalMs = 0.0;
            _grantLatencyMaxMs = 0.0;
            _sentAt.Clear();
            _refusalsLogged = 0;
        }
    }
}
