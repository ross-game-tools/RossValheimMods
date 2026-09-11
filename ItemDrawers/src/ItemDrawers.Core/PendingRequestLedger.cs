using System;
using System.Collections.Generic;

namespace ItemDrawers.Core
{
    /// <summary>
    /// One entry in a <see cref="PendingRequestLedger{T}"/>: a request this
    /// client is waiting on a reply for, plus whatever caller-defined
    /// payload it needs to finish the job once a reply (or a give-up)
    /// happens.
    /// </summary>
    public sealed class PendingRequest<T>
    {
        public readonly long Id;
        public readonly T Payload;
        public int Attempts;
        public float Deadline;

        public PendingRequest(long id, T payload, int attempts, float deadline)
        {
            Id = id;
            Payload = payload;
            Attempts = attempts;
            Deadline = deadline;
        }
    }

    /// <summary>
    /// Pure (no engine, no network) bookkeeping for the requester side of
    /// the RPC-to-owner protocol: which requests are outstanding, when each
    /// is next due for a retry or a give-up, and the single-completion rule
    /// that stops a reply from being banked twice.
    ///
    /// Time is passed in by the caller on every call rather than read from
    /// a clock this class owns, specifically so this is unit-testable with
    /// an injected, fake clock -- see PendingRequestLedgerTests.
    ///
    /// This does not know or care what "retry" or "give up" mean for the
    /// caller's payload (an RPC re-send, a spill, a message) -- Tick only
    /// reports which ids need which action; the caller decides what to do.
    /// </summary>
    public sealed class PendingRequestLedger<T>
    {
        private readonly Dictionary<long, PendingRequest<T>> _pending = new Dictionary<long, PendingRequest<T>>();
        private readonly float _timeoutSeconds;
        private readonly int _maxAttempts;

        public PendingRequestLedger(float timeoutSeconds, int maxAttempts)
        {
            _timeoutSeconds = timeoutSeconds;
            _maxAttempts = maxAttempts;
        }

        public int Count => _pending.Count;

        /// <summary>Registers a new outstanding request, due for its first retry check at now + timeout.</summary>
        public void Add(long id, T payload, float now)
        {
            _pending[id] = new PendingRequest<T>(id, payload, attempts: 1, deadline: now + _timeoutSeconds);
        }

        /// <summary>
        /// The only way a request ever leaves the ledger via a reply.
        /// Returns true and removes the entry exactly once per id -- a
        /// second call with the same id (a duplicate or late-arriving
        /// second reply) returns false and changes nothing, which is what
        /// stops a reply from being banked twice.
        /// </summary>
        public bool TryComplete(long id, out T payload)
        {
            if (_pending.TryGetValue(id, out var request))
            {
                payload = request.Payload;
                _pending.Remove(id);
                return true;
            }
            payload = default;
            return false;
        }

        /// <summary>
        /// Removes an entry unconditionally (the request is being abandoned
        /// for a reason that has nothing to do with a reply -- e.g. the
        /// requester's own component is being destroyed). Returns true and
        /// hands back the payload if it was present.
        /// </summary>
        public bool Remove(long id, out T payload)
        {
            if (_pending.TryGetValue(id, out var request))
            {
                payload = request.Payload;
                _pending.Remove(id);
                return true;
            }
            payload = default;
            return false;
        }

        /// <summary>All still-pending (id, payload) pairs, for a last-resort drain (e.g. on application quit).</summary>
        public IEnumerable<KeyValuePair<long, T>> All()
        {
            foreach (var kv in _pending)
                yield return new KeyValuePair<long, T>(kv.Key, kv.Value.Payload);
        }

        /// <summary>
        /// Advances the ledger's notion of time. Every request past its
        /// deadline either gets another attempt (bumping Attempts and
        /// Deadline, then invoking <paramref name="retry"/> with its
        /// payload) or, once <see cref="_maxAttempts"/> is spent, is
        /// removed and reported via <paramref name="giveUp"/>. Neither
        /// callback may re-enter this ledger (both fire mid-iteration).
        /// </summary>
        public void Tick(float now, Action<long, T> retry, Action<long, T> giveUp)
        {
            List<long> due = null;
            foreach (var kv in _pending)
            {
                if (now < kv.Value.Deadline) continue;
                (due ?? (due = new List<long>())).Add(kv.Key);
            }
            if (due == null) return;

            foreach (var id in due)
            {
                var request = _pending[id];
                if (request.Attempts >= _maxAttempts)
                {
                    _pending.Remove(id);
                    giveUp(id, request.Payload);
                }
                else
                {
                    request.Attempts++;
                    request.Deadline = now + _timeoutSeconds;
                    retry(id, request.Payload);
                }
            }
        }
    }
}
