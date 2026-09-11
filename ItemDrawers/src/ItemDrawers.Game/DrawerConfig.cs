using BepInEx.Configuration;

namespace ItemDrawers.Game
{
    /// <summary>
    /// All player-tunable knobs. Every entry that affects gameplay balance
    /// (capacities, pickup behaviour) is marked admin-only so Jotunn's
    /// SynchronizationManager pushes the server's values to every client --
    /// a client raising its own capacity locally would otherwise let it see
    /// a different world state than the server enforces.
    /// </summary>
    public static class DrawerConfig
    {
        public static ConfigEntry<int> WoodCapacity;
        public static ConfigEntry<int> StoneCapacity;
        public static ConfigEntry<int> BlackMarbleCapacity;
        public static ConfigEntry<bool> AutoPickupEnabled;
        public static ConfigEntry<float> PickupRadius;
        public static ConfigEntry<float> PickupScanRange;
        public static ConfigEntry<float> PickupInterval;
        public static ConfigEntry<float> LabelDistance;

        public static void Bind(ConfigFile config)
        {
            WoodCapacity = config.Bind("Capacity", "Wood", 1000,
                new ConfigDescription("How many items a wood drawer holds.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            StoneCapacity = config.Bind("Capacity", "Stone", 2000,
                new ConfigDescription("How many items a stone drawer holds.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            BlackMarbleCapacity = config.Bind("Capacity", "BlackMarble", 10000,
                new ConfigDescription("How many items a black marble drawer holds.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            AutoPickupEnabled = config.Bind("Pickup", "Enabled", true,
                new ConfigDescription("Drawers absorb matching items dropped nearby.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            // Default raised from 4m to 40m: 4m meant a dropped item had to
            // land almost on top of a drawer to be absorbed, which read as
            // "auto-pickup doesn't work" for anything thrown down a few
            // steps away. 40m matches PickupScanRange below, so a drop
            // anywhere this mod considers at all is also within absorption
            // range of a drawer that wants it -- Radius bounded tighter
            // than ScanRange would silently reintroduce the same "why
            // didn't it pick this up" gap one config layer down.
            //
            // Cost tradeoff, sanity-checked rather than assumed: this is
            // the radius SpatialGrid.Query uses per qualifying dropped item
            // (see DrawerManager.RunAutoPickup), not a one-time cost.
            // SpatialGrid's cell size is 8m, and Query visits
            // (2*ceil(radius/8)+1)^3 candidate cells -- 27 at 4m, 1331 at
            // 40m, roughly 49x more cell lookups per drop. For a packed
            // hundred-drawer wall, a 40m query can also return the entire
            // wall as candidates for every drop within range, where a 4m
            // query would only ever have matched a handful of immediately
            // adjacent drawers. This is a real, non-trivial per-query cost
            // increase, not a free change -- it is accepted here because
            // RunAutoPickup only runs once per PickupInterval (0.5s
            // default), not per frame, and because the outer scan is
            // already bounded to the same 40m by PickupScanRange, so this
            // does not touch any MORE dropped items than before, only makes
            // each one's own query more thorough. If a server with very
            // large walls and frequent simultaneous drops (e.g. a boss
            // fight loot pile near a storage room) ever sees this show up
            // in profiling, lowering PickupInterval's frequency or capping
            // drops-processed-per-tick are the next levers, not implemented
            // here since raising this default was the only change asked
            // for.
            PickupRadius = config.Bind("Pickup", "Radius", 40f,
                new ConfigDescription("How far from a drawer a dropped item is absorbed, in metres.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            PickupScanRange = config.Bind("Pickup", "ScanRange", 40f,
                new ConfigDescription("How far around you dropped items are considered at all. " +
                    "One query covers every drawer, so this is cheap.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            PickupInterval = config.Bind("Pickup", "Interval", 0.5f,
                new ConfigDescription("Seconds between pickup passes.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            // Purely a local display preference -- not admin-only, since a
            // client choosing when labels fade affects nobody else.
            LabelDistance = config.Bind("Display", "LabelDistance", 30f,
                "Beyond this distance drawer icons and counts switch off. " +
                "A wall of text nobody can read is wasted work.");
        }

        public static int CapacityFor(DrawerTier tier)
        {
            switch (tier)
            {
                case DrawerTier.Wood: return WoodCapacity?.Value ?? DrawerTiers.DefaultCapacity(tier);
                case DrawerTier.Stone: return StoneCapacity?.Value ?? DrawerTiers.DefaultCapacity(tier);
                case DrawerTier.BlackMarble: return BlackMarbleCapacity?.Value ?? DrawerTiers.DefaultCapacity(tier);
                default: return DrawerTiers.DefaultCapacity(tier);
            }
        }
    }
}
