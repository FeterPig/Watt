using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Watt.Plugins.Support;

namespace Watt.Plugins.BleGatt
{
    /// <summary>
    /// Some BLE peripherals expose a broken Battery Level characteristic: every 0x2A19 read
    /// returns the previous value + 1 instead of the remaining charge. The value looks plausible
    /// and climbs to 100, so a host that trusts every read would show a fake increasing battery.
    ///
    /// This guard remembers the first plausible level for each BLE device and detects the
    /// "consecutive +1" signature. Once detected it keeps returning the stable baseline; a later
    /// lower reading is treated as a real discharge and becomes the new baseline. State is stored
    /// next to the other Watt user data so a restart does not resurrect the fake climb.
    ///
    /// A rising level while the device reports charging is normal, so the caller passes that flag
    /// and the guard never learns the counter quirk from a charging device.
    /// </summary>
    internal static class BleBatteryGuard
    {
        private const int RequiredSequentialIncrements = 2;
        private static readonly TimeSpan SequentialWindow = TimeSpan.FromMinutes(3);
        private static readonly object Sync = new object();
        private static Dictionary<string, DeviceState> _states;
        private static bool _loaded;

        private static string StatePath
        {
            get { return WattPaths.DataFile("ble-battery-guard.txt"); }
        }

        /// <summary>True after this device has shown the consecutive +1 counter signature.</summary>
        internal static bool IsQuirked(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (Sync)
            {
                EnsureLoaded();
                DeviceState state;
                return _states.TryGetValue(key, out state) && state.Quirked;
            }
        }

        /// <summary>
        /// Returns the value Watt should display for a raw GATT read. Returns null for a reserved
        /// value above 100 unless this device already has a learned counter quirk, in which case
        /// the learned baseline is returned (the device can continue counting past 100).
        /// </summary>
        internal static int? NormalizeOrIgnore(string key, int raw, bool charging = false)
        {
            if (raw < 0) return null;
            if (raw <= 100) return Normalize(key, raw, charging);

            if (string.IsNullOrEmpty(key)) return null;
            lock (Sync)
            {
                EnsureLoaded();
                DeviceState state;
                if (_states.TryGetValue(key, out state) && state.Quirked) return state.Baseline;
            }
            return null;
        }

        /// <summary>Returns the value Watt should display for a raw GATT read.</summary>
        internal static int Normalize(string key, int raw, bool charging = false)
        {
            if (string.IsNullOrEmpty(key)) return raw;
            if (raw < 0) raw = 0;
            if (raw > 100) raw = 100;

            lock (Sync)
            {
                EnsureLoaded();
                DeviceState state;
                if (!_states.TryGetValue(key, out state))
                {
                    var createdAt = DateTime.UtcNow;
                    _states[key] = new DeviceState
                    {
                        Baseline = raw,
                        LastRaw = raw,
                        LastSeenUtc = createdAt,
                        LastSeenUtcTicks = createdAt.Ticks,
                        SequentialIncrements = 0,
                        Quirked = false
                    };
                    return raw;
                }

                var now = DateTime.UtcNow;

                if (state.Quirked)
                {
                    if (raw < state.Baseline)
                    {
                        // A real discharge (or a counter reset) is useful information.
                        state.Baseline = raw;
                        state.LastRaw = raw;
                        Touch(state, now);
                        SaveLocked();
                        return raw;
                    }
                    return state.Baseline;
                }

                if (charging)
                {
                    // While charging, a level that climbs by one is the normal direction. Reset
                    // the learning window instead of misclassifying the device as a counter.
                    state.Baseline = raw;
                    state.LastRaw = raw;
                    state.SequentialIncrements = 0;
                    Touch(state, now);
                    return raw;
                }

                if (raw <= state.LastRaw)
                {
                    // A decrease/same value is normal. A lower value becomes the new baseline so
                    // the guard does not freeze an outdated percentage forever.
                    if (raw < state.Baseline) state.Baseline = raw;
                    state.SequentialIncrements = 0;
                    state.LastRaw = raw;
                    Touch(state, now);
                    return raw;
                }

                var sequential = raw == state.LastRaw + 1 &&
                                 now - state.LastSeenUtc <= SequentialWindow;
                state.SequentialIncrements = sequential ? state.SequentialIncrements + 1 : 0;
                if (!sequential) state.Baseline = raw;

                state.LastRaw = raw;
                Touch(state, now);

                if (state.SequentialIncrements >= RequiredSequentialIncrements)
                {
                    state.Quirked = true;
                    SaveLocked();
                    return state.Baseline;
                }

                return raw;
            }
        }

        /// <summary>Test hook: forgets the in-memory learned state.</summary>
        internal static void ResetForTests()
        {
            lock (Sync)
            {
                _states = null;
                _loaded = false;
            }
        }

        private static void Touch(DeviceState state, DateTime now)
        {
            state.LastSeenUtc = now;
            state.LastSeenUtcTicks = now.Ticks;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _states = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(StatePath)) return;
                foreach (var line in File.ReadAllLines(StatePath))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#' || line[0] == '\uFEFF') continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 5) continue;

                    int baseline, lastRaw;
                    long ticks;
                    bool quirked;
                    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out baseline) ||
                        !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out lastRaw) ||
                        !bool.TryParse(parts[3], out quirked) ||
                        !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                        continue;

                    var now = DateTime.UtcNow;
                    var seenUtc = now;
                    if (ticks > 0 && ticks <= now.Ticks)
                    {
                        try { seenUtc = new DateTime(ticks, DateTimeKind.Utc); }
                        catch { seenUtc = now; }
                    }

                    _states[parts[0]] = new DeviceState
                    {
                        Baseline = Clamp(baseline),
                        LastRaw = Clamp(lastRaw),
                        Quirked = quirked,
                        LastSeenUtc = seenUtc,
                        LastSeenUtcTicks = ticks > 0 ? ticks : now.Ticks,
                        SequentialIncrements = 0
                    };
                }
            }
            catch
            {
                _states.Clear();
            }
        }

        private static void SaveLocked()
        {
            try
            {
                var builder = new StringBuilder();
                builder.AppendLine("# Watt BLE battery guard; delete this file to forget learned devices.");
                foreach (var pair in _states)
                {
                    builder.Append(pair.Key).Append('\t')
                        .Append(pair.Value.Baseline).Append('\t')
                        .Append(pair.Value.LastRaw).Append('\t')
                        .Append(pair.Value.Quirked ? "true" : "false").Append('\t')
                        .Append(pair.Value.LastSeenUtcTicks)
                        .AppendLine();
                }

                AtomicFile.WriteAllText(StatePath, builder.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // A diagnostics cache is not worth breaking a battery scan over.
            }
        }

        private static int Clamp(int value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }

        private sealed class DeviceState
        {
            public int Baseline;
            public int LastRaw;
            public int SequentialIncrements;
            public bool Quirked;
            public DateTime LastSeenUtc;
            public long LastSeenUtcTicks;
        }
    }
}
