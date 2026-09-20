using System;

namespace RossPortals.Game.Portals
{
    /// <summary>
    /// The wire between peers. The server is authoritative: clients ask it to
    /// sync the list or to change a portal, and the server broadcasts the result
    /// back. This class is deliberately thin — registration, raw sends, and
    /// receivers that hand straight off to <see cref="PortalManager"/> for any
    /// real logic — so the protocol is readable in one screen.
    /// </summary>
    internal static class PortalRpc
    {
        public static void Register()
        {
            var rpc = ZRoutedRpc.instance;

            // Client → server
            rpc.Register(ModInfo.RpcSyncRequest, new Action<long, string>(OnSyncRequest));
            rpc.Register(ModInfo.RpcAddOrUpdate, new Action<long, ZPackage>(OnAddOrUpdate));
            rpc.Register(ModInfo.RpcRemove, new Action<long, ZDOID>(OnRemove));

            // Server → client
            rpc.Register(ModInfo.RpcSyncPortal, new Action<long, ZPackage>(OnPortalSynced));
            rpc.Register(ModInfo.RpcResync, new Action<long, ZPackage, string>(OnResync));
        }

        // --- Sends ---

        public static void RequestSync(string reason)
            => ZRoutedRpc.instance.InvokeRoutedRPC(Env.ServerPeerId, ModInfo.RpcSyncRequest, reason);

        public static void RequestAddOrUpdate(PortalRecord record)
            => ZRoutedRpc.instance.InvokeRoutedRPC(Env.ServerPeerId, ModInfo.RpcAddOrUpdate, record.Pack());

        public static void RequestRemove(ZDOID id)
            => ZRoutedRpc.instance.InvokeRoutedRPC(Env.ServerPeerId, ModInfo.RpcRemove, id);

        public static void BroadcastPortal(PortalRecord record)
        {
            if (ZNet.instance == null || ZNet.instance.GetConnectedPeers().Count == 0) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, ModInfo.RpcSyncPortal, record.Pack());
        }

        public static void BroadcastResync(ZPackage pkg, string reason)
        {
            if (ZNet.instance == null || ZNet.instance.GetConnectedPeers().Count == 0) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, ModInfo.RpcResync, pkg, reason);
        }

        // --- Receives ---

        private static void OnSyncRequest(long sender, string reason)
        {
            if (!Env.IsServer) return;
            PortalManager.ProcessSyncRequest(reason);
        }

        private static void OnAddOrUpdate(long sender, ZPackage pkg)
        {
            if (!Env.IsServer) return;
            PortalManager.ServerAddOrUpdate(PortalRecord.FromPackage(pkg));
        }

        private static void OnRemove(long sender, ZDOID id)
        {
            if (!Env.IsServer) return;
            PortalManager.ServerRemove(id);
        }

        private static void OnPortalSynced(long sender, ZPackage pkg)
        {
            if (Env.IsServer) return; // the server already has it
            PortalRegistry.Instance.AddOrUpdate(PortalRecord.FromPackage(pkg));
            PortalManager.NotifyListChanged();
        }

        private static void OnResync(long sender, ZPackage pkg, string reason)
        {
            if (Env.IsServer) return;
            PortalRegistry.Instance.ApplyResync(pkg);
            RossPortalsPlugin.Log.LogInfo($"Portal resync ({reason}): now know {PortalRegistry.Instance.Count} portal(s).");
            PortalManager.NotifyListChanged();
        }
    }
}
