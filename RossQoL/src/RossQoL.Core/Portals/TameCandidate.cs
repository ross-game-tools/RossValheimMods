namespace RossQoL.Core.Portals
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

        /// <summary>
        /// A creature raised by a summoning staff rather than tamed by hand.
        ///
        /// Tracked separately because a summon cannot be trusted to report
        /// <see cref="IsTamed"/> correctly: on a dedicated server, a raised
        /// skeleton's tamed flag can read false for its entire life. Vanilla
        /// sets it from <c>Tameable.Awake</c> via <c>Character.SetTamed</c>,
        /// which only fires a routed RPC targeted at the ZDO's owner, and
        /// whose handler writes the flag solely under <c>m_nview.IsOwner()</c>
        /// -- so *why* that write fails is still being pinned down (one
        /// candidate is a same-machine Awake-order race between
        /// <c>ZNetView.Awake</c> registering the instance and
        /// <c>Tameable.Awake</c> firing the RPC; decompiled evidence traces
        /// ownership of a freshly spawned summon to the summoning client in
        /// both single player and on a server, which that race alone doesn't
        /// explain, since single-player testing has not reproduced the bug).
        /// What's confirmed either way: the flag cannot be relied on, so this
        /// exempts a summon from the tamed test rather than the tamed test
        /// itself being loosened. See docs/valheim-api/summons.md,
        /// "Correcting the diagnosis", for the full reasoning and what is
        /// still open.
        /// </summary>
        public bool IsSummon { get; }

        public TameCandidate(
            Vec3 position, bool isTamed, bool isFollowingPlayer, bool isBusy, bool isSummon = false)
        {
            Position = position;
            IsTamed = isTamed;
            IsFollowingPlayer = isFollowingPlayer;
            IsBusy = isBusy;
            IsSummon = isSummon;
        }
    }
}
