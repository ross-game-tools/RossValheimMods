using System;
using System.Collections.Generic;
using System.Globalization;

namespace RossQoL.Core.Interface
{
    /// <summary>How an entry's running total reads once it is written out.</summary>
    public enum NotificationStyle
    {
        /// <summary>No total at all: the label is the whole line, e.g. a level-up.</summary>
        Plain,

        /// <summary>A count of things, e.g. "Wood x20". A total of one is left unsaid.</summary>
        Count,

        /// <summary>Progress in whole percent, e.g. "Woodcutting +11%".</summary>
        Percent,
    }

    /// <summary>
    /// One line of the notification list: what it says, how much has been
    /// folded into it, and when it was last touched.
    /// </summary>
    public sealed class Notification
    {
        internal Notification(string key, string label, NotificationStyle style, float amount, float now)
        {
            Key = key;
            Label = label;
            Style = style;
            Amount = amount;
            RefreshedAt = now;
        }

        /// <summary>What makes this line the same line next time: an item name, a skill.</summary>
        public string Key { get; }

        /// <summary>The player-facing name in front of the total.</summary>
        public string Label { get; internal set; }

        public NotificationStyle Style { get; internal set; }

        /// <summary>The running total, in the units its style reads in.</summary>
        public float Amount { get; internal set; }

        /// <summary>When this line last had something folded into it; its dwell starts here.</summary>
        public float RefreshedAt { get; internal set; }

        /// <summary>The whole line, ready to be drawn.</summary>
        public string Text
        {
            get
            {
                switch (Style)
                {
                    case NotificationStyle.Count:
                    {
                        int count = (int)Math.Round(Amount, MidpointRounding.AwayFromZero);
                        return count > 1
                            ? Label + " x" + count.ToString(CultureInfo.InvariantCulture)
                            : Label;
                    }

                    case NotificationStyle.Percent:
                    {
                        // A real but tiny gain rounds to zero, and "+0%" reads
                        // as "nothing happened" when something did. The
                        // smallest thing worth saying is one percent.
                        int percent = (int)Math.Round(Amount, MidpointRounding.AwayFromZero);
                        if (percent < 1 && Amount > 0f) percent = 1;
                        return Label + " +" + percent.ToString(CultureInfo.InvariantCulture) + "%";
                    }

                    default:
                        return Label;
                }
            }
        }
    }

    /// <summary>
    /// The stacking notification list, with no game in sight: which line a new
    /// message lands on, how long each line lasts, and what falls off when the
    /// list is full.
    ///
    /// Vanilla shows one message at a time and folds a repeat into it only
    /// when that repeat is the very next message to arrive, so any other
    /// message in between splits "Wood x12" and "Wood x8" into two separate
    /// showings. Here every line is found by key instead of by position, so a
    /// repeat updates its own line wherever it sits and the lines around it
    /// are irrelevant.
    ///
    /// Time is passed in rather than read, in whatever clock the caller uses
    /// -- the game passes unscaled seconds, so the fade keeps running while
    /// the world is paused, exactly as vanilla's own does.
    /// </summary>
    public sealed class NotificationList
    {
        /// <summary>A busy moment must not fill the screen.</summary>
        public const int DefaultCapacity = 5;

        /// <summary>Vanilla's minimum time a message stays fully readable.</summary>
        public const float DefaultHoldSeconds = 1f;

        /// <summary>Vanilla's CrossFadeAlpha duration.</summary>
        public const float DefaultFadeSeconds = 4f;

        private readonly List<Notification> _entries = new List<Notification>();

        public NotificationList(
            int capacity = DefaultCapacity,
            float holdSeconds = DefaultHoldSeconds,
            float fadeSeconds = DefaultFadeSeconds)
        {
            Capacity = Math.Max(1, capacity);
            HoldSeconds = Math.Max(0f, holdSeconds);
            FadeSeconds = Math.Max(0f, fadeSeconds);
        }

        public int Capacity { get; }

        public float HoldSeconds { get; }

        public float FadeSeconds { get; }

        /// <summary>Oldest line first, which is the one drawn at the top.</summary>
        public IReadOnlyList<Notification> Entries => _entries;

        public int Count => _entries.Count;

        /// <summary>
        /// Folds a new arrival into the list. A key already on the list
        /// updates that line where it stands -- its total grows and its dwell
        /// starts again -- rather than adding a second line or jumping to the
        /// bottom. A new key is appended, evicting the oldest line if the list
        /// is already full.
        ///
        /// An arrival in a different style to the line it lands on replaces
        /// the total rather than adding to it: a skill that has just levelled
        /// starts counting towards the next level from nothing, and a running
        /// percentage means nothing once the line has become a level-up.
        /// </summary>
        public Notification Add(string key, string label, NotificationStyle style, float amount, float now)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var existing = Find(key);
            if (existing != null)
            {
                existing.Label = label;
                existing.Amount = existing.Style == style ? existing.Amount + amount : amount;
                existing.Style = style;
                existing.RefreshedAt = now;
                return existing;
            }

            if (_entries.Count >= Capacity) _entries.RemoveAt(0);

            var entry = new Notification(key, label, style, amount, now);
            _entries.Add(entry);
            return entry;
        }

        public Notification Find(string key)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].Key, key, StringComparison.Ordinal))
                    return _entries[i];

            return null;
        }

        /// <summary>Takes a line off the list early. True when there was one.</summary>
        public bool Remove(string key)
        {
            var entry = Find(key);
            return entry != null && _entries.Remove(entry);
        }

        public void Clear() => _entries.Clear();

        /// <summary>Drops every line that has finished fading, closing the gap it leaves.</summary>
        public void Prune(float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (AlphaOf(_entries[i], now) <= 0f)
                    _entries.RemoveAt(i);
        }

        /// <summary>
        /// How visible a line is now: fully solid for its hold, then a linear
        /// fade to nothing, matching vanilla's 1 second plus 4 second fade.
        /// </summary>
        public float AlphaOf(Notification entry, float now)
        {
            if (entry == null) return 0f;

            float elapsed = now - entry.RefreshedAt;
            if (elapsed <= HoldSeconds) return 1f;
            if (FadeSeconds <= 0f) return 0f;

            float remaining = 1f - (elapsed - HoldSeconds) / FadeSeconds;
            if (remaining <= 0f) return 0f;
            return remaining >= 1f ? 1f : remaining;
        }
    }
}
