using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RossQoL.Core.Framework;
using RossQoL.Game.Framework;
using UnityEngine;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// A marker showing where you died and how far it is, sitting over your
    /// grave while it is in view and sliding to the edge of the screen,
    /// pointing, when it is not. It goes when the grave is emptied, and
    /// CycleGraveKey steps through older graves.
    ///
    /// Registered first in the Death category: it is the feature the
    /// category is named for.
    ///
    /// Client scope: a display over the player's own remembered graves,
    /// changing nothing anyone else sees.
    /// </summary>
    internal sealed class GraveMarkerFeature : Feature
    {
        public const string FeatureName = "Death/GraveMarker";

        public static GraveMarkerFeature Instance { get; private set; }

        public GraveMarkerFeature() => Instance = this;

        public override string Key => "GraveMarker";

        public override FeatureScope Scope => FeatureScope.Client;

        public override string Description =>
            "A marker showing where you died and how far it is, sitting over your grave while it is in view "
            + "and sliding to the edge of the screen, pointing, when it is not. It goes when the grave is "
            + "emptied, and CycleGraveKey steps through older graves.";

        public override IEnumerable<Type> PatchClasses =>
            new[] { typeof(GraveMarkerPatch), typeof(GraveRecordingPatch) };

        /// <summary>
        /// This feature's own members, plus the two lists it shares: the
        /// recording patch's and the looted-grave watcher's, each declared
        /// beside the code that reaches for them so no feature can end up
        /// lying about what it depends on.
        /// </summary>
        public override IEnumerable<CompatMember> RequiredMembers => new[]
        {
            new CompatMember("Hud", "Awake", "adding the marker once the HUD exists"),
            new CompatMember("EnemyHud", "instance", "waiting for EnemyHud to be ready before adding the marker"),
            new CompatMember("EnemyHud", "m_hudRoot", "parenting the marker to the HUD's own screen-space root"),
            new CompatMember("Minimap", "instance", "finding the minimap to clone its text style from"),
            new CompatMember("Minimap", "m_biomeNameSmall", "matching the minimap's text style"),
            new CompatMember("Utils", "GetMainCamera", "projecting the grave onto the screen"),
            new CompatMember("Utils", "WorldToScreenPointScaled", "projecting correctly at any render scale"),
            new CompatMember("ZInput", "GetKey", "reading ClearGraveKey directly from Update"),
            new CompatMember("ZInput", "GetKeyDown", "reading CycleGraveKey directly from Update"),
            new CompatMember("Chat", "HasFocus", "ignoring both keys while the chat box has focus"),
            new CompatMember("Console", "IsVisible", "ignoring both keys while the console is open"),
        }
            .Concat(GraveRecordingPatch.RequiredMembers)
            .Concat(GraveCleanupWatcher.RequiredMembers);

        public override void BindSettings(ConfigFile config, string section) =>
            DeathConfig.BindMarker(config, section, Scope);

        /// <summary>
        /// The marker only draws graves; forgetting a looted one belongs to
        /// the category, so the watcher is shared with the corpse run buff
        /// and added at most once.
        /// </summary>
        public override void OnActivated(GameObject host) => GraveCleanupWatcher.EnsureOn(host);
    }
}
