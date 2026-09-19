using System.Collections.Generic;
using RossQoL.Game.Framework;
using RossQoL.Game.Production;
using UnityEngine;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// The containers a craft may draw on, and the taking of materials from
    /// them.
    ///
    /// ONE rule decides membership, and it is the rule for both halves of a
    /// craft: whatever may be counted towards the cost must also be
    /// chargeable for it. A container admitted on reading rules but refused
    /// on writing rules is the shape of a duplication bug -- vanilla hands
    /// the player the crafted item before the charging step runs, so anything
    /// counted but not charged is simply free.
    ///
    /// The writing rule is therefore as narrow as it can be while still
    /// being true at write time: the container's networking is up and has a
    /// ZDO, so ownership of it can be claimed. The claim itself happens in
    /// <see cref="Take"/>, immediately before the removal, and is verified
    /// before anything is written -- ownership is unilateral and optimistic
    /// (see docs/valheim-api/containers.md), so claiming late keeps the race
    /// window as small as it can be made.
    ///
    /// Counting deliberately mirrors vanilla's own question -- quality and
    /// world level included -- so what a recipe sees in a chest is what it
    /// would see in your pack.
    /// </summary>
    internal static class ChestCrafting
    {
        private static readonly List<Container> Usable = new List<Container>();

        /// <summary>
        /// Containers near the player that a craft may both count and charge.
        /// The list is reused, so callers must finish with it -- or copy it --
        /// before asking again.
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
                if (!Chargeable(container)) continue;
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
                if (container == null) continue;

                var inventory = container.GetInventory();
                if (inventory != null) total += inventory.CountItems(sharedName, quality);
            }

            return total;
        }

        /// <summary>
        /// Removes up to amount of an item, at that one quality, from those
        /// containers. Ownership is claimed and confirmed for each container
        /// immediately before it is written; a container whose claim does not
        /// stick pays nothing, and the caller is responsible for saying so.
        /// </summary>
        /// <returns>How many were actually removed.</returns>
        public static int Take(List<Container> boxes, string sharedName, int amount, int quality)
        {
            int taken = 0;
            if (boxes == null || amount <= 0) return 0;

            foreach (var container in boxes)
            {
                if (taken >= amount) break;
                if (container == null) continue;

                var inventory = container.GetInventory();
                if (inventory == null) continue;

                int held = inventory.CountItems(sharedName, quality);
                int take = Mathf.Min(held, amount - taken);
                if (take <= 0) continue;

                // Claimed here, not earlier: the narrower the gap between the
                // claim and the write, the smaller the window in which another
                // peer's write to the same container can win the revision race.
                if (!Claim(container))
                {
                    RossQoLPlugin.Log.LogWarning(
                        $"CraftFromChests: could not take charge of a container holding {sharedName}; "
                        + "it pays nothing towards this craft.");
                    continue;
                }

                inventory.RemoveItem(sharedName, take, quality);
                container.Save();
                taken += take;
            }

            return taken;
        }

        /// <summary>
        /// Whether this container could be written at all: its networking is
        /// up and it has a ZDO, so ownership of it can be claimed. Not a
        /// promise that the claim will stick -- only <see cref="Take"/> can
        /// know that, and it checks.
        /// </summary>
        private static bool Chargeable(Container container)
        {
            var nview = container.m_nview;
            if (nview == null || !nview.IsValid()) return false;

            return nview.GetZDO() != null;
        }

        /// <summary>Takes ownership of a container and confirms it actually happened.</summary>
        private static bool Claim(Container container)
        {
            var nview = container.m_nview;
            if (nview == null || !nview.IsValid()) return false;
            if (nview.GetZDO() == null) return false;

            if (!nview.IsOwner()) nview.ClaimOwnership();
            return nview.IsOwner();
        }
    }
}
