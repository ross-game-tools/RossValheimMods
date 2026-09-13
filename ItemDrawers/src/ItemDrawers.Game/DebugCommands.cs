using ItemDrawers.Core;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Console commands for measuring the wall the whole design exists to
    /// support. Neither command is meant to ship active in a normal game --
    /// both are registered isCheat: true, so they only run once a player has
    /// enabled the developer console.
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
            // container-aware mod "cannot see" a drawer, and answers it with
            // the actual numbers rather than another hypothesis: who owns
            // this drawer's ZDO, and what does GetInventory hand back?
            //
            // Those two are the whole story for automation. A drawer whose
            // mirror is empty while its ZDO is not is a drawer this client
            // is not allowed to expose (see DrawerComponent.RefreshMirror),
            // and the owner column says why.
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
                    args.Context.AddString("name  zdoAmount  owner  isOwner  mirrorCount");

                    int shown = 0, hidden = 0;
                    foreach (var d in DrawerComponent.All)
                    {
                        if (d == null) continue;
                        if ((d.transform.position - player.transform.position).sqrMagnitude > radius * radius) continue;

                        var snap = d.Snapshot;

                        // Ownership is read BEFORE GetInventory, and the
                        // order matters. GetInventory runs RefreshMirror,
                        // which claims an unowned drawer as a side effect
                        // (see ClaimForAutomationIfUnowned) -- so reading
                        // afterwards would report the ownership this command
                        // just caused, and every drawer would look owned on
                        // the first run no matter what the real state was.
                        var view = d.GetComponent<ZNetView>();
                        var zdo = view != null ? view.GetZDO() : null;
                        long owner = zdo != null ? zdo.GetOwner() : -1L;
                        bool isOwner = view != null && view.IsValid() && view.IsOwner();

                        // Through GetInventory, deliberately: that is the
                        // exact call OttoFuel and NVLB make, so this reports
                        // what they see rather than what we believe they see.
                        var inv = d.GetInventory();
                        int mirrorCount = 0;
                        if (inv != null)
                            foreach (var item in inv.GetAllItems())
                                mirrorCount += item.m_stack;

                        string ownerDesc = owner == 0L ? "0 (nobody)" : owner == me ? $"{owner} (me)" : owner.ToString();
                        args.Context.AddString(
                            $"{(snap.IsAssigned ? snap.ItemName : "<empty>")}  {snap.Amount}  {ownerDesc}  {isOwner}  {mirrorCount}");

                        if (snap.Amount > 0 && mirrorCount == 0) hidden++;
                        shown++;
                    }

                    args.Context.AddString($"-- {shown} drawer(s) within {radius}m, {hidden} holding items but invisible to automation");
                    args.Context.AddString("note: this claims unowned drawers as a side effect, so a second run can differ from the first");
                }, isCheat: true);
        }
    }
}
