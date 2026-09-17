using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Progression
{
    /// <summary>
    /// The mist lifts once the Queen is dead. You beat the thing that made
    /// the Mistlands what they are, so the biome stops hiding itself.
    ///
    /// Wisplights still work and are still worth carrying for the dungeons;
    /// this only stops the open-world mist being emitted.
    ///
    /// Synced scope: whether a biome looks like that is a decision about the
    /// world every player shares, so the server makes it.
    /// </summary>
    internal sealed class ClearMistFeature : Feature
    {
        public const string FeatureName = "Progression/ClearMist";

        /// <summary>
        /// The key the Queen's prefab sets on defeat. Not in the GlobalKeys
        /// enum -- the enum stops at Yagluth -- so it is the plain string the
        /// prefab carries.
        /// </summary>
        public const string QueenKey = "defeated_queen";

        public static ClearMistFeature Instance { get; private set; }

        public ClearMistFeature() => Instance = this;

        public override string Key => "ClearMist";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Killing the Queen clears the mist from the Mistlands. Until then it is exactly as vanilla, and "
            + "turning this off brings the mist back.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(ClearMistPatch),
            typeof(ClearMistEnvPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("ParticleMist", "Update", "where the Mistlands mist is emitted"),
            new CompatMember("ParticleMist", "m_ps", "clearing the mist already in the air"),
            new CompatMember("ParticleMist", "instance", "the one mist system to switch off"),
            new CompatMember("EnvMan", "SetEnv", "the moment to decide whether mist exists"),
            new CompatMember("ZoneSystem", "GetGlobalKey", "reading whether the Queen is dead"),
        };
    }
}
