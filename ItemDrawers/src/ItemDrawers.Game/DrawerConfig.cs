using System.Collections.Generic;
using BepInEx.Configuration;
using ItemDrawers.Core;
using UnityEngine;
using Jotunn.Managers;

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
        public static ConfigEntry<KeyCode> TakeOneKey;
        public static ConfigEntry<KeyCode> DepositAllKey;

        private static readonly Dictionary<DrawerTier, ConfigEntry<string>> Recipes =
            new Dictionary<DrawerTier, ConfigEntry<string>>();

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

            // Admin-only like the capacities, and for the same reason: a
            // client that could set its own build cost would be building
            // pieces the server never charged it for.
            //
            // Defaults are rendered through the same formatter the parser
            // reads, so the text a fresh install writes into the config file
            // is text that round-trips -- a hand-written default string
            // could disagree with DrawerTiers.Recipe and nothing would catch
            // it.
            foreach (var tier in DrawerTiers.All)
            {
                Recipes[tier] = config.Bind("Recipe", tier.ToString(),
                    RecipeSpec.Format(DrawerTiers.Recipe(tier)),
                    new ConfigDescription(
                        "Build cost, as Item:Count separated by commas -- e.g. FineWood:5,Stone:10. "
                        + "Names are PREFAB names (FineWood, BlackMarble, RoundLog), not the names "
                        + "shown in game. An unparseable or unknown-item recipe is logged and the "
                        + "default is used instead. Takes effect on restart.",
                        null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            }

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

            // Held while interacting to take a single item, or to deposit
            // everything matching. Real keys, not Valheim input actions.
            //
            // These used to read ZInput.GetButton("Crouch") and ("Run"),
            // which meant the drawer's controls silently moved whenever a
            // player rebound crouching or running -- two actions with
            // nothing to do with storage, which people rebind for movement
            // reasons and then find their drawers behaving differently.
            // Reading the key directly makes the binding this mod's own, and
            // configurable without touching Valheim's controls.
            //
            // Local, not admin-only: which key a player holds is theirs to
            // choose and affects nobody else on the server.
            //
            // Set either to None to switch that action off; the plain E
            // interaction is unaffected either way.
            TakeOneKey = config.Bind("Controls", "TakeOneKey", KeyCode.LeftControl,
                "Held with Interact to take a single item, or to unassign an empty drawer. "
                + "A UnityEngine.KeyCode name, e.g. LeftControl, LeftAlt, C. None disables it.");

            DepositAllKey = config.Bind("Controls", "DepositAllKey", KeyCode.LeftShift,
                "Held with Interact to deposit every matching item you carry. "
                + "A UnityEngine.KeyCode name, e.g. LeftShift, LeftAlt, V. None disables it.");

            // Purely a local display preference -- not admin-only, since a
            // client choosing when labels fade affects nobody else.
            LabelDistance = config.Bind("Display", "LabelDistance", 30f,
                "Beyond this distance drawer icons and counts switch off. " +
                "A wall of text nobody can read is wasted work.");
        }

        /// <summary>
        /// The configured build cost, or the shipped default when the
        /// configured one cannot be used.
        ///
        /// Falls back loudly rather than silently: a server admin who typos
        /// a recipe gets a log line naming the tier and what is wrong with
        /// their line, and a working piece in the meantime. Returning an
        /// empty recipe instead would register a drawer that costs nothing,
        /// which is a worse failure than ignoring the edit.
        /// </summary>
        public static IReadOnlyList<(string Item, int Amount)> RecipeFor(DrawerTier tier)
        {
            var fallback = DrawerTiers.Recipe(tier);
            if (!Recipes.TryGetValue(tier, out var entry) || entry == null) return fallback;

            var spec = RecipeSpec.Parse(entry.Value);
            string problem = spec.Ok ? UnknownItem(spec.Requirements) : spec.Error;
            if (problem == null) return spec.Requirements;

            DrawerPlugin.Log.LogWarning(
                $"Recipe.{tier} is not usable ({problem}); using the default "
                + $"{RecipeSpec.Format(fallback)} instead.");
            return fallback;
        }

        /// <summary>
        /// The first ingredient Valheim has never heard of, or null if every
        /// name resolves.
        ///
        /// RecipeSpec deliberately does not do this -- Core has no prefab
        /// database -- but somebody has to, because a requirement naming a
        /// prefab that does not exist does not fail loudly. Jotunn drops it,
        /// and the drawer registers with a recipe quietly missing an
        /// ingredient, or costing nothing at all. Somebody who wrote
        /// "Fine Wood" would get a cheaper drawer and no indication of why.
        ///
        /// Asks PrefabManager, NOT ObjectDB. The first version of this used
        /// ObjectDB.instance.GetItemPrefab and rejected every recipe it was
        /// given, including the shipped defaults -- "no item named
        /// 'FineWood'" -- because pieces register on
        /// OnVanillaPrefabsAvailable and ObjectDB's items are not populated
        /// until later in the same startup. The check was querying an empty
        /// database and reading the emptiness as a typo, so configured
        /// recipes silently never applied.
        ///
        /// PrefabManager is the right source at this moment by
        /// construction: it is what OnVanillaPrefabsAvailable exists to
        /// announce, and it is what this class's own material-donor lookup
        /// already resolves against successfully at the same point in
        /// startup. It matches any prefab rather than only items, so it is a
        /// weaker check than "is this craftable" -- but it catches the
        /// misspellings this exists for, and a check that runs is worth more
        /// than a stricter one that cannot.
        /// </summary>
        private static string UnknownItem(IReadOnlyList<(string Item, int Amount)> requirements)
        {
            var prefabs = PrefabManager.Instance;

            // Called before Jotunn is ready. Do not invent a failure: let
            // the recipe through and let registration report what it cannot
            // resolve. Guessing wrong in this direction is exactly the bug
            // described above.
            if (prefabs == null) return null;

            foreach (var r in requirements)
                if (prefabs.GetPrefab(r.Item) == null)
                    return $"no item named '{r.Item}'";

            return null;
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
