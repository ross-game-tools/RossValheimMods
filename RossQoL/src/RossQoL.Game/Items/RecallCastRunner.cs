using System;
using RossQoL.Core.Items;
using UnityEngine;

namespace RossQoL.Game.Items
{
    /// <summary>
    /// Carries one pending recall from the middle-click that started it to
    /// the moment its cast time elapses and the skeletons actually move.
    ///
    /// The animation, the cast sound and the cooldown all happen immediately
    /// on the middle-click, in <see cref="RecallSummonsAttackPatch"/> --
    /// only the move itself waits. That split is deliberate: a cast that
    /// could be aborted by taking damage would mostly read as "the button
    /// didn't work" for something this short, so once started it always
    /// completes. What it must NOT do is fire into a world the player has
    /// already left, which is what <see cref="StillValid"/> guards against
    /// every frame it is pending.
    ///
    /// One instance lives on the mod's shared host object (see
    /// <see cref="RecallSummonsFeature.OnActivated"/>), the same pattern
    /// <c>Portals/PortalTamesManager</c> uses for its own pending state.
    /// </summary>
    internal sealed class RecallCastRunner : MonoBehaviour
    {
        public static RecallCastRunner Instance { get; private set; }

        private bool _pending;
        private float _startedAt;
        private float _castSeconds;

        /// <summary>
        /// Whether a cast is already in flight. Checked by
        /// <see cref="RecallSummonsAttackPatch"/> before starting a new one --
        /// casts are never queued, so a middle-click while this is true does
        /// nothing at all.
        /// </summary>
        public bool IsPending => _pending;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _pending = false;
        }

        /// <summary>
        /// Starts the cast timer. The animation, sound and cooldown have
        /// already happened by the time this is called -- this only decides
        /// when the skeletons move.
        /// </summary>
        public void Begin(float castSeconds)
        {
            _pending = true;
            _startedAt = Time.time;
            _castSeconds = castSeconds;
        }

        private void Update()
        {
            if (!_pending) return;

            // An exception escaping here would leave this component
            // permanently stuck thinking a cast is pending, silently
            // blocking every future recall -- clear the flag before
            // logging, not after.
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                _pending = false;
                RossQoLPlugin.Log.LogError($"RecallSummons: cancelling a pending recall after an error: {ex}");
            }
        }

        private void Tick()
        {
            var player = Player.m_localPlayer;
            if (!StillValid(player))
            {
                // Silent by design -- see the class summary. The world this
                // cast was aimed at is gone; there is nothing left to log
                // that would help anyone.
                _pending = false;
                return;
            }

            if (!RecallCast.IsComplete(_startedAt, Time.time, _castSeconds)) return;

            _pending = false;
            RecallSummonsAttackPatch.FinishRecall(player);
        }

        /// <summary>
        /// Whether the world this cast was aimed at still exists.
        ///
        /// Four checks, each for a specific way the cast could fire into
        /// nothing:
        /// <list type="bullet">
        /// <item><description><c>player == null</c> -- covers both death-
        /// into-a-loading-screen and an already-completed logout; Valheim
        /// tears down <see cref="Player.m_localPlayer"/> on both paths.</description></item>
        /// <item><description><see cref="Character.IsDead"/> -- a death that
        /// hasn't yet cleared <c>m_localPlayer</c>. Recalling skeletons to a
        /// corpse is not a "the button worked" moment.</description></item>
        /// <item><description><see cref="Player.IsTeleporting"/> -- the
        /// player's own transform reads as the DESTINATION for the whole of
        /// a teleport (see docs/valheim-api/summons.md's teleport section);
        /// moving skeletons mid-hop would place them at a spot the player is
        /// mid-flight away from, not where they land.</description></item>
        /// <item><description><see cref="Game.IsShuttingDown"/> -- covers
        /// logout. <c>Game.Logout</c> saves and tears the world down
        /// synchronously; nothing on <see cref="Player"/> itself marks "I am
        /// logging out", so this is the only signal available.</description></item>
        /// </list>
        /// The staff being unequipped is checked too, even though it is not
        /// a "world gone" case -- switching weapons mid-cast means the
        /// player asked for something else, and firing the recall anyway
        /// would look like the game ignored that.
        /// </summary>
        private static bool StillValid(Player player)
        {
            if (player == null) return false;
            if (player.IsDead()) return false;
            if (player.IsTeleporting()) return false;

            // global:: because this file's own namespace (RossQoL.Game) would
            // otherwise shadow the game's Game class -- the same collision
            // GraveMarkerDisplay documents for System.Console vs the game's
            // own Console.
            var game = global::Game.instance;
            if (game == null || game.IsShuttingDown()) return false;

            var weapon = player.GetCurrentWeapon();
            if (weapon == null || weapon.m_dropPrefab == null) return false;
            if (!SummonKinds.IsRecallStaff(weapon.m_dropPrefab.name)) return false;

            return true;
        }
    }
}
