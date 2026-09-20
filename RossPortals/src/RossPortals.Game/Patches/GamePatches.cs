using HarmonyLib;
using RossPortals.Game.Framework;
using RossPortals.Game.Portals;

namespace RossPortals.Game.Patches
{
    [HarmonyPatch(typeof(global::Game), nameof(global::Game.Awake))]
    internal static class Game_Awake
    {
        // Announce ourselves as a mod, as Game.messageForModders asks.
        private static void Prefix(ref bool ___isModded) => ___isModded = true;
    }

    [HarmonyPatch(typeof(global::Game), nameof(global::Game.Start))]
    internal static class Game_Start
    {
        private static void Postfix()
        {
            Env.GameStarted = true;
            PortalManager.OnGameStarted();
        }
    }

    // Vanilla pairs portals that share a tag. We don't pair by tag at all —
    // destinations are chosen explicitly and rebuilt by our own load pass
    // (ZDOMan.ConnectPortals) — so the vanilla client-side pairing routines are
    // suppressed. Guarded: if a Valheim update renames these, we log and let
    // vanilla run rather than crash (the worst case is our connections getting
    // second-guessed, not a broken load).
    [HarmonyPatch(typeof(global::Game), nameof(global::Game.ConnectPortals))]
    internal static class Game_ConnectPortals
    {
        private static bool Prepare() => ValheimCompat.RequireMethod(typeof(global::Game), nameof(global::Game.ConnectPortals), "portal destinations");
        private static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(global::Game), nameof(global::Game.ConnectPortalsCoroutine))]
    internal static class Game_ConnectPortalsCoroutine
    {
        private static bool Prepare() => ValheimCompat.RequireMethod(typeof(global::Game), nameof(global::Game.ConnectPortalsCoroutine), "portal destinations");
        private static bool Prefix() => false;
    }
}
