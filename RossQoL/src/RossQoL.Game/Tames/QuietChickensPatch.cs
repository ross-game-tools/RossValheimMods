using System;
using HarmonyLib;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// A bird's noise reaches this client two different ways: the idle and
    /// mating sounds are spawned effects, created by whichever client owns the
    /// bird and handed to everyone else to play, while footsteps and hits play
    /// from a ZSFX on the bird itself. Silencing the timer that spawns the
    /// first kind would leave every bird another player owns as loud as
    /// before.
    ///
    /// Both kinds pass through ZSFX.Play on the machine that hears them, so
    /// that is where this stops them, and ownership never enters into it.
    ///
    /// A sound is a bird's by either of two signs, because the game does not
    /// name these consistently: the audio prefab's own name says chicken
    /// ("sfx_chicken_...", "sfx_chick_hurt"), or the effect hangs somewhere
    /// under a Hen or a Chicken -- which is how the mating effect, named
    /// "fx_hen_love" with no "sfx" anywhere in it, is caught.
    ///
    /// The looping sounds start once when a bird loads, so switching the
    /// feature off returns the noise for birds that load afterwards rather
    /// than for the ones already standing around you.
    /// </summary>
    [HarmonyPatch(typeof(ZSFX), nameof(ZSFX.Play))]
    internal static class QuietChickensPatch
    {
        /// <summary>How the game names chicken audio prefabs, and the hen's mating effect.</summary>
        private static readonly string[] SoundPrefabs = { "sfx_chick", "fx_hen_love" };

        /// <summary>The two creature prefabs: the chick and the adult.</summary>
        private static readonly string[] BirdPrefabs = { "Chicken", "Hen" };

        /// <summary>
        /// How far up to look for the bird an effect belongs to. An effect is
        /// either its own object or a child of the creature, so this is only
        /// ever a short walk; the bound is here so a deep hierarchy cannot
        /// turn a per-sound check into a long one.
        /// </summary>
        private const int MaxDepth = 6;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ZSFX), nameof(ZSFX.Play), QuietChickensFeature.FeatureName);

        private static bool Prefix(ZSFX __instance)
        {
            if (QuietChickensFeature.Instance?.IsActive != true) return true;

            try
            {
                if (__instance == null) return true;

                return !IsChickenSound(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"QuietChickens: could not check a sound, leaving it alone: {ex}");
                return true;
            }
        }

        private static bool IsChickenSound(ZSFX sfx)
        {
            var node = sfx.transform;

            for (int depth = 0; node != null && depth < MaxDepth; depth++, node = node.parent)
            {
                string prefab = Utils.GetPrefabName(node.gameObject);

                foreach (var sound in SoundPrefabs)
                    if (prefab.StartsWith(sound, StringComparison.OrdinalIgnoreCase)) return true;

                foreach (var bird in BirdPrefabs)
                    if (string.Equals(prefab, bird, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
