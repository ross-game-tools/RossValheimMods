using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Console commands for measuring the wall the whole design exists to
    /// support. Most are registered isCheat: true, so they only run once a
    /// player has enabled the developer console.
    ///
    /// rid_diag is the exception and is deliberately ungated -- see its own
    /// comment. A cheat command cannot run on a client connected to a
    /// dedicated server, which is the only place its counters mean anything.
    /// </summary>
    internal static class DebugCommands
    {
        private static readonly string[] SampleItems =
            { "Wood", "Stone", "Coal", "Iron", "Resin", "Flint" };

        // A small air gap between drawers, not zero: placed exactly
        // face-to-face (spacing == Width/Height with nothing added),
        // adjacent drawers' outer frame faces sit on the exact same plane,
        // which is textbook z-fighting. Derived from DrawerProportions
        // rather than re-measured here, and re-read fresh on every rid_wall
        // call (not cached), so a future resize of the drawer -- like the
        // 1m -> 0.66m one that made the previous hardcoded 1.0f spacing
        // wrong -- cannot silently desync this command from the geometry
        // again.
        private const float WallGap = 0.02f;

        public static void Register()
        {
            new Terminal.ConsoleCommand("rid_wall",
                "rid_wall <cols> <rows> [wood|stone|blackmarble] - build a test wall of drawers",
                args =>
                {
                    if (Player.m_localPlayer == null) return;

                    int cols = args.Length > 1 ? int.Parse(args[1]) : 10;
                    int rows = args.Length > 2 ? int.Parse(args[2]) : 10;
                    var tier = DrawerTier.Wood;
                    if (args.Length > 3)
                    {
                        if (args[3].Equals("stone", System.StringComparison.OrdinalIgnoreCase)) tier = DrawerTier.Stone;
                        else if (args[3].StartsWith("black", System.StringComparison.OrdinalIgnoreCase)) tier = DrawerTier.BlackMarble;
                    }

                    var prefab = ZNetScene.instance.GetPrefab(DrawerTiers.PrefabName(tier));
                    if (prefab == null) { args.Context.AddString("Drawer prefab not found"); return; }

                    var player = Player.m_localPlayer.transform;
                    var origin = player.position + player.forward * 6f;
                    var right = player.right;
                    int built = 0;

                    // Spacing derived from the actual drawer geometry, not
                    // a guessed constant -- see WallGap's comment. All three
                    // tiers share one DrawerProportions instance (see
                    // DrawerPieces.RegisterAll), so a fresh default instance
                    // here always matches whatever size is actually built.
                    var proportions = new DrawerProportions();
                    float colSpacing = proportions.Width + WallGap;
                    float rowSpacing = proportions.Height + WallGap;

                    for (int y = 0; y < rows; y++)
                    for (int x = 0; x < cols; x++)
                    {
                        var pos = origin
                                  + right * ((x - (cols - 1) / 2f) * colSpacing)
                                  + Vector3.up * (y * rowSpacing + proportions.Height / 2f);

                        var go = Object.Instantiate(prefab, pos,
                            Quaternion.LookRotation(-player.forward, Vector3.up));

                        var drawer = go.GetComponent<DrawerComponent>();
                        if (drawer != null)
                        {
                            string item = SampleItems[(x + y * cols) % SampleItems.Length];
                            drawer.TryDepositExternally(item, 250 + (x * 7 + y * 13) % 500, out _);
                        }
                        built++;
                    }

                    args.Context.AddString($"Built {built} drawers ({cols}x{rows}, {tier})");
                }, isCheat: true);

            new Terminal.ConsoleCommand("rid_stats",
                "rid_stats - report drawer counts and rendering cost",
                args =>
                {
                    int loaded = DrawerComponent.All.Count;
                    int assigned = 0, total = 0;
                    foreach (var d in DrawerComponent.All)
                    {
                        var s = d.Snapshot;
                        if (s.IsAssigned) assigned++;
                        total += s.Amount;
                    }

                    args.Context.AddString($"drawers loaded : {loaded}");
                    args.Context.AddString($"assigned       : {assigned}");
                    args.Context.AddString($"items held     : {total}");
                    args.Context.AddString($"atlas built    : {DrawerIconAtlas.IsBuilt}");
                }, isCheat: true);

            // Answers the one question that keeps coming up when a
            // container-aware mod "cannot see" a drawer, with the actual
            // numbers: who owns this drawer's ZDO, and what does
            // GetInventory hand back? The view shows the full count to every
            // client; a mismatch with the ZDO amount means a change is
            // waiting for its owner to reconcile it.
            new Terminal.ConsoleCommand("rid_owners",
                "rid_owners [radius] - who owns nearby drawers, and what automation sees",
                args =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) { args.Context.AddString("no local player"); return; }

                    float radius = 20f;
                    if (args.Length > 1 && float.TryParse(args[1], out float parsed)) radius = parsed;

                    long me = ZDOMan.GetSessionID();
                    args.Context.AddString($"my session id : {me}");
                    args.Context.AddString("name  zdoAmount  owner  isOwner  viewCount");

                    int shown = 0, pending = 0;
                    foreach (var d in DrawerComponent.All)
                    {
                        if (d == null) continue;
                        if ((d.transform.position - player.transform.position).sqrMagnitude > radius * radius) continue;

                        var snap = d.Snapshot;
                        var view = d.GetComponent<ZNetView>();
                        var zdo = view != null ? view.GetZDO() : null;
                        long owner = zdo != null ? zdo.GetOwner() : -1L;
                        bool isOwner = view != null && view.IsValid() && view.IsOwner();
                        bool unresolved = d.View != null && d.View.CannotResolve(snap);

                        // Through GetInventory, deliberately: that is the
                        // exact call other mods make.
                        var inv = d.GetInventory();
                        int viewCount = 0;
                        if (inv != null)
                            foreach (var item in inv.GetAllItems())
                                viewCount += item.m_stack;

                        string ownerDesc = owner == 0L ? "0 (nobody)" : owner == me ? $"{owner} (me)" : owner.ToString();
                        args.Context.AddString(
                            $"{(snap.IsAssigned ? snap.ItemName : "<empty>")}  {snap.Amount}  {ownerDesc}  {isOwner}  "
                            + (unresolved ? $"{viewCount} (item unresolved on this client)" : viewCount.ToString()));

                        // A view this client cannot lay out shows nothing by
                        // design; that is not a pending change.
                        if (!unresolved && viewCount != snap.Amount) pending++;
                        shown++;
                    }

                    args.Context.AddString($"-- {shown} drawer(s) within {radius}m, {pending} with a view change not yet reconciled");
                }, isCheat: true);

            // Replays OttoFuel's container filters (TastyUtils.GetNearbyContainers
            // and Smelters.RefuelSmelter) against the nearest smelter/kiln, for
            // every container nearby, and writes each check to the console and
            // the BepInEx log. Answers "OttoFuel skips a drawer" with the failing
            // check instead of a guess.
            new Terminal.ConsoleCommand("rid_probe",
                "rid_probe [range] - replay OttoFuel's container checks for the nearest smelter/kiln",
                args =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) { args.Context.AddString("no local player"); return; }
                    float range = 40f;
                    if (args.Length > 1 && float.TryParse(args[1], out float parsed)) range = parsed;

                    void Out(string line)
                    {
                        args.Context.AddString(line);
                        Debug.Log("[rid_probe] " + line);
                    }

                    Smelter smelter = null;
                    float best = float.MaxValue;
                    foreach (var s in Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None))
                    {
                        float dist = Vector3.Distance(s.transform.position, player.transform.position);
                        if (dist < best) { best = dist; smelter = s; }
                    }
                    if (smelter == null) { Out("no smelter/kiln loaded"); return; }

                    var sview = smelter.GetComponent<ZNetView>();
                    Out($"smelter {smelter.name} at {best:F1}m  isOwner={sview != null && sview.IsValid() && sview.IsOwner()}  "
                        + $"maxOre={smelter.m_maxOre} queue={smelter.GetQueueSize()}  Game.m_worldLevel={(global::Game.m_worldLevel)}");
                    foreach (var conv in smelter.m_conversion)
                        Out($"  conversion from {conv.m_from.name} ({conv.m_from.m_itemData.m_shared.m_name})");

                    System.Collections.IList ottoList = null;
                    var ottoType = System.Type.GetType("OttoFuel.OttoFuelPlugin, OttoFuel");
                    var listField = ottoType?.GetField("ContainerList",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    ottoList = listField?.GetValue(null) as System.Collections.IList;
                    Out(ottoList == null ? "OttoFuel ContainerList: not found" : $"OttoFuel ContainerList: {ottoList.Count} entries");

                    foreach (var c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
                    {
                        float dist = Vector3.Distance(smelter.transform.position, c.transform.position);
                        if (dist >= range) continue;

                        var inv = c.GetInventory();
                        var nv = c.GetComponent<ZNetView>();
                        Out($"-- {c.name} at {dist:F1}m from smelter");
                        Out($"   inOttoList={(ottoList == null ? "?" : ottoList.Contains(c).ToString())}  "
                            + $"pieceInParent={c.GetComponentInParent<Piece>() != null}  inventoryNull={inv == null}  "
                            + $"checkAccess={c.CheckAccess(player.GetPlayerID())}  inUse={c.IsInUse()}  "
                            + $"wardAccess={PrivateArea.CheckAccess(c.transform.position, 0f, false, false)}  "
                            + $"zdoValid={nv != null && nv.IsValid()}  isOwner={nv != null && nv.IsValid() && nv.IsOwner()}");
                        if (inv == null) continue;
                        Out($"   grid {inv.GetWidth()}x{inv.GetHeight()}  items={inv.GetAllItems().Count}");

                        c.Load();
                        foreach (var conv in smelter.m_conversion)
                        {
                            var found = new System.Collections.Generic.List<ItemDrop.ItemData>();
                            inv.GetAllItems(conv.m_from.m_itemData.m_shared.m_name, found);
                            if (found.Count == 0) continue;
                            foreach (var it in found)
                                Out($"   GetAllItems({conv.m_from.m_itemData.m_shared.m_name}): stack={it.m_stack} worldLevel={it.m_worldLevel} "
                                    + $"dropPrefab={(it.m_dropPrefab != null ? it.m_dropPrefab.name : "NULL")} quality={it.m_quality}");
                        }
                        foreach (var it in inv.GetAllItems())
                            Out($"   item {it.m_shared.m_name} stack={it.m_stack} worldLevel={it.m_worldLevel} "
                                + $"dropPrefab={(it.m_dropPrefab != null ? it.m_dropPrefab.name : "NULL")} pos={it.m_gridPos}");
                    }
                }, isCheat: true);

            // Answers "why is pulling items out slow, and why does it keep
            // saying try again" with counts rather than another theory.
            // Play for a minute with other players nearby, then run this.
            //
            // Reading the numbers:
            //   ownership changes high AND claims high -> this mod's own
            //     view-flush claim cycle is trading the drawer between
            //     peers, and nobody's ownership ever settles.
            //   refusals high but claims ~0 -> ownership is churning for a
            //     reason outside this mod.
            //   requests high with a real mean latency -> ordinary round
            //     trips; the drawer just belongs to another player.
            // NOT isCheat, unlike everything else here, and the reason is
            // that a cheat command cannot run where this one is needed.
            // Terminal.IsCheatsEnabled() is `m_cheat && ZNet.instance.IsServer()`,
            // so on a client connected to a dedicated server it is always
            // false -- being a server admin makes no difference. The
            // contention this counts only happens with several players on a
            // server, which is exactly the case a cheat gate excludes.
            //
            // Safe to leave open: it reports counters this client already
            // collected and changes no game state. "reset" zeroes those
            // counters and nothing else.
            new Terminal.ConsoleCommand("rid_diag",
                "rid_diag [reset] - withdraw contention counters since the last reset",
                args =>
                {
                    if (args.Length > 1 && args[1] == "reset")
                    {
                        DrawerDiagnostics.Reset();
                        args.Context.AddString("rid_diag: counters reset");
                        return;
                    }

                    args.Context.AddString($"refused (ownership unsettled) : {DrawerDiagnostics.RefusedUnsettled}");
                    args.Context.AddString($"ownership changes seen       : {DrawerDiagnostics.OwnershipChanges}");
                    args.Context.AddString($"claims made by this client   : {DrawerDiagnostics.ClaimsMade}");
                    args.Context.AddString($"withdraw requests sent       : {DrawerDiagnostics.RequestsSent}");
                    args.Context.AddString($"  granted                    : {DrawerDiagnostics.GrantsReceived}");
                    args.Context.AddString($"  gave up                    : {DrawerDiagnostics.GrantsGivenUp}");
                    args.Context.AddString($"  still outstanding          : {DrawerDiagnostics.OutstandingRequests}");
                    args.Context.AddString($"grant latency mean/max ms    : {DrawerDiagnostics.MeanGrantLatencyMs:F0} / {DrawerDiagnostics.MaxGrantLatencyMs:F0}");
                }, isCheat: false);
        }
    }
}
