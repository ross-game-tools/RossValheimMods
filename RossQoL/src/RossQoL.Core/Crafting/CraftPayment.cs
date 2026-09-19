using System.Collections.Generic;

namespace RossQoL.Core.Crafting
{
    /// <summary>
    /// The quality tier a craft is costed against, and the most any one tier
    /// can pay towards it.
    /// </summary>
    public readonly struct TierChoice
    {
        /// <summary>The tier payment must charge. Zero when nothing is held at any tier.</summary>
        public int Quality { get; }

        /// <summary>The most any single tier holds -- what "can you afford this" is answered from.</summary>
        public int Available { get; }

        public TierChoice(int quality, int available)
        {
            Quality = quality;
            Available = available;
        }

        public bool Covers(int need) => Available >= need;
    }

    /// <summary>
    /// The arithmetic behind paying for a craft out of a pack and the
    /// containers around it.
    ///
    /// Two rules live here because both passes of the feature -- deciding a
    /// craft is affordable, and charging for it afterwards -- must agree, and
    /// the cost of them disagreeing is free items: vanilla hands the player
    /// the crafted thing before the charging step runs, so a craft that was
    /// counted generously and charged meanly cannot be refused, only noticed.
    ///
    /// - <see cref="ChooseTier"/>: a recipe spends ONE quality tier, never a
    ///   pool of several added together, so the same tier that made a craft
    ///   look affordable is the only tier payment is allowed to take from.
    /// - <see cref="Owing"/> / <see cref="Shortfall"/>: what the containers
    ///   still have to cover after the pack paid, and what is left unpaid
    ///   after they did.
    ///
    /// Everything clamps towards charging MORE, never less: a negative
    /// "paid" (the pack grew, because the crafted item landed in it first)
    /// counts as nothing paid rather than as a credit.
    /// </summary>
    public static class CraftPayment
    {
        /// <summary>
        /// Picks the tier a craft is costed against, given what is held at
        /// each tier -- index is the quality, so index 2 is quality 2.
        ///
        /// The lowest tier that covers the cost on its own wins, so a craft
        /// spends the plainest materials that can pay for it and a better
        /// stack is not eaten while a worse one would have done. When no
        /// single tier covers it, the fullest tier is chosen: that is the one
        /// affordability was counted from, so it is the one payment may take.
        /// </summary>
        public static TierChoice ChooseTier(IReadOnlyList<int> heldByQuality, int need)
        {
            if (heldByQuality == null) return new TierChoice(0, 0);

            int fullestQuality = 0;
            int fullest = 0;
            for (int quality = 0; quality < heldByQuality.Count; quality++)
            {
                int held = heldByQuality[quality];
                if (held > fullest)
                {
                    fullest = held;
                    fullestQuality = quality;
                }
            }

            if (need > 0)
            {
                for (int quality = 0; quality < heldByQuality.Count; quality++)
                    if (heldByQuality[quality] >= need) return new TierChoice(quality, fullest);
            }

            return new TierChoice(fullestQuality, fullest);
        }

        /// <summary>What the containers must still cover once the pack has paid what it could.</summary>
        public static int Owing(int owed, int paidFromPack)
        {
            if (owed <= 0) return 0;

            int paid = paidFromPack > 0 ? paidFromPack : 0;
            int owing = owed - paid;
            return owing > 0 ? owing : 0;
        }

        /// <summary>What is still unpaid after the containers were charged -- items granted for free.</summary>
        public static int Shortfall(int owed, int paidFromPack, int takenFromContainers)
        {
            int owing = Owing(owed, paidFromPack);
            int taken = takenFromContainers > 0 ? takenFromContainers : 0;
            int shortfall = owing - taken;
            return shortfall > 0 ? shortfall : 0;
        }
    }
}
