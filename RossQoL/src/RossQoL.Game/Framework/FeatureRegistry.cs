using System.Collections.Generic;
using RossQoL.Game.Portals;

namespace RossQoL.Game.Framework
{
    /// <summary>
    /// Every category and feature, listed explicitly so the whole mod is
    /// readable in one place. No reflection discovery.
    /// </summary>
    internal static class FeatureRegistry
    {
        public static IReadOnlyList<Category> Create() => new[]
        {
            new Category("Portals", "All portal tweaks.",
                new TamesFollowFeature()),
        };
    }
}
