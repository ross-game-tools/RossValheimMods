using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// A wolf's howl is a networked effect: the client that owns the wolf runs
    /// the idle timer and spawns the sound, and every other client is handed
    /// the finished object to play. Silencing the timer therefore only quiets
    /// the wolves this client happens to own, which in multiplayer is usually
    /// none of them.
    ///
    /// This patch silences the howl where every client does get a say: the
    /// moment the sound is played. The effect carries no link back to the wolf
    /// that made it, so the tamed check is by position -- the sound spawns at
    /// the wolf, so the nearest wolf to it is the one howling. A wild wolf
    /// nearer the sound than a tamed one keeps its howl.
    /// </summary>
    [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
    internal static class QuietWolvesSoundPatch
    {
        /// <summary>The howl, as the game's audio prefab is named.</summary>
        private const string HowlPrefab = "sfx_wolf_haul";

        /// <summary>
        /// How far from the sound to look for the wolf that made it. Generous:
        /// the effect spawns at the wolf's own position, and a miss here only
        /// means the howl plays as it does in vanilla.
        /// </summary>
        private const float WolfSearchRadius = 8f;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ZSFX), nameof(ZSFX.Play), QuietWolvesFeature.FeatureName);

        private static bool Prefix(ZSFX __instance)
        {
            if (QuietWolvesFeature.Instance?.IsActive != true) return true;

            try
            {
                if (__instance == null) return true;
                if (!__instance.gameObject.name.StartsWith(HowlPrefab, StringComparison.Ordinal)) return true;

                var wolf = NearestWolf(__instance.transform.position);
                if (wolf == null) return true;

                return !wolf.IsTamed();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"QuietWolves: could not check a howl, leaving it alone: {ex}");
                return true;
            }
        }

        /// <summary>The wolf closest to a position, tamed or wild, or null.</summary>
        private static Character NearestWolf(Vector3 position)
        {
            Character nearest = null;
            float nearestDistance = WolfSearchRadius * WolfSearchRadius;

            foreach (var character in Character.GetAllCharacters())
            {
                if (character == null) continue;

                string prefab = Utils.GetPrefabName(character.gameObject);
                if (!prefab.StartsWith("Wolf", StringComparison.OrdinalIgnoreCase)) continue;

                float distance = (character.transform.position - position).sqrMagnitude;
                if (distance > nearestDistance) continue;

                nearestDistance = distance;
                nearest = character;
            }

            return nearest;
        }
    }
}
