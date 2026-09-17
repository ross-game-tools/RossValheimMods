using System.Collections.Generic;
using UnityEngine;

namespace RossQoL.Game.Crafting
{
    /// <summary>
    /// What the nearby containers hold, counted once per frame.
    ///
    /// The requirement rows are rebuilt every frame, for every material, in
    /// both the crafting panel and the build HUD. Asking the containers
    /// directly each time would walk every chest within the craft radius
    /// several times a frame for an answer that cannot have changed between
    /// rows, so the boxes and the counts are worked out on the frame's first
    /// question and reused for the rest of it.
    ///
    /// Display only. Paying for a craft counts the containers afresh, because
    /// there the answer has to be true at the moment the items are taken.
    /// </summary>
    internal static class ChestDisplayCounts
    {
        private static int _frame = -1;

        // Our own copy: ChestCrafting.Near hands back a list it reuses, and
        // anything else asking it this frame would otherwise rewrite ours.
        private static readonly List<Container> Boxes = new List<Container>();

        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

        /// <summary>How many of an item the nearby containers hold, quality -1 for any.</summary>
        public static int InContainers(Vector3 origin, string sharedName, int quality)
        {
            Refresh(origin);

            string key = sharedName + "|" + quality;
            if (Counts.TryGetValue(key, out int cached)) return cached;

            int total = ChestCrafting.Count(Boxes, sharedName, quality);
            Counts[key] = total;
            return total;
        }

        private static void Refresh(Vector3 origin)
        {
            if (_frame == Time.frameCount) return;

            _frame = Time.frameCount;
            Counts.Clear();
            Boxes.Clear();
            Boxes.AddRange(ChestCrafting.Near(origin));
        }
    }
}
