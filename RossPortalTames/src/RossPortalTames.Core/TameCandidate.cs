namespace RossPortalTames.Core
{
    /// <summary>
    /// Everything Core needs to decide whether one creature comes along.
    ///
    /// Deliberately flags rather than engine objects: the Game layer has
    /// already asked Valheim whether the creature is tamed, who it is
    /// following, and whether it is ridden, so those questions are answered
    /// once, at the boundary, instead of being re-asked inside rules that
    /// then could not be tested.
    /// </summary>
    public readonly struct TameCandidate
    {
        public Vec3 Position { get; }
        public bool IsTamed { get; }

        /// <summary>Following THIS player specifically, not merely following something.</summary>
        public bool IsFollowingPlayer { get; }

        /// <summary>Ridden, saddled-and-mounted, or otherwise attached.</summary>
        public bool IsBusy { get; }

        public TameCandidate(Vec3 position, bool isTamed, bool isFollowingPlayer, bool isBusy)
        {
            Position = position;
            IsTamed = isTamed;
            IsFollowingPlayer = isFollowingPlayer;
            IsBusy = isBusy;
        }
    }
}
