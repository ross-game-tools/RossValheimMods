using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// Beating a biome's boss lets you carry that biome's metal through a
    /// portal: the Elder frees copper and tin, Bonemass frees iron, Moder
    /// frees silver, Yagluth frees black metal. Those four are the whole
    /// list, because those are the four biomes whose materials vanilla
    /// refuses to carry.
    ///
    /// Per biome, not cumulative. Killing a later boss says nothing about an
    /// earlier biome, because the point is that a place opens up once you
    /// have beaten it.
    ///
    /// Synced scope: what may go through a portal is a rule about the world
    /// every player shares, so the server decides it.
    /// </summary>
    internal sealed class TeleportUnlocksFeature : Feature
    {
        public const string FeatureName = "Progression/TeleportUnlocks";

        public static TeleportUnlocksFeature Instance { get; private set; }

        public TeleportUnlocksFeature() => Instance = this;

        public override string Key => "TeleportUnlocks";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Metal and ore may be carried through a portal once you have killed the boss of the biome it comes "
            + "from: the Elder for copper and tin, Bonemass for iron, Moder for silver, Yagluth for black metal. "
            + "Everything else vanilla refuses to teleport, it still refuses.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(TeleportUnlocksPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Inventory", "IsTeleportable", "the check that refuses ore at a portal"),
            new CompatMember("ZoneSystem", "GetGlobalKey", "reading which bosses are dead"),
        };
    }
}
