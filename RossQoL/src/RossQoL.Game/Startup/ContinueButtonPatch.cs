using System;
using HarmonyLib;
using RossQoL.Core.Startup;
using RossQoL.Game.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RossQoL.Game.Startup
{
    /// <summary>
    /// Adds the Continue button to the main menu, directly above Start game.
    ///
    /// Above Start game rather than at index 0, because other mods
    /// (ServerQuickConnect, menu redesigns) also insert at the top; placing
    /// relative to Start game lets both buttons appear.
    /// </summary>
    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.SetupGui))]
    internal static class ContinueButtonPatch
    {
        private const string ButtonName = "RossQoL_Continue";

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(FejdStartup), nameof(FejdStartup.SetupGui), ContinueButtonFeature.FeatureName);

        private static void Postfix(FejdStartup __instance)
        {
            if (ContinueButtonFeature.Instance?.IsActive != true) return;

            // An exception escaping a SetupGui postfix aborts FejdStartup.Start
            // and leaves the player with a broken menu. Log it and carry on
            // without the button instead.
            try
            {
                AddButton(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Continue: could not add the main menu button: {ex}");
            }
        }

        private static void AddButton(FejdStartup menu)
        {
            var list = menu.m_menuList;
            if (list == null) return;

            var buttons = list.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
                if (b.name == ButtonName) return;

            var log = RossQoLPlugin.Log;
            var session = SessionStore.Load();
            var profile = ContinueLauncher.FindProfile(SaveSystem.GetAllPlayerProfiles(), session);
            bool worldLoadable = session?.Kind == SessionKind.LocalWorld && ContinueLauncher.FindWorld(session) != null;

            if (!ContinuePolicy.ShouldShow(session, profile != null, worldLoadable,
                    ContinuePolicy.HasJoinArguments(Environment.GetCommandLineArgs())))
            {
                log.LogInfo(session == null
                    ? "Continue: no previous session recorded; button hidden."
                    : $"Continue: {session} cannot be resumed right now; button hidden.");
                return;
            }

            var start = FindStartGameButton(buttons);
            if (start == null)
            {
                log.LogWarning("Continue: could not find the Start game button; button not added.");
                return;
            }

            var clone = Object.Instantiate(start.gameObject, start.transform.parent);
            clone.name = ButtonName;
            clone.transform.SetSiblingIndex(start.transform.GetSiblingIndex());

            // A cloned Localize component would put "Start game" back on a
            // language change.
            foreach (var localize in clone.GetComponentsInChildren<Localize>(true))
                Object.DestroyImmediate(localize);

            var label = clone.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = ContinuePolicy.Label(profile.GetName(), session);

            // A fresh event drops Start game's scene-wired OnStartGame listener.
            var button = clone.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => ContinueLauncher.Resume(menu, session));

            // Awake cached the button list; without refreshing it, keyboard and
            // gamepad selection skip the new button.
            menu.m_menuButtons = list.GetComponentsInChildren<Button>();
            if (menu.m_merchStoreButton != null) GuiUtils.SetNavigationRight(button, menu.m_merchStoreButton);

            log.LogInfo($"Continue: button added for {session}.");
        }

        private static Button FindStartGameButton(Button[] buttons)
        {
            foreach (var b in buttons)
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                    if (b.onClick.GetPersistentMethodName(i) == nameof(FejdStartup.OnStartGame))
                        return b;

            return buttons.Length > 0 ? buttons[0] : null;
        }
    }
}
