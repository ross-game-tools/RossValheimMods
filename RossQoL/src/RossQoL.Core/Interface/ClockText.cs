using System;

namespace RossQoL.Core.Interface
{
    /// <summary>
    /// The clock's text. Valheim's day fraction runs 0 at midnight, 0.25 at
    /// sunrise, 0.5 at noon and 0.75 at sunset, so it maps straight onto a
    /// 24-hour clock.
    /// </summary>
    public static class ClockText
    {
        private const int MinutesPerDay = 24 * 60;

        /// <param name="dayLabel">Already localized, e.g. "Day 42"; may be empty.</param>
        public static string Format(string dayLabel, float dayFraction, bool use24Hour)
        {
            string time = FormatTime(dayFraction, use24Hour);
            return string.IsNullOrEmpty(dayLabel) ? time : dayLabel + "  " + time;
        }

        private static string FormatTime(float dayFraction, bool use24Hour)
        {
            double fraction = float.IsNaN(dayFraction) || float.IsInfinity(dayFraction) ? 0d : dayFraction;
            fraction -= Math.Floor(fraction);

            // Floor, not round: the clock must not show a minute before it arrives.
            int minutes = (int)Math.Floor(fraction * MinutesPerDay) % MinutesPerDay;
            int hour = minutes / 60;
            int minute = minutes % 60;

            if (use24Hour) return $"{hour:00}:{minute:00}";

            int hour12 = hour % 12 == 0 ? 12 : hour % 12;
            return $"{hour12}:{minute:00} {(hour < 12 ? "AM" : "PM")}";
        }
    }
}
