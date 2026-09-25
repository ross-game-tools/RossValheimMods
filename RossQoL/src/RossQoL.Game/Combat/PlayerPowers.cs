using RossQoL.Core.Combat;

namespace RossQoL.Game.Combat
{
    /// <summary>
    /// The local player's second guardian-power slot, bridged to the character
    /// save.
    ///
    /// State lives in <see cref="Player.m_customData"/> under one
    /// <c>rossqol.</c>-namespaced key, which vanilla's own <see cref="Player.Save"/>
    /// writes out with the character -- so persistence is free and needs no
    /// save patch, no ZDO and no RPC. Slot 1 (Valheim's single selected power)
    /// is never touched here.
    ///
    /// Load-through: the slot is resolved once from <c>m_customData</c> and kept
    /// for the session, re-parsed only when <see cref="Player.m_localPlayer"/>
    /// changes (a relog or a new character), so a stale holder is never handed
    /// to a different player.
    /// </summary>
    internal static class PlayerPowers
    {
        public const string SecondPowerKey = "rossqol.powers.second";

        private static Player _owner;
        private static SecondPower _slot;

        /// <summary>
        /// The local player's second-power slot, parsed from the save on first
        /// use and whenever the local player changes. A name that no longer
        /// resolves to a StatusEffect in the ObjectDB is dropped, so a
        /// hand-edited or removed-mod power leaves no ghost slot behind.
        /// </summary>
        public static SecondPower ForLocalPlayer()
        {
            var player = Player.m_localPlayer;
            if (player == null) return _slot ?? (_slot = new SecondPower());
            if (!ReferenceEquals(player, _owner) || _slot == null)
            {
                _owner = player;
                _slot = Parse(player);
            }
            return _slot;
        }

        /// <summary>
        /// Write the slot (power + live cooldown) back into the player's
        /// <c>m_customData</c>. Vanilla's <see cref="Player.Save"/> persists the
        /// dictionary, so this is all the durability the slot needs.
        /// </summary>
        public static void Persist(Player player, SecondPower slot)
        {
            if (player == null || slot == null) return;
            player.m_customData[SecondPowerKey] = slot.Serialize();
        }

        private static SecondPower Parse(Player player)
        {
            player.m_customData.TryGetValue(SecondPowerKey, out string data);
            return SecondPower.Parse(data, IsKnownPower);
        }

        /// <summary>
        /// A power name is valid only while the ObjectDB still resolves it to a
        /// StatusEffect -- the same check vanilla uses when it grants a power.
        /// </summary>
        private static bool IsKnownPower(string name) =>
            ObjectDB.instance != null
            && ObjectDB.instance.GetStatusEffect(name.GetStableHashCode()) != null;
    }
}
