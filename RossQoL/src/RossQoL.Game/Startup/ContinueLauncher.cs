using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using RossQoL.Core.Startup;
using UnityEngine;
using UnityEngine.UI;

namespace RossQoL.Game.Startup
{
    /// <summary>
    /// Resumes a recorded session through vanilla's own methods, so Jotunn's
    /// and ServerSync's version checks, the password prompt and every error
    /// dialog behave exactly as if the player had clicked through the menus.
    /// </summary>
    internal static class ContinueLauncher
    {
        // A static reference rather than threading the Button through every
        // private method below: only one Continue click can ever be in
        // flight (the button is disabled for the duration), so there is
        // nothing to disambiguate between calls.
        private static Button _button;

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

        public static void Resume(FejdStartup menu, LastSession session, Button button)
        {
            // Guards against a second click while a PlayFab join-code lookup
            // is resolving or JoinServer's scene-transition delay is running;
            // both leave the menu visible and clickable for a moment. Every
            // path below that returns to a usable menu re-enables it.
            _button = button;
            if (_button != null) _button.interactable = false;

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

            // OnWorldStart persists the crossplay toggle to PlatformPrefs
            // whenever it is interactable (FejdStartup.OnWorldStart), and
            // RefreshWorldSelection reads that pref back as the toggle's
            // default (1, i.e. on) next time the menu opens. Forcing the
            // toggle off here to keep this world private would silently flip
            // the player's saved crossplay preference for every future world,
            // so the prior value is restored once OnWorldStart is done.
            int priorCrossplay = PlatformPrefs.GetInt("crossplay", 1);
            menu.m_crossplayServerToggle.SetIsOnWithoutNotify(false);
            menu.m_serverPassword.text = "";

            RossQoLPlugin.Log.LogInfo($"Continue: starting {session}.");
            menu.OnWorldStart();

            if (menu.m_crossplayServerToggle.IsInteractable())
            {
                PlatformPrefs.SetInt("crossplay", priorCrossplay);
                PlatformPrefs.Save();
            }

            // OnWorldStart returns early (e.g. the cloud-storage warning
            // popup) without transitioning, leaving the button disabled on a
            // menu the player is still looking at.
            menu.StartCoroutine(ReenableAfterDelay(_button));
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
            // The menu may be gone, or the player may have already navigated
            // past it (e.g. into character select), by the time an async
            // join-code lookup returns. Joining from there would yank them
            // out of whatever they are doing now, so just log and stop.
            if (menu == null || !menu.m_mainMenu.activeInHierarchy)
            {
                RossQoLPlugin.Log.LogInfo(
                    $"Continue: {session} resolved after the main menu was left; not joining.");

                // The player may back out to this same menu later with the
                // button still disabled from the click that led here. menu
                // being non-null but its main menu inactive still has a
                // MonoBehaviour to run the coroutine on; menu being null
                // means the button was destroyed along with everything else.
                if (menu != null) menu.StartCoroutine(ReenableAfterDelay(_button));
                return;
            }

            if (!data.IsValid)
            {
                FallBack(menu, $"'{session.ServerAddress}' is not a usable address");
                return;
            }

            RossQoLPlugin.Log.LogInfo($"Continue: joining {session}.");
            menu.SetServerToJoin(data);
            menu.JoinServer();

            // JoinServer often returns early to the still-active main menu
            // without transitioning (version mismatch, crossplay/privilege
            // popups, a cancelled login prompt, a failed server host) --
            // none of those are worth enumerating here, so just check back
            // later and re-enable the button if nothing ended up loading a
            // new scene.
            menu.StartCoroutine(ReenableAfterDelay(_button));
        }

        /// <summary>
        /// Turns the Continue button back on after vanilla has had time to
        /// either transition away (destroying this coroutine along with the
        /// menu and button) or fall back to the still-open menu. The delay
        /// is longer than TransitionToMainScene's own 1.5s Invoke so a real
        /// scene change wins the race. A disabled-but-hidden button has no
        /// value, so this re-enables unconditionally rather than checking
        /// whether the menu is currently visible.
        /// </summary>
        private static IEnumerator ReenableAfterDelay(Button button)
        {
            yield return new WaitForSecondsRealtime(3f);
            if (button != null) button.interactable = true;
        }

        /// <summary>
        /// Anything that cannot be resumed after the button was shown lands the
        /// player on vanilla's own character selection, with the reason logged.
        /// </summary>
        private static void FallBack(FejdStartup menu, string reason)
        {
            RossQoLPlugin.Log.LogWarning($"Continue: {reason}; opening character selection instead.");
            menu.OnStartGame();

            // OnStartGame leaves the player on a usable menu (character
            // select), so a further click on Continue -- if it is even still
            // visible -- must work again.
            if (_button != null) _button.interactable = true;
        }
    }
}
