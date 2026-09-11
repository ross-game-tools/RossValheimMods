using System.Collections.Generic;
using UnityEngine;

namespace ItemDrawers.Game
{
    /// <summary>
    /// Deliberate integration point for other mods, so a future integration
    /// need not infer behaviour from the Container surface the way
    /// OttoFuel and NoVikingLeftBehind do today. This is what we would have
    /// wanted from the original ItemDrawers.
    /// </summary>
    public static class ItemDrawersAPI
    {
        public static IReadOnlyList<DrawerComponent> AllDrawers => DrawerComponent.All;

        /// <summary>Total amount of <paramref name="itemName"/> (a prefab name) held in drawers within <paramref name="radius"/> of <paramref name="near"/>.</summary>
        public static int CountItem(Vector3 near, float radius, string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || DrawerManager.Instance == null) return 0;

            var found = new List<DrawerComponent>();
            DrawerManager.Instance.QueryNear(near, radius, found);

            int total = 0;
            foreach (var drawer in found)
            {
                var s = drawer.Snapshot;
                if (s.ItemName == itemName) total += s.Amount;
            }
            return total;
        }

        /// <summary>
        /// Takes up to <paramref name="amount"/> of <paramref name="itemName"/>
        /// (a prefab name) from drawers within <paramref name="radius"/> of
        /// <paramref name="near"/>, across as many drawers as it takes.
        /// Returns how much was actually taken -- may be less than
        /// requested, never more.
        /// </summary>
        public static int Withdraw(Vector3 near, float radius, string itemName, int amount)
        {
            if (string.IsNullOrEmpty(itemName) || amount <= 0 || DrawerManager.Instance == null) return 0;

            var found = new List<DrawerComponent>();
            DrawerManager.Instance.QueryNear(near, radius, found);

            int taken = 0;
            foreach (var drawer in found)
            {
                if (taken >= amount) break;
                if (drawer.Snapshot.ItemName != itemName) continue;

                if (drawer.TryWithdrawExternally(amount - taken, out int got)) taken += got;
            }
            return taken;
        }
    }
}
