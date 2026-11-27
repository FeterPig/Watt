using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Watt.Models;
using Watt.Plugins.Support;

namespace Watt.Battery
{
    /// <summary>
    /// Per-device last-known battery cache. The plugin host uses it to keep a peripheral's row
    /// visible while the device sleeps or a plugin has no fresh reading. Entries are owned by the
    /// plugin that produced the reading; when a plugin's transport disappears for several scans
    /// the host removes that plugin's entries. The cache also survives app restarts.
    /// </summary>
    internal static class LastKnownStore
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
        private static readonly object Sync = new object();
        private static Dictionary<string, KnownDeviceEntry> _entries;
        private static bool _loaded;

        private static string CachePath
        {
            get { return WattPaths.DataFile("last-known.json"); }
        }

        internal static PeripheralDevice TryGet(string id, out DateTime savedAtUtc, out string pluginId)
        {
            savedAtUtc = DateTime.MinValue;
            pluginId = null;
            if (string.IsNullOrEmpty(id)) return null;

            lock (Sync)
            {
                EnsureLoaded();
                KnownDeviceEntry entry;
                if (!_entries.TryGetValue(id, out entry)) return null;

                savedAtUtc = entry.SavedAtUtc;
                if (DateTime.UtcNow - savedAtUtc > MaxAge)
                {
                    _entries.Remove(id);
                    SaveLocked();
                    return null;
                }

                pluginId = entry.PluginId;
                return ToDevice(entry);
            }
        }

        internal static void Save(PeripheralDevice device, string pluginId, DateTime savedAtUtc)
        {
            if (device == null || string.IsNullOrEmpty(device.Id) || !device.BatteryPercent.HasValue) return;

            lock (Sync)
            {
                EnsureLoaded();
                if (UpsertLocked(device, string.IsNullOrEmpty(device.SourceId) ? pluginId : device.SourceId,
                        savedAtUtc))
                    SaveLocked();
            }
        }

        /// <summary>
        /// Saves the merged winners of one scan with a single file write. Each device's
        /// <see cref="PeripheralDevice.SourceId"/> becomes the cache owner, so the row that is
        /// displayed is the row that survives a restart.
        /// </summary>
        internal static void SaveMany(IReadOnlyList<PeripheralDevice> devices, DateTime savedAtUtc)
        {
            if (devices == null || devices.Count == 0) return;

            lock (Sync)
            {
                EnsureLoaded();
                var changed = false;
                for (var i = 0; i < devices.Count; i++)
                {
                    var device = devices[i];
                    if (device == null || string.IsNullOrEmpty(device.Id) || !device.BatteryPercent.HasValue)
                        continue;
                    if (UpsertLocked(device, device.SourceId, savedAtUtc)) changed = true;
                }

                if (changed) SaveLocked();
            }
        }

        internal static void Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (Sync)
            {
                EnsureLoaded();
                if (_entries.Remove(id)) SaveLocked();
            }
        }

        internal static void RemoveByPlugin(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return;
            lock (Sync)
            {
                EnsureLoaded();
                var remove = new List<string>();
                foreach (var pair in _entries)
                {
                    if (string.Equals(pair.Value.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
                        remove.Add(pair.Key);
                }
                if (remove.Count == 0) return;
                foreach (var id in remove) _entries.Remove(id);
                SaveLocked();
            }
        }

        /// <summary>All cached devices owned by a plugin (used when a plugin read failed/timed out).</summary>
        internal static IReadOnlyList<PeripheralDevice> GetByPlugin(string pluginId)
        {
            var result = new List<PeripheralDevice>();
            if (string.IsNullOrEmpty(pluginId)) return result;
            lock (Sync)
            {
                EnsureLoaded();
                foreach (var entry in _entries.Values)
                {
                    if (!string.Equals(entry.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (DateTime.UtcNow - entry.SavedAtUtc > MaxAge) continue;
                    result.Add(ToDevice(entry));
                }
            }
            return result;
        }

        internal static IReadOnlyList<PeripheralDevice> Snapshot()
        {
            var result = new List<PeripheralDevice>();
            lock (Sync)
            {
                EnsureLoaded();
                foreach (var entry in _entries.Values)
                {
                    if (DateTime.UtcNow - entry.SavedAtUtc > MaxAge) continue;
                    result.Add(ToDevice(entry));
                }
            }
            result.Sort(delegate(PeripheralDevice left, PeripheralDevice right)
            {
                return string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        /// <summary>Test hook: drops the in-memory view so the next access reloads the file.</summary>
        internal static void ResetForTests()
        {
            lock (Sync)
            {
                _entries = null;
                _loaded = false;
            }
        }

        private static bool UpsertLocked(PeripheralDevice device, string pluginId, DateTime savedAtUtc)
        {
            var percent = device.BatteryPercent.Value;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            KnownDeviceEntry existing;
            if (string.IsNullOrEmpty(pluginId) && _entries.TryGetValue(device.Id, out existing))
                pluginId = existing.PluginId;

            _entries[device.Id] = new KnownDeviceEntry
            {
                Id = device.Id,
                Name = device.Name,
                Kind = device.Kind.ToString(),
                Percent = percent,
                Charging = device.IsCharging,
                Address = device.Address,
                PluginId = pluginId,
                SavedAtTicksUtc = savedAtUtc.ToUniversalTime().Ticks
            };
            return true;
        }

        private static PeripheralDevice ToDevice(KnownDeviceEntry entry)
        {
            DeviceKind kind;
            if (!Enum.TryParse(entry.Kind, out kind)) kind = DeviceKind.Other;

            var percent = entry.Percent;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            return new PeripheralDevice
            {
                Id = entry.Id,
                Name = entry.Name,
                Kind = kind,
                BatteryPercent = percent,
                IsCharging = entry.Charging,
                Address = entry.Address,
                SourceId = entry.PluginId
            };
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _entries = new Dictionary<string, KnownDeviceEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(CachePath)) return;
                var bytes = File.ReadAllBytes(CachePath);
                if (bytes.Length == 0) return;
                using (var stream = new MemoryStream(bytes))
                {
                    var serializer = new DataContractJsonSerializer(typeof(KnownDeviceCacheFile));
                    var file = serializer.ReadObject(stream) as KnownDeviceCacheFile;
                    if (file == null || file.Devices == null) return;
                    var pruned = false;
                    foreach (var entry in file.Devices)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                        if (DateTime.UtcNow - entry.SavedAtUtc > MaxAge)
                        {
                            pruned = true;
                            continue;
                        }
                        _entries[entry.Id] = entry;
                    }

                    // Drop stale entries from the file once, so a cache used by a long-lived
                    // install does not keep devices that expired weeks ago.
                    if (pruned) SaveLocked();
                }
            }
            catch
            {
                // A corrupt or unreadable cache must never break device scanning.
            }
        }

        private static void SaveLocked()
        {
            try
            {
                var file = new KnownDeviceCacheFile();
                foreach (var entry in _entries.Values)
                {
                    if (DateTime.UtcNow - entry.SavedAtUtc <= MaxAge) file.Devices.Add(entry);
                }

                using (var stream = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(KnownDeviceCacheFile));
                    serializer.WriteObject(stream, file);
                    AtomicFile.WriteAllBytes(CachePath, stream.ToArray());
                }
            }
            catch
            {
                // Diagnostics must not be able to take the app down; a missed cache write only
                // means the row disappears after a restart while the device is asleep.
            }
        }

        [DataContract]
        private sealed class KnownDeviceCacheFile
        {
            [DataMember(Name = "devices")]
            public List<KnownDeviceEntry> Devices { get; set; }

            public KnownDeviceCacheFile()
            {
                Devices = new List<KnownDeviceEntry>();
            }
        }

        [DataContract]
        private sealed class KnownDeviceEntry
        {
            [DataMember(Name = "id")] public string Id { get; set; }
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "kind")] public string Kind { get; set; }
            [DataMember(Name = "percent")] public int Percent { get; set; }
            [DataMember(Name = "charging")] public bool Charging { get; set; }
            [DataMember(Name = "address")] public string Address { get; set; }
            [DataMember(Name = "pluginId")] public string PluginId { get; set; }
            [DataMember(Name = "savedAtTicksUtc")] public long SavedAtTicksUtc { get; set; }

            public DateTime SavedAtUtc
            {
                get { return new DateTime(SavedAtTicksUtc, DateTimeKind.Utc); }
            }
        }
    }
}
