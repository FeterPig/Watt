using System.Collections.Generic;
using Watt.Plugins.Support;

namespace Watt.Plugins.Windows
{
    /// <summary>
    /// Property keys and decode logic for the Windows Association Endpoint (AEP) battery shell
    /// properties. Moved out of the old <c>WindowsPropertyProvider</c> so the plugin class only
    /// deals with enumeration and result shaping.
    /// </summary>
    internal static class AepProperties
    {
        internal const string AepAddress = "System.Devices.Aep.DeviceAddress";
        internal const string AepIsConnected = "System.Devices.Aep.IsConnected";
        internal const string AepIsPresent = "System.Devices.Aep.IsPresent";
        internal const string AepCodMajor = "System.Devices.Aep.Bluetooth.Cod.Major";
        internal const string AepCodMinor = "System.Devices.Aep.Bluetooth.Cod.Minor";
        internal const string ModelName = "System.Devices.ModelName";

        // Requested as "additional properties" so they are populated on the returned
        // DeviceInformation. Unknown/unavailable ones are simply absent from Properties.
        // Note: "System.Devices.BatteryLifePercent" is NOT a valid property key on Windows 11
        // (FindAllAsync rejects it), so only the two documented battery values are requested.
        internal const string PropBatteryLife = "System.Devices.BatteryLife";
        internal const string PropBatteryPlusCharging = "System.Devices.BatteryPlusCharging";

        internal static readonly string[] Requested =
        {
            AepAddress,
            AepIsConnected,
            AepIsPresent,
            AepCodMajor,
            AepCodMinor,
            ModelName,
            PropBatteryLife,
            PropBatteryPlusCharging
        };

        /// <summary>
        /// Decodes the Windows battery AEP properties. BatteryPlusCharging is the richer value:
        /// 0-100 is a plain percentage and 101-200 is (percent + 100) while charging.
        /// </summary>
        internal static bool TryReadBattery(IReadOnlyDictionary<string, object> properties, out int? percent,
            out bool charging)
        {
            percent = null;
            charging = false;

            var plusCharging = ProviderUtil.TryGetInt(properties, PropBatteryPlusCharging);
            if (plusCharging.HasValue)
            {
                var value = plusCharging.Value;
                if (value >= 0 && value <= 100)
                {
                    percent = value;
                }
                else if (value > 100 && value <= 200)
                {
                    percent = value - 100;
                    charging = true;
                }
            }

            if (!percent.HasValue)
            {
                var life = ProviderUtil.TryGetInt(properties, PropBatteryLife);
                // 101 means "unknown battery status" per the property schema.
                if (life.HasValue && life.Value >= 0 && life.Value <= 100) percent = life.Value;
            }

            return percent.HasValue;
        }
    }
}
