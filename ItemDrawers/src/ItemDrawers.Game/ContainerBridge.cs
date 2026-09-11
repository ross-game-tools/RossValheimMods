using HarmonyLib;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Four patches on Container, each guarded by a type check so vanilla
    /// containers are completely untouched. This is what makes OttoFuel and
    /// NoVikingLeftBehind able to read and drain drawers without either mod
    /// knowing this one exists: both find a DrawerComponent via
    /// GetComponent&lt;Container&gt;() (DrawerComponent already derives from
    /// Container) and then call the ordinary Container API -- GetInventory,
    /// and whatever Inventory methods they use from there. Both also
    /// discover containers in the first place via their own Harmony postfix
    /// on Container.Awake (confirmed by decompiling both), which is what
    /// AwakePatch below exists to make possible without also running
    /// vanilla's own Awake body on a drawer.
    /// </summary>
    internal static class ContainerBridge
    {
        /// <summary>
        /// Container.Awake constructs a second, unused Inventory for a
        /// drawer (m_inventory -- GetInventoryPatch below substitutes the
        /// mirror regardless), registers vanilla's own container RPCs on
        /// m_nview, subscribes Container.OnDestroyed to WearNTear/
        /// Destructible (risking a double-spill alongside
        /// DrawerComponent.OnDrawerDestroyed's own, independent
        /// subscription), and starts polling via
        /// InvokeRepeating("CheckForChanges", ...) -- none of which this
        /// mod wants running on a drawer. But Container.Awake is also
        /// exactly the method both OttoFuel and NoVikingLeftBehind patch
        /// with their own postfix to discover containers at all, and a
        /// Harmony postfix only runs when the patched method is actually
        /// invoked -- so if nothing ever calls Container.Awake for a
        /// drawer, those postfixes never fire and neither mod ever learns a
        /// drawer exists (this was task-12-report.md's round-4 finding).
        ///
        /// This prefix is the resolution: DrawerComponent.Awake calls
        /// base.Awake() (invoking this patched method), and this prefix
        /// returns false for a DrawerComponent, skipping vanilla's body
        /// entirely -- confirmed by decompiling HarmonyX's own IL generation
        /// (HarmonyManipulator.WritePrefixes/WritePostfixes) that a
        /// false-returning prefix branches directly to the exact label
        /// postfixes are emitted at, never past them, so postfixes on this
        /// method still run unconditionally regardless of this prefix's
        /// result. Neither OttoFuel's nor NoVikingLeftBehind's postfix
        /// declares a `__runOriginal` parameter, so neither can detect that
        /// the skip happened; both proceed exactly as if vanilla's Awake had
        /// run.
        ///
        /// The one piece of state both mods' discovery paths actually
        /// depend on -- Container's own private m_nview field -- is
        /// restored independently by DrawerComponent.Awake (via
        /// AccessTools.FieldRefAccess) before it calls base.Awake(), so it
        /// is already valid by the time this prefix (and then those
        /// postfixes) run. See DrawerComponent.Awake's own docstring for the
        /// full audit of every other Container member that reads m_nview or
        /// m_inventory, confirming none of them become newly reachable and
        /// unsafe as a result of m_nview now being non-null.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
        private static class AwakePatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Container), nameof(Container.Awake));

            private static bool Prefix(Container __instance) => !(__instance is DrawerComponent);
        }

        /// <summary>
        /// Container.GetInventory is not virtual (confirmed by decompiling
        /// assembly_valheim.dll: `public Inventory GetInventory() { return
        /// m_inventory; }`, no virtual/override modifiers), so a subclass
        /// cannot override it to substitute the mirror. A Harmony patch is
        /// the only route: refresh the mirror first (cheap when nothing
        /// changed -- see DrawerComponent.RefreshMirror), then hand it back
        /// in place of the (always-null, for a drawer) m_inventory field.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.GetInventory))]
        private static class GetInventoryPatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Container), nameof(Container.GetInventory));

            private static void Prefix(Container __instance)
            {
                if (__instance is DrawerComponent drawer) drawer.RefreshMirror();
            }

            private static void Postfix(Container __instance, ref Inventory __result)
            {
                if (__instance is DrawerComponent drawer) __result = drawer.MirrorInventory;
            }
        }

        /// <summary>
        /// Container.Save serialises m_inventory into the ZDO's "items"
        /// byte array. Vanilla only ever calls it from two places, both
        /// wired up inside Container.Awake's `if (m_nview.GetZDO() != null)`
        /// block: OnContainerChanged (subscribed to m_inventory.m_onChanged)
        /// and the InvokeRepeating("CheckForChanges", ...) that drives
        /// Load/UpdateUseVisual once a second. That whole block still never
        /// runs for a DrawerComponent -- DrawerComponent.Awake does call
        /// base.Awake() now (see AwakePatch above, added so other mods'
        /// Container.Awake postfixes fire), but AwakePatch's own prefix
        /// skips vanilla's body on every such call, so the subscription and
        /// the InvokeRepeating inside it still never execute -- so in
        /// practice Container.Save/Load are simply never invoked on a
        /// drawer through any vanilla code path today; m_inventory stays
        /// null, nothing is subscribed to it, and nothing is polling. These
        /// two patches are belt-and-braces
        /// against a future Valheim change (or another mod) that calls
        /// Save/Load directly on a Container reference without going
        /// through GetInventory first, in which case Save would serialise
        /// the mirror's oversized single stack into the ZDO's items field
        /// -- bloating the save and shadowing the two fields that are
        /// actually authoritative -- and Load would overwrite the mirror
        /// from that field. Kept for that reason even though nothing
        /// exercises them today; do not remove on the assumption they're
        /// dead code.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Save))]
        private static class SavePatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Container), nameof(Container.Save));

            private static bool Prefix(Container __instance) => !(__instance is DrawerComponent);
        }

        /// <summary>
        /// Counterpart to SavePatch -- see its docstring for why both are
        /// currently unreachable via any vanilla call path for a drawer, and
        /// kept anyway. Container.Load returns bool; skipping it for a
        /// drawer leaves that bool at its default, false ("nothing to
        /// react to"), which is the correct answer regardless -- a
        /// drawer's contents live in the two ZDO fields
        /// DrawerComponent.Commit and Snapshot read/write directly, never
        /// in the items field this would otherwise decode.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Load))]
        private static class LoadPatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Container), nameof(Container.Load));

            private static bool Prefix(Container __instance) => !(__instance is DrawerComponent);
        }
    }
}
