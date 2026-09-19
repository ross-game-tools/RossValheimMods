using RossQoL.Core.Death;
using UnityEngine;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// The one place that turns a remembered grave and the player's position
    /// into the point everything aims at. Shared by the on-screen marker and
    /// the corpse run buff so the arrow and the stamina both answer to the
    /// same target: a marker pointing at a crypt's door while the buff still
    /// scaled on five kilometres of sky would be worse than either alone.
    ///
    /// The rule itself lives in <see cref="GraveAim"/>, in Core, where it is
    /// tested without the game.
    /// </summary>
    internal static class GraveAimPoint
    {
        internal static Vector3 For(GraveRecord grave, Vector3 playerAt) =>
            GraveAim.Choose(grave.Y, grave.HasEntrance, playerAt.y) == GraveAimTarget.Entrance
                ? new Vector3(grave.EntranceX, grave.EntranceY, grave.EntranceZ)
                : new Vector3(grave.X, grave.Y, grave.Z);
    }
}
