using System;
using System.Collections.Generic;
using HarmonyLib;
using RossQoL.Core.Death;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Remembers where you died. A postfix on <see cref="Player.OnDeath"/>
    /// for the local player: marks the one-shot died flag for whichever
    /// respawn handout features consume it, then finds the tombstone
    /// vanilla has just created and records it.
    ///
    /// Listed in <c>PatchClasses</c> by every feature that needs a
    /// recorded grave to work from -- GraveMarker today, respawn food and
    /// the corpse run buff once they land -- rather than owned by one of
    /// them. <see cref="FeatureActivator"/> applies a shared patch class
    /// once no matter how many features list it, so this still runs once
    /// per death as long as at least one of those features is active.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class GraveRecordingPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Player), nameof(Player.OnDeath), "Death/GraveRecording");

        /// <summary>
        /// Every Valheim member this patch body reaches by name. Declared
        /// here, once, and spliced into the RequiredMembers of all four
        /// features that list this patch class -- otherwise a rename would
        /// disable whichever feature happened to declare the member while the
        /// others patched on, matched no tombstone, recorded nothing and
        /// reported themselves healthy.
        /// </summary>
        internal static IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Player", "OnDeath", "the moment a grave is worth remembering"),
            new CompatMember("Player", "m_customData", "where a remembered grave and the died flag live"),
            new CompatMember("Player", "GetCenterPoint", "where the tombstone was spawned"),
            new CompatMember("TombStone", "GetOwnerName", "confirming the tombstone type is still shaped as expected"),
            new CompatMember("ZDOVars", "s_owner", "matching a tombstone to the player who left it"),
            new CompatMember("Container", "GetInventory", "counting what a grave holds"),
            new CompatMember("Inventory", "NrOfItems", "counting what a grave holds"),
            new CompatMember("ZNet", "GetWorldUID", "keying a grave to the world it is in"),
            new CompatMember("Game", "GetPlayerProfile", "matching a tombstone to its owner"),
            new CompatMember("PlayerProfile", "GetPlayerID", "matching a tombstone to its owner"),
            new CompatMember("EnvMan", "GetCurrentDay", "stamping a grave with the day it was made"),
            new CompatMember("Character", "InInterior", "telling a grave inside a dungeon from one on the surface"),
            new CompatMember("Teleport", "m_targetPoint", "following a dungeon's inner door back out to its entrance"),
        };

        private static void Postfix(Player __instance)
        {
            try
            {
                if (__instance != Player.m_localPlayer) return;

                // Three of the four features listing this class are Synced, so
                // it is patched whether or not any of them is switched on. Both
                // writes below land in m_customData, which Valheim merges on
                // load and never clears, so a category switched off must leave
                // nothing behind at all.
                bool needsFlag = DeathCategory.NeedsDiedFlag;
                bool needsRecord = DeathCategory.NeedsGraveRecord;
                if (!needsFlag && !needsRecord) return;

                if (needsFlag) DeathState.MarkDied();
                if (!needsRecord) return;

                var grave = FindOwnTombstone(__instance);
                if (grave == null) return; // a death that kept the inventory leaves no grave

                // Explicit Unity-aware null checks, not `?.`: Container is a
                // UnityEngine.Object, and a destroyed one still passes a
                // reference-null test.
                var container = grave.GetComponent<Container>();
                var inventory = container != null ? container.GetInventory() : null;
                int items = inventory != null ? inventory.NrOfItems() : 0;
                if (items < (DeathConfig.MinItemsToTrack?.Value ?? 1)) return;

                var nview = grave.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) return;

                var id = nview.GetZDO().m_uid;
                var at = grave.transform.position;
                int day = EnvMan.instance != null ? EnvMan.instance.GetCurrentDay() : 0;

                // A dungeon entrance is worth writing down HERE and nowhere
                // else: the interior is loaded and the player is standing in
                // it, which is the one moment its door is reachable. Asked for
                // later, from the surface, the dungeon is usually not streamed
                // in at all -- and "not streamed in" is exactly the case the
                // marker exists for.
                DeathState.Record(
                    Character.InInterior(at) && TryFindEntrance(at, out var entrance)
                        ? new GraveRecord(
                            DeathState.WorldId, at.x, at.y, at.z, day, items, id.UserID, id.ID,
                            entrance.x, entrance.y, entrance.z)
                        : new GraveRecord(
                            DeathState.WorldId, at.x, at.y, at.z, day, items, id.UserID, id.ID));

                // We are looking straight at the tombstone: that is the
                // strongest sighting there is, and taking it here is what
                // lets GraveCleanupWatcher clear a grave looted before any
                // of its 1s passes ever caught the tombstone standing.
                GraveCleanupWatcher.NoteJustBuried(id.ID);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Death: this grave was not remembered: {ex}");
            }
        }

        /// <summary>
        /// Vanilla has already spawned the tombstone by the time this postfix
        /// runs. Matched by owner rather than by proximity alone, so another
        /// player's grave in the same spot is never mistaken for this one.
        /// </summary>
        private static TombStone FindOwnTombstone(Player player)
        {
            long playerId = global::Game.instance.GetPlayerProfile().GetPlayerID();
            Vector3 centre = player.GetCenterPoint();

            TombStone nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None))
            {
                var nview = candidate.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;
                if (nview.GetZDO().GetLong(ZDOVars.s_owner, 0L) != playerId) continue;

                float distance = Vector3.Distance(candidate.transform.position, centre);
                if (distance > 5f || distance >= nearestDistance) continue;

                nearest = candidate;
                nearestDistance = distance;
            }

            return nearest;
        }

        /// <summary>
        /// Finds the surface door of the dungeon a grave is inside.
        ///
        /// A dungeon door is a <c>Teleport</c>, one on each side of the pair,
        /// each wired to the other through <c>m_targetPoint</c> -- there is no
        /// separate "exit" type (docs/valheim-api/dungeons.md §5). So the inner
        /// door is the one standing in interior space whose partner is not, and
        /// the partner's own transform position is where the player came in.
        /// Vanilla's <c>GetTeleportPoint()</c> offset is private and not worth
        /// reaching for: it moves the point about a metre, which is nothing at
        /// the ranges a marker is read at.
        ///
        /// Nearest to the grave, so a location carrying more than one pair
        /// names the door for the part of the dungeon the player died in.
        /// </summary>
        private static bool TryFindEntrance(Vector3 grave, out Vector3 entrance)
        {
            entrance = Vector3.zero;
            float nearestDistance = float.MaxValue;

            foreach (var door in UnityEngine.Object.FindObjectsByType<Teleport>(FindObjectsSortMode.None))
            {
                // Explicit Unity-aware null checks, not `?.`: a destroyed
                // component still passes a reference-null test.
                if (door == null) continue;

                var partner = door.m_targetPoint;
                if (partner == null) continue;

                // The inside half of the pair, leading out. A buildable portal
                // is a TeleportWorld, a different type entirely, so nothing
                // else can be caught by this.
                if (!Character.InInterior(door.transform.position)) continue;

                var outside = partner.transform.position;
                if (Character.InInterior(outside)) continue;

                float distance = Vector3.Distance(door.transform.position, grave);
                if (distance >= nearestDistance) continue;

                nearestDistance = distance;
                entrance = outside;
            }

            return nearestDistance < float.MaxValue;
        }
    }
}
