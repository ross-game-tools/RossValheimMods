using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Every live static container on this client, whatever mod made it.
    /// Storage mods that subclass Container are containers here too, used
    /// only through their own Inventory.
    ///
    /// Nothing filters on the ZDO creator (a piece built this session has
    /// none yet at Awake) or on prefab names. Destroyed containers are
    /// pruned when queried: Unity reports them as null.
    /// </summary>
    internal static class ContainerRegistry
    {
        private static readonly List<Container> Containers = new List<Container>();
        private static readonly Predicate<Container> IsDestroyed = c => c == null;

        private const int PruneEvery = 64;
        private static int _addsSincePrune;

        public static void Register(Container container)
        {
            if (!IsStatic(container)) return;

            // Near prunes only while the feature is on; this keeps the list bounded while it stays off.
            if (++_addsSincePrune >= PruneEvery)
            {
                _addsSincePrune = 0;
                Containers.RemoveAll(IsDestroyed);
            }

            Containers.Add(container);
        }

        /// <summary>
        /// A built piece that stays put: has a ZDO (placement ghosts have
        /// none), a Piece, and is not cart or ship storage.
        /// </summary>
        public static bool IsStatic(Container container)
        {
            if (container == null) return false;

            var nview = container.m_nview;
            if (nview == null || nview.GetZDO() == null) return false;
            if (container.m_wagon != null || container.m_rootObjectOverride != null) return false;
            if (container.GetComponent<Piece>() == null) return false;
            if (container.GetComponentInParent<Ship>() != null) return false;
            if (container.GetComponentInParent<Vagon>() != null) return false;
            return true;
        }

        /// <summary>Containers within radius (3D) of point. Empty while AutoHarvest is off.</summary>
        public static void Near(Vector3 point, float radius, List<Container> results)
        {
            results.Clear();
            if (AutoHarvestFeature.Instance?.IsActive != true) return;

            Containers.RemoveAll(IsDestroyed);

            float limit = radius * radius;
            foreach (var container in Containers)
            {
                var nview = container.m_nview;
                if (nview == null || !nview.IsValid()) continue;
                if ((container.transform.position - point).sqrMagnitude <= limit) results.Add(container);
            }
        }
    }

    /// <summary>
    /// Registers every container as it wakes. Deliberately NOT gated on
    /// AutoHarvest being active, the one patch in this feature that is not:
    /// a server can switch the feature on after this client loaded a base,
    /// and the harvester must then see containers that already existed.
    /// Registering is one list add; ContainerRegistry.Near returns nothing
    /// while the feature is off.
    /// </summary>
    [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
    internal static class ContainerAwakeRegistryPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Container), nameof(Container.Awake), AutoHarvestFeature.FeatureName);

        private static void Postfix(Container __instance)
        {
            // An exception escaping here would break Container.Awake for
            // every container in the game.
            try
            {
                ContainerRegistry.Register(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"AutoHarvest: registering a container failed and it was skipped: {ex}");
            }
        }
    }
}
