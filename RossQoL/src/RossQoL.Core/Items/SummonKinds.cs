using System;

namespace RossQoL.Core.Items
{
    /// <summary>
    /// Which creatures RossQoL treats as its own summons -- recalled by the
    /// recall staffs and barred from petting so they keep counting against the
    /// vanilla summon cap -- and which staffs get the recall secondary attack.
    /// Pure name rules (no game types), so the Dead Raiser and the Spirit
    /// Caller are recognised from one tested place rather than several prefab
    /// constants drifting apart.
    ///
    /// Live creature instances carry Unity's "(Clone)" suffix, stripped before
    /// comparing. The Spirit Caller summons several creatures whose prefabs all
    /// end in "_spiritcaller" (Bjorn/Moose/Wolf/Boar, and any added later), so
    /// they are matched by that suffix rather than an exhaustive list.
    /// </summary>
    public static class SummonKinds
    {
        /// <summary>The Dead Raiser's summoned skeleton prefab.</summary>
        public const string SkeletonPrefab = "Skeleton_Friendly";

        /// <summary>Every Spirit Caller creature prefab ends with this.</summary>
        public const string SpiritCallerSuffix = "_spiritcaller";

        public const string DeadRaiserStaff = "StaffSkeleton";
        public const string SpiritCallerStaff = "StaffSpiritCaller";

        private const string CloneSuffix = "(Clone)";

        /// <summary>A creature RossQoL summoned: a Dead Raiser skeleton or a Spirit Caller creature.</summary>
        public static bool IsSummon(string prefabName)
        {
            string name = Strip(prefabName);
            if (name.Length == 0) return false;

            return name.Equals(SkeletonPrefab, StringComparison.OrdinalIgnoreCase)
                || (name.Length > SpiritCallerSuffix.Length
                    && name.EndsWith(SpiritCallerSuffix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A staff whose blank secondary attack RossQoL fills with the recall.</summary>
        public static bool IsRecallStaff(string prefabName)
        {
            string name = Strip(prefabName);
            return name.Equals(DeadRaiserStaff, StringComparison.OrdinalIgnoreCase)
                || name.Equals(SpiritCallerStaff, StringComparison.OrdinalIgnoreCase);
        }

        private static string Strip(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.EndsWith(CloneSuffix, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - CloneSuffix.Length)
                : name;
        }
    }
}
