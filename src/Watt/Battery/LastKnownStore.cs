using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Watt.Models;

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
            get
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "Watt", "last-known.json");
            }
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
                _entries[device.Id] = new KnownDeviceEntry
                {
                    Id = device.Id,
                    Name = device.Name,
                    Kind = device.Kind.ToString(),
                    Percent = device.BatteryPercent.Value,
                    Charging = device.IsCharging,
                    Address = device.Address,
                    PluginId = pluginId,
                    SavedAtTicksUtc = savedAtUtc.ToUniversalTime().Ticks
                };
                SaveLocked();
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

        private static PeripheralDevice ToDevice(KnownDeviceEntry entry)
        {
            DeviceKind kind;
            if (!Enum.TryParse(entry.Kind, out kind)) kind = DeviceKind.Other;
            return new PeripheralDevice
            {
                Id = entry.Id,
                Name = entry.Name,
                Kind = kind,
                BatteryPercent = entry.Percent,
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
                    foreach (var entry in file.Devices)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                        if (DateTime.UtcNow - entry.SavedAtUtc > MaxAge) continue;
                        _entries[entry.Id] = entry;
                    }
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

                var directory = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using (var stream = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(KnownDeviceCacheFile));
                    serializer.WriteObject(stream, file);
                    File.WriteAllBytes(CachePath, stream.ToArray());
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
