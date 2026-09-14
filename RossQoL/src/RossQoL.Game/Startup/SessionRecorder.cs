using System.Globalization;
using RossQoL.Core.Startup;

namespace RossQoL.Game.Startup
{
    /// <summary>
    /// Stashes what the player asked to join and commits it on the session's
    /// first spawn -- Game.m_playerInitialSpawn, which fires only after the
    /// connection and any password have succeeded.
    /// </summary>
    internal static class SessionRecorder
    {
        private static readonly SessionCapture Capture = new SessionCapture();
        private static bool _subscribed;

        public static void Subscribe()
        {
            if (_subscribed) return;
            global::Game.m_playerInitialSpawn += OnInitialSpawn;
            _subscribed = true;
        }

        public static void ServerJoinRequested(ServerJoinData data)
        {
            if (!data.IsValid) return;

            ServerKind kind;
            string address;
            string joinCode = "";

            switch (data.m_type)
            {
                case ServerJoinDataType.Dedicated:
                    kind = ServerKind.Dedicated;
                    address = data.Dedicated.ToString();
                    break;
                case ServerJoinDataType.SteamUser:
                    // Steam invites and lobby joins resolve to this before
                    // JoinServer, so they are recorded as their host.
                    kind = ServerKind.SteamUser;
                    address = data.SteamUser.m_joinUserID.m_SteamID.ToString(CultureInfo.InvariantCulture);
                    break;
                case ServerJoinDataType.PlayFabUser:
                    kind = ServerKind.PlayFab;
                    address = data.PlayFabUser.m_remotePlayerId;
                    joinCode = MultiBackendMatchmaking.GetServerMatchmakingData(data).m_joinCode ?? "";
                    break;
                default:
                    return;
            }

            Capture.ServerJoinRequested(kind, address, joinCode, MultiBackendMatchmaking.GetServerName(data));
        }

        public static void LocalWorldStartRequested() => Capture.LocalWorldStartRequested();

        private static void OnInitialSpawn()
        {
            if (ContinueButtonFeature.Instance?.IsActive != true) return;

            var znet = ZNet.instance;
            var profile = global::Game.instance != null ? global::Game.instance.GetPlayerProfile() : null;
            if (znet == null || profile == null) return;

            bool hosting = znet.IsServer() && !znet.IsDedicated();
            var world = hosting ? ZNet.World : null;

            var session = Capture.CommitOnInitialSpawn(
                hosting,
                profile.GetFilename(),
                profile.m_fileSource.ToString(),
                world?.m_name,
                world?.m_fileSource.ToString());

            if (session == null)
            {
                RossQoLPlugin.Log.LogInfo("Continue: this session cannot be resumed later; keeping the previous record.");
                return;
            }

            SessionStore.Save(session);
            RossQoLPlugin.Log.LogInfo($"Continue: recorded {session}.");
        }
    }
}
