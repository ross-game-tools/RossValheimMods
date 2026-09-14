using System;

namespace RossQoL.Core.Framework
{
    /// <summary>
    /// When a config file edited on disk should be reloaded. Times are passed
    /// in, so the rules are tested without a clock or a file.
    ///
    /// Modelled on NoVikingLeftBehind's ConfigWatcher, which found that
    /// FileSystemWatcher never fires under the headless dedicated server:
    /// a write-time poll is the real detector, watcher events a bonus.
    ///
    /// - Changes are debounced: one save can touch the file several times.
    /// - The write time a reload leaves behind is adopted, so a reload does
    ///   not trigger itself. There is no ignore window after a reload: the
    ///   reload never writes the file, and a window would drop a real edit
    ///   saved a moment later.
    /// - A failed reload (file still locked, mid-rename) is retried.
    ///
    /// Not thread-safe: call only from the main thread.
    /// </summary>
    public sealed class ConfigReloadSchedule
    {
        public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(0.5);
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(0.5);
        public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

        private DateTime _lastKnownWriteUtc;
        private DateTime _dueUtc;
        private DateTime _lastPollUtc = DateTime.MinValue;
        private bool _pending;

        public ConfigReloadSchedule(DateTime initialWriteUtc) => _lastKnownWriteUtc = initialWriteUtc;

        /// <summary>True when it is time to read the file's write time again.</summary>
        public bool TakePollDue(DateTime nowUtc)
        {
            if (_lastPollUtc != DateTime.MinValue && nowUtc - _lastPollUtc < PollInterval) return false;
            _lastPollUtc = nowUtc;
            return true;
        }

        /// <param name="writeUtc">The file's last write time; DateTime.MinValue when unreadable.</param>
        public void ObserveWriteTime(DateTime writeUtc, DateTime nowUtc)
        {
            if (writeUtc == DateTime.MinValue || writeUtc == _lastKnownWriteUtc) return;
            _lastKnownWriteUtc = writeUtc;
            NotifyChanged(nowUtc);
        }

        /// <summary>The file changed: reload once it has been quiet for the debounce.</summary>
        public void NotifyChanged(DateTime nowUtc)
        {
            _pending = true;
            _dueUtc = nowUtc + Debounce;
        }

        /// <summary>True, once, when a change has settled and the file should be reloaded now.</summary>
        public bool TakeDueReload(DateTime nowUtc)
        {
            if (!_pending || nowUtc < _dueUtc) return false;
            _pending = false;
            return true;
        }

        /// <param name="writeUtc">The file's write time after reloading, which is not a new change.</param>
        public void Reloaded(DateTime nowUtc, DateTime writeUtc)
        {
            if (writeUtc != DateTime.MinValue) _lastKnownWriteUtc = writeUtc;
        }

        /// <summary>Try again later, unless a newer edit asks sooner.</summary>
        public void ReloadFailed(DateTime nowUtc)
        {
            _pending = true;
            _dueUtc = nowUtc + RetryDelay;
        }
    }
}
