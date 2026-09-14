using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// A killed creature's loot drops the moment it dies instead of when its
    /// corpse fades away. The corpse itself is left alone.
    ///
    /// Synced scope: loot drops on whichever machine owns the dying creature,
    /// often not the player who killed it, so a personal setting would make
    /// the result depend on who happens to own that area.
    /// </summary>
    internal sealed class InstantLootFeature : Feature
    {
        public const string FeatureName = "Combat/InstantLoot";

        public static InstantLootFeature Instance { get; private set; }

        public InstantLootFeature() => Instance = this;

        public override string Key => "InstantLoot";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "A killed creature's loot drops the moment it dies, at its body, instead of when the corpse "
            + "fades a few seconds later. The corpse still falls and fades as normal.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(InstantLootPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Ragdoll", "Setup", "the moment a corpse receives its loot"),
            new CompatMember("Ragdoll", "SpawnLoot", "dropping the corpse's loot the way vanilla does"),
            new CompatMember("Ragdoll", "m_nview", "dropping loot only on the corpse's owner"),
            new CompatMember("Ragdoll", "m_lootSpawnJoint", "dropping loot where vanilla would"),
            new CompatMember("Ragdoll", "GetAverageBodyPosition", "dropping loot where vanilla would"),
            new CompatMember("ZDOVars", "s_drops", "clearing the corpse's loot so it cannot drop twice"),
        };
    }
}
