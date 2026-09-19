using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Puts a meal in your belly when you respawn after a death, chosen by
    /// how far the world has got. Never fires on a plain login.
    ///
    /// Synced scope: the food handed out, and how many portions, is a
    /// world-balance knob like every other death-penalty setting.
    /// </summary>
    internal sealed class RespawnFoodFeature : Feature
    {
        public const string FeatureName = "Death/RespawnFood";

        public static RespawnFoodFeature Instance { get; private set; }

        public RespawnFoodFeature() => Instance = this;

        public override string Key => "RespawnFood";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Respawning after a death puts a meal in your belly, chosen by how far the world has got -- "
            + "berries early on, a cooked meal once the world is further along. Logging in never does this.";

        public override IEnumerable<Type> PatchClasses =>
            new[] { typeof(RespawnPatch), typeof(GraveRecordingPatch) };

        /// <summary>
        /// This feature's own members, plus the shared recording patch's own
        /// list, declared beside that patch so a rename there disables every
        /// feature depending on it rather than just one of them.
        /// </summary>
        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Player", "OnSpawned", "the hook shared with RespawnRested to tell a death-respawn from a login"),
            new CompatMember("Player", "EatFood", "putting the handed-out food in the belly"),
            new CompatMember("ObjectDB", "GetItemPrefab", "finding the food prefab to hand out"),
            new CompatMember("ItemDrop", "m_itemData", "the shared item data to clone for each helping"),
            new CompatMember("ItemDrop+ItemData", "Clone", "a fresh helping per portion, never the prefab's own instance"),
            new CompatMember("ItemDrop+ItemData", "m_dropPrefab", "EatFood reads its name, and the save needs it set"),
            new CompatMember("ZoneSystem", "GetGlobalKey", "reading how far the world has got"),
        }
            .Concat(GraveRecordingPatch.RequiredMembers);

        public override void BindSettings(ConfigFile config, string section) =>
            DeathConfig.BindRespawnFood(config, section, Scope);
    }
}
