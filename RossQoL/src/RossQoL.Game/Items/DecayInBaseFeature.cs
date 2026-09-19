using System;
using System.Collections.Generic;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Items dropped inside a player base decay on vanilla's ordinary
    /// schedule instead of being permanently exempt from it.
    ///
    /// Synced scope: which items survive is shared by everyone in the
    /// world, and the decay check runs on whichever client owns the item.
    /// </summary>
    internal sealed class DecayInBaseFeature : Feature
    {
        public const string FeatureName = "World/DecayInBase";

        public static DecayInBaseFeature Instance { get; private set; }

        public DecayInBaseFeature() => Instance = this;

        public override string Key => "DecayInBase";

        public override FeatureScope Scope => FeatureScope.Synced;

        public override string Description =>
            "Things you drop at home no longer lie there forever -- they keep the same one-hour life as "
            + "anything else dropped anywhere. Nothing disappears while you are standing nearby.";

        public override IEnumerable<Type> PatchClasses => new[] { typeof(DecayInBasePatch) };

        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("ItemDrop", "TimedDestruction", "the check this feature extends to items inside a base"),
            new CompatMember("ItemDrop", "GetTimeSinceSpawned", "how old the item is, vanilla's own clock"),
            new CompatMember("ItemDrop", "IsInsideBase", "telling a base-exempt item from an ordinary one"),
            new CompatMember("ItemDrop", "InTar", "leaving an item stuck in tar alone, as vanilla does"),
            new CompatMember("ItemDrop", "IsPiece", "leaving building debris alone, as vanilla does"),
            new CompatMember("ItemDrop", "m_autoDestroy", "items that never decay at all, such as quest items"),
            new CompatMember("ItemDrop", "m_nview", "the network view whose Destroy() removes the item"),
        };
    }
}
