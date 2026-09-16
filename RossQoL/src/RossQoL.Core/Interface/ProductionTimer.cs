using System;

namespace RossQoL.Core.Interface
{
    /// <summary>
    /// How long a producer still needs, and how to say it. Beehives and sap
    /// collectors count up a "product" timer and gain a level every
    /// secPerUnit seconds; a fermenter finishes a fixed duration after it
    /// was filled.
    /// </summary>
    public static class ProductionTimer
    {
        /// <summary>Seconds until the next honey or sap, given the progress so far.</summary>
        public static double SecondsToNextUnit(double product, double secPerUnit)
        {
            if (secPerUnit <= 0d) return 0d;
            double remaining = secPerUnit - product;
            return remaining > 0d ? remaining : 0d;
        }

        /// <summary>
        /// Seconds until a producer reaches its cap, counting the level it is
        /// part-way through. Zero once it is full.
        /// </summary>
        public static double SecondsToFull(double product, double secPerUnit, int level, int maxLevel)
        {
            if (secPerUnit <= 0d || level >= maxLevel) return 0d;

            int missing = maxLevel - level;
            double remaining = missing * secPerUnit - product;
            return remaining > 0d ? remaining : 0d;
        }

        /// <summary>
        /// Seconds until a fermenter is ready. Negative when it holds nothing
        /// (no start time), zero when it is already done.
        /// </summary>
        public static double SecondsUntilReady(long startTicks, long nowTicks, double durationSeconds)
        {
            if (startTicks <= 0L) return -1d;

            double elapsed = (nowTicks - startTicks) / (double)TimeSpan.TicksPerSecond;
            double remaining = durationSeconds - elapsed;
            return remaining > 0d ? remaining : 0d;
        }

        /// <summary>
        /// A short countdown: "45s", "4m 12s", "1h 03m". Rounded up, so it
        /// never reads 0 while there is time left.
        /// </summary>
        public static string Format(double seconds)
        {
            if (seconds <= 0d) return "0s";

            long total = (long)Math.Ceiling(seconds);
            long hours = total / 3600;
            long minutes = total % 3600 / 60;
            long secs = total % 60;

            if (hours > 0) return $"{hours}h {minutes:00}m";
            if (minutes > 0) return $"{minutes}m {secs:00}s";
            return $"{secs}s";
        }
    }
}
