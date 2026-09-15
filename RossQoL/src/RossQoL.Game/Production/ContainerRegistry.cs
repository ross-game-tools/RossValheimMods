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

        // Reference identity, not Unity's ==: a destroyed container stays the
        // same key until pruned from both collections together.
        private static readonly HashSet<Container> Known = new HashSet<Container>(ReferenceComparer.Instance);
        private static readonly Predicate<Container> IsDestroyed = c => c == null;

        private sealed class ReferenceComparer : IEqualityComparer<Container>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(Container a, Container b) => ReferenceEquals(a, b);
            public int GetHashCode(Container c) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(c);
        }

        private static void Prune()
        {
            Known.RemoveWhere(c => c == null);
            Containers.RemoveAll(IsDestroyed);
        }

        private const int PruneEvery = 64;
        private static int _addsSincePrune;

        public static void Register(Container container)
        {
            if (!IsStatic(container)) return;

            // Several features patch Container.Awake with the same registry
            // postfix; a container is listed once however many ran.
            if (!Known.Add(container)) return;

            // Near prunes only while a feature queries it; this keeps the list bounded otherwise.
            if (++_addsSincePrune >= PruneEvery)
            {
                _addsSincePrune = 0;
                Prune();
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

        /// <summary>Containers within radius (3D) of point. Callers check their own feature is active.</summary>
        public static void Near(Vector3 point, float radius, List<Container> results)
        {
            results.Clear();

            Prune();

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
    /// Registers every container as it wakes, for every feature that takes
    /// from or puts into containers (AutoHarvest, FeedFromContainers); each
    /// lists this patch. Deliberately NOT gated on a feature being active:
    /// a server can switch a feature on after this client loaded a base,
    /// and it must then see containers that already existed. Registering is
    /// one list add; each feature checks it is active before querying.
    /// </summary>
    [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
    internal static class ContainerAwakeRegistryPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Container), nameof(Container.Awake), "container registry");

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
                RossQoLPlugin.Log.LogError($"Container registry: registering a container failed and it was skipped: {ex}");
            }
        }
    }
}
