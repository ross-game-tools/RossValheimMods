using System;
using System.Collections.Generic;

namespace RossQoL.Core.Items
{
    /// <summary>
    /// One creature a multi-creature summon staff could raise, reduced to what
    /// the pick asks about: whether the staff may raise it at all right now,
    /// how many of it are already following you, and how hurt the most
    /// wounded of those is.
    /// </summary>
    public readonly struct SummonKind
    {
        public SummonKind(bool available, int following, float weakestHealthFraction)
        {
            Available = available;
            Following = following;
            WeakestHealthFraction = weakestHealthFraction;
        }

        /// <summary>
        /// False for a kind the cast would refuse -- the staff's own
        /// per-creature spawn limit is already reached -- so the pick never
        /// steers a cast into "max summons reached".
        /// </summary>
        public bool Available { get; }

        public int Following { get; }

        /// <summary>Lowest health fraction (0..1) among the ones following; ignored when none are.</summary>
        public float WeakestHealthFraction { get; }
    }

    /// <summary>
    /// Which creature a multi-creature summon staff (the Spirit Caller) raises
    /// next, in place of vanilla's uniform random pick.
    ///
    /// Vanilla rolls <c>m_spawnPrefab[Random.Range(0, length)]</c> on every
    /// cast, so with four kinds it regularly takes many casts to see all four,
    /// and repeats of one kind just replace each other under the per-kind
    /// summon cap. The rule here, in order:
    ///
    ///  1. a kind you have none of following you, at random among those;
    ///  2. otherwise the kind whose most wounded member has the lowest health
    ///     fraction -- the cast tops up (or, at the cap, replaces) the
    ///     creature that most needs it;
    ///  3. kinds tied on that fraction -- typically everyone at full health --
    ///     at random among the tied ones, which is vanilla's behaviour when
    ///     nothing distinguishes them.
    ///
    /// Health as a fraction, not raw points: the four creatures have very
    /// different pools, and a bear on 60% is not less in need than a boar on
    /// 40% just because it has more points left.
    /// </summary>
    public static class SummonPick
    {
        /// <summary>
        /// Fractions closer than this count as equal. Health is a float read
        /// back off the ZDO; two untouched creatures both read 1.0 exactly,
        /// but regeneration ticks leave hair-width differences that should not
        /// decide the pick on their own.
        /// </summary>
        public const float HealthTieTolerance = 0.005f;

        /// <summary>
        /// The index into <paramref name="kinds"/> to raise, or -1 when no kind
        /// is available (the caller leaves the cast to vanilla).
        /// <paramref name="randomBelow"/> returns a uniform integer in
        /// [0, n) -- UnityEngine.Random in the game, scripted in tests.
        /// </summary>
        public static int Choose(IReadOnlyList<SummonKind> kinds, Func<int, int> randomBelow)
        {
            if (kinds == null || randomBelow == null) return -1;

            var missing = new List<int>();
            var present = new List<int>();
            for (int i = 0; i < kinds.Count; i++)
            {
                if (!kinds[i].Available) continue;
                (kinds[i].Following <= 0 ? missing : present).Add(i);
            }

            if (missing.Count > 0) return PickAmong(missing, randomBelow);
            if (present.Count == 0) return -1;

            float weakest = float.MaxValue;
            foreach (int i in present)
                weakest = Math.Min(weakest, Clamp01(kinds[i].WeakestHealthFraction));

            var tied = new List<int>();
            foreach (int i in present)
                if (Clamp01(kinds[i].WeakestHealthFraction) - weakest <= HealthTieTolerance)
                    tied.Add(i);

            return PickAmong(tied, randomBelow);
        }

        /// <summary>
        /// The most creatures one cast can raise, given the spawner's
        /// <c>m_minToSpawn</c>/<c>m_maxToSpawn</c>. Vanilla draws the count
        /// with the int overload of <c>Random.Range(min, max)</c>, whose upper
        /// bound is exclusive -- except when min == max, where it returns min.
        /// The pick only makes sense for a one-creature cast: fixing a
        /// several-creature cast to one kind would make it less varied than
        /// vanilla, not more.
        /// </summary>
        public static int MostPerCast(int minToSpawn, int maxToSpawn) =>
            maxToSpawn > minToSpawn ? maxToSpawn - 1 : minToSpawn;

        private static int PickAmong(List<int> indices, Func<int, int> randomBelow)
        {
            if (indices.Count == 1) return indices[0];

            int roll = randomBelow(indices.Count);
            if (roll < 0 || roll >= indices.Count) roll = 0;
            return indices[roll];
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 1f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
