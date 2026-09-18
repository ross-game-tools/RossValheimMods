using System;
using System.Collections.Generic;
using UnityEngine;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// Rebuilding one dungeon.
    ///
    /// Valheim only ever generates a dungeon once, when its zone is first
    /// placed, so there is no vanilla path for doing it again and the teardown
    /// is ours to get right. Destroying the GameObjects alone would leave
    /// their ZDOs behind and the world would spawn the old contents back on
    /// top of the new ones, so every networked object inside is destroyed
    /// through its own ZNetView, which is what takes its ZDO with it.
    ///
    /// What counts as "inside" is the dungeon's own volume, the same box
    /// vanilla generates the rooms within: Bounds(m_zoneCenter, m_zoneSize).
    /// A radius was tried first and was wrong -- a 64-metre box reaches 55
    /// metres into its corners, and bigger dungeons set a bigger size, so
    /// rooms out at the edges survived the teardown and came back doubled.
    ///
    /// The rebuild then uses the dungeon's own seed. Valheim derives it from
    /// the world seed and the dungeon's position, so the same dungeon always
    /// generates the same rooms: what comes back is what was there.
    /// </summary>
    internal static class DungeonReset
    {
        /// <summary>Vanilla's default when a generator does not say otherwise.</summary>
        private static readonly Vector3 DefaultZoneSize = new Vector3(64f, 64f, 64f);

        /// <summary>
        /// A little wider than the box the rooms were generated in. A door
        /// sitting exactly on the boundary belongs to the dungeon, and leaving
        /// one behind is what doubles it.
        /// </summary>
        private const float Margin = 1.25f;

        /// <summary>How far outside the dungeon a player still blocks a rebuild.</summary>
        private const float PlayerSafety = 96f;

        /// <summary>The dungeon's own volume: what vanilla filled, and so what we clear.</summary>
        public static Bounds VolumeOf(DungeonGenerator generator)
        {
            var size = generator.m_zoneSize;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f) size = DefaultZoneSize;

            return new Bounds(generator.transform.position, size * Margin);
        }

        /// <summary>
        /// Whether this dungeon may be rebuilt right now. Answers with a
        /// reason when not, for the log.
        /// </summary>
        public static bool IsSafe(DungeonGenerator generator, out string why)
        {
            why = null;
            var volume = VolumeOf(generator);

            // Never under anyone's feet: rebuilding drops the floor a player
            // is standing on and everything they are fighting.
            var reach = volume;
            reach.Expand(PlayerSafety * 2f);

            foreach (var player in Player.GetAllPlayers())
            {
                if (player == null) continue;
                if (!reach.Contains(player.transform.position)) continue;

                why = "a player is inside or close to it";
                return false;
            }

            if (ProgressionConfig.ProtectPlayerBuilds?.Value != false && HasPlayerBuild(volume))
            {
                why = "it holds something a player built";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Destroys everything inside and generates the dungeon again.
        /// </summary>
        /// <returns>True when the dungeon was rebuilt.</returns>
        public static bool Run(DungeonGenerator generator, string biome)
        {
            try
            {
                var nview = generator.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return false;

                var volume = VolumeOf(generator);
                int removed = ClearInside(generator, volume, out int stubborn);

                // Something left inside will be there twice once the rooms are
                // placed again, so a rebuild on top of it is worse than none.
                if (stubborn > 0)
                {
                    RossQoLPlugin.Log.LogWarning(
                        $"DungeonRespawn: leaving the {biome} dungeon at {generator.transform.position} alone -- "
                        + $"{stubborn} of its objects would not clear, and rebuilding over them would double them.");
                    return false;
                }

                int seed = generator.GetSeed();
                generator.Generate(seed, ZoneSystem.SpawnMode.Full);

                RossQoLPlugin.Log.LogInfo(
                    $"DungeonRespawn: rebuilt the {biome} dungeon at {generator.transform.position} "
                    + $"from seed {seed}, clearing {removed} objects.");
                return true;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"DungeonRespawn: could not rebuild a dungeon, leaving it as it is: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Every networked object in the dungeon's volume, destroyed through
        /// its own view so its ZDO goes with it. The generator itself is left
        /// standing -- it is what rebuilds the place.
        /// </summary>
        /// <param name="stubborn">How many could not be taken and so were left.</param>
        private static int ClearInside(DungeonGenerator generator, Bounds volume, out int stubborn)
        {
            var keep = generator.GetComponent<ZNetView>();
            var doomed = new List<ZNetView>();

            // Unsorted: a reset is rare and heavy, and the order objects come
            // back in means nothing to it.
            foreach (var view in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (view == null || view == keep || !view.IsValid()) continue;
                if (!volume.Contains(view.transform.position)) continue;

                // A player, or anything carrying one, is never ours to remove.
                if (view.GetComponent<Player>() != null) continue;

                doomed.Add(view);
            }

            int removed = 0;
            stubborn = 0;

            foreach (var view in doomed)
            {
                try
                {
                    if (view == null || !view.IsValid()) continue;

                    // An unowned object is claimed; one another peer holds is
                    // counted rather than wrestled for.
                    if (!view.IsOwner()) view.ClaimOwnership();
                    if (!view.IsOwner())
                    {
                        stubborn++;
                        continue;
                    }

                    view.Destroy();
                    removed++;
                }
                catch (Exception ex)
                {
                    stubborn++;
                    RossQoLPlugin.Log.LogWarning($"DungeonRespawn: one object would not clear: {ex}");
                }
            }

            return removed;
        }

        /// <summary>Anything a player placed, which a rebuild would destroy.</summary>
        private static bool HasPlayerBuild(Bounds volume)
        {
            foreach (var piece in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (piece == null || !piece.IsPlacedByPlayer()) continue;
                if (!volume.Contains(piece.transform.position)) continue;

                return true;
            }

            return false;
        }
    }
}
