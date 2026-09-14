using System;

namespace RossQoL.Core.Startup
{
    public enum SessionKind
    {
        LocalWorld,
        Server,
    }

    public enum ServerKind
    {
        None,
        Dedicated,
        SteamUser,
        PlayFab,
    }

    /// <summary>
    /// What the player last actually played: a local world or a server, and
    /// with which character. File sources are the names of Valheim's
    /// FileHelpers.FileSource values, kept as strings so Core stays engine-free.
    /// Never contains a password.
    /// </summary>
    public sealed class LastSession : IEquatable<LastSession>
    {
        public SessionKind Kind { get; }
        public string CharacterFile { get; }
        public string CharacterSource { get; }
        public string WorldName { get; }
        public string WorldSource { get; }
        public ServerKind ServerKind { get; }

        /// <summary>host:port, a host Steam id, or a PlayFab remote player id.</summary>
        public string ServerAddress { get; }

        /// <summary>Crossplay join code when one was known. Codes change when a host restarts.</summary>
        public string JoinCode { get; }

        public string DisplayName { get; }

        private LastSession(
            SessionKind kind, string characterFile, string characterSource,
            string worldName, string worldSource,
            ServerKind serverKind, string serverAddress, string joinCode, string displayName)
        {
            Kind = kind;
            CharacterFile = characterFile ?? "";
            CharacterSource = characterSource ?? "";
            WorldName = worldName ?? "";
            WorldSource = worldSource ?? "";
            ServerKind = serverKind;
            ServerAddress = serverAddress ?? "";
            JoinCode = joinCode ?? "";
            DisplayName = displayName ?? "";
        }

        public static LastSession LocalWorld(
            string characterFile, string characterSource, string worldName, string worldSource) =>
            new LastSession(SessionKind.LocalWorld, characterFile, characterSource,
                worldName, worldSource, ServerKind.None, "", "", worldName);

        public static LastSession Server(
            string characterFile, string characterSource,
            ServerKind serverKind, string serverAddress, string joinCode, string displayName) =>
            new LastSession(SessionKind.Server, characterFile, characterSource, "", "",
                serverKind, serverAddress, joinCode,
                string.IsNullOrEmpty(displayName) ? serverAddress : displayName);

        /// <summary>Has everything needed to resume. An incomplete record is treated as none.</summary>
        public bool IsComplete
        {
            get
            {
                if (CharacterFile.Length == 0 || CharacterSource.Length == 0) return false;

                return Kind == SessionKind.LocalWorld
                    ? WorldName.Length > 0 && WorldSource.Length > 0
                    : ServerKind != ServerKind.None && ServerAddress.Length > 0;
            }
        }

        public bool Equals(LastSession other) =>
            other != null
            && Kind == other.Kind
            && CharacterFile == other.CharacterFile
            && CharacterSource == other.CharacterSource
            && WorldName == other.WorldName
            && WorldSource == other.WorldSource
            && ServerKind == other.ServerKind
            && ServerAddress == other.ServerAddress
            && JoinCode == other.JoinCode
            && DisplayName == other.DisplayName;

        public override bool Equals(object obj) => Equals(obj as LastSession);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ CharacterFile.GetHashCode();
                hash = (hash * 397) ^ WorldName.GetHashCode();
                hash = (hash * 397) ^ ServerAddress.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"{Kind} '{DisplayName}' as {CharacterFile}";
    }
}
