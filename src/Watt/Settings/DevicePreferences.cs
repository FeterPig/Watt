using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Watt.Models;

namespace Watt.Settings
{
    /// <summary>
    /// Per-device user preferences: custom display names and devices hidden from the panel.
    /// Stored beside the last-known battery cache so both survive restarts; the plugin host keeps
    /// reading a hidden device, but the panel no longer shows it.
    /// </summary>
    internal sealed class DevicePreferences
    {
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
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Watt", "devices.json");
        }

        /// <summary>Applies stored names and removes hidden devices from a panel snapshot.</summary>
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

                    string name;
                    if (_names.TryGetValue(device.Id, out name) && !string.IsNullOrWhiteSpace(name))
                        device.Name = name;
                    result.Add(device);
                }
            }
            return result;
        }

        /// <summary>Stores a custom name. An empty name removes the override.</summary>
        public void Rename(string deviceId, string name)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            var normalized = name == null ? string.Empty : name.Trim();
            if (normalized.Length > 40) normalized = normalized.Substring(0, 40);

            lock (_sync)
            {
                EnsureLoaded();
                if (normalized.Length == 0)
                {
                    if (!_names.Remove(deviceId)) return;
                }
                else
                {
                    if (_names.TryGetValue(deviceId, out var existing) &&
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
                        if (entry == null || string.IsNullOrEmpty(entry.Id) ||
                            string.IsNullOrWhiteSpace(entry.Name)) continue;
                        _names[entry.Id] = entry.Name.Trim();
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

                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var temporary = _path + ".tmp";
                var serializer = new DataContractJsonSerializer(typeof(PreferencesFile));
                using (var stream = File.Create(temporary))
                {
                    serializer.WriteObject(stream, file);
                }
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(temporary, _path);
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
