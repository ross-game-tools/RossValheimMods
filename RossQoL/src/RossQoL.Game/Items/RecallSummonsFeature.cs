using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// The Dead Raiser's secondary attack calls every skeleton it raised
    /// back to your side, instead of doing nothing -- vanilla ships the
    /// staff with a blank secondary attack, which is why middle-click has
    /// never done anything with it.
    ///
    /// Two independent pieces make this work: <see cref="RecallSummonsItemPatch"/>
    /// gives the staff a real (but harmless) secondary attack so vanilla's
    /// own checks accept the input at all, and <see cref="RecallSummonsAttackPatch"/>
    /// intercepts that input before vanilla resolves it, so the recall never
    /// touches the resource-cost or hit-resolution machinery a real attack
    /// would.
    ///
    /// Synced scope: it moves creatures by writing their ZDOs directly,
    /// which on a server can belong to another client -- the same reason
    /// Portals/TamesFollow is synced.
    /// </summary>
    internal sealed class RecallSummonsFeature : Feature
    {
        public const string FeatureName = "Items/RecallSummons";

        public static RecallSummonsFeature Instance { get; private set; }

        public RecallSummonsFeature() => Instance = this;

        public override string Key => "RecallSummons";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "The Dead Raiser's secondary attack calls every skeleton it raised, and that is still following "
            + "you, back to your side -- spread out around you rather than piled on top of each other. Plays "
            + "the staff's own cast animation and sound and takes a brief moment to complete, set by "
            + "RecallCastSeconds. Costs no eitr, stamina or health, and is on a cooldown set by "
            + "RecallCooldownSeconds.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(RecallSummonsItemPatch), typeof(RecallSummonsAttackPatch)
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("ObjectDB", "Awake", "the moment item definitions are ready to change"),
            new CompatMember("ObjectDB", "CopyOtherDB", "item definitions replaced when a world loads"),
            new CompatMember("ObjectDB", "m_items", "finding the Dead Raiser"),
            new CompatMember("ItemDrop+ItemData", "m_dropPrefab", "identifying the Dead Raiser by prefab"),
            new CompatMember("ItemDrop+ItemData+SharedData", "m_attack", "the primary attack to base the secondary on, and whose start effect the cast sound reuses"),
            new CompatMember("ItemDrop+ItemData+SharedData", "m_secondaryAttack", "the blank secondary attack to fill in"),
            new CompatMember("Attack", "m_startEffect", "the staff's own cast sound and visual, played on the middle-click"),
            new CompatMember("EffectList", "Create", "playing the cast sound the same way vanilla plays any attack effect"),
            new CompatMember("EffectList", "HasEffects", "checking the start effect isn't empty before playing it"),
            new CompatMember("Humanoid", "StartAttack", "where a secondary attack fires, telling primary from secondary"),
            new CompatMember("Humanoid", "GetCurrentWeapon", "identifying which item was swung, and whether the staff is still equipped once a cast is pending"),
            new CompatMember("Character", "GetAllCharacters", "finding every summoned skeleton"),
            new CompatMember("MonsterAI", "GetFollowTarget", "whether a skeleton is following you right now"),
            new CompatMember("ZDOVars", "s_follow", "the saved follow name a non-owned skeleton still carries"),
            new CompatMember("ZNetView", "GetZDO", "reading and moving a skeleton's ZDO"),
            new CompatMember("Player", "GetPlayerName", "matching a skeleton's saved follow name to you"),
            new CompatMember("Player", "IsDead", "cancelling a pending cast if the player has died"),
            new CompatMember("Player", "IsTeleporting", "cancelling a pending cast if the player is mid-teleport"),
            new CompatMember("Game", "IsShuttingDown", "cancelling a pending cast if the player is logging out"),
            new CompatMember("Character", "GetZAnim", "reaching the player's animation sync component to play the cast"),
            new CompatMember("ZSyncAnimation", "SetTrigger", "playing the recall's cast animation the same way vanilla plays any attack"),
            new CompatMember("Humanoid", "m_currentAttack", "the attack the staff is still holding from its last swing, which the cast animation would otherwise fire again"),
            new CompatMember("Humanoid", "m_previousAttack", "where vanilla parks a retired attack, so the primary attack's chain level is unaffected"),
            new CompatMember("Attack", "IsDone", "telling a finished attack from one still swinging"),
            new CompatMember("Character", "InInterior", "telling a dungeon interior from the overworld, which the arrival search has to ask differently"),
            new CompatMember("ZoneSystem", "IsBlocked", "whether a spot a skeleton is being recalled to is obstructed, outdoors"),
            new CompatMember("ZoneSystem", "GetSolidHeight", "the floor under a recall spot, and whether there is one at all at the height you are standing at"),
            new CompatMember("ZoneSystem", "GetGroundHeight", "the terrain surface over a recall spot, which vanilla would lift a skeleton up to"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            RecallSummonsConfig.Bind(config, section, Scope);

        public override void OnActivated(GameObject host) => host.AddComponent<RecallCastRunner>();
    }
}
