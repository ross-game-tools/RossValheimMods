using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Producers take what they need from nearby containers: ore and fuel for
    /// smelters, kilns, blast furnaces, windmills and spinning wheels; fuel
    /// for ovens and shield generators; a mead base for an empty fermenter.
    /// With AutoHarvest taking the output away, a base runs itself while a
    /// player is near it. Fires are fed by the Fires category, which shares
    /// the settings bound here.
    ///
    /// Everything is handed over with vanilla's own RPCs, one unit at a time
    /// as a player at the switch would, so queue limits, cheat flags and
    /// effects stay the game's. Containers are used under the same rules as
    /// harvesting: ward access at the producer, the container's own privacy,
    /// fresh contents, and settled ownership, never claimed.
    ///
    /// Synced scope: feeding moves real items between real containers and
    /// producers, so whether it happens, and how far it reaches, is the
    /// server's rule.
    /// </summary>
    internal sealed class AutoFeedFeature : Feature
    {
        public const string FeatureName = "Production/AutoFeed";

        public static AutoFeedFeature Instance { get; private set; }

        public AutoFeedFeature() => Instance = this;

        public override string Key => "AutoFeed";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Producers near a player take what they need from containers within FeedRadius: ore and fuel for "
            + "smelters, kilns, blast furnaces, windmills and spinning wheels, fuel for ovens and shield "
            + "generators, and a mead base for an empty fermenter. MinimumLeftBehind keeps a reserve in your "
            + "chests, KilnFuel limits what a kiln may burn, and MaxOutput stops a producer once you have enough.";

        public override IEnumerable<Type> PatchClasses => new[]
        {
            typeof(ContainerAwakeRegistryPatch),
            typeof(SmelterFeedPatch),
            typeof(CookingStationFeedPatch),
            typeof(ShieldGeneratorFeedPatch),
            typeof(FermenterFeedPatch),
        };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Container", "Awake", "finding the containers to take from"),
            new CompatMember("Container", "GetInventory", "taking items out of containers"),
            new CompatMember("Container", "Save", "writing a container back after taking from it"),
            new CompatMember("Inventory", "CountItems", "how much a container holds"),
            new CompatMember("Inventory", "GetAllItems", "reading the cheat flag off what is taken"),
            new CompatMember("Inventory", "RemoveItem", "taking items out of containers"),
            new CompatMember("PrivateArea", "CheckAccess", "respecting wards"),
            new CompatMember("Smelter", "UpdateSmelter", "the moment a smelter is fed"),
            new CompatMember("Smelter", "m_conversion", "what a smelter takes and makes"),
            new CompatMember("Smelter", "m_maxOre", "how much ore a smelter still has room for"),
            new CompatMember("Smelter", "m_maxFuel", "how much fuel a smelter still has room for"),
            new CompatMember("Smelter", "m_fuelItem", "what a smelter burns"),
            new CompatMember("Smelter", "m_windmill", "telling a windmill from the other smelters"),
            new CompatMember("Smelter", "GetQueueSize", "how much ore is already queued"),
            new CompatMember("Smelter", "GetFuel", "how much fuel is already in"),
            new CompatMember("CookingStation", "UpdateFuel", "the moment an oven is fed"),
            new CompatMember("CookingStation", "m_useFuel", "ovens that burn nothing are left alone"),
            new CompatMember("CookingStation", "m_fuelItem", "what an oven burns"),
            new CompatMember("CookingStation", "m_maxFuel", "how much fuel an oven still has room for"),
            new CompatMember("CookingStation", "GetFuel", "how much fuel is already in"),
            new CompatMember("ShieldGenerator", "UpdateShield", "the moment a shield generator is fed"),
            new CompatMember("ShieldGenerator", "m_fuelItems", "what a shield generator burns"),
            new CompatMember("ShieldGenerator", "m_maxFuel", "how much fuel it still has room for"),
            new CompatMember("ShieldGenerator", "m_defaultFuel", "what it holds before anyone fuels it"),
            new CompatMember("Fermenter", "SlowUpdate", "the moment a fermenter is fed"),
            new CompatMember("Fermenter", "m_conversion", "which mead bases a fermenter accepts"),
            new CompatMember("ZDOVars", "s_fuel", "reading how much fuel a producer holds"),
            new CompatMember("ZDOVars", "s_content", "telling an empty fermenter from a working one"),
        };

        public override void BindSettings(ConfigFile config, string section) =>
            AutoFeedConfig.Bind(config, section, Scope);
    }
}
