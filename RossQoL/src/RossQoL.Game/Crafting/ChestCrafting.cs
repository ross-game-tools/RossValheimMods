using System.Collections.Generic;
using RossQoL.Game.Production;
using UnityEngine;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// The containers a craft may draw on, and the taking of materials from
    /// them.
    ///
    /// Held to the same rules as the rest of the mod's container work: the
    /// shared registry finds them, a container is used only when this client
    /// may use it and its contents are up to date, and it is written only
    /// when this client's ownership of it has settled. Ownership is never
    /// claimed.
    ///
    /// Counting deliberately mirrors vanilla's own question -- quality and
    /// world level included -- so what a recipe sees in a chest is what it
    /// would see in your pack.
    /// </summary>
    internal static class ChestCrafting
    {
        private static readonly List<Container> Usable = new List<Container>();

        /// <summary>
        /// Containers near the player that may be read from. The list is
        /// reused, so callers must finish with it before asking again.
        /// </summary>
        public static List<Container> Near(Vector3 origin)
        {
            Usable.Clear();
            if (Player.m_localPlayer == null) return Usable;

            float radius = CraftFromChestsConfig.CraftRadius?.Value ?? 40f;
            long playerId = global::Game.instance.GetPlayerProfile().GetPlayerID();

            var found = new List<Container>();
            ContainerRegistry.Near(origin, radius, found);

            foreach (var container in found)
            {
                if (!ContainerAccess.MayUse(container, playerId)) continue;
                if (!ContainerAccess.IsFresh(container)) continue;
                if (container.GetInventory() == null) continue;

                Usable.Add(container);
            }

            return Usable;
        }

        /// <summary>How many of an item sit in those containers.</summary>
        public static int Count(List<Container> boxes, string sharedName, int quality)
        {
            int total = 0;
            foreach (var container in boxes)
            {
                var inventory = container.GetInventory();
                if (inventory != null) total += inventory.CountItems(sharedName, quality);
            }

            return total;
        }

        /// <summary>
        /// Removes up to amount of an item from those containers.
        /// </summary>
        /// <returns>How many were actually removed.</returns>
        public static int Take(List<Container> boxes, string sharedName, int amount, int quality)
        {
            int taken = 0;

            foreach (var container in boxes)
            {
                if (taken >= amount) break;

                // Written, not just read: the same settle rule the rest of the
                // mod uses, so two clients never write one container at once.
                if (!ContainerOwnership.IsSettled(container, container.m_nview)) continue;

                var inventory = container.GetInventory();
                if (inventory == null) continue;

                int held = inventory.CountItems(sharedName, quality);
                int take = Mathf.Min(held, amount - taken);
                if (take <= 0) continue;

                inventory.RemoveItem(sharedName, take, quality);
                container.Save();
                taken += take;
            }

            return taken;
        }
    }
}
