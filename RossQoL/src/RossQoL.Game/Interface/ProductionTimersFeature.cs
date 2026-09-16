using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Interface
{
    /// <summary>
    /// Beehives, sap collectors and fermenters say how long they still need
    /// in their hover text.
    ///
    /// Client scope: it only adds text to what this player sees. Every number
    /// comes from values the game already shares with every client, so any
    /// player reads the same countdown.
    /// </summary>
    internal sealed class ProductionTimersFeature : Feature
    {
        public const string FeatureName = "Interface/ProductionTimers";

        public static ProductionTimersFeature Instance { get; private set; }

        public ProductionTimersFeature() => Instance = this;

        public override string Key => "ProductionTimers";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "Beehives, sap collectors and fermenters show how long until the next honey or sap, until they are "
            + "full, and until a batch is ready.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(BeehiveTimerPatch),
            typeof(SapCollectorTimerPatch),
            typeof(FermenterTimerPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Beehive", "GetHoverText", "where the beehive countdown is added"),
            new CompatMember("Beehive", "m_secPerUnit", "how long a honey takes"),
            new CompatMember("Beehive", "m_maxHoney", "how much a beehive holds"),
            new CompatMember("SapCollector", "GetHoverText", "where the sap countdown is added"),
            new CompatMember("SapCollector", "m_secPerUnit", "how long a sap takes"),
            new CompatMember("SapCollector", "m_maxLevel", "how much a sap collector holds"),
            new CompatMember("Fermenter", "GetHoverText", "where the fermenter countdown is added"),
            new CompatMember("Fermenter", "m_fermentationDuration", "how long a batch takes"),
            new CompatMember("ZDOVars", "s_product", "progress towards the next honey or sap"),
            new CompatMember("ZDOVars", "s_level", "how much a producer holds"),
            new CompatMember("ZDOVars", "s_startTime", "when a fermenter was filled"),
        };
    }
}
