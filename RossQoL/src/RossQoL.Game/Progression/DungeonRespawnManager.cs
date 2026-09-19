using System;
using System.Collections.Generic;
using RossQoL.Core.Progression;
using UnityEngine;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// Watches the dungeons loaded around you: stamps the one you are standing
    /// in with today's date, and rebuilds the ones whose time has come.
    ///
    /// A dungeon's state lives on its own DungeonGenerator ZDO, which Valheim
    /// already uses to remember the rooms it placed. That means the clock
    /// travels with the world save and is the same for every player, rather
    /// than being a thing this client believes privately.
    ///
    /// Only the client that owns a dungeon acts on it, so two players standing
    /// in the same doorway cannot both rebuild it.
    /// </summary>
    internal sealed class DungeonRespawnManager : MonoBehaviour
    {
        /// <summary>
        /// Seconds between sweeps. Slow on purpose: a day is half an hour of
        /// play, so nothing here needs to be prompt, and the sweep walks every
        /// loaded dungeon.
        /// </summary>
        private const float CheckInterval = 10f;

        private readonly List<DungeonGenerator> _loaded = new List<DungeonGenerator>();

        private void Awake() => InvokeRepeating(nameof(Sweep), CheckInterval, CheckInterval);

        private void Sweep()
        {
            if (DungeonRespawnFeature.Instance?.IsActive != true) return;
            if (Player.m_localPlayer == null || EnvMan.instance == null || ZoneSystem.instance == null) return;

            try
            {
                int today = EnvMan.instance.GetDay();
                int span = ProgressionConfig.RespawnDays?.Value ?? 24;

                DungeonRegistry.Current(_loaded);
                foreach (var generator in _loaded)
                    Consider(generator, today, span);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"DungeonRespawn: leaving the dungeons alone this round: {ex}");
            }
        }

        private void Consider(DungeonGenerator generator, int today, int span)
        {
            if (generator == null) return;

            var nview = generator.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;

            // A surface camp -- a goblin village, a farm -- is built by this
            // same component, and tearing one down and building it again is
            // not what this feature is for. Only real interiors are ours.
            if (generator.m_algorithm != DungeonGenerator.Algorithm.Dungeon) return;

            if (!DungeonSite.Resolve(generator, out var site)) return;

            // Asked from the dungeon's own identity, not the ground under it.
            bool cleared = DungeonUnlock.IsCleared(site.DeclaredBiomes, site.Themes, HasKey);

            // Standing in it counts as a visit, and a visit resets the clock.
            // Measured against the dungeon's own volume, the same box its
            // rooms were generated in; the interior sits 5000 m above the
            // entrance, so nobody on the surface is ever inside it.
            if (DungeonReset.VolumeOf(generator).Contains(Player.m_localPlayer.transform.position))
            {
                Stamp(nview, today);
                return;
            }

            // Reading the stamp needs no ownership: a ZDO's fields reach every
            // client that has the object loaded. Ownership is claimed only at
            // the moment of writing, below, because on a dedicated server a
            // dungeon changes hands constantly and gating the whole check on
            // owning it meant most sweeps did nothing at all.
            int? lastVisit = ReadVisitDay(nview);
            if (lastVisit == null)
            {
                // First sight of a dungeon from a world that predates this
                // feature: start its clock rather than resetting it now, so
                // an old save does not rebuild every crypt at once.
                Stamp(nview, today);
                return;
            }

            if (!DungeonRespawnMath.IsDue(lastVisit, today, span)) return;
            if (!cleared) return;

            // Not an error when it is not safe: a player inside, or a build to
            // protect. Left for the next sweep, and the stamp is untouched so
            // it stays due.
            if (!DungeonReset.IsSafe(generator, out _)) return;

            // Take the dungeon before touching it, then ask again from the ZDO
            // we now own. Two clients can both decide a dungeon is due in the
            // same breath; only the one that ends up owning it, and still
            // finds it due, goes on to tear it down.
            if (!nview.IsOwner()) nview.ClaimOwnership();
            if (!nview.IsOwner()) return;

            if (!DungeonRespawnMath.IsDue(ReadVisitDay(nview), today, span)) return;

            if (DungeonReset.Run(generator, site.Label))
                Stamp(nview, today);
        }

        private static void Stamp(ZNetView nview, int today) => DungeonVisits.Stamp(nview, today);

        private static int? ReadVisitDay(ZNetView nview) => DungeonVisits.Read(nview);

        private static bool HasKey(string key) =>
            ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
    }

    /// <summary>
    /// What a dungeon is, as the dungeon itself says it.
    ///
    /// A DungeonGenerator is a root object of its own -- not a child of the
    /// Location that placed it -- so its identity comes from the location that
    /// owns its zone: vanilla's own Location lookup when the location object is
    /// loaded, and ZoneSystem's record of what it placed in that zone when it
    /// is not. Both carry the biome the world generator placed the dungeon
    /// FOR, which is the thing that decides which boss it waits for.
    ///
    /// The terrain biome under the entrance is kept too, but only for the log.
    /// It used to be what the gate was decided on, and it is the wrong
    /// question: it answers for whatever ground is there, one sample, at a
    /// point chosen for convenience -- while the dungeon's own declared biome
    /// travels with it and cannot drift. A gate that opens by mistake destroys
    /// everything a player left inside, so the deciding value must be the one
    /// that cannot be somewhere else's answer.
    /// </summary>
    internal readonly struct DungeonSite
    {
        private DungeonSite(string declaredBiomes, string themes, string terrainBiome, string locationName)
        {
            DeclaredBiomes = declaredBiomes;
            Themes = themes;
            TerrainBiome = terrainBiome;
            LocationName = locationName;
        }

        /// <summary>The biomes the dungeon was placed for, named as Heightmap.Biome spells them.</summary>
        public string DeclaredBiomes { get; }

        /// <summary>The dungeon's own themes, named as Room.Theme spells them.</summary>
        public string Themes { get; }

        /// <summary>The biome of the ground sampled at the entrance. For the log only.</summary>
        public string TerrainBiome { get; }

        /// <summary>The location prefab that placed this dungeon, when one is loaded.</summary>
        public string LocationName { get; }

        /// <summary>What to call this dungeon in a log line.</summary>
        public string Label => string.IsNullOrEmpty(DeclaredBiomes) ? TerrainBiome : DeclaredBiomes;

        public static bool Resolve(DungeonGenerator generator, out DungeonSite site)
        {
            site = default;
            if (generator == null || ZoneSystem.instance == null) return false;

            string themes = FlagNames.Of(generator.m_themes);
            var zone = ZoneSystem.GetZone(generator.transform.position);

            // The location object itself, when this client has it loaded. On a
            // dedicated server this is the path that runs: m_locationInstances
            // is the server's own record and a joining client has none.
            var location = Location.GetZoneLocation(zone);
            if (location != null && location.m_hasInterior)
            {
                site = new DungeonSite(
                    FlagNames.Of(location.m_biome),
                    themes,
                    Heightmap.FindBiome(location.transform.position).ToString(),
                    location.gameObject.name);
                return true;
            }

            // Otherwise what the world generator recorded for this zone, which
            // is where the entrance is and what kind of place it is.
            if (!ZoneSystem.instance.m_locationInstances.TryGetValue(zone, out var instance)) return false;
            if (instance.m_location == null || instance.m_location.m_interiorRadius <= 0f) return false;

            site = new DungeonSite(
                FlagNames.Of(instance.m_location.m_biome),
                themes,
                Heightmap.FindBiome(instance.m_position).ToString(),
                instance.m_location.m_prefabName);
            return true;
        }
    }

    /// <summary>
    /// The names of the bits set in one of Valheim's bit-mask enums.
    ///
    /// ToString() will not do it: neither Heightmap.Biome nor Room.Theme is
    /// declared [Flags], although both are edited as bit masks, so a value with
    /// two bits set prints as a number. Aggregate members (Biome.All, Land) are
    /// skipped by taking single bits only, which is what the callers mean.
    /// </summary>
    internal static class FlagNames
    {
        public static string Of(Enum flags)
        {
            if (flags == null) return "";

            var type = flags.GetType();
            long bits = Convert.ToInt64(flags);
            if (bits == 0L) return "";

            var names = new List<string>();
            foreach (var value in Enum.GetValues(type))
            {
                long bit = Convert.ToInt64(value);
                if (bit == 0L || (bit & (bit - 1L)) != 0L) continue;
                if ((bits & bit) != bit) continue;

                string name = Enum.GetName(type, value);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }

            return string.Join(", ", names.ToArray());
        }
    }
}
