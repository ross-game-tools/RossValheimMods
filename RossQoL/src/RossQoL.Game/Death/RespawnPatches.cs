using System;
using HarmonyLib;
using RossQoL.Core.Death;
using RossQoL.Core.Progression;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// The single hook for every respawn-after-death handout. A postfix on
    /// <see cref="Player.OnSpawned"/>, which runs for every spawn, login and
    /// death-respawn alike; the died flag set by
    /// <see cref="GraveRecordingPatch"/> is what tells the two apart, and it
    /// can only be consumed once, so both handouts share this one patch
    /// rather than each carrying its own postfix.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class RespawnPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), nameof(Player.OnSpawned), "Death/Respawn");

        private static void Postfix(Player __instance)
        {
            try
            {
                if (__instance != Player.m_localPlayer) return;

                // Consumed unconditionally, whether or not either handout is
                // active, so turning one on mid-session never fires it for a
                // death that already happened.
                if (!DeathState.ConsumeDiedFlag()) return;

                if (RespawnFoodFeature.Instance?.IsActive == true) GrantFood(__instance);
                if (RespawnRestedFeature.Instance?.IsActive == true) GrantRested(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Death: the respawn handouts were skipped: {ex}");
            }
        }

        private static bool HasKey(string key) =>
            ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);

        private static bool Exists(string prefabName) =>
            ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(prefabName) != null;

        private static void GrantFood(Player player)
        {
            if (ObjectDB.instance == null) return;

            int count = Mathf.Clamp(DeathConfig.RespawnFoodCount.Value, 0, 3);
            string tier = WorldFrontier.TierFor(HasKey);
            string table = DeathConfig.RespawnFoodsByFrontier.Value;

            string name = RespawnFoods.Pick(table, tier, Exists);
            if (name == null)
            {
                RossQoLPlugin.Log.LogWarning(
                    $"RespawnFood: nothing in the {tier} row of RespawnFoodsByFrontier is an item this game "
                    + "has, so no food was given. Check the food names in the config.");
                return;
            }

            var prefab = ObjectDB.instance.GetItemPrefab(name);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) return;

            for (int i = 0; i < count; i++)
            {
                // A clone every time: EatFood keeps the reference it is handed
                // in the belly slot, so two slots must never share one
                // ItemData.
                var data = drop.m_itemData.Clone();

                // EatFood reads m_dropPrefab.name, and the save writes that
                // name; a prefab cloned straight off ObjectDB has it unset.
                data.m_dropPrefab = prefab;

                // The pick is deterministic, so a second helping is the same
                // food and vanilla refuses it while the first is still fresh.
                // That is the end of the handout, not an error, so the loop
                // simply breaks rather than retrying or logging.
                if (!player.EatFood(data)) break;
            }
        }

        private static void GrantRested(Player player)
        {
            float minimum = DeathConfig.RestedMinutes.Value * 60f;
            if (minimum <= 0f) return;

            // The hash overload dereferences ObjectDB.instance internally; an
            // absent or mid-construction ObjectDB would otherwise throw here
            // for no reason -- there is nothing to grant yet.
            if (ObjectDB.instance == null) return;

            // SEMan is a plain class, not a UnityEngine.Object, so an
            // ordinary null check is correct and sufficient here.
            var seman = player.GetSEMan();
            if (seman == null) return;

            seman.AddStatusEffect(SEMan.s_statusEffectRested, resetTime: false, 0, 0f, -1);

            // The live instance is the clone SEMan holds; the ObjectDB asset
            // must not be touched, because mutating it leaks into the rest of
            // the session.
            var rested = seman.GetStatusEffect(SEMan.s_statusEffectRested);
            if (rested == null) return;

            // Only raised when short of the minimum, never cut short: a
            // better Rested from the player's own house must not be reduced.
            float remaining = rested.GetRemaningTime();
            if (remaining < minimum) rested.m_ttl += minimum - remaining;
        }
    }
}
