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

        /// <summary>Re-sends of a request already in flight, counted separately from first sends.</summary>
        public static int Retries;

        /// <summary>Drawers claimed because nobody owned them, rather than refusing the player.</summary>
        public static int UnownedClaims;

        /// <summary>Withdrawals held for ownership to settle instead of refused.</summary>
        public static int DeferredWithdraws;

        /// <summary>View-flush claims postponed because a player was using the drawer.</summary>
        public static int ClaimsHeldForPlayer;
        public static int GrantsReceived;
        public static int GrantsGivenUp;

        private static double _grantLatencyTotalMs;
        private static double _grantLatencyMaxMs;

        /// <summary>Request id -> send time, so a grant can be timed against its own send.</summary>
        private static readonly Dictionary<long, float> _sentAt = new Dictionary<long, float>();

        // Deposits go through the same request/grant round trip as
        // withdrawals and were never counted, so "lag assigning an item to a
        // drawer" was invisible here while taking one out was measured in
        // detail. Counted separately rather than merged: the two paths differ
        // -- a deposit has already taken the items off the player before it
        // sends -- so a delay in one says nothing about the other.
        public static int DepositRequestsSent;
        public static int DepositRetries;
        public static int DepositGrantsReceived;

        private static double _depositLatencyTotalMs;
        private static double _depositLatencyMaxMs;

        public static double MeanDepositLatencyMs =>
            DepositGrantsReceived > 0 ? _depositLatencyTotalMs / DepositGrantsReceived : 0.0;

        public static double MaxDepositLatencyMs => _depositLatencyMaxMs;

        public static void DepositSent(long id, float now)
        {
            DepositRequestsSent++;

            // Same first-send-wins rule as withdrawals: a retry that reset
            // the clock would report the fast tail of a slow request.
            if (_sentAt.ContainsKey(id))
            {
                DepositRetries++;
                return;
            }

            _sentAt[id] = now;
        }

        public static void DepositGranted(long id, float now)
        {
            DepositGrantsReceived++;
            if (!_sentAt.TryGetValue(id, out float sent)) return;
            _sentAt.Remove(id);

            double ms = (now - sent) * 1000.0;
            _depositLatencyTotalMs += ms;
            if (ms > _depositLatencyMaxMs) _depositLatencyMaxMs = ms;

            if (ms >= SlowGrantMs && _slowGrantsLogged < SlowGrantLogLimit)
            {
                _slowGrantsLogged++;
                DrawerPlugin.Log.LogInfo(
                    $"Deposit waited {ms:F0}ms for the owner to answer."
                    + (_slowGrantsLogged == SlowGrantLogLimit
                        ? " Further slow grants will not be logged; use rid_diag for totals."
                        : ""));
            }
        }

        public static void RequestSent(long id, float now)
        {
            RequestsSent++;

            // FIRST send only. A retry re-sends the same id, and overwriting
            // the timestamp made the reported latency the time from the last
            // retry to the grant -- so a request that waited five seconds and
            // was answered promptly after a retry was reported as a fast
            // request. The measurement hid exactly the delay it existed to
            // find, and every latency figure taken before this was a
            // best-case reading of the requests that never retried.
            if (_sentAt.ContainsKey(id))
            {
                Retries++;
                return;
            }

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

            // A slow grant is what a player feels as lag, and it needs to be
            // visible without anyone happening to run rid_diag at the right
            // moment. The threshold is well above a normal round trip on any
            // server worth playing on, so an ordinary withdrawal never logs.
            if (ms >= SlowGrantMs && _slowGrantsLogged < SlowGrantLogLimit)
            {
                _slowGrantsLogged++;
                DrawerPlugin.Log.LogInfo(
                    $"Withdraw waited {ms:F0}ms for the owner to answer."
                    + (_slowGrantsLogged == SlowGrantLogLimit
                        ? " Further slow grants will not be logged; use rid_diag for totals."
                        : ""));
            }
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

        /// <summary>A grant slower than this is worth a line of its own.</summary>
        private const double SlowGrantMs = 1000.0;

        private const int SlowGrantLogLimit = 10;
        private static int _slowGrantsLogged;

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
            Retries = 0;
            DepositRequestsSent = 0;
            DepositRetries = 0;
            DepositGrantsReceived = 0;
            _depositLatencyTotalMs = 0.0;
            _depositLatencyMaxMs = 0.0;
            UnownedClaims = 0;
            DeferredWithdraws = 0;
            ClaimsHeldForPlayer = 0;
            GrantsReceived = 0;
            GrantsGivenUp = 0;
            _grantLatencyTotalMs = 0.0;
            _grantLatencyMaxMs = 0.0;
            _sentAt.Clear();
            _refusalsLogged = 0;
            _slowGrantsLogged = 0;
        }
    }
}
