using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// The Spirit Caller raises a creature you do not already have out before
    /// it repeats one, instead of rolling uniformly among its four every cast.
    ///
    /// Vanilla picks <c>m_spawnPrefab[Random.Range(0, length)]</c> each cast,
    /// so getting all four out means casting until the dice happen to land on
    /// the last one, while each repeat just replaces its own kind under the
    /// per-kind summon cap. The order the pick follows lives in
    /// <see cref="RossQoL.Core.Items.SummonPick"/>: missing kinds first, then
    /// the kind with the most wounded creature, then random among ties.
    ///
    /// Written against SpawnAbility generally rather than the Spirit Caller by
    /// name: it acts on any player-cast spawner that raises one creature per
    /// cast, commands it to follow, and chooses between more than one prefab.
    /// Today that is exactly the Spirit Caller -- the Dead Raiser has a single
    /// prefab and is untouched -- and a future staff of the same shape gets
    /// the same treatment without a new name to maintain.
    ///
    /// Client scope: the pick runs on the caster's own machine, in the
    /// projectile its own attack spawned, and only chooses which vanilla
    /// prefab the vanilla spawn goes on to raise.
    /// </summary>
    internal sealed class SpiritCallerVarietyFeature : Feature
    {
        public const string FeatureName = "Items/SpiritCallerVariety";

        public static SpiritCallerVarietyFeature Instance { get; private set; }

        public SpiritCallerVarietyFeature() => Instance = this;

        public override string Key => "SpiritCallerVariety";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "The Spirit Caller summons a creature you don't already have out before repeating one. Once all "
            + "four are out, it summons whichever kind has the most badly wounded creature (by percentage of "
            + "its health), and picks at random when they are all equally healthy.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(SpiritCallerVarietyPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("SpawnAbility", "Setup", "the moment a cast's spawner learns who cast it, just before it picks a creature"),
            new CompatMember("SpawnAbility", "m_spawnPrefab", "the creatures the staff chooses between"),
            new CompatMember("SpawnAbility", "m_commandOnSpawn", "telling a summon staff from any other spawner"),
            new CompatMember("SpawnAbility", "m_minToSpawn", "making sure a cast raises one creature"),
            new CompatMember("SpawnAbility", "m_maxToSpawn", "making sure a cast raises one creature"),
            new CompatMember("SpawnAbility", "m_maxSpawned", "never steering a cast onto a creature the staff would refuse"),
            new CompatMember("SpawnSystem", "GetNrOfInstances", "counting a creature the way the staff's own limit does"),
            new CompatMember("Character", "GetAllCharacters", "finding the creatures already following you"),
            new CompatMember("MonsterAI", "GetFollowTarget", "whether a creature is following you right now"),
            new CompatMember("ZDOVars", "s_follow", "the saved follow name a creature another client owns still carries"),
            new CompatMember("Player", "GetPlayerName", "matching a creature's saved follow name to you"),
            new CompatMember("Character", "GetHealth", "reading how hurt a creature is"),
            new CompatMember("Character", "GetMaxHealth", "turning that health into a percentage"),
        };
    }
}
