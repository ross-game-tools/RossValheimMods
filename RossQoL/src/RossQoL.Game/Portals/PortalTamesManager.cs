using System.Collections.Generic;
using RossQoL.Core.Portals;
using RossQoL.Game.Items;
using UnityEngine;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// The only ticking object in this mod. Holds the pending capture and
    /// performs the arrival placement, for every teleport alike -- a portal or
    /// a dungeon door, in either direction. See TeleportPatch.
    /// </summary>
    internal class PortalTamesManager : MonoBehaviour
    {
        public static PortalTamesManager Instance { get; private set; }

        /// <summary>
        /// How long a capture stays valid. A teleport that includes loading a
        /// distant zone can take a while, so this is generous -- but not
        /// unbounded, because an arrival that never registers must not move
        /// creatures to a stale destination minutes later.
        /// </summary>
        private const float PendingExpirySeconds = 30f;

        private PendingArrival _pending;

        /// <summary>
        /// Tracks the teleporting-&gt;not-teleporting transition in Update.
        /// It is the transition that matters, not the state: acting while
        /// still teleporting would place tames at a position the player is
        /// about to leave.
        /// </summary>
        private bool _wasTeleporting;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // The last way out. Every other release happens in Update, which
            // stops running the moment this component goes away -- leaving the
            // world mid-hop, or switching the feature off, would otherwise
            // strand ZDOIDs in a static set that outlives the world they came
            // from. A stranded id would exempt whatever creature later matched
            // it from vanilla's unsummon rules forever, which is a worse bug
            // than the one the guard fixes.
            _pending = null;
            _wasTeleporting = false;
            SummonUnsummonGuard.ReleaseAll();
        }

        /// <summary>
        /// Records which tames are coming, and takes ownership of each while
        /// they are still loaded and there is still a live ZNetView to claim
        /// through.
        /// </summary>
        public void CaptureDeparture()
        {
            // Re-entrancy guard. Without this, a second capture that finds
            // nothing eligible falls through to `_pending = null` below and
            // silently discards a good capture already in flight.
            if (_pending != null) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            var characters = Character.GetAllCharacters();
            if (characters == null || characters.Count == 0) return;

            var candidates = new List<TameCandidate>(characters.Count);
            var views = new List<ZNetView>(characters.Count);

            foreach (var character in characters)
            {
                if (character == null) continue;

                var view = character.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;

                // Tameable.Command is an RPC routed to the tame's owner, so
                // only the owner's copy of the MonsterAI has a follow target.
                // A tame another client owns (usually whoever tamed it, or is
                // nearest) follows this player only as far as the ZDO's saved
                // follow name, which the owner writes and every client has.
                var ai = character.GetComponent<MonsterAI>();
                string savedFollow = view.GetZDO().GetString(ZDOVars.s_follow);
                bool followingMe = ai != null
                    && (ai.GetFollowTarget() == player.gameObject
                        || savedFollow == player.GetPlayerName());

                candidates.Add(new TameCandidate(
                    position: ToVec3(character.transform.position),
                    isTamed: character.IsTamed(),
                    isFollowingPlayer: followingMe,
                    isBusy: character.IsAttached() || IsMounted(view),

                    // A raised skeleton cannot be trusted to report itself as
                    // tamed -- vanilla can drop that write entirely -- so it is
                    // recognised by prefab instead. See TameCandidate.IsSummon.
                    isSummon: SummonedSkeleton.Is(character)));
                views.Add(view);
            }

            var chosen = TameEligibility.SelectIndices(
                candidates, ToVec3(player.transform.position), PortalTamesConfig.FollowRadius.Value);

            if (chosen.Count == 0)
            {
                _pending = null;
                SummonUnsummonGuard.ReleaseAll();
                return;
            }

            var ids = new List<ZDOID>(chosen.Count);
            foreach (int index in chosen)
            {
                var view = views[index];

                // Claim now, while the object is loaded. This is only a head
                // start: ownership is re-asserted at arrival because
                // ZDOMan.ReleaseNearbyZDOS can take it back while the player is
                // far away in transit. See TameMover.
                if (!view.IsOwner()) view.ClaimOwnership();

                ids.Add(view.GetZDO().m_uid);
            }

            _pending = new PendingArrival(ids, Time.time);
            _wasTeleporting = true;

            // Hold vanilla's unsummon rules off these creatures for the
            // duration of the hop. A portal jump is further than a summon's
            // m_unsummonDistance allows, and a dungeon door is further still --
            // an interior sits 5000 m above its entrance, against an unsummon
            // distance of 150 -- and the ClaimOwnership above is what
            // makes THIS client the one that runs the check -- so without this
            // the capture itself guarantees the summon's destruction. Released
            // on every path below that ends the capture. See
            // SummonUnsummonGuard.
            SummonUnsummonGuard.Guard(ids);

            RossQoLPlugin.Log.LogInfo($"TamesFollow: bringing {ids.Count} tame(s).");
        }

        private void Update()
        {
            if (_pending == null) return;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                // Logged out, or died into a loading screen. Drop the capture:
                // nothing has been moved, and the tames are untouched.
                _pending = null;
                _wasTeleporting = false;
                SummonUnsummonGuard.ReleaseAll();
                return;
            }

            if (Time.time - _pending.CapturedAt > PendingExpirySeconds)
            {
                RossQoLPlugin.Log.LogWarning(
                    $"TamesFollow: arrival never registered within {PendingExpirySeconds:F0}s; "
                    + $"leaving {_pending.Tames.Count} tame(s) where they are.");
                _pending = null;
                _wasTeleporting = false;

                // Vanilla's own rules resume here. A guarded summon that never
                // arrived is now unsummoned where it stands, which is what
                // would have happened without this mod -- just 30s later.
                SummonUnsummonGuard.ReleaseAll();
                return;
            }

            bool teleporting = player.IsTeleporting();

            // The transition matters, not the state: acting while still
            // teleporting would place tames at a position the player is about
            // to leave.
            if (_wasTeleporting && !teleporting)
            {
                PlaceArrivals(player);
                _pending = null;

                // The creatures are beside the player again, so the distance
                // rule this guard was holding off no longer wants to fire.
                SummonUnsummonGuard.ReleaseAll();
            }

            _wasTeleporting = teleporting;
        }

        private void PlaceArrivals(Player player)
        {
            var arrival = player.transform.position;
            var facing = player.transform.forward;

            var spots = ArrivalPlacement.Compute(
                ToVec3(arrival),
                ToVec3(facing),
                _pending.Tames.Count,
                PortalTamesConfig.SearchDistance.Value,
                IsFree);

            int moved = 0;
            for (int i = 0; i < _pending.Tames.Count; i++)
            {
                var target = ToVector3(spots[i]);

                // The fallback placement IS the player's own arrival
                // position -- a spot a body is already standing on, and
                // therefore known-good. Snapping it can only make it worse
                // (e.g. finding a solid a couple of metres below a raised
                // floor). Only searched candidates need their Y corrected.
                if (!spots[i].Equals(ToVec3(arrival)))
                    target.y = GroundHeight(target);

                var outcome = TameMover.Move(_pending.Tames[i], target);
                if (TameMoveOutcomes.Arrived(outcome)) moved++;
                else
                    RossQoLPlugin.Log.LogWarning(
                        $"TamesFollow: tame {_pending.Tames[i]} did not arrive ({outcome}).");
            }

            RossQoLPlugin.Log.LogInfo($"TamesFollow: {moved} of {_pending.Tames.Count} tame(s) arrived.");
        }

        /// <summary>
        /// Whether a creature is currently ridden.
        ///
        /// <see cref="Character.IsRiding"/> is virtual and returns false on
        /// the base class; the only override is on <c>Player</c>, where it
        /// reports whether the PLAYER is mounted, not whether the creature
        /// under them has a rider. The mount side instead has its rider
        /// written into its OWN ZDO: <c>Sadle.RPC_RequestControl</c> calls
        /// <c>m_nview.GetZDO().Set(ZDOVars.s_user, playerID)</c> on the
        /// creature's ZDO (<c>Sadle.Awake</c> resolves <c>m_nview</c> via
        /// <c>GetComponentInParent&lt;Character&gt;().GetComponent&lt;ZNetView&gt;()</c>,
        /// i.e. the creature's own view, not a separate saddle object), and
        /// clears it back to 0 on <c>RPC_ReleaseControl</c>/dismount. A
        /// non-zero <c>ZDOVars.s_user</c> is therefore exactly "someone is
        /// riding this creature right now" -- verified by decompiling
        /// <c>Sadle</c> and <c>ZDOVars</c> against assembly_valheim.dll.
        /// </summary>
        private static bool IsMounted(ZNetView view)
        {
            var zdo = view.GetZDO();
            return zdo != null && zdo.GetLong(ZDOVars.s_user, 0L) != 0L;
        }

        /// <summary>
        /// Whether a creature could stand at this point. Backed by the real
        /// world here; the tests supply their own predicate, which is what
        /// makes the wall case assertable without a running game.
        ///
        /// Internal rather than private: RecallSummonsManager places arriving
        /// skeletons with the exact same world query rather than a second
        /// copy of it.
        /// </summary>
        internal static bool IsFree(Vec3 point)
        {
            var zones = ZoneSystem.instance;
            if (zones == null) return true;

            var world = ToVector3(point);

            // Outdoors the block check is still worth asking -- it is what
            // keeps a creature out of a boulder or a building -- but inside an
            // instanced interior it answers "blocked" for everywhere, so it is
            // skipped there. Nothing about correctness rests on getting that
            // call right any more: if InInterior answers wrongly, IsBlocked
            // merely rejects candidates, and a rejected candidate falls back
            // to the player's own position, which is known-good. See
            // StandingRoom.
            if (!Character.InInterior(world) && zones.IsBlocked(world)) return false;

            return StandingRoom(zones, world);
        }

        /// <summary>
        /// The real test: is there floor here, at about the height the player
        /// is standing at, and would the game leave a creature standing on it
        /// alone? Asked for every candidate, indoors and out.
        ///
        /// It began as the replacement for <c>ZoneSystem.IsBlocked</c> inside
        /// an interior, because that call is meaningless there.
        ///
        /// <c>IsBlocked</c> is <c>p.y += 2000f; Physics.Raycast(p, down,
        /// 10000f, m_blockRayMask)</c> -- a ten-kilometre column starting two
        /// kilometres overhead, over "Default", "static_solid",
        /// "Default_small" and "piece" (decompiled 1.0.15,
        /// <c>ZoneSystem.cs:2728</c>). A dungeon interior is instantiated at
        /// its zone's centre plus 5000 on Y (<c>Location.cs:60</c>), so for a
        /// point inside one that column runs from about y=7000 down to about
        /// y=-3000: it contains the interior's own ceiling and the entire
        /// overworld underneath. Every candidate therefore reports blocked,
        /// <c>ArrivalPlacement</c> falls through to its fallback for all of
        /// them, and every creature is put down on the player's exact
        /// position -- a pile of overlapping rigidbodies, which is how one
        /// gets squeezed through the dungeon shell. Once outside it, vanilla's
        /// own <c>Character.UnderWorldCheck</c> is no help: it compares
        /// against <c>ZoneSystem.GetGroundHeight</c>, which probes the terrain
        /// layer from a fixed y=6000 and so returns the OVERWORLD height for
        /// an interior position. A creature falling out of a dungeon is only
        /// "rescued" once it is below the overworld terrain -- five kilometres
        /// from its owner, which is far past <c>Tameable.m_unsummonDistance</c>,
        /// so <c>Tameable.UpdateSummon</c> destroys it
        /// (<c>Tameable.cs:629-637</c>).
        ///
        /// Asking whether there is floor here at about the height the player
        /// is standing at answers the real question at any Y. A candidate's Y
        /// is the player's own height (Core leaves it alone; see
        /// <c>ArrivalPlacement.Rotate</c>), and the probe starts only
        /// <see cref="GroundSnapMarginMetres"/> above that, so the top of a
        /// wall reads as roughly that margin above the player and is rejected,
        /// a pit reads far below and is rejected, and a probe that finds
        /// nothing at all is rejected too -- which is the fail-safe: the
        /// fallback is the player's own position, a spot a body is already
        /// standing on.
        ///
        /// The outdoor question is no safer than the interior one, which is
        /// why this now runs everywhere. <c>m_blockRayMask</c> excludes
        /// "terrain" (<c>ZoneSystem.cs:677</c>), so a candidate buried inside
        /// a hillside or hanging over a drop is not "blocked" at all, and the
        /// height correction that follows can only search DOWNWARD
        /// (<c>GetSolidHeight</c> raycasts 2000 m down from
        /// <c>p.y + heightMargin</c>, <c>ZoneSystem.cs:2768</c>) -- so a
        /// candidate accepted on the old rule could be put down far below the
        /// player, or left at the player's height inside solid rock. Either
        /// way <c>Character.UnderWorldCheck</c> then lifts it to the terrain
        /// surface at that x/z within five seconds: part-way down an entrance
        /// shaft, that is the ground outside the entrance. Hence the second
        /// half of <see cref="PlacementFooting.IsAcceptable"/>.
        /// </summary>
        private static bool StandingRoom(ZoneSystem zones, Vector3 point)
        {
            bool floorFound = zones.GetSolidHeight(point, out float floor, GroundSnapMarginMetres);
            bool terrainFound = zones.GetGroundHeight(point, out float terrain);

            return PlacementFooting.IsAcceptable(
                floorFound, floor, point.y, PlacementFooting.SameFloorToleranceMetres,
                terrainFound, terrain, PlayerIsUnderTerrain(zones),
                PlacementFooting.UnderTerrainMarginMetres);
        }

        /// <summary>
        /// Whether the player themselves is below the terrain surface by
        /// vanilla's own margin -- an instanced interior five kilometres up
        /// never is, a cave or a dungeon built into the world at ordinary
        /// altitude is. When they are, the terrain sample says nothing useful
        /// about anywhere near them, so the under-terrain half of the rule is
        /// stood down rather than rejecting every candidate.
        ///
        /// No local player is treated as "not under terrain", i.e. the strict
        /// reading: a candidate must then survive the terrain test on its own.
        /// </summary>
        private static bool PlayerIsUnderTerrain(ZoneSystem zones)
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;

            var at = player.transform.position;
            bool terrainFound = zones.GetGroundHeight(at, out float terrain);

            return PlacementFooting.IsUnderTerrain(
                at.y, terrainFound, terrain, PlacementFooting.UnderTerrainMarginMetres);
        }

        /// <summary>
        /// Snaps a candidate's Y to the solid directly below it.
        ///
        /// <c>ZoneSystem.GetSolidHeight(Vector3, out float)</c> defaults its
        /// <c>heightMargin</c> parameter to 1000, which raycasts DOWNWARD
        /// from <c>p.y + 1000</c> over <c>m_solidRayMask</c> (which includes
        /// "piece") and returns the height of the FIRST hit -- i.e. the
        /// topmost solid in the whole column. Inside a portal hut that is
        /// the roof, so every arriving tame with the default margin lands on
        /// top of the building.
        ///
        /// A candidate's Y is already the player's own arrival height (Core
        /// leaves Y untouched; see ArrivalPlacement.Rotate), which is by
        /// definition standing room. Starting the raycast only a couple of
        /// metres above that -- comfortably below a hut's roof, comfortably
        /// above uneven ground -- finds the floor under the candidate
        /// instead of the ceiling over the whole zone.
        /// </summary>
        private const int GroundSnapMarginMetres = 2;

        internal static float GroundHeight(Vector3 point)
        {
            var zones = ZoneSystem.instance;
            if (zones == null) return point.y;

            return zones.GetSolidHeight(point, out float height, GroundSnapMarginMetres) ? height : point.y;
        }

        internal static Vec3 ToVec3(Vector3 v) => new Vec3(v.x, v.y, v.z);

        internal static Vector3 ToVector3(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
