namespace RossQoL.Core.Startup
{
    /// <summary>
    /// Turns "the player asked to join X" plus "the player actually spawned"
    /// into a record. Only a real spawn commits, so a failed connection or a
    /// wrong password never replaces a good record.
    /// </summary>
    public sealed class SessionCapture
    {
        private ServerKind _kind;
        private string _address;
        private string _joinCode;
        private string _displayName;

        public void ServerJoinRequested(ServerKind kind, string address, string joinCode, string displayName)
        {
            _kind = kind;
            _address = address;
            _joinCode = joinCode;
            _displayName = displayName;
        }

        public void LocalWorldStartRequested() => Clear();

        public LastSession CommitOnInitialSpawn(
            bool hostingLocalWorld, string characterFile, string characterSource,
            string worldName, string worldSource)
        {
            LastSession session;
            if (hostingLocalWorld)
                session = LastSession.LocalWorld(characterFile, characterSource, worldName, worldSource);
            else if (_kind != ServerKind.None)
                session = LastSession.Server(characterFile, characterSource, _kind, _address, _joinCode, _displayName);
            else
                session = null;

            Clear();
            return session != null && session.IsComplete ? session : null;
        }

        private void Clear()
        {
            _kind = ServerKind.None;
            _address = null;
            _joinCode = null;
            _displayName = null;
        }
    }
}
