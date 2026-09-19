using System.Collections.Generic;

namespace RossQoL.Core.Death
{
    /// <summary>
    /// Which tombstones have actually been seen this world-session.
    ///
    /// A grave is only ever forgotten for being looted once its ZDO has been
    /// observed present at least once, because an unloaded zone and an emptied
    /// grave look identical from the outside. That sighting must not outlive
    /// the world it was made in: rejoining the same world in the same process
    /// starts with nothing streamed in, so a remembered sighting from last time
    /// would let a not-yet-arrived ZDO be read as looted and delete a record of
    /// where the player's gear is.
    ///
    /// Sightings are therefore held against the world they were made in and
    /// dropped the moment the world changes -- including the change to "no
    /// world at all" on the way back to the main menu, which is what makes
    /// leaving and rejoining the same world re-arm the rule. Graves are already
    /// per world, so a tombstone's ZDO id alone identifies it within one.
    /// </summary>
    public sealed class GraveSightings
    {
        private readonly HashSet<uint> _seen = new HashSet<uint>();

        /// <summary>The world these sightings were made in; 0 is "no world", e.g. the main menu.</summary>
        private long _worldId;

        /// <summary>How many sightings are being held; for tests and diagnostics.</summary>
        public int Count => _seen.Count;

        /// <summary>
        /// Declares which world is current, dropping every sighting if that is
        /// not the world they were made in. Returns true when the world
        /// changed, so a caller can also hold off for a moment while the new
        /// world's zones stream in.
        /// </summary>
        public bool EnterWorld(long worldId)
        {
            if (worldId == _worldId) return false;

            _worldId = worldId;
            _seen.Clear();
            return true;
        }

        /// <summary>Records that this tombstone's ZDO was present just now.</summary>
        public void Observe(uint zdoId) => _seen.Add(zdoId);

        /// <summary>True when this tombstone has been seen present earlier in this world-session.</summary>
        public bool HasSeen(uint zdoId) => _seen.Contains(zdoId);
    }
}
