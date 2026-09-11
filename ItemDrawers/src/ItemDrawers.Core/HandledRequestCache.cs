using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// Pure (no engine, no network) owner-side idempotency cache: "have I
    /// already answered this exact (sender, requestId) once before, and if
    /// so, with what?" Keyed by sender AND id together -- two different
    /// requesters can legitimately reuse the same small id, and must not be
    /// confused with each other.
    ///
    /// This is a mitigation, not the fix, for a request id being reused
    /// across two DIFFERENT logical requests from the same sender (see
    /// RequestIdGenerator's docstring for why that must not happen): if it
    /// ever does happen, this cache cannot tell the difference between a
    /// genuine retry and a coincidentally-colliding fresh request -- it
    /// will confidently replay the OLD answer to the NEW request. See
    /// HandledRequestCacheTests for that failure demonstrated directly.
    ///
    /// Time is passed in by the caller on every call, not read from a
    /// clock this class owns, so this is unit-testable with a fake clock.
    /// </summary>
    public sealed class HandledRequestCache<T>
    {
        private readonly struct Entry
        {
            public readonly T Result;
            public readonly float RecordedAt;
            public Entry(T result, float recordedAt) { Result = result; RecordedAt = recordedAt; }
        }

        private readonly Dictionary<(long Sender, long Id), Entry> _entries = new Dictionary<(long, long), Entry>();
        private readonly float _lifetimeSeconds;

        public HandledRequestCache(float lifetimeSeconds)
        {
            _lifetimeSeconds = lifetimeSeconds;
        }

        public int Count => _entries.Count;

        public bool TryGet(long sender, long id, out T result)
        {
            if (_entries.TryGetValue((sender, id), out var entry))
            {
                result = entry.Result;
                return true;
            }
            result = default;
            return false;
        }

        public void Record(long sender, long id, T result, float now)
        {
            _entries[(sender, id)] = new Entry(result, now);
        }

        /// <summary>Evicts every entry older than the configured lifetime. Cheap no-op when nothing is old enough to matter.</summary>
        public void Prune(float now)
        {
            if (_entries.Count == 0) return;
            List<(long, long)> stale = null;
            foreach (var kv in _entries)
            {
                if (now - kv.Value.RecordedAt > _lifetimeSeconds)
                    (stale ?? (stale = new List<(long, long)>())).Add(kv.Key);
            }
            if (stale == null) return;
            foreach (var key in stale) _entries.Remove(key);
        }
    }
}
