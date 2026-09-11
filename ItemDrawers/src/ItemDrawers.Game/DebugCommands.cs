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
        }
    }
}
