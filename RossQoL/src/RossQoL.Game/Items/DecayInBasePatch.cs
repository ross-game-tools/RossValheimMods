using System;
using System.Reflection;
using HarmonyLib;
using RossQoL.Core.Items;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Postfix on ItemDrop.TimedDestruction, vanilla's own decay check.
    /// Vanilla short-circuits on IsInsideBase() -- everything else (age,
    /// a nearby player, tar, building debris) goes unevaluated once an item
    /// is inside a base, which is why base litter never decays at all. This
    /// only ever has work to do when vanilla's own call left the item alive
    /// AND it is inside a base; outside a base vanilla already destroyed it
    /// when every condition held, or correctly left it alone.
    ///
    /// A postfix, not a prefix: vanilla's method is a complete, self-
    /// contained check with a single destructive side effect (m_nview
    /// .Destroy()) and no return value to preserve, so there is nothing to
    /// intercept before it runs -- only a case to add after it returns.
    ///
    /// GetTimeSinceSpawned and IsInsideBase are private in vanilla and
    /// called by reflection; InTar and IsPiece are public and called
    /// directly. Do not patch IsInsideBase itself -- it is a two-line
    /// method and Mono inlines those, which would make the patch appear to
    /// apply while every call site kept using the original body.
    /// </summary>
    [HarmonyPatch(typeof(ItemDrop), "TimedDestruction")]
    internal static class DecayInBasePatch
    {
        private static MethodInfo s_getTimeSinceSpawned;
        private static MethodInfo s_isInsideBase;
        private static FieldInfo s_nview;

        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(ItemDrop), "TimedDestruction", DecayInBaseFeature.FeatureName)
            & ValheimCompat.RequireMethod(typeof(ItemDrop), "GetTimeSinceSpawned", DecayInBaseFeature.FeatureName)
            & ValheimCompat.RequireMethod(typeof(ItemDrop), "IsInsideBase", DecayInBaseFeature.FeatureName);

        private static void Postfix(ItemDrop __instance)
        {
            if (DecayInBaseFeature.Instance?.IsActive != true) return;
            if (__instance == null) return;

            // An exception escaping here would break the item's own slow
            // update loop, not just this feature.
            try
            {
                Decay(__instance);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"DecayInBase: could not check {__instance.name}: {ex}");
            }
        }

        private static void Decay(ItemDrop item)
        {
            // Items that never decay at all (m_autoDestroy = false) are
            // vanilla's own exemption and stay exempt here too.
            if (!item.m_autoDestroy) return;

            var nview = NView(item);
            // Vanilla's own call above already destroyed the item -- and
            // its ZNetView with it -- whenever every ordinary condition
            // held outside a base. Nothing is left to check in that case.
            if (nview == null || !nview.IsValid()) return;

            // Outside a base vanilla's own check just ran this same
            // decision; asking again here would be redundant, not wrong,
            // but the base exemption is the only case this feature adds.
            if (!IsInsideBase(item)) return;

            bool playerNearby = Player.IsPlayerInRange(item.transform.position, ItemDecayRule.PlayerRangeMetres);
            double age = GetTimeSinceSpawned(item);

            bool destroy = ItemDecayRule.ShouldDestroy(
                age, playerNearby, item.InTar(), item.IsPiece(), ignoreBaseExemption: true);

            if (destroy) nview.Destroy();
        }

        private static double GetTimeSinceSpawned(ItemDrop item)
        {
            s_getTimeSinceSpawned = s_getTimeSinceSpawned ?? AccessTools.Method(typeof(ItemDrop), "GetTimeSinceSpawned");
            if (s_getTimeSinceSpawned == null)
                throw new InvalidOperationException("ItemDrop.GetTimeSinceSpawned not found");

            return (double)s_getTimeSinceSpawned.Invoke(item, null);
        }

        private static bool IsInsideBase(ItemDrop item)
        {
            s_isInsideBase = s_isInsideBase ?? AccessTools.Method(typeof(ItemDrop), "IsInsideBase");
            if (s_isInsideBase == null)
                throw new InvalidOperationException("ItemDrop.IsInsideBase not found");

            return (bool)s_isInsideBase.Invoke(item, null);
        }

        private static ZNetView NView(ItemDrop item)
        {
            s_nview = s_nview ?? AccessTools.Field(typeof(ItemDrop), "m_nview");
            if (s_nview == null) throw new InvalidOperationException("ItemDrop.m_nview not found");

            return (ZNetView)s_nview.GetValue(item);
        }
    }
}
