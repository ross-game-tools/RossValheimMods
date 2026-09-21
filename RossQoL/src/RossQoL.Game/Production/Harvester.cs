using System;
using System.Collections.Generic;
using RossQoL.Core.Production;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// One harvest of one loaded producer this client owns, into live
    /// containers near it. Every container is used through its own
    /// Inventory, so its own rules decide what fits: chest-size mods and
    /// storage mods that subclass Container set their own room.
    ///
    /// Never duplicates: what landed is counted per container in a
    /// finally, and the producer is reduced in a finally by exactly that,
    /// so an exception part-way still takes out only what was placed.
    /// Never loses: units are taken rounded up, and what that rounding owes
    /// (or a fermenter batch's unplaced part) drops at the producer, as
    /// vanilla's extract and tap drop their output.
    /// </summary>
    internal static class Harvester
    {
        private static readonly List<Container> Nearby = new List<Container>();
        private static readonly List<Inventory> Destinations = new List<Inventory>();
        private static readonly List<DestinationCandidate> Candidates = new List<DestinationCandidate>();

        public static void HarvestBeehive(Beehive hive)
        {
            // As Beehive.Interact (Beehive.cs:99): no ward access, no harvest.
            if (!PrivateArea.CheckAccess(hive.transform.position, 0f, flash: false)) return;

            HarvestLevels(hive.m_nview, hive.m_honeyItem, hive.transform.position, hive.m_spawnPoint);
        }

        public static void HarvestSapCollector(SapCollector collector)
        {
            // As SapCollector.Interact (SapCollector.cs:89): no ward access, no harvest.
            if (!PrivateArea.CheckAccess(collector.transform.position, 0f, flash: false)) return;

            if (HarvestLevels(collector.m_nview, collector.m_spawnItem, collector.transform.position, collector.m_spawnPoint))
                collector.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_UpdateEffects");
        }

        public static void HarvestFermenter(Fermenter fermenter)
        {
            // As Fermenter.Interact (Fermenter.cs:198): no ward access, no harvest.
            if (!PrivateArea.CheckAccess(fermenter.transform.position, 0f, flash: false)) return;

            var zdo = fermenter.m_nview.GetZDO();
            int content = zdo.GetInt(ZDOVars.s_content);
            long start = zdo.GetLong(ZDOVars.s_startTime, 0L);
            if (!FermenterReadiness.IsReady(content, start, ZNet.instance.GetTime().Ticks, fermenter.m_fermentationDuration))
                return;

            var conversion = fermenter.GetItemConversion(content);
            if (conversion == null || conversion.m_to == null || conversion.m_producedItems <= 0) return;

            // As vanilla's DelayedTap decides it.
            bool cheated = (zdo.GetBool(ZDOVars.s_cheatedQueued) || zdo.GetBool(ZDOVars.s_cheated))
                           && !PlayerProfile.s_bypassCheatChecks;

            // DelayedTap spawns m_producedItems copies of the prefab, each
            // keeping the prefab's own stack.
            int batch = conversion.m_producedItems * Math.Max(1, conversion.m_to.m_itemData.m_stack);

            int placed = 0;
            try
            {
                Place(conversion.m_to, batch, batch, wholeOnly: true, cheated, fermenter.transform.position, ref placed);
            }
            finally
            {
                if (placed > 0)
                {
                    // As vanilla's RPC_Tap. The start time is cleared as the
                    // long it is read as.
                    zdo.Set(ZDOVars.s_content, 0);
                    zdo.Set(ZDOVars.s_startTime, 0L);
                    zdo.Set(ZDOVars.s_cheatedQueued, false);

                    var at = fermenter.m_outputPoint ? fermenter.m_outputPoint.position : fermenter.transform.position;
                    int owed = Math.Max(0, batch - placed);
                    try
                    {
                        DropAt(conversion.m_to, owed, at, cheated);
                    }
                    catch (Exception ex)
                    {
                        RossQoLPlugin.Log.LogError($"AutoHarvest: dropping {owed} x {conversion.m_to.name} at a fermenter failed: {ex}");
                    }
                }
            }
        }

        /// <summary>Beehives and sap collectors: a level counter worth ScaleDrops(item, 1) items per level.</summary>
        /// <returns>True when the producer's level changed.</returns>
        private static bool HarvestLevels(ZNetView nview, ItemDrop item, Vector3 origin, Transform spawnPoint)
        {
            if (item == null) return false;

            var zdo = nview.GetZDO();
            int levels = zdo.GetInt(ZDOVars.s_level);
            if (levels <= 0) return false;

            int perLevel = Math.Max(1, global::Game.instance.ScaleDrops(item.m_itemData, 1));

            int placed = 0;
            try
            {
                Place(item, levels * perLevel, perLevel, wholeOnly: false, cheated: false, origin, ref placed);
            }
            finally
            {
                if (placed > 0)
                {
                    int taken = Math.Min(levels, HarvestMath.UnitsTaken(placed, perLevel));
                    zdo.Set(ZDOVars.s_level, Math.Max(0, levels - taken));

                    var at = spawnPoint ? spawnPoint.position : origin;
                    int owed = HarvestMath.Shortfall(placed, perLevel);
                    try
                    {
                        DropAt(item, owed, at, cheated: false);
                    }
                    catch (Exception ex)
                    {
                        RossQoLPlugin.Log.LogError($"AutoHarvest: dropping {owed} x {item.name} at a producer failed: {ex}");
                    }
                }
            }

            return placed > 0;
        }

        /// <summary>
        /// Places a producer's whole output stack into one nearby container.
        /// The caller decides what to do when it does not all fit.
        /// </summary>
        /// <returns>How much landed: amount, or 0.</returns>
        public static int PlaceWholeStack(ItemDrop item, int amount, bool cheated, Vector3 origin)
        {
            int placed = 0;
            Place(item, amount, amount, wholeOnly: true, cheated, origin, ref placed);
            return placed;
        }

        /// <summary>
        /// Plans and places output. placed grows as each container's add
        /// is counted, so a throw part-way still reports what landed.
        /// </summary>
        private static void Place(ItemDrop item, int amount, int unitSize, bool wholeOnly, bool cheated, Vector3 origin, ref int placed)
        {
            if (amount <= 0 || Player.m_localPlayer == null) return;

            var shared = item.m_itemData.m_shared;
            string name = shared.m_name;
            int maxStack = Math.Max(1, shared.m_maxStackSize);
            float radius = ProductionConfig.HarvestRadius?.Value ?? 40f;
            long playerId = global::Game.instance.GetPlayerProfile().GetPlayerID();

            ContainerRegistry.Near(origin, radius, Nearby);
            Destinations.Clear();
            Candidates.Clear();

            foreach (var container in Nearby)
            {
                // Theft is stopped by the ward check at the producer, before anything is harvested.
                if (!ContainerAccess.MayUse(container, playerId)) continue;

                // A producer never fills a personal chest. Private privacy is
                // owner-only (and Group is unused by vanilla), so MayUse would
                // otherwise admit the harvesting player's OWN private chests --
                // output belongs in shared, public storage, not a personal one.
                if (container.m_privacy != Container.PrivacySetting.Public) continue;

                if (!ContainerAccess.IsFresh(container)) continue;

                var inventory = container.GetInventory();
                if (inventory == null) continue;

                int room = HarvestMath.Room(
                    inventory.FindFreeStackSpace(name, global::Game.m_worldLevel),
                    inventory.GetEmptySlots(),
                    maxStack);
                if (room <= 0) continue;

                // Used only when this client owns it and that ownership has
                // settled. Never claimed here: ClaimOwnership only raises the
                // owner revision by one, and peers adopt an owner only for a
                // strictly higher revision, so two claims at once (or a claim
                // racing the server's ReleaseNearbyZDOS) can leave two clients
                // both owning it and one write lost. Nobody owns it: the
                // server gives it to a nearby player within about 2 s, as for
                // any chest. Another player owns it: leave it to them.
                var nview = container.m_nview;
                // A self-managing container -- a storage mod's Container
                // subclass such as an item drawer -- persists a non-owner
                // deposit safely through its own AddItem/Save protocol, so
                // RossQoL's settle wait (there to stop two overlapping raw
                // writes losing one on a VANILLA chest) does not apply. It is
                // exactly what dropped a stocked drawer whenever the drawer's
                // own flush had just re-claimed it, inside the 2s revision-
                // settle window, sending output to a nearer normal chest.
                // Mirrors IsFresh, which already trusts every subclass.
                bool selfManaging = container.GetType() != typeof(Container);
                if (!selfManaging && !ContainerOwnership.IsSettled(container, nview)) continue;

                Candidates.Add(new DestinationCandidate(
                    Destinations.Count,
                    inventory.HaveItem(name, matchWorldLevel: false),
                    room,
                    (container.transform.position - origin).sqrMagnitude));
                Destinations.Add(inventory);
            }

            if (Candidates.Count == 0) return;

            var plan = HarvestPlan.Plan(amount, unitSize, HarvestPlan.Rank(Candidates), wholeOnly);
            foreach (var placement in plan)
                AddTo(Destinations[placement.Index], item, placement.Amount, cheated, maxStack, ref placed);
        }

        /// <summary>
        /// Adds amount in stacks no larger than the max stack size (vanilla
        /// AddItem(ItemData) would otherwise place one oversized stack) and
        /// counts what the inventory actually gained.
        /// </summary>
        private static void AddTo(Inventory inventory, ItemDrop item, int amount, bool cheated, int maxStack, ref int placed)
        {
            string name = item.m_itemData.m_shared.m_name;
            int before = inventory.CountItems(name, -1, matchWorldLevel: false);
            try
            {
                int remaining = amount;
                while (remaining > 0)
                {
                    int chunk = Math.Min(remaining, maxStack);

                    // As Inventory.AddItem(GameObject, int) builds it, with
                    // the cheated flag vanilla's drop would carry.
                    var data = item.m_itemData.Clone();
                    data.m_dropPrefab = item.gameObject;
                    data.m_stack = chunk;
                    data.m_worldLevel = (byte)global::Game.m_worldLevel;
                    data.m_cheated = cheated;

                    // A partial fit keeps what it added and returns false.
                    if (!inventory.AddItem(data)) break;
                    remaining -= chunk;
                }
            }
            finally
            {
                int gained = inventory.CountItems(name, -1, matchWorldLevel: false) - before;
                placed += Mathf.Clamp(gained, 0, amount);
            }
        }

        /// <summary>Drops items at a producer, as vanilla's extract and tap do.</summary>
        private static void DropAt(ItemDrop item, int amount, Vector3 position, bool cheated)
        {
            int maxStack = Math.Max(1, item.m_itemData.m_shared.m_maxStackSize);
            while (amount > 0)
            {
                int stack = Math.Min(amount, maxStack);
                var drop = UnityEngine.Object.Instantiate(item, position + Vector3.up * 0.25f, Quaternion.identity);
                ItemDrop.OnCreateNew(drop, cheated);
                drop.SetStack(stack);
                amount -= stack;
            }
        }
    }
}
