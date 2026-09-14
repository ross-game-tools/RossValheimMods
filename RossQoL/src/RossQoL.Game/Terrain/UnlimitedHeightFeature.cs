using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Terrain
{
    /// <summary>
    /// Raise and dig terrain beyond vanilla's 8 metres from the original
    /// ground, up to MaxRaise and MaxDig.
    ///
    /// Synced scope: every machine that sees the terrain draws it through the
    /// same height clamp (TerrainComp.ApplyToHeightmap), so a limit that
    /// differed per client would show and collide the ground differently.
    /// </summary>
    internal sealed class UnlimitedHeightFeature : Feature
    {
        public const string FeatureName = "Terrain/UnlimitedHeight";

        public static UnlimitedHeightFeature Instance { get; private set; }

        public UnlimitedHeightFeature() => Instance = this;

        public override string Key => "UnlimitedHeight";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Raise and dig terrain beyond vanilla's 8 metres from the original ground, up to MaxRaise and MaxDig. "
            + "Edits past 8 metres are saved in the world: with this off, or RossQoL removed, that ground is "
            + "drawn at 8 metres until it is turned on again. Editing ground while this is off, or after "
            + "lowering MaxRaise or MaxDig, permanently cuts nearby deeper edits down to the lower limit.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(ApplyToHeightmapLimitPatch),
            typeof(LevelTerrainLimitPatch),
            typeof(RaiseTerrainLimitPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("TerrainComp", "ApplyToHeightmap", "drawing edited ground past 8 metres"),
            new CompatMember("TerrainComp", "LevelTerrain", "levelling ground past 8 metres"),
            new CompatMember("TerrainComp", "RaiseTerrain", "raising and digging ground past 8 metres"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            TerrainConfig.Bind(config, section, Scope);
    }
}
