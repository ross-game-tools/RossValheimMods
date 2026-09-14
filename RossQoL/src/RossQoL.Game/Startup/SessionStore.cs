using RossQoL.Core.Startup;

namespace RossQoL.Game.Startup
{
    /// <summary>
    /// The last session, kept in PlatformPrefs next to vanilla's own "profile"
    /// and "world" keys. Per machine; never in the synced config.
    /// </summary>
    internal static class SessionStore
    {
        private const string Key = "RossQoL.LastSession";

        public static LastSession Load() => LastSessionFormat.Read(PlatformPrefs.GetString(Key, ""));

        public static void Save(LastSession session)
        {
            PlatformPrefs.SetString(Key, LastSessionFormat.Write(session));
            PlatformPrefs.Save();
        }
    }
}
