using System.Collections.Generic;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// Every read and write of a portal's persisted state, in one place.
    ///
    /// A portal stores its name in the vanilla tag (<see cref="ZDOVars.s_tag"/>)
    /// and its chosen destination in our own <c>RossPortals_TargetId</c> ZDO key
    /// — plus, crucially, in the vanilla portal CONNECTION, which is what
    /// vanilla <c>TeleportWorld.Teleport</c> actually reads. We write both; the
    /// key is our durable source of truth, the connection is what makes the jump
    /// work with no changes to the teleport code.
    ///
    /// ZDOIDs are reassigned every session, so a stored target id is stale on the
    /// next load. <see cref="RestoreConnections"/> is the server-side pass that
    /// remaps stale ids (via the <c>PreviousId</c> stamp) and rebuilds the live
    /// connection — and, on the way, imports a save that was previously managed
    /// by XPortal by falling back to XPortal's keys when ours aren't set yet.
    /// </summary>
    internal static class PortalZdo
    {
        public static string GetName(ZDO zdo) => zdo.GetString(ZDOVars.s_tag);

        public static void SetName(ZDO zdo, string name) => zdo.Set(ZDOVars.s_tag, name ?? string.Empty);

        /// <summary>Chosen destination, preferring our key and falling back to
        /// XPortal's once (the import). None when unset.</summary>
        public static ZDOID GetTarget(ZDO zdo)
        {
            var target = zdo.GetZDOID(ModInfo.KeyTarget);
            if (target == ZDOID.None) target = zdo.GetZDOID(ModInfo.LegacyKeyTarget);
            return target;
        }

        /// <summary>This portal's id as of the last save, ours or XPortal's.</summary>
        public static ZDOID GetPrevious(ZDO zdo)
        {
            var previous = zdo.GetZDOID(ModInfo.KeyPrevious);
            if (previous == ZDOID.None) previous = zdo.GetZDOID(ModInfo.LegacyKeyPrevious);
            return previous;
        }

        /// <summary>Read a portal ZDO into a record for the list.</summary>
        public static PortalRecord Read(ZDO zdo)
            => new PortalRecord(zdo.m_uid)
            {
                Name = GetName(zdo) ?? string.Empty,
                Location = zdo.GetPosition(),
                Target = GetTarget(zdo),
            };

        /// <summary>Apply a chosen name and destination to the portal's ZDO.
        /// Server-side: takes ownership, then writes name, our previous-id stamp,
        /// our target key and the vanilla connection together.</summary>
        public static void Write(PortalRecord record)
        {
            var zdo = ZDOMan.instance.GetZDO(record.Id);
            if (zdo == null) return;

            zdo.SetOwner(ZDOMan.GetSessionID());
            SetName(zdo, record.Name);
            zdo.Set(ModInfo.KeyPrevious, zdo.m_uid);
            SetTarget(zdo, record.Target);
        }

        private static void SetTarget(ZDO zdo, ZDOID target)
        {
            zdo.Set(ModInfo.KeyTarget, target);
            // The connection is what vanilla teleport reads. Setting it here is
            // what removes the need to patch Teleport at all.
            zdo.SetConnection(ZDOExtraData.ConnectionType.Portal, target);
        }

        /// <summary>
        /// Server-side load pass replacing vanilla's tag-based pairing. For every
        /// portal, resolve its stored destination to a currently-valid ZDOID and
        /// rebuild the live connection; migrate XPortal-authored targets onto our
        /// key as we go; finally re-stamp every portal's previous-id so the NEXT
        /// session can remap this session's ids.
        /// </summary>
        public static void RestoreConnections()
        {
            var portals = ZDOMan.instance.GetPortalList();
            if (portals == null || portals.Count == 0) return;

            // Membership + old-id → new-id remap, built from the previous-id
            // stamp each portal saved last session (ours, or XPortal's on the
            // first load after switching).
            var portalIds = new HashSet<ZDOID>();
            var byPreviousId = new Dictionary<ZDOID, ZDOID>();
            foreach (var zdo in portals)
            {
                portalIds.Add(zdo.m_uid);
                var previous = GetPrevious(zdo);
                if (previous != ZDOID.None) byPreviousId[previous] = zdo.m_uid;
            }

            foreach (var zdo in portals)
            {
                var target = GetTarget(zdo);
                if (target == ZDOID.None) continue;

                // If the stored id no longer names a live portal, it's stale from
                // a previous session — remap it through the previous-id table.
                if (!portalIds.Contains(target))
                {
                    if (!byPreviousId.TryGetValue(target, out target)) continue;
                }

                zdo.SetOwner(ZDOMan.GetSessionID());
                zdo.SetConnection(ZDOExtraData.ConnectionType.Portal, target);
                zdo.Set(ModInfo.KeyTarget, target); // migrate onto our key
            }

            foreach (var zdo in portals)
                zdo.Set(ModInfo.KeyPrevious, zdo.m_uid);
        }
    }
}
