namespace RossQoL.Game.Items
{
    /// <summary>
    /// Recognises a skeleton raised by the Dead Raiser.
    ///
    /// Shared rather than duplicated: both Items/RecallSummons and
    /// Portals/TamesFollow have to pick summons out of
    /// <c>Character.GetAllCharacters()</c>, and two copies of the same
    /// prefab-name test would drift apart the moment one of them was fixed.
    /// </summary>
    internal static class SummonedSkeleton
    {
        /// <summary>
        /// The summoned skeleton's prefab name. Live instances carry Unity's
        /// "(Clone)" suffix, so that is stripped before comparing; the
        /// comparison itself is case-insensitive because nothing about the
        /// name is guaranteed to be typed consistently at runtime.
        /// </summary>
        private const string SkeletonPrefabName = "Skeleton_Friendly";

        private const string CloneSuffix = "(Clone)";

        public static bool Is(Character character)
        {
            if (character == null) return false;

            return Is(character.gameObject);
        }

        /// <summary>
        /// The same test taken straight from the object, for callers that hold
        /// a component other than the Character -- a Tameable, say. Both
        /// components live on the one creature object, so this is the same
        /// question asked from a different handle, not a second rule.
        /// </summary>
        public static bool Is(UnityEngine.GameObject go)
        {
            if (go == null) return false;

            string name = go.name;
            if (string.IsNullOrEmpty(name)) return false;

            if (name.EndsWith(CloneSuffix, System.StringComparison.Ordinal))
                name = name.Substring(0, name.Length - CloneSuffix.Length);

            return string.Equals(name, SkeletonPrefabName, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
