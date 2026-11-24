using System;

namespace Watt.Models
{
    /// <summary>Visual category of a peripheral. The panel uses this to pick its line icon.</summary>
    public enum DeviceKind
    {
        Keyboard,
        Mouse,
        Headphone,
        Other
    }

    /// <summary>
    /// One peripheral with a known or last-known battery level. This is the only shape the UI
    /// and the plugin API exchange; the plugin host fills <see cref="SourceId"/> after a read.
    /// </summary>
    public sealed class PeripheralDevice
    {
        /// <summary>Stable identity: Bluetooth MAC ("bt:aa:..."), or a plugin-specific stable id.</summary>
        public string Id { get; set; }

        /// <summary>Human readable name, e.g. "MX Master 3S" or "WH-1000XM5".</summary>
        public string Name { get; set; }

        public DeviceKind Kind { get; set; }

        /// <summary>0-100. Null means "device observed but no fresh reading"; the host then
        /// substitutes the last-known value (if any) instead of hiding the row.</summary>
        public int? BatteryPercent { get; set; }

        public bool IsCharging { get; set; }

        /// <summary>Current transport connection state when known: true = connected, false =
        /// observed but disconnected/asleep, null = plugin cannot tell. The host uses this to
        /// decide whether a manual refresh should drop a cache-only row.</summary>
        public bool? IsConnected { get; set; }

        /// <summary>Id of the plugin that produced/last refreshed this reading (set by the host).</summary>
        public string SourceId { get; set; }

        /// <summary>Normalized Bluetooth address ("aa:bb:cc:dd:ee:ff") when applicable, else null.</summary>
        public string Address { get; set; }

        public override string ToString()
        {
            return string.Format("{0} [{1}] {2}%", Name, Kind, BatteryPercent);
        }
    }
}
