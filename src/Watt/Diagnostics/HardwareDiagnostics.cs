using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Storage.Streams;
using Watt.Battery;
using Watt.Models;
using Watt.Plugins;
using Watt.Plugins.Support;

namespace Watt.Diagnostics
{
    /// <summary>
    /// <c>--diagnose &lt;file&gt;</c>: dumps the Bluetooth/HID/plugin picture of this machine so
    /// "why is my device missing" and "how do I write a plugin for it" can be answered without
    /// a debugger. Everything is best effort and never throws into the caller.
    /// </summary>
    internal static class HardwareDiagnostics
    {
        private static readonly Guid BatteryServiceUuid = new Guid("0000180f-0000-1000-8000-00805f9b34fb");
        private static readonly Guid BatteryLevelCharacteristicUuid = new Guid("00002a19-0000-1000-8000-00805f9b34fb");

        private static readonly string[] RequestedProperties =
        {
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.Aep.IsConnected",
            "System.Devices.Aep.IsPresent",
            "System.Devices.Aep.Bluetooth.Cod.Major",
            "System.Devices.Aep.Bluetooth.Cod.Minor",
            "System.Devices.ModelName",
            "System.Devices.BatteryLife",
            "System.Devices.BatteryPlusCharging"
        };

        private sealed class ProbeTarget
        {
            internal string Name;
            internal string Address;
            internal bool Connected;
        }

        internal static bool Run(string outputFile, IEnumerable<string> pluginDirectories)
        {
            if (string.IsNullOrWhiteSpace(outputFile)) return false;

            var report = new StringBuilder();
            report.AppendLine("Watt hardware diagnostics");
            report.AppendLine("time : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("os   : " + Environment.OSVersion.VersionString + "  x64=" + Environment.Is64BitProcess);
            report.AppendLine("exe  : " + AppDomain.CurrentDomain.BaseDirectory);
            report.AppendLine();

            PluginHost host = null;
            try
            {
                host = PluginHost.Load(pluginDirectories, null);
            }
            catch (Exception ex)
            {
                report.AppendLine("plugin host failed: " + ex);
            }

            Safe(report, "plugins", delegate(StringBuilder target) { AppendPlugins(target, host); });
            Safe(report, "bluetooth adapter / radios", AppendRadios);
            var targets = new List<ProbeTarget>();
            Safe(report, "bluetooth devices (AEP)", delegate(StringBuilder target) { AppendBluetoothDevices(target, targets); });
            Safe(report, "classic Bluetooth HFP node battery", AppendHfpNodes);
            Safe(report, "last known battery cache", AppendLastKnown);
            Safe(report, "BLE GATT battery probe", delegate(StringBuilder target) { AppendGattProbes(target, targets); });
            Safe(report, "HID collections (SetupAPI)", AppendHidDevices);
            Safe(report, "HID vendor input reports (listening)", AppendHidListen);

            try
            {
                File.WriteAllText(outputFile, report.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                // Diagnostics must never crash the caller; the non-zero exit code reports the
                // missing report file to automation.
                return false;
            }
        }

        private static void Safe(StringBuilder report, string section, Action<StringBuilder> action)
        {
            report.AppendLine("== " + section + " ==");
            try
            {
                action(report);
            }
            catch (Exception ex)
            {
                report.AppendLine("  ERROR: " + ex.GetType().Name + ": " + ex.Message);
            }
            report.AppendLine();
        }

        private static void AppendPlugins(StringBuilder report, PluginHost host)
        {
            if (host == null)
            {
                report.AppendLine("plugin host unavailable");
                return;
            }

            report.AppendLine("search order (later overrides earlier by Id):");
            foreach (var directory in host.SearchDirectories) report.AppendLine("  " + directory);

            report.AppendLine("loaded plugins: " + host.Plugins.Count);
            foreach (var loaded in host.Plugins)
            {
                var plugin = loaded.Plugin;
                report.AppendLine(string.Format(
                    "  - {0}  v{1}  priority={2}  timeout={3}ms  writes={4}",
                    plugin.Id, plugin.Version, plugin.Priority, plugin.SuggestedTimeoutMs, plugin.WritesToDevice));
                if (!string.IsNullOrEmpty(plugin.Description))
                    report.AppendLine("      " + plugin.Description);
                report.AppendLine("      assembly: " + loaded.AssemblyPath);
            }

            if (host.Issues.Count > 0)
            {
                report.AppendLine("load issues: " + host.Issues.Count);
                foreach (var issue in host.Issues)
                {
                    report.AppendLine("  " + issue.Path);
                    report.AppendLine("      " + issue.Message);
                }
            }

            report.AppendLine();
            report.AppendLine("per-plugin read result:");
            foreach (var loaded in host.Plugins)
            {
                var plugin = loaded.Plugin;
                var timeout = Math.Max(500, Math.Min(15000, plugin.SuggestedTimeoutMs));
                try
                {
                    var started = DateTime.UtcNow;
                    var task = plugin.ReadAsync(loaded.Context, CancellationToken.None);
                    PluginReadResult result = null;
                    string error = null;
                    try
                    {
                        var finished = Task.WhenAny(task, Task.Delay(timeout)).GetAwaiter().GetResult();
                        if (finished == task)
                        {
                            result = task.GetAwaiter().GetResult();
                        }
                        else
                        {
                            error = "timeout after " + timeout + " ms";
                            ProviderUtil.Observe(task);
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex.GetType().Name + ": " + ex.Message;
                    }

                    var elapsed = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                    if (error != null)
                    {
                        report.AppendLine("  [" + plugin.Id + "] ERROR " + error + " (" + elapsed + " ms)");
                        continue;
                    }

                    report.AppendLine("  [" + plugin.Id + "] hardwarePresent=" +
                                      (result == null ? "?" : result.HardwarePresent.ToString()) +
                                      " devices=" + (result == null ? "?" : result.Devices.Count.ToString()) +
                                      " (" + elapsed + " ms)");
                    if (result == null) continue;
                    foreach (var device in result.Devices)
                    {
                        report.AppendLine("      " + device.Name + "  kind=" + device.Kind +
                                          "  battery=" + (device.BatteryPercent.HasValue
                                              ? device.BatteryPercent.Value + "%"
                                              : "(asleep / no fresh value)") +
                                          "  id=" + device.Id);
                    }
                }
                catch (Exception ex)
                {
                    report.AppendLine("  [" + plugin.Id + "] ERROR " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void AppendRadios(StringBuilder report)
        {
            try
            {
                var adapter = WinRtAsync.AsTask(BluetoothAdapter.GetDefaultAsync()).GetAwaiter().GetResult();
                report.AppendLine(adapter == null
                    ? "BluetoothAdapter: NULL (no Bluetooth radio or stack unavailable)"
                    : "BluetoothAdapter: present, address=0x" + adapter.BluetoothAddress.ToString("X12") +
                      ", deviceId=" + adapter.DeviceId);
            }
            catch (Exception ex)
            {
                report.AppendLine("BluetoothAdapter: ERROR " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                var radios = WinRtAsync.AsTask(Radio.GetRadiosAsync()).GetAwaiter().GetResult();
                foreach (var radio in radios)
                {
                    report.AppendLine("Radio: name=\"" + radio.Name + "\" kind=" + radio.Kind +
                                      " state=" + radio.State);
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("Radio.GetRadiosAsync: ERROR " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void AppendBluetoothDevices(StringBuilder report, List<ProbeTarget> targets)
        {
            var selectors = new List<KeyValuePair<string, string>>();
            AddSelector(selectors, "classic-paired",
                delegate { return BluetoothDevice.GetDeviceSelectorFromPairingState(true); });
            AddSelector(selectors, "le-paired",
                delegate { return BluetoothLEDevice.GetDeviceSelectorFromPairingState(true); });
            AddSelector(selectors, "classic-connected",
                delegate { return BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected); });
            AddSelector(selectors, "le-connected",
                delegate { return BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected); });

            foreach (var selector in selectors)
            {
                report.AppendLine("[" + selector.Key + "] selector=" + selector.Value);
                try
                {
                    var devices = WinRtAsync.AsTask(
                        DeviceInformation.FindAllAsync(selector.Value, RequestedProperties)).GetAwaiter().GetResult();

                    report.AppendLine("  count=" + (devices == null ? "null" : devices.Count.ToString()));
                    if (devices == null) continue;

                    foreach (var device in devices)
                    {
                        try
                        {
                            report.AppendLine("  - " + (device.Name ?? "(no name)"));
                            report.AppendLine("      id       = " + device.Id);
                            report.AppendLine("      kind     = " + device.Kind);
                            report.AppendLine("      enabled  = " + device.IsEnabled);
                            report.AppendLine("      paired   = " + device.Pairing.IsPaired);

                            string address = null;
                            var connected = false;
                            foreach (var pair in device.Properties)
                            {
                                var value = pair.Value;
                                report.AppendLine("      " + pair.Key + " = " +
                                                  (value == null ? "null" : value.ToString()) +
                                                  "  [" + (value == null ? "null" : value.GetType().Name) + "]");
                                if (pair.Key == "System.Devices.Aep.DeviceAddress" && value is string)
                                    address = (string)value;
                                if (pair.Key == "System.Devices.Aep.IsConnected" && value is bool)
                                    connected = (bool)value;
                            }

                            if (!string.IsNullOrEmpty(address) && targets.Count < 12)
                            {
                                var duplicate = false;
                                foreach (var target in targets)
                                {
                                    if (string.Equals(target.Address, address, StringComparison.OrdinalIgnoreCase))
                                    {
                                        duplicate = true;
                                        break;
                                    }
                                }
                                if (!duplicate)
                                {
                                    targets.Add(new ProbeTarget
                                    {
                                        Name = device.Name ?? address,
                                        Address = address,
                                        Connected = connected
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            report.AppendLine("      device ERROR: " + ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    report.AppendLine("  ERROR: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void AddSelector(List<KeyValuePair<string, string>> selectors, string label,
            Func<string> factory)
        {
            try
            {
                var selector = factory();
                if (!string.IsNullOrEmpty(selector))
                    selectors.Add(new KeyValuePair<string, string>(label, selector));
            }
            catch (Exception ex)
            {
                selectors.Add(new KeyValuePair<string, string>(label + "-unavailable", "ERROR: " + ex.Message));
            }
        }

        private static void AppendHfpNodes(StringBuilder report)
        {
            var map = HfpBatteryReader.ReadByMac();
            report.AppendLine("classic Bluetooth device nodes with DEVPKEY_Device_BatteryLevel: " + map.Count);
            foreach (var pair in map) report.AppendLine("  " + pair.Key + " = " + pair.Value + "%");
        }

        private static void AppendLastKnown(StringBuilder report)
        {
            var devices = LastKnownStore.Snapshot();
            report.AppendLine("cached (used while a peripheral sleeps): " + devices.Count);
            foreach (var device in devices)
            {
                report.AppendLine("  " + device.Id + "  " + device.Name + "  " +
                                  (device.BatteryPercent.HasValue ? device.BatteryPercent.Value + "%" : "-") +
                                  "  source=" + device.SourceId);
            }
        }

        private static void AppendGattProbes(StringBuilder report, List<ProbeTarget> targets)
        {
            report.AppendLine("Trying the standard BLE Battery Service (0x180F/0x2A19) on each endpoint seen above.");
            if (targets.Count == 0) return;

            foreach (var target in targets)
            {
                ulong address;
                if (!TryParseBluetoothAddress(target.Address, out address))
                {
                    report.AppendLine("  " + target.Name + ": bad address \"" + target.Address + "\"");
                    continue;
                }

                BluetoothLEDevice device = null;
                try
                {
                    device = ProviderUtil.AwaitOrNullAsync(
                        WinRtAsync.AsTask(BluetoothLEDevice.FromBluetoothAddressAsync(address)), 2500)
                        .GetAwaiter().GetResult();
                    if (device == null)
                    {
                        report.AppendLine("  " + target.Name + ": BLE device NULL (no BLE side / timeout)");
                        continue;
                    }

                    report.AppendLine("  " + target.Name + ": BLE connection=" + device.ConnectionStatus +
                                      " (AEP connected=" + target.Connected + ")");

                    var servicesResult = ProviderUtil.AwaitOrNullAsync(
                        WinRtAsync.AsTask(device.GetGattServicesForUuidAsync(BatteryServiceUuid,
                            BluetoothCacheMode.Uncached)), 3000).GetAwaiter().GetResult();
                    if (servicesResult == null || servicesResult.Status != GattCommunicationStatus.Success)
                    {
                        report.AppendLine("    battery service: " +
                                          (servicesResult == null ? "timeout" : servicesResult.Status.ToString()));
                        continue;
                    }

                    foreach (var service in servicesResult.Services)
                    {
                        try
                        {
                            var characteristics = ProviderUtil.AwaitOrNullAsync(
                                WinRtAsync.AsTask(service.GetCharacteristicsForUuidAsync(
                                    BatteryLevelCharacteristicUuid, BluetoothCacheMode.Uncached)), 2500)
                                .GetAwaiter().GetResult();
                            if (characteristics == null ||
                                characteristics.Status != GattCommunicationStatus.Success)
                                continue;

                            foreach (var characteristic in characteristics.Characteristics)
                            {
                                var read = ProviderUtil.AwaitOrNullAsync(
                                    WinRtAsync.AsTask(characteristic.ReadValueAsync(BluetoothCacheMode.Uncached)),
                                    2500).GetAwaiter().GetResult();
                                if (read == null || read.Status != GattCommunicationStatus.Success ||
                                    read.Value == null)
                                    continue;

                                using (var reader = DataReader.FromBuffer(read.Value))
                                {
                                    var percent = reader.ReadByte();
                                    report.AppendLine("    BATTERY = " + percent + "%");
                                }
                            }
                        }
                        finally
                        {
                            service.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    report.AppendLine("  " + target.Name + ": GATT ERROR " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if (device != null)
                    {
                        try { device.Dispose(); } catch { }
                    }
                }
            }
        }

        private static bool TryParseBluetoothAddress(string text, out ulong address)
        {
            address = 0;
            if (string.IsNullOrEmpty(text)) return false;
            var hex = text.Replace(":", string.Empty).Replace("-", string.Empty).Trim();
            return ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address);
        }

        private static void AppendHidDevices(StringBuilder report)
        {
            var collections = HidNative.EnumerateAllPresentCollections();
            report.AppendLine("present HID top-level collections: " + collections.Count);
            foreach (var info in collections)
            {
                report.AppendLine(string.Format(
                    "  - vid=0x{0:X4} pid=0x{1:X4} usagePage=0x{2:X4} usage=0x{3:X4} in={4} out={5} feature={6} product=\"{7}\"",
                    info.VendorId, info.ProductId, info.UsagePage, info.Usage,
                    info.InputReportByteLength, info.OutputReportByteLength, info.FeatureReportByteLength,
                    info.ProductName ?? string.Empty));
            }
        }

        /// <summary>
        /// Passive capture on vendor HID collections (usage page 0xFF00+ or 0x000A) so a new
        /// 2.4 GHz protocol can be identified from the raw reports. Standard keyboard/mouse
        /// collections are intentionally not opened.
        /// </summary>
        private static void AppendHidListen(StringBuilder report)
        {
            const int listenMs = 2500;
            var collections = HidNative.EnumerateAllPresentCollections();
            var candidates = new List<HidInterfaceInfo>();
            foreach (var info in collections)
            {
                if (info.InputReportByteLength <= 0) continue;
                if (info.UsagePage < 0xFF00 && info.UsagePage != 0x000A) continue;
                candidates.Add(info);
                if (candidates.Count >= 6) break;
            }

            if (candidates.Count == 0)
            {
                report.AppendLine("no vendor input collections found");
                return;
            }

            foreach (var info in candidates)
            {
                report.AppendLine(string.Format(
                    "listening {0} ms on vid=0x{1:X4} pid=0x{2:X4} usagePage=0x{3:X4} usage=0x{4:X4} (in={5})",
                    listenMs, info.VendorId, info.ProductId, info.UsagePage, info.Usage,
                    info.InputReportByteLength));

                var handle = HidNative.OpenReadOverlapped(info.DevicePath);
                if (handle == IntPtr.Zero)
                {
                    report.AppendLine("  open failed (another process may hold the interface)");
                    continue;
                }

                try
                {
                    var buffer = new byte[Math.Max(1, info.InputReportByteLength)];
                    var deadline = DateTime.UtcNow.AddMilliseconds(listenMs);
                    var frames = 0;
                    while (DateTime.UtcNow < deadline && frames < 20)
                    {
                        int count;
                        if (!HidNative.TryReadInputReport(handle, buffer, 250, out count)) continue;
                        report.AppendLine("  " + DateTime.Now.ToString("HH:mm:ss.fff") + "  " +
                                          BitConverter.ToString(buffer, 0, count));
                        frames++;
                    }
                    if (frames == 0) report.AppendLine("  no input reports within the window");
                }
                finally
                {
                    HidNative.CloseHandlePublic(handle);
                }
            }
        }
    }
}
