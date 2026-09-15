using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Game.Framework;
using RossQoL.Game.Production;
using UnityEngine;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// MonsterAI.UpdateConsumeItem runs on the creature's owner. Every
    /// m_consumeSearchInterval seconds it resets m_consumeSearchTimer to 0,
    /// returns early if the tame is not hungry, and otherwise looks for
    /// food on the ground. It returns false when it found none.
    ///
    /// Right after such a search, with nothing found, a hungry tame eats one
    /// item it would eat from the nearest container that has one: the item
    /// is removed through the container's own Inventory (drawers included),
    /// and only when exactly one left does the tame get fed, through
    /// vanilla's own consume callback, effect and animation.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateConsumeItem))]
    internal static class FeedFromContainersPatch
    {
        private static readonly List<Container> Nearby = new List<Container>();

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(MonsterAI), nameof(MonsterAI.UpdateConsumeItem), FeedFromContainersFeature.FeatureName);

        private static void Postfix(MonsterAI __instance, Humanoid humanoid, bool __result)
        {
            if (FeedFromContainersFeature.Instance?.IsActive != true) return;
            if (__result) return;

            // An exception escaping here would break this creature's AI update.
            try
            {
                if (__instance.m_consumeSearchTimer != 0f) return;
                if (Player.m_localPlayer == null) return;

                var tameable = __instance.m_tamable;
                if (tameable == null || !tameable.IsTamed() || !tameable.IsHungry()) return;

                var nview = __instance.m_nview;
                if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;

                var foods = __instance.m_consumeItems;
                if (foods == null || foods.Count == 0) return;

                FeedFromNearestContainer(__instance, humanoid, foods);
            }
            catch (Exception ex)
            {
                // Once per exception type: a failure that repeats for every
                // tame on every search would otherwise flood the log.
                string type = ex.GetType().FullName;
                if (LoggedFailures.Add(type))
                    RossQoLPlugin.Log.LogError($"FeedFromContainers: feeding {__instance.name} failed and was skipped (further {type} failures are not logged): {ex}");
            }
        }

        private static readonly HashSet<string> LoggedFailures = new HashSet<string>();

        private static void FeedFromNearestContainer(MonsterAI ai, Humanoid humanoid, List<ItemDrop> foods)
        {
            var origin = ai.transform.position;
            float radius = FeedConfig.FeedRadius?.Value ?? 10f;
            long playerId = global::Game.instance.GetPlayerProfile().GetPlayerID();

            ContainerRegistry.Near(origin, radius, Nearby);

            Inventory bestInventory = null;
            ItemDrop.ItemData bestItem = null;
            float bestDistance = float.MaxValue;

            foreach (var container in Nearby)
            {
                float distance = (container.transform.position - origin).sqrMagnitude;
                if (distance >= bestDistance) continue;
                if (!ContainerAccess.MayUse(container, playerId)) continue;

                // Ownership first: it also starts the container's settle clock,
                // and a container another player's game owns is never loaded here.
                if (!ContainerOwnership.IsSettled(container, container.m_nview)) continue;
                if (!ContainerAccess.IsFresh(container)) continue;

                var inventory = container.GetInventory();
                if (inventory == null) continue;

                var food = FindFood(inventory, foods);
                if (food == null) continue;

                bestInventory = inventory;
                bestItem = food;
                bestDistance = distance;
            }

            if (bestInventory == null) return;

            string name = bestItem.m_shared.m_name;
            int before = bestInventory.CountItems(name, -1, matchWorldLevel: false);
            bestInventory.RemoveOneItem(bestItem);
            int taken = before - bestInventory.CountItems(name, -1, matchWorldLevel: false);
            if (taken != 1)
            {
                if (taken > 1)
                    RossQoLPlugin.Log.LogWarning($"FeedFromContainers: {taken} {name} left a container for one feeding.");
                if (taken <= 0) return;
            }

            // As MonsterAI.UpdateConsumeItem does once its target is eaten.
            var eaten = FoodDrop(bestItem, foods);
            ai.m_onConsumedItem?.Invoke(eaten);
            humanoid.m_consumeItemEffects.Create(ai.transform.position, Quaternion.identity);
            ai.m_animator.SetTrigger("consume");
        }

        private static ItemDrop.ItemData FindFood(Inventory inventory, List<ItemDrop> foods)
        {
            foreach (var item in inventory.GetAllItems())
            {
                foreach (var food in foods)
                {
                    if (food != null && food.m_itemData.m_shared.m_name == item.m_shared.m_name) return item;
                }
            }
            return null;
        }

        /// <summary>The ItemDrop passed to consume callbacks: the eaten item's prefab.</summary>
        private static ItemDrop FoodDrop(ItemDrop.ItemData item, List<ItemDrop> foods)
        {
            if (item.m_dropPrefab != null)
            {
                var drop = item.m_dropPrefab.GetComponent<ItemDrop>();
                if (drop != null) return drop;
            }

            foreach (var food in foods)
            {
                if (food != null && food.m_itemData.m_shared.m_name == item.m_shared.m_name) return food;
            }
            return foods[0];
        }
    }
}
