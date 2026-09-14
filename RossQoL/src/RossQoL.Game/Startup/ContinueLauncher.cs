using System.Collections.Generic;
using System.Globalization;
using RossQoL.Core.Startup;

namespace RossQoL.Game.Startup
{
    /// <summary>
    /// Resumes a recorded session through vanilla's own methods, so Jotunn's
    /// and ServerSync's version checks, the password prompt and every error
    /// dialog behave exactly as if the player had clicked through the menus.
    /// </summary>
    internal static class ContinueLauncher
    {
        public static PlayerProfile FindProfile(List<PlayerProfile> profiles, LastSession session)
        {
            if (profiles == null || session == null) return null;
            return profiles.Find(p =>
                p.GetFilename() == session.CharacterFile
                && p.m_fileSource.ToString() == session.CharacterSource);
        }

        /// <summary>
        /// Only a world that loads cleanly. Never World.GetCreateWorld, which
        /// creates a new world when the name is missing.
        /// </summary>
        public static World FindWorld(LastSession session)
        {
            if (session == null) return null;
            return SaveSystem.GetWorldList().Find(w =>
                w.m_name == session.WorldName
                && w.m_fileSource.ToString() == session.WorldSource
                && w.m_dataError == World.SaveDataError.None);
        }

        public static void Resume(FejdStartup menu, LastSession session)
        {
            var profiles = SaveSystem.GetAllPlayerProfiles();
            var profile = FindProfile(profiles, session);
            if (profile == null)
            {
                FallBack(menu, "the recorded character is gone");
                return;
            }

            // OnWorldStart reads m_profiles[m_profileIndex], so both must agree
            // with the character selected here.
            menu.m_profiles = profiles;
            menu.m_profileIndex = profiles.IndexOf(profile);
            menu.SelectCharacter(profile.GetFilename(), profile.m_fileSource);

            if (session.Kind == SessionKind.LocalWorld)
                StartLocalWorld(menu, session);
            else
                JoinServer(menu, session);
        }

        private static void StartLocalWorld(FejdStartup menu, LastSession session)
        {
            var world = FindWorld(session);
            if (world == null)
            {
                FallBack(menu, "the recorded world can no longer be loaded");
                return;
            }

            menu.m_world = world;

            // Resume private: vanilla does not save these per world, and the
            // password is deliberately never stored. OnWorldStart reads exactly
            // these four controls.
            menu.m_openServerToggle.SetIsOnWithoutNotify(false);
            menu.m_publicServerToggle.SetIsOnWithoutNotify(false);
            menu.m_crossplayServerToggle.SetIsOnWithoutNotify(false);
            menu.m_serverPassword.text = "";

            RossQoLPlugin.Log.LogInfo($"Continue: starting {session}.");
            menu.OnWorldStart();
        }

        private static void JoinServer(FejdStartup menu, LastSession session)
        {
            switch (session.ServerKind)
            {
                case ServerKind.Dedicated:
                    Join(menu, new ServerJoinData(new ServerJoinDataDedicated(session.ServerAddress)), session);
                    return;

                case ServerKind.SteamUser:
                    if (!ulong.TryParse(session.ServerAddress, NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId))
                    {
                        FallBack(menu, $"'{session.ServerAddress}' is not a Steam id");
                        return;
                    }
                    Join(menu, new ServerJoinData(new ServerJoinDataSteamUser(steamId)), session);
                    return;

                case ServerKind.PlayFab:
                    var byId = new ServerJoinData(new ServerJoinDataPlayFabUser(session.ServerAddress));
                    if (session.JoinCode.Length == 0 || !PlayFabManager.IsLoggedIn)
                    {
                        Join(menu, byId, session);
                        return;
                    }

                    // Codes are regenerated when a host restarts, so the code is
                    // tried first and the stored id is the fallback.
                    ZPlayFabMatchmaking.ResolveJoinCode(
                        session.JoinCode,
                        data => Join(menu, new ServerJoinData(new ServerJoinDataPlayFabUser(data.remotePlayerId)), session),
                        reason =>
                        {
                            RossQoLPlugin.Log.LogInfo(
                                $"Continue: join code {session.JoinCode} no longer resolves ({reason}); trying the server's id.");
                            Join(menu, byId, session);
                        });
                    return;

                default:
                    FallBack(menu, $"unknown server kind {session.ServerKind}");
                    return;
            }
        }

        private static void Join(FejdStartup menu, ServerJoinData data, LastSession session)
        {
            // The menu may be gone by the time an async join-code lookup returns.
            if (menu == null) return;

            if (!data.IsValid)
            {
                FallBack(menu, $"'{session.ServerAddress}' is not a usable address");
                return;
            }

            RossQoLPlugin.Log.LogInfo($"Continue: joining {session}.");
            menu.SetServerToJoin(data);
            menu.JoinServer();
        }

        /// <summary>
        /// Anything that cannot be resumed after the button was shown lands the
        /// player on vanilla's own character selection, with the reason logged.
        /// </summary>
        private static void FallBack(FejdStartup menu, string reason)
        {
            RossQoLPlugin.Log.LogWarning($"Continue: {reason}; opening character selection instead.");
            menu.OnStartGame();
        }
    }
}
