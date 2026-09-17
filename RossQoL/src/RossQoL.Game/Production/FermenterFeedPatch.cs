using System;
using HarmonyLib;
using RossQoL.Game.Framework;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// An empty fermenter takes a mead base from a nearby container and
    /// starts it, which with AutoHarvest emptying the finished batch makes a
    /// fermenter run on its own for as long as the bases last.
    ///
    /// Only an empty one: a fermenting or ready batch is vanilla's business,
    /// and its content is read from the ZDO rather than from the private
    /// status enum, the same way harvesting reads it.
    /// </summary>
    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.SlowUpdate))]
    internal static class FermenterFeedPatch
    {
        private static bool Prepare() =>
            ValheimCompat.RequireMethod(typeof(Fermenter), nameof(Fermenter.SlowUpdate), AutoFeedFeature.FeatureName);

        private static void Postfix(Fermenter __instance)
        {
            if (AutoFeedFeature.Instance?.IsActive != true) return;

            // An exception escaping here would stop this fermenter's update.
            try
            {
                if (!FeedGate.ShouldRun(__instance, __instance.m_nview, AutoFeedFeature.Instance, AutoFeedConfig.FeedFermenters)) return;

                Feed(__instance);
                FeedGate.Succeeded(__instance);
            }
            catch (Exception ex)
            {
                FeedGate.LogFailure(__instance, ex);
            }
        }

        private static void Feed(Fermenter fermenter)
        {
            var zdo = fermenter.m_nview.GetZDO();

            // Empty is content 0, as vanilla's own status check reads it.
            if (zdo.GetInt(ZDOVars.s_content) != 0) return;

            var origin = fermenter.transform.position;
            foreach (var conversion in fermenter.m_conversion)
            {
                if (conversion?.m_from == null) continue;

                int taken = ContainerSource.Take(origin, conversion.m_from, 1, out bool cheated);
                if (taken <= 0) continue;

                // As vanilla's AddItem: the prefab name's hash is the content.
                fermenter.m_nview.InvokeRPC(
                    "RPC_AddItem", conversion.m_from.gameObject.name.GetStableHashCode(), cheated);
                return;
            }
        }
    }
}
