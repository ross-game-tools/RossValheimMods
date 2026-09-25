using RossQoL.Core.Items;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Recognises a creature RossQoL summoned -- a Dead Raiser skeleton or one
    /// of the Spirit Caller's creatures. Shared by Items/RecallSummons,
    /// Tames/NoSummonCommands and Portals/TamesFollow, which each have to pick
    /// our summons out of <c>Character.GetAllCharacters()</c>; one recogniser
    /// keeps them from drifting apart. The name rule itself (skeleton prefab,
    /// the <c>_spiritcaller</c> suffix, "(Clone)" stripping) lives in and is
    /// tested from <see cref="SummonKinds"/>.
    /// </summary>
    internal static class SummonedMinion
    {
        public static bool Is(Character character) =>
            character != null && Is(character.gameObject);

        /// <summary>
        /// The same test from the object, for callers holding a component other
        /// than the Character (a Tameable, say). Both components live on the one
        /// creature object, so this is the same question from a different handle.
        /// </summary>
        public static bool Is(UnityEngine.GameObject go) =>
            go != null && SummonKinds.IsSummon(go.name);
    }
}
