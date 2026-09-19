using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Interface
{
    /// <summary>
    /// A stacking list of top-left notifications instead of vanilla's single
    /// slot, plus the skill-progress line vanilla has never had.
    ///
    /// Client scope: it changes only what this player sees, and nothing it
    /// reads leaves this machine.
    /// </summary>
    internal sealed class NotificationsFeature : Feature
    {
        public const string FeatureName = "Interface/Notifications";

        public static NotificationsFeature Instance { get; private set; }

        public NotificationsFeature() => Instance = this;

        public override string Key => "Notifications";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Stacks top-left notifications instead of letting each one wipe out the last. Picking up "
            + "the same item again updates its own line -- \"Wood x12\" becomes \"Wood x20\" -- even with "
            + "other messages in between, and skill gains show as progress towards the next level. "
            + "Up to five lines at a time, each fading on its own.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(NotificationHudPatch),
            typeof(MessageHudShowMessagePatch),
            typeof(HumanoidPickupPatch),
            typeof(SkillGainPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("MessageHud", "Start", "building the notification list once the message HUD exists"),
            new CompatMember("MessageHud", "ShowMessage", "catching every top-left message before vanilla queues it"),
            new CompatMember("MessageHud", "m_messageText", "copying vanilla's own message text so ours looks the same"),
            new CompatMember("MessageHud", "m_messageIcon", "copying vanilla's own message icon so ours looks the same"),
            new CompatMember("MessageHud", "m_showDespiteHiddenHUD", "leaving vanilla's hidden-HUD handling exactly as it was"),
            new CompatMember("MessageHud", "AddLog", "keeping the message log filled now that vanilla's own path is skipped"),
            new CompatMember("Hud", "IsUserHidden", "hiding the list whenever the rest of the HUD is hidden"),
            new CompatMember("Humanoid", "Pickup", "knowing which item a pickup message is about"),
            new CompatMember("ItemDrop", "m_itemData", "reading the item being picked up"),
            new CompatMember("ItemDrop+ItemData", "m_shared", "reaching the item's name"),
            new CompatMember("ItemDrop+ItemData+SharedData", "m_name", "keying a pickup line to its item"),
            new CompatMember("Skills", "RaiseSkill", "noticing skill progress, which vanilla never reports"),
            new CompatMember("Skills", "GetSkill", "reading a skill's progress before and after it is raised"),
            new CompatMember("Skills", "m_player", "showing only this player's own skill gains"),
            new CompatMember("Skills+Skill", "m_level", "telling a level-up apart from progress towards one"),
            new CompatMember("Skills+Skill", "m_accumulator", "measuring how much progress was just gained"),
            new CompatMember("Skills+Skill", "m_info", "reaching the skill's icon"),
            new CompatMember("Skills+SkillDef", "m_icon", "showing the skill's own icon beside its line"),
            new CompatMember("Localization", "Localize", "showing item and skill names in the player's language"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            NotificationsConfig.Bind(config, section, Scope);
    }
}
