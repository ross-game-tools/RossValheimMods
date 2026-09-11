using System.Collections.Generic;

namespace ItemDrawers.Game
{
    public enum DrawerTier { Wood, Stone, BlackMarble }

    public static class DrawerTiers
    {
        public static readonly DrawerTier[] All =
            { DrawerTier.Wood, DrawerTier.Stone, DrawerTier.BlackMarble };

        /// <summary>
        /// Written into save files, therefore permanent. Renaming one of
        /// these after release orphans every drawer players have built.
        /// </summary>
        public static string PrefabName(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return "rid_drawer_wood";
                case DrawerTier.Stone: return "rid_drawer_stone";
                case DrawerTier.BlackMarble: return "rid_drawer_blackmarble";
                default: return "rid_drawer_wood";
            }
        }

        public static string DisplayName(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return "Item Drawer";
                case DrawerTier.Stone: return "Stone Item Drawer";
                case DrawerTier.BlackMarble: return "Black Marble Item Drawer";
                default: return "Item Drawer";
            }
        }

        public static int DefaultCapacity(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return 1000;
                case DrawerTier.Stone: return 2000;
                case DrawerTier.BlackMarble: return 10000;
                default: return 1000;
            }
        }

        /// <summary>Build cost, as (item prefab name, amount) pairs.</summary>
        public static IReadOnlyList<(string Item, int Amount)> Recipe(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood:
                    return new[] { ("FineWood", 10) };
                case DrawerTier.Stone:
                    return new[] { ("FineWood", 5), ("Stone", 10) };
                case DrawerTier.BlackMarble:
                    return new[] { ("FineWood", 5), ("BlackMarble", 10) };
                default:
                    return new[] { ("FineWood", 10) };
            }
        }

        /// <summary>
        /// Candidate donor prefabs for a tier, most preferred first. The first
        /// one that resolves with a usable MeshRenderer and Piece wins.
        ///
        /// A list rather than a single name because guessing wrong is silent:
        /// 'blackmarble_post01' did not exist, so that tier simply never
        /// registered and the drawer was missing from the build menu with
        /// nothing obviously broken. Every name below was verified present in
        /// the game's asset manifest.
        /// </summary>
        public static string[] MaterialDonors(DrawerTier tier)
        {
            switch (tier)
            {
                // Structural building pieces, NOT chests. A chest's material
                // uses a bespoke unwrapped atlas -- planks, iron bands and lid
                // parts laid out in UV space for that one model. Box-projected
                // UVs sample it at arbitrary coordinates and the drawer ends
                // up wearing fragments of a chest. Walls and floors are built
                // to go on arbitrary geometry, so their textures tile.
                case DrawerTier.Wood:
                    return new[] { "wood_wall", "wood_floor", "wood_pole", "piece_chest_wood" };
                case DrawerTier.Stone:
                    return new[] { "stone_wall_1x1", "stone_wall_2x1", "stone_floor", "piece_chest_grausten" };
                case DrawerTier.BlackMarble:
                    return new[] { "blackmarble_1x1", "blackmarble_2x2", "bench_blackmarble", "blackmarble_column_small" };
                default:
                    return new[] { "piece_chest_wood" };
            }
        }

        /// <summary>Kept for callers that want a single name for logging.</summary>
        public static string MaterialDonor(DrawerTier tier) => MaterialDonors(tier)[0];
    }
}
