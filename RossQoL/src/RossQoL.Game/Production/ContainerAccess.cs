namespace RossQoL.Game.Production
{
    /// <summary>
    /// Whether this client may put into or take from a container, shared by
    /// every feature that uses containers. A container also needs
    /// ContainerOwnership.IsSettled before it is written.
    /// </summary>
    internal static class ContainerAccess
    {
        /// <summary>The same checks vanilla applies when the local player opens a container.</summary>
        public static bool MayUse(Container container, long playerId)
        {
            if (container == null) return false;

            var nview = container.m_nview;
            if (nview == null || !nview.IsValid()) return false;
            if (container.IsInUse()) return false;

            // CheckAccess reads the Piece for anything but Public.
            if (container.m_privacy != Container.PrivacySetting.Public && container.m_piece == null) return false;
            if (!container.CheckAccess(playerId)) return false;

            // As Container.Interact: the ward is checked only for containers that ask for it.
            return !container.m_checkGuardStone || PrivateArea.CheckAccess(container.transform.position, 0f, flash: false);
        }

        /// <summary>
        /// Brings the container's local inventory up to its ZDO before it is
        /// measured or changed. Vanilla reloads a chest only in
        /// CheckForChanges, once a second; a peer that just received
        /// ownership and newer data would otherwise change stale contents,
        /// and the Save that follows would write that stale state back:
        /// items the previous owner took duplicated, items it added lost.
        ///
        /// Load returns early while the container is in use; in-use
        /// containers are already skipped by MayUse. Only an exact Container
        /// must then match the ZDO's data revision: subclasses from storage
        /// mods load in their own way and need not track m_lastRevision.
        /// </summary>
        public static bool IsFresh(Container container)
        {
            container.Load();
            if (container.GetType() != typeof(Container)) return true;
            return container.m_lastRevision == container.m_nview.GetZDO().DataRevision;
        }
    }
}
