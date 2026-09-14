using HarmonyLib;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Four patches on Container, each guarded by a type check so vanilla
    /// containers are completely untouched. Together they make a drawer
    /// behave like a small chest for any mod that finds it via
    /// GetComponent&lt;Container&gt;() (DrawerComponent derives from
    /// Container) and uses the ordinary Container/Inventory API: GetInventory
    /// returns the drawer's view (DrawerView), and Save/Load persist that
    /// view in the drawer's own ZDO fields. OttoFuel and NoVikingLeftBehind
    /// also discover containers via their own Harmony postfix on
    /// Container.Awake, which AwakePatch keeps working without running
    /// vanilla's Awake body on a drawer.
    /// </summary>
    internal static class ContainerBridge
    {
        private static bool? _viewPatchesApplied;

        /// <summary>
        /// True when this plugin's prefixes on Container.GetInventory, Save
        /// and Load are all installed. DrawerComponent.Awake sets
        /// Container.m_inventory to the view only then: without the Save and
        /// Load prefixes, vanilla would serialize the view as vanilla item
        /// bytes and reload it with stacks capped at max stack size. Checked
        /// once; patching finishes in DrawerPlugin.Awake, before any drawer
        /// exists.
        /// </summary>
        internal static bool ViewPatchesApplied
        {
            get
            {
                if (_viewPatchesApplied == null)
                    _viewPatchesApplied = HasOwnPrefix(nameof(Container.GetInventory))
                                          && HasOwnPrefix(nameof(Container.Save))
                                          && HasOwnPrefix(nameof(Container.Load));
                return _viewPatchesApplied.Value;
            }
        }

        private static bool HasOwnPrefix(string methodName)
        {
            var method = AccessTools.Method(typeof(Container), methodName);
            var info = method == null ? null : Harmony.GetPatchInfo(method);
            if (info == null) return false;
            foreach (var prefix in info.Prefixes)
                if (prefix.owner == DrawerPlugin.PluginGuid) return true;
            return false;
        }

        /// <summary>
        /// Container.Awake would construct a second, unused Inventory,
        /// register vanilla's container RPCs, subscribe Container.OnDestroyed
        /// (a double spill alongside DrawerComponent.OnDrawerDestroyed) and
        /// start CheckForChanges polling -- none of which a drawer wants. But
        /// other mods discover containers with a postfix on this method, and
        /// a postfix only runs when the method is invoked. DrawerComponent.Awake
        /// therefore calls base.Awake(), and this prefix skips vanilla's body
        /// for a drawer; HarmonyX still runs postfixes after a false-returning
        /// prefix. Container.m_nview, which those postfixes read, is set by
        /// DrawerComponent.Awake before the call.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
        private static class AwakePatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Container), nameof(Container.Awake));

            private static bool Prefix(Container __instance) => !(__instance is DrawerComponent);
        }

        /// <summary>
        /// Container.GetInventory is not virtual. m_inventory already holds
        /// the view's Inventory (DrawerComponent.Awake), but this prefix also
        /// refreshes it from ViewSlots first when the ZDO changed and nothing
        /// local is pending, and still returns the view if m_inventory could
        /// not be set.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.GetInventory))]
        private static class GetInventoryPatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Container), nameof(Container.GetInventory));

            private static bool Prefix(Container __instance, ref Inventory __result)
            {
                if (!(__instance is DrawerComponent drawer)) return true;
                __result = drawer.GetViewInventory();
                return false;
            }
        }

        /// <summary>
        /// Vanilla never calls Save on a drawer (its callers are wired in the
        /// skipped Awake body), but craft-from-containers style mods call it
        /// directly after removing items. For a drawer it never writes
        /// vanilla's items field (whose loader caps stacks at max stack
        /// size); it marks a changed view dirty, and the end-of-frame flush
        /// claims, reconciles once ownership has settled, and republishes
        /// (DrawerComponent.SaveView/FlushView). Nothing is written or
        /// rebuilt here: this can run inside another mod's loop over the
        /// inventory.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Save))]
        private static class SavePatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Container), nameof(Container.Save));

            private static bool Prefix(Container __instance)
            {
                if (!(__instance is DrawerComponent drawer)) return true;
                drawer.SaveView();
                return false;
            }
        }

        /// <summary>
        /// Counterpart to SavePatch: reloads the view from ViewSlots and
        /// returns, like vanilla, whether anything was reloaded.
        /// </summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Load))]
        private static class LoadPatch
        {
            private static bool Prepare() =>
                ValheimCompat.RequireMethod(typeof(Container), nameof(Container.Load));

            private static bool Prefix(Container __instance, ref bool __result)
            {
                if (!(__instance is DrawerComponent drawer)) return true;
                __result = drawer.LoadView();
                return false;
            }
        }
    }
}
