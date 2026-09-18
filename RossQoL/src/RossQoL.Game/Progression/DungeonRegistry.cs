using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// The dungeons loaded on this client right now.
    ///
    /// Valheim has no list of its own to ask -- DungeonGenerator is an
    /// ordinary component that wakes with its zone -- so the two ends of its
    /// life are where the list is kept. Destroyed entries are pruned when read
    /// as well, because a generator can go with its zone without OnDestroy
    /// reaching us in a torn-down scene.
    /// </summary>
    internal static class DungeonRegistry
    {
        private static readonly List<DungeonGenerator> Loaded = new List<DungeonGenerator>();

        public static void Add(DungeonGenerator generator)
        {
            if (generator == null || Loaded.Contains(generator)) return;

            Loaded.Add(generator);
        }

        public static void Remove(DungeonGenerator generator)
        {
            if (generator == null) return;

            Loaded.Remove(generator);
        }

        /// <summary>The live generators, copied so a reset may load or unload zones while it runs.</summary>
        public static void Current(List<DungeonGenerator> results)
        {
            results.Clear();
            Loaded.RemoveAll(g => g == null);
            results.AddRange(Loaded);
        }

        public static int Count => Loaded.Count;
    }

    [HarmonyPatch(typeof(DungeonGenerator), "Awake")]
    internal static class DungeonRegistryAwakePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(DungeonGenerator), "Awake", DungeonRespawnFeature.FeatureName);

        // Registered whatever the feature's state: switching it on mid-session
        // then finds the dungeons already loaded, and an empty list costs
        // nothing while it is off.
        private static void Postfix(DungeonGenerator __instance)
        {
            DungeonRegistry.Add(__instance);
            DungeonVisits.Listen(__instance);
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), "OnDestroy")]
    internal static class DungeonRegistryDestroyPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(DungeonGenerator), "OnDestroy", DungeonRespawnFeature.FeatureName);

        private static void Prefix(DungeonGenerator __instance) => DungeonRegistry.Remove(__instance);
    }
}
