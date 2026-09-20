namespace RossPortals.Game
{
    /// <summary>
    /// The one place plugin identity and the persisted key/RPC strings live.
    ///
    /// The ZDO keys and RPC names are load-bearing in the same way a file
    /// format is: once a world has been saved with them, changing the strings
    /// silently orphans every portal's stored destination. Treat them as
    /// permanent.
    /// </summary>
    internal static class ModInfo
    {
        public const string Guid = "com.rossdwest.rossportals";
        public const string Name = "RossPortals";
        public const string Version = "0.1.0";

        /// <summary>XPortal's plugin GUID. We replace it wholesale — both mods
        /// patch the same portal hover/interact path, so they cannot coexist.
        /// Declaring the incompatibility makes that a clear load-time message
        /// instead of two mods fighting over the UI.</summary>
        public const string XPortalGuid = "yay.spikehimself.xportal";

        // --- ZDO keys (persisted in the world save, per portal) ---

        /// <summary>ZDOID of this portal's chosen destination. Our own key.</summary>
        public const string KeyTarget = Name + "_TargetId";

        /// <summary>The portal's ZDOID as of the last save. ZDOIDs are
        /// reassigned each session, so on load this is how a stored target
        /// ZDOID (which is now stale) is remapped to the portal's new ZDOID.</summary>
        public const string KeyPrevious = Name + "_PreviousId";

        /// <summary>XPortal's destination key. Read once, as a fallback, to
        /// import a save that was previously managed by XPortal; never
        /// written. Self-retires per portal the first time we save it.</summary>
        public const string LegacyKeyTarget = "XPortal_TargetId";

        /// <summary>XPortal's previous-id key, used the same way during import.</summary>
        public const string LegacyKeyPrevious = "XPortal_PreviousId";

        // --- RPC names (server-authoritative portal-list sync) ---

        public const string RpcResync = Name + "_Resync";
        public const string RpcSyncPortal = Name + "_SyncPortal";
        public const string RpcSyncRequest = Name + "_SyncRequest";
        public const string RpcAddOrUpdate = Name + "_AddOrUpdate";
        public const string RpcRemove = Name + "_Remove";
    }
}
