using System.Collections.Generic;
using Watt.Plugins.Support;

namespace Watt.Plugins.BleGatt
{
    /// <summary>
    /// Minimal decode of the Windows AEP battery shell properties.
    ///
    /// The BLE GATT plugin only needs to know whether Windows already reports a level for a paired
    /// LE endpoint: those devices are owned by the Windows AEP plugin, and opening a GATT
    /// connection would just wake the peripheral for a value the host already has.
    /// </summary>
    internal static class AepBattery
    {
        internal const string PropBatteryLife = "System.Devices.BatteryLife";
        internal const string PropBatteryPlusCharging = "System.Devices.BatteryPlusCharging";

        /// <summary>
        /// True when the AEP properties carry a plausible battery level. Decoding matches the
        /// Windows AEP plugin: BatteryPlusCharging 0-100 = percent, 101-200 = percent+100 while
        /// charging; BatteryLife 0-100 is valid (101 = unknown).
        /// </summary>
        internal static bool TryRead(IReadOnlyDictionary<string, object> properties)
        {
            if (properties == null) return false;

            var plusCharging = ProviderUtil.TryGetInt(properties, PropBatteryPlusCharging);
            if (plusCharging.HasValue)
            {
                var value = plusCharging.Value;
                if (value >= 0 && value <= 200) return true;
            }

            var life = ProviderUtil.TryGetInt(properties, PropBatteryLife);
            return life.HasValue && life.Value >= 0 && life.Value <= 100;
        }
    }
}
