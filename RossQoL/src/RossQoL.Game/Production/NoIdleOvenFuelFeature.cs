using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// An oven (CookingStation) only spends fuel while it has something to
    /// cook. Vanilla ovens whose <c>m_useFueldWhileEmpty</c> is set burn their
    /// fuel down even when idle; this holds the fuel until there is food to
    /// cook, so a fuelled oven left alone keeps its fuel -- and AutoFeed is not
    /// left refilling an idle oven from your chests for nothing.
    ///
    /// Synced scope: fuel is real world state every player shares, and the burn
    /// runs on whichever client owns the oven, so whether ovens idle-burn is the
    /// server's rule -- the same reasoning as Fires/InfiniteFuel.
    /// </summary>
    internal sealed class NoIdleOvenFuelFeature : Feature
    {
        public const string FeatureName = "Production/NoIdleOvenFuel";

        public static NoIdleOvenFuelFeature Instance { get; private set; }

        public NoIdleOvenFuelFeature() => Instance = this;

        public override string Key => "NoIdleOvenFuel";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Ovens only spend fuel while they have something to cook. A fuelled oven left idle keeps its "
            + "fuel instead of slowly burning it away, so it is not refilled from your chests for nothing. "
            + "Cooking is unchanged.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(NoIdleOvenFuelPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("CookingStation", "UpdateCooking", "the oven tick where idle fuel burn is switched off"),
            new CompatMember("CookingStation", "m_useFuel", "ovens that burn nothing are left alone"),
            new CompatMember("CookingStation", "m_useFueldWhileEmpty", "vanilla's own switch for burning fuel with nothing to cook"),
        };
    }
}
