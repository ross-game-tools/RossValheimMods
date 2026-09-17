using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Tames
{
    /// <summary>
    /// Chicks and hens make no sound at all: no peeping, wing flapping,
    /// clucking, pecking, footsteps, hurt or death. Every other creature is
    /// unchanged.
    ///
    /// Client scope: the sound is silenced as this machine plays it, so it
    /// changes only what this player hears, and it works the same for birds
    /// another player owns as for this client's own.
    /// </summary>
    internal sealed class QuietChickensFeature : Feature
    {
        public const string FeatureName = "Tames/QuietChickens";

        public static QuietChickensFeature Instance { get; private set; }

        public QuietChickensFeature() => Instance = this;

        public override string Key => "QuietChickens";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Chicks and hens make no sound: no peeping, clucking, wing flapping, pecking, footsteps, hurt or "
            + "death. Other creatures keep their own sounds.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(QuietChickensPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("ZSFX", "Play", "silencing a chicken sound whoever owns the bird"),
        };
    }
}
