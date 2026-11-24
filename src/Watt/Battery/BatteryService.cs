using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Watt.Models;
using Watt.Plugins;
using Watt.Plugins.Support;

namespace Watt.Battery
{
    /// <summary>
    /// Runs every loaded battery plugin in parallel, merges their observations, and keeps each
    /// peripheral's row alive with the last-known reading while it is asleep.
    ///
    /// Per-plugin scan semantics:
    ///   PluginReadResult.Absent()           transport gone; after 3 consecutive absent scans the
    ///                                       plugin's cached devices are forgotten.
    ///   PluginReadResult.Empty()/Observed() transport present. Readings with a percent refresh
    ///                                       the cache; readings without a percent are filled from
    ///                                       the cache (sleeping device stays visible). Devices the
    ///                                       plugin no longer observes are removed.
    ///   timeout / exception                  keep the plugin's cached rows for this scan, do not
    ///                                       count it as absent (a slow device is not unplugged).
    /// </summary>
    internal sealed class BatteryService : IDisposable
    {
        private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(60);
        private const int MaxAbsentScans = 3;
        private const int MinPluginTimeoutMs = 500;
        private const int MaxPluginTimeoutMs = 15000;

        private sealed class PluginOutcome
        {
            internal LoadedPlugin Loaded;
            internal PluginReadResult Result;
            internal string Error;
        }

        private sealed class Candidate
        {
            internal PeripheralDevice Device;
            internal int Priority;
            internal bool IsFresh;
        }

        private readonly object _sync = new object();
        private readonly IReadOnlyList<LoadedPlugin> _plugins;
        private readonly Dictionary<string, int> _absentScans = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly Timer _timer;
        private readonly Action<string> _log;

        private IReadOnlyList<PeripheralDevice> _devices = new PeripheralDevice[0];
        private Task _inflight;
        private bool _started;
        private bool _disposed;
        private bool _isScanning;

        public BatteryService(IReadOnlyList<LoadedPlugin> plugins) : this(plugins, null)
        {
        }

        public BatteryService(IReadOnlyList<LoadedPlugin> plugins, Action<string> log)
        {
            _plugins = plugins ?? new LoadedPlugin[0];
            _log = log;
            _timer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public event EventHandler Changed;

        public IReadOnlyList<PeripheralDevice> Devices
        {
            get
            {
                lock (_sync) return _devices;
            }
        }

        public bool IsScanning
        {
            get
            {
                lock (_sync) return _isScanning;
            }
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started || _disposed) return;
                _started = true;
            }

            try
            {
                _timer.Change(TimeSpan.Zero, ScanInterval);
            }
            catch
            {
                // a disposed timer must not break startup
            }
        }

        /// <summary>Raises a scan. Concurrent calls share the in-flight scan. The task never faults.</summary>
        public Task RefreshAsync(bool force)
        {
            return RefreshAsync(force, false);
        }

        /// <summary>
        /// Raises a scan. When <paramref name="removeDisconnected"/> is true (manual refresh),
        /// devices observed as disconnected are removed from the cache immediately instead of
        /// keeping their last-known row; a scan already in flight is awaited first so the manual
        /// refresh always reloads the current device list.
        /// </summary>
        public Task RefreshAsync(bool force, bool removeDisconnected)
        {
            lock (_sync)
            {
                if (_disposed) return Task.FromResult(0);

                if (_inflight != null && !_inflight.IsCompleted)
                {
                    if (!removeDisconnected) return _inflight;

                    var previous = _inflight;
                    return previous.ContinueWith(
                        delegate { return RefreshAsync(true, true); },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default).Unwrap();
                }

                _inflight = Task.Run(() => RefreshCoreAsync(removeDisconnected));
                return _inflight;
            }
        }

        private async Task RefreshCoreAsync(bool removeDisconnected)
        {
            try
            {
                var cancellationToken = _cancellation.Token;
                SetScanning(true);
                var snapshot = await ScanAsync(cancellationToken, removeDisconnected).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested) ReplaceDevices(snapshot);
            }
            catch
            {
                // Never let a plugin failure escape to the caller.
            }
            finally
            {
                SetScanning(false);
            }
        }

        private async Task<IReadOnlyList<PeripheralDevice>> ScanAsync(CancellationToken cancellationToken,
            bool removeDisconnected)
        {
            if (_plugins.Count == 0) return new PeripheralDevice[0];

            var tasks = new Task<PluginOutcome>[_plugins.Count];
            for (var i = 0; i < _plugins.Count; i++)
            {
                tasks[i] = ScanPluginAsync(_plugins[i], cancellationToken);
            }

            PluginOutcome[] outcomes;
            try
            {
                outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                outcomes = new PluginOutcome[0];
            }

            var candidates = new List<Candidate>(16);
            foreach (var outcome in outcomes)
            {
                if (outcome == null || outcome.Loaded == null) continue;
                var plugin = outcome.Loaded.Plugin;

                if (outcome.Result == null)
                {
                    // Timeout/exception: a slow device is not evidence of removal. Keep its rows.
                    AddCachedCandidates(candidates, plugin);
                    continue;
                }

                if (!outcome.Result.HardwarePresent)
                {
                    if (removeDisconnected)
                    {
                        LastKnownStore.RemoveByPlugin(plugin.Id);
                        ResetAbsent(plugin.Id);
                        continue;
                    }

                    var absent = IncrementAbsent(plugin.Id);
                    if (absent >= MaxAbsentScans)
                    {
                        LastKnownStore.RemoveByPlugin(plugin.Id);
                        ResetAbsent(plugin.Id);
                    }
                    else
                    {
                        AddCachedCandidates(candidates, plugin);
                    }
                    continue;
                }

                ResetAbsent(plugin.Id);

                foreach (var reading in outcome.Result.Devices)
                {
                    if (reading == null || string.IsNullOrEmpty(reading.Id)) continue;

                    if (reading.BatteryPercent.HasValue)
                    {
                        reading.SourceId = plugin.Id;
                        LastKnownStore.Save(reading, plugin.Id, DateTime.UtcNow);
                        candidates.Add(new Candidate { Device = reading, Priority = plugin.Priority, IsFresh = true });
                        continue;
                    }

                    // Observed but asleep / no fresh value: substitute the last-known reading.
                    // On manual refresh, a device the plugin reports as disconnected is removed
                    // from the cache instead of being shown again.
                    if (removeDisconnected && reading.IsConnected == false)
                    {
                        LastKnownStore.Remove(reading.Id);
                        continue;
                    }

                    DateTime savedAt;
                    string cachedPluginId;
                    var cached = LastKnownStore.TryGet(reading.Id, out savedAt, out cachedPluginId);
                    if (cached == null || !cached.BatteryPercent.HasValue) continue;

                    var device = new PeripheralDevice
                    {
                        Id = reading.Id,
                        Name = string.IsNullOrWhiteSpace(reading.Name) ? cached.Name : reading.Name,
                        Kind = reading.Kind != DeviceKind.Other ? reading.Kind : cached.Kind,
                        Address = string.IsNullOrWhiteSpace(reading.Address) ? cached.Address : reading.Address,
                        BatteryPercent = cached.BatteryPercent,
                        IsCharging = cached.IsCharging,
                        IsConnected = reading.IsConnected,
                        SourceId = cached.SourceId ?? cachedPluginId ?? plugin.Id
                    };
                    candidates.Add(new Candidate { Device = device, Priority = plugin.Priority, IsFresh = false });
                }
            }

            return BuildSnapshot(candidates);
        }

        private async Task<PluginOutcome> ScanPluginAsync(LoadedPlugin loaded, CancellationToken cancellationToken)
        {
            var outcome = new PluginOutcome { Loaded = loaded };
            var plugin = loaded.Plugin;
            var timeout = plugin.SuggestedTimeoutMs;
            if (timeout < MinPluginTimeoutMs) timeout = MinPluginTimeoutMs;
            if (timeout > MaxPluginTimeoutMs) timeout = MaxPluginTimeoutMs;

            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task<PluginReadResult> task;
            try
            {
                task = plugin.ReadAsync(loaded.Context, linked.Token);
            }
            catch (Exception ex)
            {
                linked.Dispose();
                outcome.Error = ex.GetType().Name + ": " + ex.Message;
                SafeLog("plugin " + plugin.Id + " threw: " + outcome.Error);
                return outcome;
            }

            if (task == null)
            {
                linked.Dispose();
                outcome.Error = "plugin returned a null task";
                return outcome;
            }

            try
            {
                var completed = await Task.WhenAny(task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
                if (completed != task)
                {
                    try { linked.Cancel(); } catch { }
                    ProviderUtil.Observe(task);
                    outcome.Error = "timeout after " + timeout + " ms";
                    SafeLog("plugin " + plugin.Id + " timed out after " + timeout + " ms");
                    return outcome;
                }

                outcome.Result = await task.ConfigureAwait(false);
                return outcome;
            }
            catch (OperationCanceledException)
            {
                outcome.Error = "canceled";
                return outcome;
            }
            catch (Exception ex)
            {
                outcome.Error = ex.GetType().Name + ": " + ex.Message;
                SafeLog("plugin " + plugin.Id + " failed: " + outcome.Error);
                return outcome;
            }
            finally
            {
                try { linked.Dispose(); } catch { }
            }
        }

        private void AddCachedCandidates(List<Candidate> candidates, IBatteryPlugin plugin)
        {
            foreach (var cached in LastKnownStore.GetByPlugin(plugin.Id))
            {
                if (!cached.BatteryPercent.HasValue) continue;
                candidates.Add(new Candidate
                {
                    Device = cached,
                    Priority = plugin.Priority,
                    IsFresh = false
                });
            }
        }

        private static IReadOnlyList<PeripheralDevice> BuildSnapshot(List<Candidate> candidates)
        {
            // Lower priority number wins. Fresh readings beat cached ones even from a lower
            // priority source (a new value is more useful than an old one).
            candidates.Sort(delegate(Candidate left, Candidate right)
            {
                var byPriority = left.Priority.CompareTo(right.Priority);
                if (byPriority != 0) return byPriority;
                return right.IsFresh.CompareTo(left.IsFresh);
            });

            var merged = new List<Candidate>(candidates.Count);
            foreach (var candidate in candidates)
            {
                var target = FindMergeTarget(merged, candidate.Device);
                if (target == null)
                {
                    merged.Add(candidate);
                    continue;
                }

                var existing = target.Device;
                if (!target.IsFresh && candidate.IsFresh)
                {
                    existing.BatteryPercent = candidate.Device.BatteryPercent;
                    existing.IsCharging = candidate.Device.IsCharging;
                    existing.SourceId = candidate.Device.SourceId;
                    target.IsFresh = true;
                    target.Priority = Math.Min(target.Priority, candidate.Priority);
                }
                else if (target.IsFresh && candidate.IsFresh && candidate.Priority < target.Priority)
                {
                    existing.BatteryPercent = candidate.Device.BatteryPercent;
                    existing.IsCharging = candidate.Device.IsCharging;
                    existing.SourceId = candidate.Device.SourceId;
                    target.Priority = candidate.Priority;
                }

                if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(candidate.Device.Name))
                    existing.Name = candidate.Device.Name;
                if (existing.Kind == DeviceKind.Other && candidate.Device.Kind != DeviceKind.Other)
                    existing.Kind = candidate.Device.Kind;
                if (string.IsNullOrWhiteSpace(existing.Address) && !string.IsNullOrWhiteSpace(candidate.Device.Address))
                    existing.Address = candidate.Device.Address;
                if (!existing.IsConnected.HasValue && candidate.Device.IsConnected.HasValue)
                    existing.IsConnected = candidate.Device.IsConnected;
            }

            var devices = new List<PeripheralDevice>(merged.Count);
            foreach (var candidate in merged)
            {
                if (candidate.Device.BatteryPercent.HasValue) devices.Add(candidate.Device);
            }

            devices.Sort(CompareDevices);
            return devices.ToArray();
        }

        private static Candidate FindMergeTarget(List<Candidate> merged, PeripheralDevice device)
        {
            var address = DeviceClassifier.NormalizeAddress(device.Address);
            if (address != null)
            {
                foreach (var candidate in merged)
                {
                    var other = DeviceClassifier.NormalizeAddress(candidate.Device.Address);
                    if (other != null && string.Equals(other, address, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }

            var name = DeviceClassifier.NormalizeName(device.Name);
            if (name.Length >= 4)
            {
                foreach (var candidate in merged)
                {
                    var other = DeviceClassifier.NormalizeName(candidate.Device.Name);
                    if (string.Equals(other, name, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }

            return null;
        }

        private static int CompareDevices(PeripheralDevice left, PeripheralDevice right)
        {
            var byKind = DeviceClassifier.SortRank(left.Kind).CompareTo(DeviceClassifier.SortRank(right.Kind));
            if (byKind != 0) return byKind;

            var byName = string.Compare(left.Name ?? string.Empty, right.Name ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            if (byName != 0) return byName;

            return string.Compare(left.Id ?? string.Empty, right.Id ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        private void ReplaceDevices(IReadOnlyList<PeripheralDevice> snapshot)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _devices = snapshot ?? new PeripheralDevice[0];
            }
            RaiseChanged();
        }

        private void SetScanning(bool scanning)
        {
            var changed = false;
            lock (_sync)
            {
                if (_disposed || _isScanning == scanning) return;
                _isScanning = scanning;
                changed = true;
            }
            if (changed) RaiseChanged();
        }

        private int IncrementAbsent(string pluginId)
        {
            lock (_sync)
            {
                int count;
                _absentScans.TryGetValue(pluginId, out count);
                count++;
                _absentScans[pluginId] = count;
                return count;
            }
        }

        private void ResetAbsent(string pluginId)
        {
            lock (_sync)
            {
                _absentScans.Remove(pluginId);
            }
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // A misbehaving subscriber (e.g. a closed window) must not break the scan loop.
            }
        }

        private void OnTimerTick(object state)
        {
            try
            {
                ProviderUtil.Observe(RefreshAsync(false));
            }
            catch
            {
                // ignore
            }
        }

        private void SafeLog(string message)
        {
            var log = _log;
            if (log == null) return;
            try
            {
                log(message);
            }
            catch
            {
                // ignore
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _devices = new PeripheralDevice[0];
            }

            try { _timer.Dispose(); } catch { }
            try { _cancellation.Cancel(); } catch { }
            try { _cancellation.Dispose(); } catch { }
        }
    }
}

