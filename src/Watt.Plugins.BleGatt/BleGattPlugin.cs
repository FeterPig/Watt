using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Watt.Models;
using Watt.Plugins;
using Watt.Plugins.Support;

namespace Watt.Plugins.BleGatt
{
    /// <summary>
    /// BLE GATT plugin: reads the standard Battery Service (service 0x180F, characteristic 0x2A19)
    /// for paired, connected BLE peripherals. A fresh GATT value is authoritative even when the
    /// Windows AEP also carries a (possibly stale) value; the higher priority lets the host prefer
    /// it for connected devices.
    ///
    /// The GATT calls are capped with independent timeouts because a paired-but-out-of-range
    /// device makes them hang for many seconds. The plugin only reads connected devices (a GATT
    /// connect would wake a sleeping one) and only returns fresh readings; it keeps no cache and
    /// lets the host own last-known values, removal and ordering.
    /// </summary>
    public sealed class BleGattPlugin : BatteryPluginBase
    {
        private static readonly Guid BatteryServiceUuid =
            new Guid("0000180f-0000-1000-8000-00805f9b34fb");

        private static readonly Guid BatteryLevelCharacteristicUuid =
            new Guid("00002a19-0000-1000-8000-00805f9b34fb");

        // Same per-step timeouts as the old BleGattProvider: the host budget of 6000 ms has to
        // absorb the worst case, and a dead device must never block the whole scan.
        private const int AdapterTimeoutMs = 1000;
        private const int EnumerateTimeoutMs = 2000;
        private const int ConnectTimeoutMs = 1500;
        private const int ServiceTimeoutMs = 2000;
        private const int CharacteristicTimeoutMs = 1200;
        private const int ReadTimeoutMs = 1500;
        private const int TotalBudgetMs = 4200;
        private const int MaxDevicesPerScan = 4;

        private const string AepAddress = "System.Devices.Aep.DeviceAddress";
        private const string AepIsConnected = "System.Devices.Aep.IsConnected";
        private const string AepBatteryPlusCharging = "System.Devices.BatteryPlusCharging";

        private static readonly string[] RequestedProperties =
        {
            AepAddress,
            AepIsConnected,
            AepBatteryPlusCharging
        };

        public override string Id
        {
            get { return "ble-gatt"; }
        }

        public override string DisplayName
        {
            get { return "Bluetooth LE GATT"; }
        }

        public override string Description
        {
            get
            {
                return "Reads the standard BLE Battery Service (0x180F/0x2A19) from connected paired "
                    + "devices; a fresh GATT value takes precedence over a cached Windows AEP value.";
            }
        }

        public override string Version
        {
            get { return "1.0.0"; }
        }

        public override int Priority
        {
            // GATT is a direct, uncached read from a connected device. Priority 5 lets it override
            // the Windows AEP plugin (10), whose shell property can lag behind the real level.
            get { return 5; }
        }

        public override int SuggestedTimeoutMs
        {
            get { return 6000; }
        }

        public override bool WritesToDevice
        {
            get { return false; }
        }

        public override async Task<PluginReadResult> ReadAsync(IPluginContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested) return PluginReadResult.Empty();

                var adapterState = await IsBluetoothAdapterPresentAsync().ConfigureAwait(false);
                if (adapterState == false) return PluginReadResult.Absent();

                var results = new List<PeripheralDevice>();

                DeviceInformationCollection devices;
                try
                {
                    var operation = DeviceInformation.FindAllAsync(
                        BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), RequestedProperties);
                    devices = await ProviderUtil.AwaitOrNullAsync(
                        WinRtAsync.AsTask(operation), EnumerateTimeoutMs).ConfigureAwait(false);
                }
                catch
                {
                    // No LE radio / permission denied / driver fault: transport present, no reading.
                    return PluginReadResult.Empty();
                }

                if (devices == null) return PluginReadResult.Empty();

                var deadline = DateTime.UtcNow.AddMilliseconds(TotalBudgetMs);
                var attempted = 0;

                foreach (var information in devices)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (attempted >= MaxDevicesPerScan || DateTime.UtcNow > deadline) break;

                    try
                    {
                        var properties = information.Properties;
                        var address = DeviceClassifier.NormalizeAddress(
                            ProviderUtil.TryGetString(properties, AepAddress));
                        var name = information.Name != null ? information.Name.Trim() : string.Empty;

                        // Only connected devices: opening GATT to a sleeping peripheral would wake
                        // it for nothing, and the host already shows its last-known value.
                        bool connected;
                        if (ProviderUtil.TryGetBool(properties, AepIsConnected, out connected) && !connected)
                            continue;

                        // The standard Battery Level characteristic says nothing about charging.
                        // Windows already knows the charging bit from the same AEP property the
                        // Windows plugin reads; carrying it here keeps the lightning bolt visible
                        // even though this higher-priority fresh reading wins the merge.
                        var charging = TryReadCharging(properties);

                        attempted++;
                        var reading = await TryReadBatteryLevelAsync(information.Id, address, name,
                            charging, cancellationToken).ConfigureAwait(false);
                        if (reading != null) results.Add(reading);
                    }
                    catch
                    {
                        // One misbehaving device must not hide the others.
                    }
                }

                return results.Count == 0
                    ? PluginReadResult.Empty()
                    : PluginReadResult.Observed(results);
            }
            catch
            {
                // Never throw into the host. A failed read is not proof that the transport is
                // gone; only an explicit "no radio" probe result reports Absent.
                return PluginReadResult.Empty();
            }
        }

        /// <summary>
        /// Returns true when a default Bluetooth adapter exists, false when Windows explicitly
        /// reports no radio, and null when the probe timed out or failed (unknown). A slow WinRT
        /// call must not be treated as "transport gone", or the host would delete cached devices
        /// after three scans.
        /// </summary>
        private static async Task<bool?> IsBluetoothAdapterPresentAsync()
        {
            try
            {
                var task = WinRtAsync.AsTask(BluetoothAdapter.GetDefaultAsync());
                var completed = await Task.WhenAny(task, Task.Delay(AdapterTimeoutMs)).ConfigureAwait(false);
                if (completed != task)
                {
                    ProviderUtil.Observe(task);
                    return null;
                }

                var adapter = await task.ConfigureAwait(false);
                return adapter != null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// A timed-out WinRT connect can still complete later; dispose the late device so a slow
        /// attempt cannot leak a BluetoothLEDevice handle.
        /// </summary>
        private static void DisposeLateDevice(Task<BluetoothLEDevice> connectTask)
        {
            if (connectTask == null) return;
            try
            {
                connectTask.ContinueWith(
                    task =>
                    {
                        if (task.Status != TaskStatus.RanToCompletion) return;
                        try
                        {
                            var device = task.Result;
                            if (device != null) device.Dispose();
                        }
                        catch
                        {
                            // ignore
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch
            {
                // ignore
            }
        }

        private static bool TryReadCharging(IReadOnlyDictionary<string, object> properties)
        {
            // BatteryPlusCharging: 101-200 = (percent - 100) while charging; 201 = unknown.
            var plusCharging = ProviderUtil.TryGetInt(properties, AepBatteryPlusCharging);
            return plusCharging.HasValue && plusCharging.Value > 100 && plusCharging.Value <= 200;
        }

        private static async Task<PeripheralDevice> TryReadBatteryLevelAsync(string deviceId, string address,
            string name, bool charging, CancellationToken cancellationToken)
        {
            // Devices that exposed the broken "increment on every read" characteristic must be
            // read from the Windows GATT cache (what Settings shows), not by forcing a new read.
            // The cache value is the last value Windows received on connect/notification and does
            // not advance the firmware read counter.
            var guardKey = address ?? deviceId;
            if (BleBatteryGuard.IsQuirked(guardKey))
            {
                var cached = await TryReadBatteryLevelAsync(deviceId, address, name, charging, cancellationToken, true)
                    .ConfigureAwait(false);
                if (cached != null) return cached;
            }

            return await TryReadBatteryLevelAsync(deviceId, address, name, charging, cancellationToken, false)
                .ConfigureAwait(false);
        }

        private static async Task<PeripheralDevice> TryReadBatteryLevelAsync(string deviceId, string address,
            string name, bool charging, CancellationToken cancellationToken, bool useCached)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;

            var cacheMode = useCached ? BluetoothCacheMode.Cached : BluetoothCacheMode.Uncached;
            BluetoothLEDevice bleDevice = null;
            try
            {
                var connectTask = WinRtAsync.AsTask(BluetoothLEDevice.FromIdAsync(deviceId));
                bleDevice = await ProviderUtil.AwaitOrNullAsync(connectTask, ConnectTimeoutMs)
                    .ConfigureAwait(false);
                if (bleDevice == null)
                {
                    DisposeLateDevice(connectTask);
                    return null;
                }

                var servicesResult = await ProviderUtil.AwaitOrNullAsync(
                    WinRtAsync.AsTask(bleDevice.GetGattServicesForUuidAsync(BatteryServiceUuid,
                        cacheMode)),
                    ServiceTimeoutMs).ConfigureAwait(false);
                if (servicesResult == null || servicesResult.Status != GattCommunicationStatus.Success)
                    return null;

                var services = servicesResult.Services;
                if (services == null) return null;

                for (var i = 0; i < services.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return null;

                    var service = services[i];
                    try
                    {
                        var level = await TryReadCharacteristicAsync(service, cancellationToken, cacheMode)
                            .ConfigureAwait(false);
                        if (level.HasValue)
                        {
                            if (string.IsNullOrWhiteSpace(name) && address != null)
                                name = "Bluetooth device " + address;
                            if (string.IsNullOrWhiteSpace(name)) name = "Bluetooth LE device";

                            var normalizedName = name.Trim();
                            var effectiveAddress = address;
                            if (effectiveAddress == null && bleDevice.BluetoothAddress != 0)
                                effectiveAddress = ProviderUtil.FormatBluetoothAddress(bleDevice.BluetoothAddress);

                            var guardKey = effectiveAddress ?? deviceId;
                            var effectiveLevel = BleBatteryGuard.NormalizeOrIgnore(guardKey, level.Value, charging);
                            if (!effectiveLevel.HasValue) continue;

                            return Device(
                                DeviceClassifier.BuildId(effectiveAddress, normalizedName, deviceId),
                                normalizedName,
                                DeviceClassifier.Classify(normalizedName, null, null),
                                DeviceClassifier.ClampPercent(effectiveLevel.Value),
                                charging,
                                effectiveAddress,
                                true);
                        }
                    }
                    finally
                    {
                        try
                        {
                            service.Dispose();
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bleDevice != null)
                {
                    try
                    {
                        bleDevice.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            return null;
        }

        private static async Task<int?> TryReadCharacteristicAsync(GattDeviceService service,
            CancellationToken cancellationToken, BluetoothCacheMode cacheMode)
        {
            try
            {
                var characteristicsResult = await ProviderUtil.AwaitOrNullAsync(
                    WinRtAsync.AsTask(service.GetCharacteristicsForUuidAsync(BatteryLevelCharacteristicUuid,
                        cacheMode)),
                    CharacteristicTimeoutMs).ConfigureAwait(false);
                if (characteristicsResult == null ||
                    characteristicsResult.Status != GattCommunicationStatus.Success)
                    return null;

                var characteristics = characteristicsResult.Characteristics;
                if (characteristics == null) return null;

                for (var i = 0; i < characteristics.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return null;

                    var characteristic = characteristics[i];
                    try
                    {
                        var readResult = await ProviderUtil.AwaitOrNullAsync(
                            WinRtAsync.AsTask(characteristic.ReadValueAsync(cacheMode)),
                            ReadTimeoutMs).ConfigureAwait(false);
                        if (readResult == null || readResult.Status != GattCommunicationStatus.Success ||
                            readResult.Value == null)
                            continue;

                        try
                        {
                            using (var reader = DataReader.FromBuffer(readResult.Value))
                            {
                                if (reader.UnconsumedBufferLength < 1) continue;
                                var level = reader.ReadByte();
                                return level;
                            }
                        }
                        catch
                        {
                            // malformed buffer: try the next characteristic
                        }
                    }
                    catch
                    {
                        // characteristic read failed; try the next one
                    }
                }
            }
            catch
            {
                // access denied / protocol error / device vanished
            }

            return null;
        }
    }
}
