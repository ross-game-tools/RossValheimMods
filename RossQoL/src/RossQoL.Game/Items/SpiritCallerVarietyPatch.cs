using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Core.Items;
using RossQoL.Core.Tames;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Chooses the creature before vanilla rolls for it, by narrowing the
    /// cast's own spawner to that one prefab.
    ///
    /// Vanilla's pick is one line in the middle of the <c>SpawnAbility.Spawn</c>
    /// coroutine (1.0.15):
    ///
    ///     GameObject prefab = m_spawnPrefab[UnityEngine.Random.Range(0, m_spawnPrefab.Length)];
    ///
    /// Rather than transpile the compiler-generated iterator, the prefix on
    /// <c>Setup</c> replaces this instance's <c>m_spawnPrefab</c> with a
    /// one-element array, so vanilla's roll can only land on our choice and
    /// every line around it -- spawn point, per-kind limit, level-ups,
    /// command-to-follow -- is untouched vanilla.
    ///
    /// Why Setup and a prefix: Setup is where the attack hands the freshly
    /// instantiated projectile its caster, and it ends in
    /// <c>StartCoroutine("Spawn")</c>, which runs the coroutine synchronously
    /// up to its first yield. With no initial delay the prefab is picked
    /// inside that call, so a postfix would be too late. Setup is reached only
    /// through <c>IProjectile.Setup</c> (an interface call from Attack), which
    /// Mono cannot inline, so the patch is reliably hit.
    ///
    /// Safe to mutate: the spawner is a per-cast clone (Attack instantiates the
    /// projectile each cast and the coroutine destroys it after), and a new
    /// array is assigned rather than the shared one written into.
    /// </summary>
    [HarmonyPatch(typeof(SpawnAbility), MethodName)]
    internal static class SpiritCallerVarietyPatch
    {
        private const string MethodName = "Setup";

        private const string CloneSuffix = "(Clone)";

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(SpawnAbility), MethodName, SpiritCallerVarietyFeature.FeatureName);

        private static void Prefix(SpawnAbility __instance, Character owner)
        {
            try
            {
                if (SpiritCallerVarietyFeature.Instance?.IsActive != true) return;
                if (__instance == null || !__instance.m_commandOnSpawn) return;

                var player = owner as Player;
                if (player == null || player != Player.m_localPlayer) return;

                var prefabs = __instance.m_spawnPrefab;
                if (prefabs == null || prefabs.Length < 2) return;
                if (SummonPick.MostPerCast(__instance.m_minToSpawn, __instance.m_maxToSpawn) > 1) return;

                var kinds = Describe(__instance, prefabs, player);
                int choice = SummonPick.Choose(kinds, n => UnityEngine.Random.Range(0, n));
                if (choice < 0) return;

                __instance.m_spawnPrefab = new[] { prefabs[choice] };
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError(
                    $"SpiritCallerVariety: could not choose the creature, leaving the pick to vanilla's roll: {ex}");
            }
        }

        /// <summary>
        /// One SummonKind per prefab slot, in the same order, so the chosen
        /// index maps straight back. A null slot is marked unavailable -- vanilla
        /// would log an error and raise nothing for it.
        /// </summary>
        private static List<SummonKind> Describe(SpawnAbility spawner, GameObject[] prefabs, Player player)
        {
            var following = new Dictionary<string, int>(StringComparer.Ordinal);
            var weakest = new Dictionary<string, float>(StringComparer.Ordinal);

            string playerName = player.GetPlayerName();
            var characters = Character.GetAllCharacters();
            if (characters != null)
            {
                foreach (var character in characters)
                {
                    if (character == null || character.IsDead()) continue;
                    if (!FollowsPlayer(character, player, playerName)) continue;

                    string name = PrefabName(character.gameObject.name);
                    float fraction = new SummonCullCandidate(character.GetHealth(), character.GetMaxHealth(), 0d).HealthFraction;

                    following.TryGetValue(name, out int count);
                    following[name] = count + 1;
                    weakest[name] = weakest.TryGetValue(name, out float low) ? Math.Min(low, fraction) : fraction;
                }
            }

            var kinds = new List<SummonKind>(prefabs.Length);
            foreach (var prefab in prefabs)
            {
                if (prefab == null)
                {
                    kinds.Add(new SummonKind(false, 0, 1f));
                    continue;
                }

                following.TryGetValue(prefab.name, out int count);
                float low = weakest.TryGetValue(prefab.name, out float w) ? w : 1f;
                kinds.Add(new SummonKind(WouldSpawn(spawner, prefab), count, low));
            }

            return kinds;
        }

        /// <summary>
        /// The spawner's own refusal, asked the way vanilla asks it
        /// (<c>SpawnSystem.GetNrOfInstances(prefab, targetPosition, 0f)</c> --
        /// a range of 0 counts every loaded instance, so the position is
        /// irrelevant). Steering a cast onto a refused kind would turn a
        /// random success into a guaranteed "max summons reached".
        /// </summary>
        private static bool WouldSpawn(SpawnAbility spawner, GameObject prefab) =>
            spawner.m_maxSpawned <= 0
            || SpawnSystem.GetNrOfInstances(prefab, spawner.transform.position, 0f) < spawner.m_maxSpawned;

        /// <summary>
        /// The same "following me" test RecallSummons and TamesFollow use: the
        /// live follow target on a creature this client owns, or the saved
        /// follow name on one another client owns.
        /// </summary>
        private static bool FollowsPlayer(Character character, Player player, string playerName)
        {
            var ai = character.GetComponent<MonsterAI>();
            if (ai == null) return false;
            if (ai.GetFollowTarget() == player.gameObject) return true;

            var view = character.GetComponent<ZNetView>();
            return view != null && view.IsValid()
                && view.GetZDO().GetString(ZDOVars.s_follow) == playerName;
        }

        private static string PrefabName(string objectName) =>
            objectName != null && objectName.EndsWith(CloneSuffix, StringComparison.Ordinal)
                ? objectName.Substring(0, objectName.Length - CloneSuffix.Length)
                : objectName ?? "";
    }
}
