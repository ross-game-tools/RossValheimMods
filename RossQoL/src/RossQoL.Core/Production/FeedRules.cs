using System;
using System.Collections.Generic;

namespace RossQoL.Core.Production
{
    /// <summary>
    /// The rules of one feed -- what may be taken, how much, and when a
    /// producer has made enough -- kept here so they are tested without a
    /// game.
    ///
    /// Settings are plain text so a player can edit them in the config file:
    /// "Wood:50, Barley:20" for amounts, "Wood, FineWood" for lists. Both are
    /// read on every feed, so edits apply without a restart, and both are
    /// forgiving: unknown text is skipped rather than throwing, because a
    /// typo in a config file must not stop a base from running.
    /// </summary>
    public static class FeedRules
    {
        /// <summary>Names are prefab names, matched however the player typed them.</summary>
        public static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>
        /// Parses "Wood:50, Barley:20". A repeated name keeps the last value,
        /// as a config file's last word wins; entries without a positive
        /// number are skipped.
        /// </summary>
        public static Dictionary<string, int> ParseAmounts(string setting)
        {
            var amounts = new Dictionary<string, int>(NameComparer);
            if (string.IsNullOrWhiteSpace(setting)) return amounts;

            foreach (string entry in setting.Split(','))
            {
                string text = entry.Trim();
                if (text.Length == 0) continue;

                int colon = text.LastIndexOf(':');
                if (colon <= 0 || colon == text.Length - 1) continue;

                string name = text.Substring(0, colon).Trim();
                string number = text.Substring(colon + 1).Trim();
                if (name.Length == 0) continue;
                if (!int.TryParse(number, out int value) || value < 0) continue;

                amounts[name] = value;
            }

            return amounts;
        }

        /// <summary>Parses "Wood, FineWood, RoundLog" into a set of names.</summary>
        public static HashSet<string> ParseNames(string setting)
        {
            var names = new HashSet<string>(NameComparer);
            if (string.IsNullOrWhiteSpace(setting)) return names;

            foreach (string entry in setting.Split(','))
            {
                string name = entry.Trim();
                if (name.Length > 0) names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// An allow list that is empty allows everything: a player who has
        /// not restricted a station's fuel means "whatever the station
        /// takes", not "nothing".
        /// </summary>
        public static bool Allowed(ICollection<string> allowList, string name) =>
            allowList == null || allowList.Count == 0 || (name != null && allowList.Contains(name));

        /// <summary>The minimum to leave of one item: its own entry, or the default.</summary>
        public static int MinimumFor(IDictionary<string, int> perItem, string name, int fallback)
        {
            if (name != null && perItem != null && perItem.TryGetValue(name, out int minimum))
                return Math.Max(0, minimum);

            return Math.Max(0, fallback);
        }

        /// <summary>
        /// How many may be taken from a container: what it holds above the
        /// minimum to leave behind, and never more than is wanted.
        /// </summary>
        public static int Takeable(int available, int wanted, int minimum)
        {
            if (wanted <= 0) return 0;

            int spare = available - Math.Max(0, minimum);
            if (spare <= 0) return 0;

            return Math.Min(spare, wanted);
        }

        /// <summary>
        /// True when a product has reached its cap and its producer should be
        /// left idle. No entry, or a cap of zero, means no limit: a cap of
        /// zero would otherwise stop production the moment it is configured,
        /// which is never what a player means by leaving a setting blank.
        /// </summary>
        public static bool AtCap(IDictionary<string, int> caps, string product, int existing)
        {
            if (product == null || caps == null) return false;
            if (!caps.TryGetValue(product, out int cap) || cap <= 0) return false;

            return existing >= cap;
        }
    }
}
