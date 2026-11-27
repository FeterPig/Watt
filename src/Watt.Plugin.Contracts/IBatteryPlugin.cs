using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Watt.Models;

namespace Watt.Plugins
{
    /// <summary>
    /// One battery acquisition method: Windows AEP properties, BLE GATT, a vendor 2.4 GHz
    /// HID protocol, passive telemetry, ... The plugin host discovers implementations from
    /// DLLs in the plugins folder, calls <see cref="ReadAsync"/> in parallel on every refresh
    /// and merges their observations. A plugin never talks to the UI or the cache directly.
    /// </summary>
    public interface IBatteryPlugin
    {
        /// <summary>Stable lowercase identifier, e.g. "windows-aep". Also used as SourceId.</summary>
        string Id { get; }

        string DisplayName { get; }

        /// <summary>One or two sentences shown by diagnostics/--plugins.</summary>
        string Description { get; }

        /// <summary>Plugin version string, e.g. "1.0.0" or "1.0.0+abc1234".</summary>
        string Version { get; }

        /// <summary>Merge preference; lower wins when two plugins observe the same device. Built-in
        /// plugins use 10 = Windows AEP/HFP and 5 = BLE GATT; third-party plugins should use >= 60.</summary>
        int Priority { get; }

        /// <summary>How long the host waits for one scan. Clamped by the host (500..15000 ms).</summary>
        int SuggestedTimeoutMs { get; }

        /// <summary>True when this plugin sends query frames to the device (shown in diagnostics).</summary>
        bool WritesToDevice { get; }

        /// <summary>
        /// Reads current observations. Must never throw: return <see cref="PluginReadResult.Absent"/>
        /// when the transport (Bluetooth radio, receiver, HID interface) is gone. Devices may be
        /// returned with <see cref="PeripheralDevice.BatteryPercent"/> = null to say "observed but
        /// asleep"; the host keeps the row and fills the last-known value.
        /// </summary>
        Task<PluginReadResult> ReadAsync(IPluginContext context, CancellationToken cancellationToken);
    }
}
