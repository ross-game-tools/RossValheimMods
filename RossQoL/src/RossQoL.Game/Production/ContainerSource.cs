using System;
using System.Collections.Generic;
using RossQoL.Core.Production;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Taking items out of nearby containers, the mirror of Harvester's
    /// putting them in, and held to the same rules: a container is used only
    /// through its own Inventory, only when this client may use it, only when
    /// its contents are up to date, and only when this client's ownership of
    /// it has settled. Ownership is never claimed.
    ///
    /// Never duplicates: what a producer is given is exactly what was removed,
    /// counted per container as it is removed, so a throw part-way leaves the
    /// items in the container rather than in both places.
    /// </summary>
    internal static class ContainerSource
    {
        private static readonly List<Container> Nearby = new List<Container>();
        private static readonly List<Container> Usable = new List<Container>();
        private static readonly List<ItemDrop.ItemData> Found = new List<ItemDrop.ItemData>();

        /// <summary>Parsed MinimumPerItem, rebuilt only when the setting changes.</summary>
        private static string _minimumsText;
        private static Dictionary<string, int> _minimums = new Dictionary<string, int>(FeedRules.NameComparer);

        /// <summary>Parsed MaxOutput, rebuilt only when the setting changes.</summary>
        private static string _capsText;
        private static Dictionary<string, int> _caps = new Dictionary<string, int>(FeedRules.NameComparer);

        public static IDictionary<string, int> Minimums()
        {
            string text = AutoFeedConfig.MinimumPerItem?.Value ?? string.Empty;
            if (!ReferenceEquals(text, _minimumsText) && text != _minimumsText)
            {
                _minimums = FeedRules.ParseAmounts(text);
                _minimumsText = text;
            }
            return _minimums;
        }

        public static IDictionary<string, int> Caps()
        {
            string text = AutoFeedConfig.MaxOutput?.Value ?? string.Empty;
            if (!ReferenceEquals(text, _capsText) && text != _capsText)
            {
                _caps = FeedRules.ParseAmounts(text);
                _capsText = text;
            }
            return _caps;
        }

        /// <summary>
        /// Removes up to wanted of an item from containers near a producer.
        /// </summary>
        /// <param name="cheated">True when any stack taken carried vanilla's cheated flag.</param>
        /// <returns>How many were actually removed.</returns>
        public static int Take(Vector3 origin, ItemDrop item, int wanted, out bool cheated)
        {
            cheated = false;
            if (item == null || wanted <= 0 || Player.m_localPlayer == null) return 0;

            string sharedName = item.m_itemData.m_shared.m_name;
            string prefabName = item.gameObject.name;
            int fallback = AutoFeedConfig.MinimumLeftBehind?.Value ?? 0;
            int minimum = FeedRules.MinimumFor(Minimums(), prefabName, fallback);
            long playerId = global::Game.instance.GetPlayerProfile().GetPlayerID();

            ContainerRegistry.Near(origin, AutoFeedConfig.FeedRadius?.Value ?? 40f, Nearby);

            // The reserve is what stays in the area, not what stays in each
            // chest: with five chests of wood and a minimum of 50, the player
            // means "keep 50 wood", not "keep 250". So the total is counted
            // first, and only the surplus above it is taken.
            Usable.Clear();
            int available = 0;
            foreach (var container in Nearby)
            {
                if (!ContainerAccess.MayUse(container, playerId)) continue;
                if (!ContainerAccess.IsFresh(container)) continue;

                var inventory = container.GetInventory();
                if (inventory == null) continue;

                int held = inventory.CountItems(sharedName, -1, matchWorldLevel: false);
                if (held <= 0) continue;

                Usable.Add(container);
                available += held;
            }

            int allowed = FeedRules.Takeable(available, wanted, minimum);
            if (allowed <= 0)
            {
                Usable.Clear();
                return 0;
            }

            int taken = 0;
            foreach (var container in Usable)
            {
                if (taken >= allowed) break;

                // Only when this client owns it and that ownership has settled:
                // the same rule harvesting uses, and for the same reason -- two
                // clients writing one container lose one of the two writes.
                if (!ContainerOwnership.IsSettled(container, container.m_nview)) continue;

                var inventory = container.GetInventory();
                if (inventory == null) continue;

                int held = inventory.CountItems(sharedName, -1, matchWorldLevel: false);
                int take = Math.Min(held, allowed - taken);
                if (take <= 0) continue;

                Found.Clear();
                inventory.GetAllItems(sharedName, Found);
                foreach (var found in Found)
                    if (found != null && found.m_cheated) cheated = true;
                Found.Clear();

                inventory.RemoveItem(sharedName, take, -1, worldLevelBased: false);
                container.Save();
                taken += take;
            }

            Usable.Clear();
            return taken;
        }

        /// <summary>
        /// How many of an item sit in containers near a producer, for the
        /// output caps. Reads only, so no ownership is needed.
        /// </summary>
        public static int CountNearby(Vector3 origin, string sharedName)
        {
            if (string.IsNullOrEmpty(sharedName) || Player.m_localPlayer == null) return 0;

            long playerId = global::Game.instance.GetPlayerProfile().GetPlayerID();
            ContainerRegistry.Near(origin, AutoFeedConfig.FeedRadius?.Value ?? 40f, Nearby);

            int total = 0;
            foreach (var container in Nearby)
            {
                if (!ContainerAccess.MayUse(container, playerId)) continue;

                var inventory = container.GetInventory();
                if (inventory == null) continue;

                total += inventory.CountItems(sharedName, -1, matchWorldLevel: false);
            }

            return total;
        }
    }
}
