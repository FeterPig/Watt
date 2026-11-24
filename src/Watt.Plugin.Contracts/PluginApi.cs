using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Watt.Models;

namespace Watt.Plugins
{
    /// <summary>Result of one plugin scan.</summary>
    public sealed class PluginReadResult
    {
        public PluginReadResult(bool hardwarePresent, IReadOnlyList<PeripheralDevice> devices)
        {
            HardwarePresent = hardwarePresent;
            Devices = devices ?? new PeripheralDevice[0];
        }

        /// <summary>False = the plugin's transport (radio/receiver/HID interface) is not available.
        /// After a few consecutive absent scans the host forgets that plugin's cached devices.</summary>
        public bool HardwarePresent { get; private set; }

        /// <summary>Observed devices. BatteryPercent may be null ("asleep, keep last known value").</summary>
        public IReadOnlyList<PeripheralDevice> Devices { get; private set; }

        /// <summary>Transport is gone (dongle unplugged, no radio).</summary>
        public static PluginReadResult Absent()
        {
            return new PluginReadResult(false, null);
        }

        /// <summary>Transport is present but no device needs to be reported this scan.</summary>
        public static PluginReadResult Empty()
        {
            return new PluginReadResult(true, null);
        }

        /// <summary>Transport present and at least one device observed (fresh or asleep).</summary>
        public static PluginReadResult Observed(IEnumerable<PeripheralDevice> devices)
        {
            var list = new List<PeripheralDevice>();
            if (devices != null)
            {
                foreach (var device in devices)
                {
                    if (device != null) list.Add(device);
                }
            }
            return new PluginReadResult(true, list);
        }

        public static PluginReadResult Observed(params PeripheralDevice[] devices)
        {
            return Observed((IEnumerable<PeripheralDevice>)devices);
        }
    }

    public interface IPluginContext
    {
        /// <summary>Per-user writable directory for this plugin (created by the host).</summary>
        string DataDirectory { get; }

        IPluginLogger Logger { get; }
    }

    public interface IPluginLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception exception);
    }

    /// <summary>Convenience base: sensible metadata defaults, only Id + ReadAsync are required.</summary>
    public abstract class BatteryPluginBase : IBatteryPlugin
    {
        public abstract string Id { get; }
        public virtual string DisplayName { get { return Id; } }
        public virtual string Description { get { return string.Empty; } }
        public virtual string Version { get { return "1.0.0"; } }
        public virtual int Priority { get { return 100; } }
        public virtual int SuggestedTimeoutMs { get { return 5000; } }
        public virtual bool WritesToDevice { get { return false; } }

        public abstract Task<PluginReadResult> ReadAsync(IPluginContext context, CancellationToken cancellationToken);

        /// <summary>Helper for plugins: create a reading with the fields the panel needs.</summary>
        protected static PeripheralDevice Device(string id, string name, DeviceKind kind, int? percent,
            bool charging = false, string address = null, bool? connected = null)
        {
            return new PeripheralDevice
            {
                Id = id,
                Name = name,
                Kind = kind,
                BatteryPercent = percent,
                IsCharging = charging,
                Address = address,
                IsConnected = connected
            };
        }
    }
}
