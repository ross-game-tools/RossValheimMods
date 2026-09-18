using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// A dungeon you have not set foot in for RespawnDays comes back as it
    /// was first found: chests stocked, creatures home, ore veins whole.
    ///
    /// Only in a biome whose boss is dead, so a crypt is a one-off until the
    /// swamp is beaten and a renewable run afterwards. The rebuild uses the
    /// dungeon's own seed, which Valheim derives from the world seed and the
    /// dungeon's position, so the layout that comes back is the layout that
    /// was there -- same rooms, same corners, same chests.
    ///
    /// Synced scope: this deletes and recreates real objects in a shared
    /// world, so whether it happens at all is the server's rule.
    /// </summary>
    internal sealed class DungeonRespawnFeature : Feature
    {
        public const string FeatureName = "Progression/DungeonRespawn";

        public static DungeonRespawnFeature Instance { get; private set; }

        public DungeonRespawnFeature() => Instance = this;

        public override string Key => "DungeonRespawn";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Dungeons in a biome whose boss you have killed come back RespawnDays after your last visit: "
            + "burial chambers and troll caves once the Elder is down, sunken crypts once Bonemass is, frost "
            + "caves once Moder is, infested mines once the Queen is. The layout is rebuilt from the dungeon's "
            + "own seed, so it returns exactly as it was.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(DungeonRegistryAwakePatch),
            typeof(DungeonRegistryDestroyPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("DungeonGenerator", "Awake", "noticing a dungeon load"),
            new CompatMember("DungeonGenerator", "Generate", "rebuilding a dungeon"),
            new CompatMember("DungeonGenerator", "GetSeed", "rebuilding it exactly as it was"),
            new CompatMember("Location", "m_hasInterior", "telling a dungeon from a surface ruin"),
            new CompatMember("Location", "m_interiorRadius", "how far the dungeon reaches"),
            new CompatMember("EnvMan", "GetDay", "what day it is"),
            new CompatMember("ZoneSystem", "GetGlobalKey", "reading which bosses are dead"),
            new CompatMember("ZNetView", "ClaimOwnership", "taking charge of what is being replaced"),
            new CompatMember("ZNetView", "Register", "listening for a visit stamp on a dungeon"),
            new CompatMember("ZNetView", "InvokeRPC", "asking a dungeon's owner to stamp it"),
            new CompatMember("Piece", "IsPlacedByPlayer", "leaving dungeons you have built in alone"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            ProgressionConfig.BindDungeonRespawn(config, section, Scope);

        public override void OnActivated(GameObject host) => host.AddComponent<DungeonRespawnManager>();
    }
}
