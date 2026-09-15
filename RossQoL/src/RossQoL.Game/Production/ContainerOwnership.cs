using System.Runtime.CompilerServices;
using UnityEngine;

namespace RossQoL.Game.Production
{
    /// <summary>
    /// Whether this client may write a container now: it owns it, and the
    /// ownership has not changed for SettleSeconds.
    ///
    /// Why wait: an ownership change only moves the owner. The previous
    /// owner may still be writing the container, and the server can hand
    /// it to another player meanwhile. ZDOMan.RPC_ZDOData keeps the whole ZDO with the higher
    /// revision, so one of two overlapping writes is discarded, after its
    /// producer was already reduced: items lost. Waiting until the owner
    /// revision has held still removes the overlap.
    /// </summary>
    internal static class ContainerOwnership
    {
        internal const float SettleSeconds = 2f;

        private sealed class Seen
        {
            public ushort Revision;
            public float Since;
        }

        // Keyed by the instance, so an entry dies with its container. One
        // allocation per container on first sight, none after.
        private static readonly ConditionalWeakTable<Container, Seen> OwnerRevisions =
            new ConditionalWeakTable<Container, Seen>();

        /// <summary>
        /// Records the ZDO's current owner revision. A revision not seen
        /// before, including the first sight of a container, counts as
        /// changed now.
        /// </summary>
        public static void Observe(Container container, ZDO zdo)
        {
            ushort revision = zdo.OwnerRevision;
            if (OwnerRevisions.TryGetValue(container, out var seen))
            {
                if (seen.Revision != revision)
                {
                    seen.Revision = revision;
                    seen.Since = Time.time;
                }
                return;
            }

            OwnerRevisions.Add(container, new Seen { Revision = revision, Since = Time.time });
        }

        /// <summary>True when this client owns the container and its owner revision has held for SettleSeconds.</summary>
        public static bool IsSettled(Container container, ZNetView nview)
        {
            var zdo = nview.GetZDO();
            Observe(container, zdo);
            if (!nview.IsOwner()) return false;

            return OwnerRevisions.TryGetValue(container, out var seen)
                   && Time.time - seen.Since >= SettleSeconds;
        }
    }
}
