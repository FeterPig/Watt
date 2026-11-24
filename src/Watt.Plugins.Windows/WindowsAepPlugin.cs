using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Watt.Models;
using Watt.Plugins;
using Watt.Plugins.Support;

namespace Watt.Plugins.Windows
{
    /// <summary>
    /// Windows AEP plugin: reads the battery level Windows itself already knows about paired
    /// Bluetooth devices (classic BR/EDR and BLE) without opening a GATT connection.
    ///
    /// The values live on the device's Association Endpoint (AEP) as shell properties:
    ///   System.Devices.BatteryLife           - byte, 0-100, 101 = unknown
    ///   System.Devices.BatteryPlusCharging   - byte, percent, +100 while charging, 201 = unknown
    ///
    /// This is the "what Windows Settings shows" source, including classic-Bluetooth audio
    /// devices whose level Windows learned over HFP/AVRCP, which no GATT call can reproduce.
    /// For classic headsets whose AEP carries no level, <see cref="HfpBatteryReader"/> reads the
    /// value from the HFP gateway PnP node as a fallback.
    ///
    /// The plugin reports observations only: it deliberately keeps disconnected/asleep paired
    /// devices with <see cref="PeripheralDevice.BatteryPercent"/> = null and leaves last-known
    /// substitution, removal and ordering to the plugin host.
    /// </summary>
    public sealed class WindowsAepPlugin : BatteryPluginBase
    {
        // The host calls us with a ~3000 ms budget. GetDefaultAsync returns quickly on a machine
        // with a radio, so keep the adapter probe and each selector enumeration short enough that
        // both selectors plus the adapter check still fit inside that budget.
        private const int AdapterTimeoutMs = 1000;
        private const int EnumerateTimeoutMs = 1200;
        private const int TotalBudgetMs = 2800;

        public override string Id
        {
            get { return "windows-aep"; }
        }

        public override string DisplayName
        {
            get { return "Windows Bluetooth properties"; }
        }

        public override string Description
        {
            get
            {
                return "Reads the battery level Windows exposes on paired Bluetooth devices (AEP shell "
                    + "properties), with an HFP PnP-node fallback for classic headsets.";
            }
        }

        public override string Version
        {
            get { return "1.0.0"; }
        }

        public override int Priority
        {
            get { return 10; }
        }

        public override int SuggestedTimeoutMs
        {
            get { return 3000; }
        }

        public override bool WritesToDevice
        {
            get { return false; }
        }

        public override async Task<PluginReadResult> ReadAsync(IPluginContext context,
            CancellationToken cancellationToken)
        {
            var adapterPresent = false;
            try
            {
                if (cancellationToken.IsCancellationRequested) return PluginReadResult.Empty();

                adapterPresent = await IsBluetoothAdapterPresentAsync().ConfigureAwait(false);
                if (!adapterPresent) return PluginReadResult.Absent();

                var found = new Dictionary<string, PeripheralDevice>(StringComparer.OrdinalIgnoreCase);
                var deadline = DateTime.UtcNow.AddMilliseconds(TotalBudgetMs);

                // The HFP PnP-node scan is synchronous and only needed for classic devices whose AEP
                // carries no level; load it at most once per scan, and only if it is needed.
                IReadOnlyDictionary<string, int> hfp = null;
                Func<IReadOnlyDictionary<string, int>> getHfp = () => hfp ?? (hfp = HfpBatteryReader.ReadByMac());

                foreach (var selector in BuildSelectors())
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (DateTime.UtcNow > deadline) break;

                    try
                    {
                        var operation = DeviceInformation.FindAllAsync(selector, AepProperties.Requested);
                        var devices = await ProviderUtil.AwaitOrNullAsync(
                            WinRtAsync.AsTask(operation), EnumerateTimeoutMs).ConfigureAwait(false);
                        if (devices == null) continue;

                        foreach (var information in devices)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            try
                            {
                                var device = TryCreateDevice(information, getHfp);
                                if (device != null) Merge(found, device);
                            }
                            catch
                            {
                                // One malformed device must not hide the others.
                            }
                        }
                    }
                    catch
                    {
                        // No radio / permission denied / driver fault: the other selector may still work.
                    }
                }

                return found.Count == 0
                    ? PluginReadResult.Empty()
                    : PluginReadResult.Observed(found.Values);
            }
            catch
            {
                // Never throw into the host. Only claim "absent" when we could not confirm a radio.
                return adapterPresent ? PluginReadResult.Empty() : PluginReadResult.Absent();
            }
        }

        /// <summary>
        /// AEP devices are present iff the Bluetooth radio (default adapter) exists. A machine
        /// without any Bluetooth hardware returns null from GetDefaultAsync.
        /// </summary>
        private static async Task<bool> IsBluetoothAdapterPresentAsync()
        {
            try
            {
                var adapter = await ProviderUtil.AwaitOrNullAsync(
                    WinRtAsync.AsTask(BluetoothAdapter.GetDefaultAsync()), AdapterTimeoutMs).ConfigureAwait(false);
                return adapter != null;
            }
            catch
            {
                return false;
            }
        }

        private static List<string> BuildSelectors()
        {
            var selectors = new List<string>(2);

            // Classic BR/EDR (headphones, older mice/keyboards). Also covers dual-mode devices
            // that Windows treats as classic endpoints.
            try
            {
                var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
                if (!string.IsNullOrEmpty(selector)) selectors.Add(selector);
            }
            catch
            {
                // Bluetooth stack missing / no radio.
            }

            try
            {
                var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
                if (!string.IsNullOrEmpty(selector)) selectors.Add(selector);
            }
            catch
            {
                // Bluetooth stack missing / no radio.
            }

            return selectors;
        }

        private static PeripheralDevice TryCreateDevice(DeviceInformation information,
            Func<IReadOnlyDictionary<string, int>> getHfp)
        {
            if (information == null) return null;

            var properties = information.Properties;

            var address = DeviceClassifier.NormalizeAddress(
                ProviderUtil.TryGetString(properties, AepProperties.AepAddress));

            var name = information.Name;
            if (string.IsNullOrWhiteSpace(name)) name = ProviderUtil.TryGetString(properties, AepProperties.ModelName);
            if (string.IsNullOrWhiteSpace(name) && address != null) name = "Bluetooth device " + address;
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();

            var id = DeviceClassifier.BuildId(address, name, information.Id);

            int? percent;
            bool charging;
            if (!AepProperties.TryReadBattery(properties, out percent, out charging) || !percent.HasValue)
            {
                // Fallback: some classic-Bluetooth headsets expose their charge on the HFP gateway
                // PnP node instead of the AEP shell properties (see HfpBatteryReader).
                percent = null;
                charging = false;
                if (address != null)
                {
                    try
                    {
                        int hfpPercent;
                        var hfp = getHfp();
                        if (hfp != null && hfp.TryGetValue(address.Replace(":", string.Empty), out hfpPercent))
                            percent = hfpPercent;
                    }
                    catch
                    {
                        // HFP helper never throws by contract, but stay defensive.
                    }
                }
            }

            var kind = DeviceClassifier.Classify(
                name,
                ProviderUtil.TryGetInt(properties, AepProperties.AepCodMajor),
                ProviderUtil.TryGetInt(properties, AepProperties.AepCodMinor));

            bool? connected = null;
            bool connectedValue;
            if (ProviderUtil.TryGetBool(properties, AepProperties.AepIsConnected, out connectedValue))
                connected = connectedValue;

            // A paired device that is currently disconnected/asleep stays visible with a null
            // level; the host substitutes the last-known value instead of dropping the row.
            return Device(
                id,
                name,
                kind,
                percent.HasValue ? DeviceClassifier.ClampPercent(percent.Value) : (int?)null,
                percent.HasValue && charging,
                address,
                connected);
        }

        /// <summary>
        /// Both paired selectors can return the same dual-mode device. Merge into one row and
        /// keep a value if either endpoint decoded one.
        /// </summary>
        private static void Merge(Dictionary<string, PeripheralDevice> found, PeripheralDevice device)
        {
            PeripheralDevice existing;
            if (!found.TryGetValue(device.Id, out existing))
            {
                found[device.Id] = device;
                return;
            }

            if (!existing.BatteryPercent.HasValue && device.BatteryPercent.HasValue)
            {
                existing.BatteryPercent = device.BatteryPercent;
                existing.IsCharging = device.IsCharging;
            }
            if (string.IsNullOrEmpty(existing.Address) && !string.IsNullOrEmpty(device.Address))
                existing.Address = device.Address;
            if (existing.Kind == DeviceKind.Other && device.Kind != DeviceKind.Other)
                existing.Kind = device.Kind;
        }
    }
}
