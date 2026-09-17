using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// A wisplight keeps the mist away from wherever it is in your pack, so it
    /// stops competing for a slot at all.
    ///
    /// Vanilla makes it a utility item, the one slot that also holds the
    /// Megingjord, so carrying a light costs you your belt. Moving it to the
    /// trinket slot would only move the argument, so instead the light does
    /// its work from the inventory: the same object vanilla hangs on your hip
    /// when the wisplight is equipped is kept on the player while one is
    /// carried.
    ///
    /// It looks exactly as it does when worn, because it is the same thing:
    /// the wisplight has no model to hang on you -- its prefab carries no
    /// attach children -- and everything you see when it is equipped comes
    /// from the item's equip status effect. That effect is what this grants.
    ///
    /// Equipping it for real still works; the carried grant stands down while
    /// it is worn, so vanilla's own is the only one.
    ///
    /// Synced scope: how far the mist is pushed back is a rule about the world
    /// every player shares, so the server decides whether this applies.
    /// </summary>
    internal sealed class WisplightCarryFeature : Feature
    {
        public const string FeatureName = "Items/WisplightCarry";

        public static WisplightCarryFeature Instance { get; private set; }

        public WisplightCarryFeature() => Instance = this;

        public override string Key => "WisplightCarry";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "A wisplight in your inventory works exactly as if it were equipped -- the wisp circling you, its "
            + "light and the mist it pushes back -- without costing you the utility slot, so it never competes "
            + "with the Megingjord. Equipping it for real still works as it always did.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(WisplightCarryPatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Player", "Update", "noticing a wisplight coming and going"),
            new CompatMember("Humanoid", "m_utilityItem", "standing down while the wisplight is worn"),
            new CompatMember("Character", "m_seman", "granting the wisp that circles you"),
            new CompatMember("SEMan", "AddStatusEffect", "granting the wisp that circles you"),
            new CompatMember("SEMan", "HaveStatusEffect", "granting it once rather than every second"),
            new CompatMember("SEMan", "RemoveStatusEffect", "taking it back when the wisplight goes"),
        };
    }
}
