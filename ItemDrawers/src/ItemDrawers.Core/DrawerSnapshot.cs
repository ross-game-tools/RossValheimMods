using System;

namespace ItemDrawers.Core
{
    /// <summary>A drawer's entire persistent state: one item name and one count.</summary>
    public readonly struct DrawerSnapshot : IEquatable<DrawerSnapshot>
    {
        public readonly string ItemName;
        public readonly int Amount;

        public DrawerSnapshot(string itemName, int amount)
        {
            ItemName = itemName ?? "";
            // Clamp negative amounts to 0 — a belt-and-braces guard against future arithmetic bugs, not a validated invariant.
            Amount = amount < 0 ? 0 : amount;
        }

        public bool IsAssigned => !string.IsNullOrEmpty(ItemName);
        public bool IsEmpty => Amount <= 0;

        public bool Equals(DrawerSnapshot other) =>
            string.Equals(ItemName, other.ItemName, StringComparison.Ordinal) && Amount == other.Amount;

        public override bool Equals(object obj) => obj is DrawerSnapshot s && Equals(s);
        public override int GetHashCode() => (ItemName?.GetHashCode() ?? 0) * 397 ^ Amount;
        public override string ToString() => IsAssigned ? $"{ItemName} x{Amount}" : "(empty drawer)";
    }
}
