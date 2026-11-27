using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Watt.Models;
using Watt.Plugins.Support;

namespace Watt.Settings
{
    /// <summary>
    /// Per-device user preferences: custom display names and devices hidden from the panel.
    /// Stored beside the last-known battery cache so both survive restarts; the plugin host keeps
    /// reading a hidden device, but the panel no longer shows it. Hidden devices can be restored
    /// from the tray menu.
    /// </summary>
    internal sealed class DevicePreferences
    {
        private const int MaxNameLength = 40;

        private readonly object _sync = new object();
        private readonly string _path;
        private readonly Dictionary<string, string> _names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _hidden =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string> _log;
        private bool _loaded;

        public DevicePreferences(Action<string> log) : this(DefaultPath(), log)
        {
        }

        internal DevicePreferences(string path, Action<string> log)
        {
            _path = path;
            _log = log;
        }

        internal static string DefaultPath()
        {
            return WattPaths.DataFile("devices.json");
        }

        /// <summary>Applies stored names and removes hidden devices from a panel snapshot.
        /// The input readings are not modified: the service keeps its own snapshot objects.</summary>
        public IReadOnlyList<PeripheralDevice> Apply(IReadOnlyList<PeripheralDevice> devices)
        {
            var result = new List<PeripheralDevice>();
            if (devices == null) return result;

            lock (_sync)
            {
                EnsureLoaded();
                foreach (var device in devices)
                {
                    if (device == null || string.IsNullOrEmpty(device.Id)) continue;
                    if (_hidden.Contains(device.Id)) continue;

                    var copy = Clone(device);
                    string name;
                    if (_names.TryGetValue(copy.Id, out name) && !string.IsNullOrWhiteSpace(name))
                        copy.Name = name;
                    result.Add(copy);
                }
            }
            return result;
        }

        /// <summary>Number of devices currently hidden from the panel.</summary>
        public int HiddenCount
        {
            get
            {
                lock (_sync)
                {
                    EnsureLoaded();
                    return _hidden.Count;
                }
            }
        }

        /// <summary>Stores a custom name. An empty name removes the override.</summary>
        public void Rename(string deviceId, string name)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            var normalized = SanitizeName(name);

            lock (_sync)
            {
                EnsureLoaded();
                if (normalized.Length == 0)
                {
                    if (!_names.Remove(deviceId)) return;
                }
                else
                {
                    string existing;
                    if (_names.TryGetValue(deviceId, out existing) &&
                        string.Equals(existing, normalized, StringComparison.Ordinal))
                        return;
                    _names[deviceId] = normalized;
                }
                SaveLocked();
            }
        }

        /// <summary>Hides a device from the panel. The preference survives restarts.</summary>
        public void Hide(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            lock (_sync)
            {
                EnsureLoaded();
                if (!_hidden.Add(deviceId)) return;
                SaveLocked();
            }
        }

        /// <summary>Shows every hidden device again. Returns true when something changed.</summary>
        public bool RestoreHidden()
        {
            lock (_sync)
            {
                EnsureLoaded();
                if (_hidden.Count == 0) return false;
                _hidden.Clear();
                SaveLocked();
                return true;
            }
        }

        private static string SanitizeName(string name)
        {
            if (name == null) return string.Empty;
            var builder = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                builder.Append(char.IsControl(c) ? ' ' : c);
            }

            var trimmed = builder.ToString().Trim();
            if (trimmed.Length > MaxNameLength) trimmed = trimmed.Substring(0, MaxNameLength).Trim();
            return trimmed;
        }

        private static PeripheralDevice Clone(PeripheralDevice device)
        {
            return new PeripheralDevice
            {
                Id = device.Id,
                Name = device.Name,
                Kind = device.Kind,
                BatteryPercent = device.BatteryPercent,
                IsCharging = device.IsCharging,
                IsConnected = device.IsConnected,
                SourceId = device.SourceId,
                Address = device.Address
            };
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(_path)) return;
                var serializer = new DataContractJsonSerializer(typeof(PreferencesFile));
                PreferencesFile file;
                using (var stream = File.OpenRead(_path))
                {
                    file = serializer.ReadObject(stream) as PreferencesFile;
                }
                if (file == null) return;

                if (file.Names != null)
                {
                    foreach (var entry in file.Names)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
                        var name = SanitizeName(entry.Name);
                        if (name.Length == 0) continue;
                        _names[entry.Id] = name;
                    }
                }
                if (file.Hidden != null)
                {
                    foreach (var id in file.Hidden)
                    {
                        if (!string.IsNullOrEmpty(id)) _hidden.Add(id);
                    }
                }
            }
            catch (Exception ex)
            {
                SafeLog("device preferences load failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void SaveLocked()
        {
            try
            {
                var file = new PreferencesFile
                {
                    Names = new List<NameEntry>(),
                    Hidden = new List<string>()
                };
                foreach (var pair in _names)
                {
                    file.Names.Add(new NameEntry { Id = pair.Key, Name = pair.Value });
                }
                foreach (var id in _hidden) file.Hidden.Add(id);

                using (var stream = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(PreferencesFile));
                    serializer.WriteObject(stream, file);
                    AtomicFile.WriteAllBytes(_path, stream.ToArray());
                }
            }
            catch (Exception ex)
            {
                SafeLog("device preferences save failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void SafeLog(string message)
        {
            var log = _log;
            if (log == null) return;
            try { log(message); } catch { }
        }

        [DataContract]
        private sealed class PreferencesFile
        {
            [DataMember(Name = "names")]
            public List<NameEntry> Names { get; set; }

            [DataMember(Name = "hidden")]
            public List<string> Hidden { get; set; }
        }

        [DataContract]
        private sealed class NameEntry
        {
            [DataMember(Name = "id")]
            public string Id { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }
        }
    }
}
