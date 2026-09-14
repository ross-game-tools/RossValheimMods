using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using RossQoL.Core.Framework;

namespace RossQoL.Game.Framework
{
    /// <summary>
    /// Reloads the config file when it is edited on disk, so changes apply
    /// without a restart. Reloading raises SettingChanged for every changed
    /// entry and then ConfigReloaded, on which Jotunn pushes changed
    /// admin-only entries from a server to every connected client.
    ///
    /// Detection is a write-time poll plus a best-effort FileSystemWatcher;
    /// see ConfigReloadSchedule for why the poll is the one relied on. The
    /// reload itself runs on the main thread from Pump, because setting
    /// handlers touch game state.
    /// </summary>
    internal sealed class ConfigHotReload : IDisposable
    {
        private readonly ConfigFile _config;
        private readonly ManualLogSource _log;
        private readonly ConfigReloadSchedule _schedule;
        private FileSystemWatcher _watcher;

        /// <summary>Set from the watcher's thread; consumed on the main thread.</summary>
        private volatile bool _watcherFired;

        public ConfigHotReload(ConfigFile config, ManualLogSource log)
        {
            _config = config;
            _log = log;
            _schedule = new ConfigReloadSchedule(ReadWriteTime());

            try
            {
                string dir = Path.GetDirectoryName(config.ConfigFilePath);
                _watcher = new FileSystemWatcher(dir, Path.GetFileName(config.ConfigFilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                };
                _watcher.Changed += OnWatcherEvent;
                _watcher.Created += OnWatcherEvent;
                _watcher.Renamed += OnWatcherEvent;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                _watcher = null;
                _log.LogInfo($"Config reload: file watcher unavailable, polling only ({ex.Message}).");
            }

            _log.LogInfo($"Config reload: watching {config.ConfigFilePath} for edits.");
        }

        private void OnWatcherEvent(object sender, FileSystemEventArgs e) => _watcherFired = true;

        /// <summary>Call every frame from the main thread. Cheap when nothing changed.</summary>
        public void Pump()
        {
            var now = DateTime.UtcNow;

            if (_watcherFired)
            {
                _watcherFired = false;
                _schedule.NotifyChanged(now);
            }

            if (_schedule.TakePollDue(now))
                _schedule.ObserveWriteTime(ReadWriteTime(), now);

            if (!_schedule.TakeDueReload(now)) return;

            try
            {
                Reload();
            }
            catch (Exception ex)
            {
                _schedule.ReloadFailed(DateTime.UtcNow);
                _log.LogWarning($"Config reload failed, retrying shortly: {ex.Message}");
            }
        }

        private void Reload()
        {
            var before = Snapshot();

            // Reload sets every entry; saving on each set would rewrite the
            // file mid-reload and be detected as a fresh edit.
            bool saveOnSet = _config.SaveOnConfigSet;
            _config.SaveOnConfigSet = false;
            try
            {
                _config.Reload();
            }
            finally
            {
                _config.SaveOnConfigSet = saveOnSet;
            }
            _schedule.Reloaded(DateTime.UtcNow, ReadWriteTime());

            var changes = new List<string>();
            foreach (var pair in Snapshot())
                if (!before.TryGetValue(pair.Key, out var old) || old != pair.Value)
                    changes.Add($"[{pair.Key.Section}] {pair.Key.Key} {old ?? "?"} -> {pair.Value}");

            _log.LogInfo(changes.Count == 0
                ? "Config reload: no changes."
                : "Config reload: " + string.Join(", ", changes));
        }

        private Dictionary<ConfigDefinition, string> Snapshot()
        {
            var values = new Dictionary<ConfigDefinition, string>();
            foreach (var pair in _config)
            {
                try
                {
                    values[pair.Key] = pair.Value.GetSerializedValue();
                }
                catch
                {
                    // One unreadable entry must not stop the change log.
                }
            }
            return values;
        }

        private DateTime ReadWriteTime()
        {
            try
            {
                // A missing file reads as 1601-01-01 rather than throwing.
                var written = File.GetLastWriteTimeUtc(_config.ConfigFilePath);
                return written.Year < 1980 ? DateTime.MinValue : written;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public void Dispose()
        {
            if (_watcher == null) return;
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
            catch
            {
                // Shutting down.
            }
            _watcher = null;
        }
    }
}
