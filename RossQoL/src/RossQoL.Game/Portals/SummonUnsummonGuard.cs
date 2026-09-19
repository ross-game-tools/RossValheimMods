using System.Collections.Generic;

namespace RossQoL.Game.Portals
{
    /// <summary>
    /// Which creatures vanilla must not unsummon right now, because they are
    /// mid-portal-hop.
    ///
    /// WHY THIS IS NEEDED. A summoned creature's <c>Tameable</c> carries a
    /// non-zero <c>m_unsummonDistance</c>, and <c>Tameable.Update</c> runs
    /// <c>UpdateSummon</c> every frame on the ZDO's OWNER:
    ///
    /// <code>
    /// private void UpdateSummon()
    /// {
    ///     if (m_nview.IsValid() &amp;&amp; m_nview.IsOwner() &amp;&amp; m_unsummonDistance > 0f &amp;&amp; (bool)m_monsterAI)
    ///     {
    ///         GameObject followTarget = m_monsterAI.GetFollowTarget();
    ///         if ((bool)followTarget &amp;&amp; Vector3.Distance(followTarget.transform.position, base.gameObject.transform.position) > m_unsummonDistance)
    ///         {
    ///             UnSummon();
    ///         }
    ///     }
    /// }
    /// </code>
    ///
    /// A portal hop moves the follow target -- the player -- far further than
    /// that. <c>Player.UpdateTeleport</c> sets
    /// <c>base.transform.position = m_teleportTargetPos</c> two seconds in,
    /// while <c>m_teleporting</c> stays true until the destination area is
    /// ready, so for the rest of the transit the player's transform reads as
    /// the DESTINATION while the creature is still standing at the departure
    /// point, still loaded, and still running its own Update. The distance
    /// check therefore measures the whole length of the journey, and fires.
    ///
    /// <c>UnSummon()</c> is not a despawn that can be undone: it invokes
    /// <c>RPC_UnSummon</c> on everybody, and the owner runs
    /// <c>ZNetScene.instance.Destroy(gameObject)</c>, which calls
    /// <c>ZDOMan.instance.DestroyZDO(zDO)</c>. The ZDO itself is gone, so by
    /// arrival <c>ZDOMan.GetZDO(id)</c> returns null and there is nothing left
    /// to move.
    ///
    /// Note the sting: Portals/TamesFollow CLAIMS ownership of every captured
    /// creature at departure, which guarantees that the client running this
    /// check is the one that just claimed it. Capturing a summon is what makes
    /// its destruction certain.
    ///
    /// WHAT THIS RISKS, honestly. While an id is held here, that creature is
    /// exempt from vanilla's unsummon rules, so a summon can survive a moment
    /// where vanilla would have removed it -- both the distance rule and the
    /// owner-logout timer, since the guard suppresses <c>Tameable.Update</c>
    /// as a whole rather than one of its two halves (its other half,
    /// <c>UpdateSavedFollowTarget</c>, only re-derives a follow target that is
    /// about to be re-derived anyway). That exemption is bounded three ways:
    /// only creatures this mod captured are ever in the set, only for one
    /// teleport, and <see cref="PortalTamesManager"/> clears the set on every
    /// path that ends a capture -- arrival, expiry (30s), a lost player, and a
    /// capture that selected nobody. If an arrival never registers, the expiry
    /// path releases the guard and vanilla's own rules resume on the next
    /// frame and unsummon the creature where it stands, which is exactly what
    /// would have happened without this mod, just later.
    ///
    /// What this deliberately does NOT do is change <c>m_unsummonDistance</c>
    /// on the prefab or the instance. That value is the creature's behaviour
    /// for every player, everywhere, for the rest of the world's life; this
    /// feature only asks for a few seconds of reprieve for creatures it is
    /// actively carrying.
    /// </summary>
    internal static class SummonUnsummonGuard
    {
        private static readonly HashSet<ZDOID> Guarded = new HashSet<ZDOID>();

        /// <summary>
        /// Fast enough to call from a per-frame patch on every Tameable in the
        /// scene: the set is empty except during the few seconds of a hop.
        /// </summary>
        public static bool IsGuarded(ZDOID id) => Guarded.Count != 0 && Guarded.Contains(id);

        public static void Guard(IReadOnlyList<ZDOID> ids)
        {
            if (ids == null) return;

            foreach (var id in ids)
                if (id != ZDOID.None) Guarded.Add(id);
        }

        /// <summary>
        /// Releases every guarded creature. Called from every path that ends a
        /// capture -- there is never more than one capture in flight, so there
        /// is nothing to release selectively.
        /// </summary>
        public static void ReleaseAll() => Guarded.Clear();
    }
}
