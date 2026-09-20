using System.Collections.Generic;
using RossPortals.Game.UI;
using UnityEngine;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// The domain logic tying the patches, the registry, the RPCs and the panel
    /// together: what happens when a portal is placed, destroyed, hovered,
    /// configured, or when the list needs syncing. The server-only steps guard
    /// on <see cref="Env.IsServer"/> at the RPC boundary, so the methods here can
    /// read straightforwardly.
    /// </summary>
    internal static class PortalManager
    {
        // Client-local "recently teleported to", most-recent-first, for the
        // Recent sort. Never persisted or synced — it's a personal convenience,
        // and a fresh session starting empty is fine.
        private const int RecentCap = 30;
        private static readonly List<string> _recent = new List<string>();
        public static IReadOnlyList<string> RecentKeys => _recent;

        public static void OnGameStarted()
        {
            PortalRegistry.Instance.Reset();
            PortalRpc.Register();
        }

        /// <summary>Client just finished loading the world: ask the server for
        /// the full portal list.</summary>
        public static void RequestInitialSync()
        {
            var profile = global::Game.instance != null ? global::Game.instance.GetPlayerProfile() : null;
            var who = profile != null ? profile.GetName() : "a player";
            PortalRpc.RequestSync($"{who} joined");
        }

        /// <summary>Ask the server to resend the full portal list — used when the
        /// panel opens, so the list is complete even if the join-time sync was
        /// missed.</summary>
        public static void RefreshList() => PortalRpc.RequestSync("panel opened");

        // --- Server-authoritative ---

        public static void ProcessSyncRequest(string reason)
        {
            var portals = ZDOMan.instance.GetPortalList();
            PortalRegistry.Instance.ApplyPortalZdos(portals);
            RossPortalsPlugin.Log.LogInfo($"Portal sync ({reason}): server sees {portals.Count} portal(s); broadcasting.");
            PortalRpc.BroadcastResync(PortalRegistry.Instance.Pack(), reason);
        }

        public static void ServerAddOrUpdate(PortalRecord record)
        {
            var stored = PortalRegistry.Instance.AddOrUpdate(record);
            WriteWithRetry(stored, attempts: 5);
            PortalRpc.BroadcastPortal(stored);

            // Convenience back-link: if the destination has no destination of its
            // own yet, point it back here so a fresh pair links both ways. The
            // "no target yet" guard means this never overrides a deliberate
            // choice on an established portal.
            if (stored.HasTarget)
            {
                var target = PortalRegistry.Instance.GetById(stored.Target);
                if (target != null && !target.HasTarget)
                {
                    target.Target = stored.Id;
                    ServerAddOrUpdate(target);
                }
            }
        }

        public static void ServerRemove(ZDOID id)
        {
            if (!PortalRegistry.Instance.Remove(id)) return;

            foreach (var orphan in PortalRegistry.Instance.GetPortalsWithTarget(id))
            {
                orphan.Target = ZDOID.None;
                ServerAddOrUpdate(orphan);
            }

            PortalRpc.BroadcastResync(PortalRegistry.Instance.Pack(), "portal removed");
        }

        // A just-placed portal's ZDO can reach the server a frame or two after
        // the add request; retry a few times before giving up rather than
        // dropping the write.
        private static void WriteWithRetry(PortalRecord record, int attempts)
        {
            if (ZDOMan.instance.GetZDO(record.Id) != null)
            {
                PortalZdo.Write(record);
                return;
            }
            if (attempts <= 0 || Scheduler.Instance == null)
            {
                RossPortalsPlugin.Log.LogWarning($"Portal ZDO {record.Id} never materialised; name/target not persisted.");
                return;
            }
            Scheduler.Instance.AfterFrames(3, () => WriteWithRetry(record, attempts - 1));
        }

        // --- Placement / destruction (client side, from patches) ---

        public static void OnPortalPlaced(ZDOID id, Vector3 location)
        {
            ZDOMan.instance.ForceSendZDO(id);
            PortalRpc.RequestAddOrUpdate(new PortalRecord(id) { Location = location });
        }

        public static void OnPortalDestroyed(ZDOID id) => PortalRpc.RequestRemove(id);

        // --- Hover / interact ---

        public static string BuildHoverText(ZDOID id, Vector3 location)
        {
            var portal = PortalRegistry.Instance.GetById(id) ?? Adopt(id, location);
            var name = FriendlyName(portal.Name);
            var target = FriendlyTarget(portal);
            // Only $KEY_Use is a real vanilla token (it resolves to the bound
            // key); the labels are literals because the portal-specific tokens
            // XPortal used aren't registered here.
            return Localization.instance.Localize(
                $"Name: {name}\n"
                + $"Destination: {target}\n"
                + "[<color=yellow><b>$KEY_Use</b></color>] Configure");
        }

        public static void OnPortalInteract(ZDOID id)
        {
            if (Env.IsHeadless || PortalConfigPanel.Instance == null) return;
            var zdo = ZDOMan.instance.GetZDO(id);
            var portal = PortalRegistry.Instance.GetById(id)
                ?? Adopt(id, zdo != null ? zdo.GetPosition() : Vector3.zero);
            PortalConfigPanel.Instance.Open(portal);
        }

        /// <summary>The panel's OK button: push the chosen name and destination
        /// to the server, but only if something actually changed.</summary>
        public static void SubmitPortalConfig(PortalRecord portal, string newName, ZDOID newTarget)
        {
            newName ??= string.Empty;
            if (portal.Name == newName && portal.Target == newTarget) return;

            portal.Name = newName;
            portal.Target = newTarget;
            PortalRpc.RequestAddOrUpdate(portal);
        }

        public static void RecordUsed(ZDOID target)
        {
            if (target == ZDOID.None) return;
            var key = PortalKey.Of(target);
            _recent.Remove(key);
            _recent.Insert(0, key);
            if (_recent.Count > RecentCap) _recent.RemoveRange(RecentCap, _recent.Count - RecentCap);
        }

        public static void NotifyListChanged()
        {
            if (!Env.IsHeadless) PortalConfigPanel.Instance?.OnRegistryChanged();
        }

        // A portal we don't have a record for yet (freshly placed, or the list
        // hasn't synced): register what we can read locally and nudge a resync so
        // the server fills in the rest.
        private static PortalRecord Adopt(ZDOID id, Vector3 location)
        {
            var zdo = ZDOMan.instance.GetZDO(id);
            var portal = new PortalRecord(id)
            {
                Location = location,
                Name = zdo != null ? (PortalZdo.GetName(zdo) ?? string.Empty) : string.Empty,
                Target = zdo != null ? PortalZdo.GetTarget(zdo) : ZDOID.None,
            };
            PortalRegistry.Instance.AddOrUpdate(portal);
            PortalRpc.RequestSync("adopted an unknown portal");
            return portal;
        }

        private static string FriendlyName(string name)
            => string.IsNullOrEmpty(name) ? "(no name)" : name;

        private static string FriendlyTarget(PortalRecord portal)
        {
            if (!portal.HasTarget) return "(none)";
            var target = PortalRegistry.Instance.GetById(portal.Target);
            return target != null ? FriendlyName(target.Name) : "(none)";
        }
    }
}
