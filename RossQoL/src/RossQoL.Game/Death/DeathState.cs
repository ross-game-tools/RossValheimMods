using System.Collections.Generic;
using RossQoL.Core.Death;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// The single owner of everything the Death category keeps per
    /// character: remembered graves and the one-shot "you just died" flag,
    /// both held in <see cref="Player.m_customData"/> so they survive a
    /// relog, plus the session-only selected-grave cursor, which does not.
    ///
    /// Every method is a no-op when there is no local player or no
    /// connected world, and none of them throw -- this is called straight
    /// from patch bodies during death and spawn, when both come and go.
    /// </summary>
    public static class DeathState
    {
        /// <summary>Custom data key holding every remembered grave, formatted by <see cref="GraveStore"/>.</summary>
        public static readonly string CustomDataKey = "rossqol.graves";

        /// <summary>Custom data key set by a death and consumed once by the first spawn or relog after it.</summary>
        public static readonly string DiedKey = "rossqol.died";

        /// <summary>
        /// Session-only cursor into <see cref="Graves"/>. Never saved: which
        /// grave is highlighted on screen resets with every session and
        /// every new death.
        /// </summary>
        private static int _selected;

        /// <summary>
        /// Explicit Unity-aware null checks throughout, not `?.`: a
        /// destroyed local player still passes a reference-null test --
        /// `Game._RequestRespawn()` saves the profile and destroys the
        /// player object before `SpawnPlayer` runs, so there is a real
        /// window with a destroyed-but-not-null local player -- and
        /// `UnityEngine.Object`'s overloaded `==` is the only thing that
        /// catches it.
        /// </summary>
        private static Dictionary<string, string> Data =>
            Player.m_localPlayer == null || ZNet.instance == null ? null : Player.m_localPlayer.m_customData;

        /// <summary>
        /// The world's persistent identity, used to key graves per world in
        /// the shared custom-data string. 0 when not connected.
        /// </summary>
        public static long WorldId => ZNet.instance == null ? 0L : ZNet.instance.GetWorldUID();

        /// <summary>
        /// Never cached: another death, or the player clearing a grave, can
        /// change the stored list between frames.
        /// </summary>
        public static IReadOnlyList<GraveRecord> Graves()
        {
            var data = Data;
            if (data == null) return System.Array.Empty<GraveRecord>();

            data.TryGetValue(CustomDataKey, out string stored);
            return GraveStore.ForWorld(stored, WorldId);
        }

        /// <summary>Remembers a new grave, capped per world and in total, and selects it.</summary>
        public static void Record(GraveRecord grave)
        {
            var data = Data;
            if (data == null) return;

            data.TryGetValue(CustomDataKey, out string stored);
            data[CustomDataKey] = GraveStore.Add(stored, grave, GraveStore.DefaultPerWorld, GraveStore.DefaultTotal);
            _selected = 0;
        }

        /// <summary>Stops tracking one grave, e.g. once it has been looted.</summary>
        public static void Forget(GraveRecord grave)
        {
            var data = Data;
            if (data == null) return;

            data.TryGetValue(CustomDataKey, out string stored);
            string updated = GraveStore.Remove(stored, grave.WorldId, grave.ZdoId);

            // Custom data is merged on load and never cleared, so an empty
            // string left behind here would outlive the character's last
            // grave forever. Remove the key instead of storing "".
            if (string.IsNullOrEmpty(updated)) data.Remove(CustomDataKey);
            else data[CustomDataKey] = updated;

            _selected = 0;
        }

        /// <summary>The grave currently marked on screen, clamped into range on every read.</summary>
        public static GraveRecord? Selected
        {
            get
            {
                var graves = Graves();
                if (graves.Count == 0) return null;

                // The two writes below are clamping, not selection: graves
                // come and go under the cursor, and this only drags it back
                // inside the list. Which grave the player chose is changed by
                // SelectNext/SelectNewest alone, never by reading this.
                if (_selected < 0) _selected = 0;
                if (_selected >= graves.Count) _selected = graves.Count - 1;
                return graves[_selected];
            }
        }

        /// <summary>Moves the marker to the next remembered grave, wrapping back to the first.</summary>
        public static void SelectNext()
        {
            int count = Graves().Count;
            if (count == 0) return;

            _selected = (_selected + 1) % count;
        }

        /// <summary>Points the marker at the most recent grave, e.g. right after a death.</summary>
        public static void SelectNewest() => _selected = 0;

        /// <summary>Sets the one-shot flag a death leaves for the next spawn or relog to consume.</summary>
        public static void MarkDied()
        {
            var data = Data;
            if (data == null) return;

            data[DiedKey] = "1";
        }

        /// <summary>
        /// Reads and clears the died flag in one step, so a respawn that
        /// follows the death directly and one that follows a relog both
        /// fire their handouts exactly once.
        /// </summary>
        public static bool ConsumeDiedFlag()
        {
            var data = Data;
            if (data == null) return false;

            bool died = data.ContainsKey(DiedKey);
            if (died) data.Remove(DiedKey);
            return died;
        }
    }
}
