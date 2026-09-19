using System;
using RossQoL.Core.Death;
using UnityEngine;

namespace RossQoL.Game.Death
{
    /// <summary>
    /// Owns one runtime <see cref="SE_Stats"/> status effect and keeps its
    /// stamina numbers in step with the distance to the player's NEWEST
    /// grave. Recomputed about once a second -- the distance to a grave does
    /// not need per-frame precision.
    ///
    /// The player object is destroyed and rebuilt on every death, so the
    /// live clone <c>SEMan</c> hands back goes stale at that same moment;
    /// this compares the cached <see cref="Player"/> reference every tick
    /// and starts clean when it changes.
    /// </summary>
    internal sealed class CorpseRunEffect : MonoBehaviour
    {
        private const string EffectName = "RossQoL_CorpseRun";
        private const float RecomputeSeconds = 1f;

        /// <summary>
        /// Matches what <see cref="StatusEffect.NameHash"/> computes once
        /// <see cref="_template"/>'s <c>name</c> is set to <see cref="EffectName"/>
        /// -- kept separately so removal works even before a template has
        /// ever been built.
        /// </summary>
        private static readonly int Hash = EffectName.GetStableHashCode();

        private SE_Stats _template;

        /// <summary>Set once no icon at all could be found, so BuildTemplate never retries.</summary>
        private bool _iconMissing;

        /// <summary>The player this component last saw; used to notice a death/respawn swap.</summary>
        private Player _cachedPlayer;

        /// <summary>The CLONE SEMan holds and returned when the effect was added -- what must be mutated.</summary>
        private StatusEffect _live;

        private float _nextRecompute;

        /// <summary>
        /// The grave the time limit is currently timing, and when that
        /// timing started (<see cref="Time.unscaledTime"/>). Session state
        /// only, deliberately not written to custom data: a relog is a
        /// simpler, honest reset rather than a saved timestamp that would
        /// need its own versioning and clock-skew handling for a cosmetic
        /// cap. Compared by tombstone identity, not by reference, so a
        /// reread of the same grave from custom data still matches.
        /// </summary>
        private long? _timerGraveZdoUserId;
        private uint _timerGraveZdoId;
        private float _timerStart;

        private void Update()
        {
            // Nothing here may throw past Update -- an exception escaping a
            // MonoBehaviour callback would stop this component ticking for
            // the rest of the session, silently freezing the buff in place.
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Death: corpse run buff update failed: {ex}");
            }
        }

        /// <summary>
        /// A plugin unload or HotReload destroys this component, and the buff
        /// has no timer of its own -- without this it would stay frozen on
        /// the player with nothing left running to take it off.
        /// </summary>
        private void OnDestroy()
        {
            try
            {
                Remove(_cachedPlayer);
            }
            catch (Exception ex)
            {
                RossQoLPlugin.Log.LogError($"Death: the corpse run buff could not be removed on teardown: {ex}");
            }
        }

        private void Tick()
        {
            if (Time.unscaledTime < _nextRecompute) return;
            _nextRecompute = Time.unscaledTime + RecomputeSeconds;

            bool active = CorpseRunFeature.Instance?.IsActive == true;
            var player = active ? Player.m_localPlayer : null;

            if (_cachedPlayer != player)
            {
                // Either a fresh death (the old player object is destroyed)
                // or the feature/toggle changed. The old clone is stale
                // either way; still worth an explicit removal while the old
                // reference is still valid, so a buff never lingers under a
                // player object nothing else is tracking any more.
                Remove(_cachedPlayer);
                _cachedPlayer = player;
                _live = null;
                _timerGraveZdoUserId = null;
            }

            if (player == null) return; // nothing to do without a local player

            // Deliberately NOT DeathState.Selected: that is the on-screen
            // marker's cursor, moved by CycleGraveKey so the player can look
            // at an older grave without touching this buff. A server-
            // controlled buff re-aiming and re-scaling itself just because
            // someone tapped a look-around key would be a bug wearing a
            // feature's clothes. This always follows the newest grave --
            // Graves() is newest-first, so its first entry -- independent of
            // whatever the marker is pointed at.
            var graves = DeathState.Graves();
            GraveRecord? grave = graves.Count > 0 ? graves[0] : (GraveRecord?)null;
            if (grave == null) { Remove(player); _timerGraveZdoUserId = null; return; }

            if (_timerGraveZdoUserId != grave.Value.ZdoUserId || _timerGraveZdoId != grave.Value.ZdoId)
            {
                // A different grave than the one the clock was timing --
                // either a fresh death, or the previous newest grave went
                // and an older one took its place. Either way this is a new
                // target for the buff, so it gets its own fresh clock rather
                // than inheriting time already spent on a different grave.
                _timerGraveZdoUserId = grave.Value.ZdoUserId;
                _timerGraveZdoId = grave.Value.ZdoId;
                _timerStart = Time.unscaledTime;
            }

            float limitMinutes = DeathConfig.CorpseRunMinutes?.Value ?? 10f;
            if (CorpseRunTimeout.Expired(_timerStart, Time.unscaledTime, limitMinutes))
            {
                // The cap is reached: the buff stops here and does not come
                // back for this grave, even if the player never got close to
                // it. Nothing resets that but a new grave, above.
                Remove(player);
                return;
            }

            // The same target the on-screen marker aims at: for a grave inside
            // a dungeon that is the entrance while the player is still outside.
            // Scaling on the raw grave position would peg the buff at full
            // strength for the whole run back, because an interior is recorded
            // 5000m above the surface.
            var target = GraveAimPoint.For(grave.Value, player.transform.position);
            float distance = Vector3.Distance(player.transform.position, target);
            float ramped = CorpseRunStrength.For(
                distance,
                DeathConfig.CorpseRunMinDistance?.Value ?? 50f,
                DeathConfig.CorpseRunFullDistance?.Value ?? 1000f);

            float minStrength = DeathConfig.CorpseRunMinStrength?.Value ?? 0.5f;
            float strength = CorpseRunStrength.WithBaseline(ramped, minStrength);

            if (strength <= 0f)
            {
                // Only reachable with CorpseRunMinStrength configured to 0:
                // with any positive floor the buff always has SOME strength
                // while the grave stands, and is removed only by the grave
                // going, the player going null, or the feature switching
                // off (all handled above/below). A zero floor is the one
                // case that still reaches zero, and a do-nothing tile is
                // worse than no tile, so it is removed explicitly here
                // rather than left to fall out of RegenMultiplier/DrainModifier
                // returning their no-op values by accident.
                Remove(player);
                return;
            }

            var effect = EnsureLive(player) as SE_Stats;
            if (effect == null) return; // no icon: BuildTemplate already warned once, never add an invisible buff

            effect.m_staminaRegenMultiplier = CorpseRunStrength.RegenMultiplier(
                strength, DeathConfig.CorpseRunMaxRegenBonus?.Value ?? 1f);

            float drain = CorpseRunStrength.DrainModifier(
                strength, DeathConfig.CorpseRunMaxDrainReduction?.Value ?? 0.5f);
            effect.m_runStaminaDrainModifier = drain;
            effect.m_jumpStaminaUseModifier = drain;
        }

        private StatusEffect EnsureLive(Player player)
        {
            if (_live != null) return _live;

            var template = BuildTemplate();
            if (template == null) return null;

            var seman = player.GetSEMan();
            if (seman == null) return null;

            // The instance overload neither touches ObjectDB nor goes
            // through an RPC. It stores and returns a CLONE; a null return
            // means the effect was already present, in which case the live
            // clone is fetched by hash instead.
            var added = seman.AddStatusEffect(template, resetTime: false, 0, 0f, -1);
            _live = added != null ? added : seman.GetStatusEffect(Hash);
            return _live;
        }

        private void Remove(Player player)
        {
            // Explicit Unity-aware null check, not `?.`: a destroyed Player
            // still passes a reference-null test, and only the overloaded
            // `==` catches it.
            if (player == null) return;

            var seman = player.GetSEMan();
            if (seman == null) return;

            seman.RemoveStatusEffect(Hash, quiet: true);
            _live = null;
        }

        /// <summary>
        /// Builds the template SE once. Never mutated after the fact --
        /// only the clone SEMan hands back (<see cref="_live"/>) is ever
        /// changed frame to frame.
        /// </summary>
        private SE_Stats BuildTemplate()
        {
            if (_template != null) return _template;
            if (_iconMissing) return null;

            // Prefer the icon vanilla itself shows right after a death --
            // SoftDeath is a plain StatusEffect (not SE_Stats), so only its
            // m_icon is read here, and it is never mutated. Rested is the
            // fallback that worked before this icon was known to exist.
            var softDeath = ObjectDB.instance != null
                ? ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectSoftDeath)
                : null;
            var icon = softDeath != null ? softDeath.m_icon : null;

            if (icon == null)
            {
                var rested = ObjectDB.instance != null
                    ? ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectRested)
                    : null;
                icon = rested != null ? rested.m_icon : null;
            }

            if (icon == null)
            {
                // An effect with no m_icon is skipped entirely by
                // SEMan.GetHUDStatusEffects -- an invisible buff is worse
                // than none, so it is never added at all.
                _iconMissing = true;
                RossQoLPlugin.Log.LogWarning(
                    "Death: could not find an icon to borrow for the corpse run buff (tried SoftDeath and "
                    + "Rested), so it will never be shown.");
                return null;
            }

            var effect = ScriptableObject.CreateInstance<SE_Stats>();

            // NameHash() hashes this; it must be unique among every status
            // effect this session, vanilla or modded.
            effect.name = EffectName;

            // A plain literal, not a localization token this mod never
            // registers. Deliberately not vanilla's "Corpse Run": that name
            // belongs to the tombstone's own reward, granted only once the
            // grave is emptied, and the two now visibly hand off rather than
            // overlap -- a shared name here would read as a bug at that
            // handoff, not a feature.
            effect.m_name = "Just Died";
            effect.m_tooltip =
                "You just died. Stamina returns faster and running and jumping cost less the farther off your "
                + "grave is, easing off in steps as you close the distance -- but never all the way to nothing "
                + "while the grave still stands. Ends the moment you loot it, or after CorpseRunMinutes if you "
                + "don't make it back in time.";
            effect.m_icon = icon;
            // No Unity TTL: CorpseRunMinutes is enforced in Tick above so the
            // buff can be removed the moment the limit is reached rather than
            // waiting for this SE's own countdown, and so 0 (no limit) is a
            // plain skip rather than needing a magic "infinite" TTL value.
            effect.m_ttl = 0f;

            _template = effect;
            return _template;
        }
    }
}
