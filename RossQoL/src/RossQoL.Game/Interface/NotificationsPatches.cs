using System;
using HarmonyLib;
using RossQoL.Core.Interface;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Interface
{
    /// <summary>
    /// Turns what Valheim is about to say into a line on the notification
    /// list. Keys are what makes a repeat land on the line it already has:
    /// an item's own name, a skill, or failing both the message text itself.
    /// </summary>
    internal static class NotificationRouter
    {
        /// <summary>
        /// The item <see cref="Humanoid.Pickup"/> is part way through picking
        /// up. Pickup calls the message out through the same door as
        /// everything else, carrying only a localized sentence, so this is how
        /// the message on the other side of that door knows which item it is
        /// about. Set on the way in, cleared on the way out, and only ever
        /// touched on the main thread between those two points.
        /// </summary>
        internal static ItemDrop.ItemData PendingPickup;

        internal static string Localize(string text)
        {
            var localization = Localization.instance;
            return localization == null ? text : localization.Localize(text);
        }

        /// <summary>
        /// Files one intercepted top-left message under the right key.
        /// </summary>
        internal static void Deliver(string raw, string localized, int amount, Sprite icon)
        {
            var item = PendingPickup;
            if (item != null && item.m_shared != null && raw == "$msg_added " + item.m_shared.m_name)
            {
                PendingPickup = null;
                NotificationHud.Push(
                    "item:" + item.m_shared.m_name,
                    Localize(item.m_shared.m_name),
                    NotificationStyle.Count,
                    amount > 0 ? amount : 1,
                    icon);
                return;
            }

            // Anything else -- a skill level-up, a mod, a server broadcast --
            // is its own line, keyed by what it says, so a repeat of the same
            // sentence counts up instead of stacking.
            NotificationHud.Push(
                "msg:" + localized,
                localized,
                amount > 0 ? NotificationStyle.Count : NotificationStyle.Plain,
                amount,
                icon);
        }

        internal static bool Watching() =>
            NotificationsFeature.Instance != null
            && NotificationsFeature.Instance.IsActive
            && NotificationHud.Ready;
    }

    /// <summary>
    /// Builds the notification list as soon as the message HUD is up.
    /// </summary>
    [HarmonyPatch(typeof(MessageHud), "Start")]
    internal static class NotificationHudPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(MessageHud), "Start", NotificationsFeature.FeatureName);

        private static void Postfix(MessageHud __instance)
        {
            // An exception escaping MessageHud.Start breaks every message the
            // game shows, ours and vanilla's alike.
            try
            {
                NotificationHud.Create(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Notifications: could not add the list: {ex}");
            }
        }
    }

    /// <summary>
    /// Takes over vanilla's top-left messages. Centre messages -- the big
    /// ones across the middle of the screen -- go through untouched; they are
    /// a separate thing and stack nothing.
    /// </summary>
    [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
    internal static class MessageHudShowMessagePatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(MessageHud), nameof(MessageHud.ShowMessage),
                NotificationsFeature.FeatureName);

        private static bool Prefix(
            MessageHud __instance,
            MessageHud.MessageType type,
            string text,
            int amount,
            Sprite icon,
            bool showDespiteHiddenHUD,
            bool log)
        {
            try
            {
                if (type != MessageHud.MessageType.TopLeft) return true;

                // Never suppress vanilla's messages when there is no list to
                // draw them on: the player would simply stop being told things.
                if (!NotificationRouter.Watching()) return true;
                if (__instance == null) return true;

                // Vanilla records this before anything else, and its Update
                // reads it for the whole HUD, centre messages included. Keeping
                // it in step means nothing outside the top-left changes.
                __instance.m_showDespiteHiddenHUD = showDespiteHiddenHUD;
                if (Hud.IsUserHidden() && !showDespiteHiddenHUD) return false;

                string localized = NotificationRouter.Localize(text);
                if (log) __instance.AddLog(localized);
                NotificationRouter.Deliver(text, localized, amount, icon);
                return false;
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError(
                    $"Notifications: letting vanilla show this message instead: {ex}");
                return true;
            }
        }
    }

    /// <summary>
    /// Notes which item a pickup is about, so its message can be keyed by the
    /// item rather than by the sentence describing it.
    ///
    /// Patched here rather than on <c>Character.ShowPickupMessage</c>, which
    /// is a two-line forwarder with one call site -- exactly the shape Mono
    /// inlines away, leaving a patch on it silently doing nothing.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    internal static class HumanoidPickupPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Humanoid), nameof(Humanoid.Pickup),
                NotificationsFeature.FeatureName);

        private static void Prefix(Humanoid __instance, GameObject go)
        {
            try
            {
                NotificationRouter.PendingPickup = null;
                if (!NotificationRouter.Watching()) return;

                var local = Player.m_localPlayer;
                if (local == null || __instance != (Humanoid)local) return;
                if (go == null) return;

                var drop = go.GetComponent<ItemDrop>();
                if (drop == null) return;

                NotificationRouter.PendingPickup = drop.m_itemData;
            }
            catch (Exception ex)
            {
                NotificationRouter.PendingPickup = null;
                RossQoLPlugin.Log.LogError($"Notifications: could not read this pickup: {ex}");
            }
        }

        private static void Postfix()
        {
            // Whether the pickup succeeded or bailed out early, nothing after
            // this point is part of it.
            NotificationRouter.PendingPickup = null;
        }
    }

    /// <summary>
    /// Adds the line vanilla has never had: how much closer a swing, a jump or
    /// a cut has taken a skill to its next level. Vanilla says nothing at all
    /// until a level actually turns over.
    ///
    /// Patched on <c>Skills.RaiseSkill</c> rather than the small
    /// <c>Skills.Skill.Raise</c> it calls, for the same inlining reason as the
    /// pickup patch above.
    /// </summary>
    [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
    internal static class SkillGainPatch
    {
        /// <summary>
        /// Where the skill stood before the game raised it. A class rather
        /// than a struct so the absence of a reading is simply null: there is
        /// nothing to report for another player's skills, or for a raise we
        /// could not read.
        /// </summary>
        internal sealed class Before
        {
            public float Level;
            public float Accumulator;
        }

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Skills), nameof(Skills.RaiseSkill),
                NotificationsFeature.FeatureName);

        private static void Prefix(Skills __instance, Skills.SkillType skillType, out Before __state)
        {
            __state = null;
            try
            {
                __state = Read(__instance, skillType);
            }
            catch (Exception ex)
            {
                __state = null;
                RossQoLPlugin.Log.LogError($"Notifications: could not read this skill: {ex}");
            }
        }

        private static void Postfix(Skills __instance, Skills.SkillType skillType, Before __state)
        {
            try
            {
                if (__state == null || !NotificationRouter.Watching()) return;
                Report(__instance, skillType, __state);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Notifications: could not show this skill gain: {ex}");
            }
        }

        private static Before Read(Skills skills, Skills.SkillType type)
        {
            if (!IsLocalPlayers(skills) || type == Skills.SkillType.None) return null;

            var skill = skills.GetSkill(type);
            if (skill == null) return null;

            return new Before { Level = skill.m_level, Accumulator = skill.m_accumulator };
        }

        private static void Report(Skills skills, Skills.SkillType type, Before before)
        {
            var skill = skills.GetSkill(type);
            if (skill == null) return;

            string key = KeyFor(type);

            // A level just turned over. Vanilla's own level-up message is
            // already on its way through as its own line, and a running
            // "83% of the way there" alongside it would be nonsense.
            if (skill.m_level > before.Level)
            {
                NotificationHud.Drop(key);
                return;
            }

            var setting = NotificationsConfig.ShowSkillGain;
            if (setting != null && !setting.Value) return;

            float gained = skill.m_accumulator - before.Accumulator;
            float percent = SkillProgress.PercentOfLevel(gained, skill.m_level);
            if (percent <= 0f) return;

            var icon = skill.m_info == null ? null : skill.m_info.m_icon;
            NotificationHud.Push(key, LabelFor(type), NotificationStyle.Percent, percent, icon);
        }

        private static string KeyFor(Skills.SkillType type) => "skill:" + type;

        /// <summary>
        /// The skill's own name in the player's language. Valheim's
        /// translation keys for skills are the enum name in lower case, and
        /// the prefix is lower case already, so one pass over the whole
        /// string gets there.
        /// </summary>
        private static string LabelFor(Skills.SkillType type) =>
            NotificationRouter.Localize(("$skill_" + type).ToLowerInvariant());

        private static bool IsLocalPlayers(Skills skills)
        {
            if (!NotificationRouter.Watching() || skills == null) return false;

            var local = Player.m_localPlayer;
            return local != null && skills.m_player == local;
        }
    }
}
