using System;
using System.Globalization;
using System.IO;
using Watt.Plugins.BleGatt;
using Watt.Plugins.Support;

namespace Watt.Tests
{
    internal static class BleGuardTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("ble guard/learns the consecutive +1 counter and pins the baseline", CounterDetection);
            runner.Run("ble guard/a lower reading becomes the new baseline", DischargeUpdatesBaseline);
            runner.Run("ble guard/values above 100 are ignored until the quirk is known", ReservedValues);
            runner.Run("ble guard/charging never teaches the counter quirk", ChargingDoesNotQuirk);
            runner.Run("ble guard/quirk state and timestamp survive a restart", Persistence);
            runner.Run("ble guard/legacy state file with zero timestamp still loads", LegacyState);
        }

        private static void CounterDetection()
        {
            Assert.Equal(50, BleBatteryGuard.Normalize("aa:bb", 50), "first value");
            Assert.Equal(51, BleBatteryGuard.Normalize("aa:bb", 51), "second value");
            Assert.Equal(50, BleBatteryGuard.Normalize("aa:bb", 52), "third value pins the learned baseline");
            Assert.True(BleBatteryGuard.IsQuirked("aa:bb"), "quirk learned");
            Assert.Equal(50, BleBatteryGuard.Normalize("aa:bb", 53), "counter value pinned to baseline");
            Assert.Equal(50, BleBatteryGuard.Normalize("aa:bb", 90), "large fake climb pinned");
            Assert.Equal(50, BleBatteryGuard.NormalizeOrIgnore("aa:bb", 120), "reserved value returns the baseline");
        }

        private static void DischargeUpdatesBaseline()
        {
            BleBatteryGuard.Normalize("k", 60);
            BleBatteryGuard.Normalize("k", 61);
            BleBatteryGuard.Normalize("k", 62);
            Assert.True(BleBatteryGuard.IsQuirked("k"), "quirk learned");

            Assert.Equal(40, BleBatteryGuard.Normalize("k", 40), "real discharge accepted");
            Assert.Equal(40, BleBatteryGuard.Normalize("k", 41), "subsequent fake rise pinned");
        }

        private static void ReservedValues()
        {
            Assert.Equal(null, BleBatteryGuard.NormalizeOrIgnore("reserved", 117), "117 ignored");
            Assert.Equal(null, BleBatteryGuard.NormalizeOrIgnore("reserved", 101), "101 ignored");
            Assert.Equal(null, BleBatteryGuard.NormalizeOrIgnore("reserved", -1), "negative ignored");
            Assert.Equal(99, BleBatteryGuard.NormalizeOrIgnore("reserved", 99), "99 accepted");
            Assert.False(BleBatteryGuard.IsQuirked("reserved"), "not quirked");
        }

        private static void ChargingDoesNotQuirk()
        {
            Assert.Equal(50, BleBatteryGuard.Normalize("charge", 50, charging: true), "50 while charging");
            Assert.Equal(51, BleBatteryGuard.Normalize("charge", 51, charging: true), "51 while charging");
            Assert.Equal(52, BleBatteryGuard.Normalize("charge", 52, charging: true), "52 while charging");
            Assert.False(BleBatteryGuard.IsQuirked("charge"), "charging did not teach the quirk");

            // After unplugging, the consecutive +1 signature is still learnable.
            Assert.Equal(53, BleBatteryGuard.Normalize("charge", 53), "off charger");
            Assert.Equal(52, BleBatteryGuard.Normalize("charge", 54), "second off-charger increment pins the baseline");
            Assert.True(BleBatteryGuard.IsQuirked("charge"), "quirk learned after charging stopped");
        }

        private static void Persistence()
        {
            BleBatteryGuard.Normalize("persist", 30);
            BleBatteryGuard.Normalize("persist", 31);
            BleBatteryGuard.Normalize("persist", 32);
            Assert.True(BleBatteryGuard.IsQuirked("persist"), "quirked");

            var path = WattPaths.DataFile("ble-battery-guard.txt");
            Assert.True(File.Exists(path), "state file written");
            var lines = File.ReadAllLines(path);
            string deviceLine = null;
            foreach (var line in lines)
            {
                if (line.StartsWith("persist\t", StringComparison.Ordinal)) deviceLine = line;
            }
            Assert.NotNull(deviceLine, "device line present");
            var parts = deviceLine.Split('\t');
            Assert.Equal(5, parts.Length, "5 columns");
            var ticks = long.Parse(parts[4], CultureInfo.InvariantCulture);
            Assert.True(ticks > 0, "timestamp persisted");
            Assert.True(Math.Abs((new DateTime(ticks, DateTimeKind.Utc) - DateTime.UtcNow).TotalMinutes) < 5,
                "timestamp is current");

            BleBatteryGuard.ResetForTests();
            Assert.True(BleBatteryGuard.IsQuirked("persist"), "quirk survives restart");
            Assert.Equal(30, BleBatteryGuard.Normalize("persist", 45), "baseline survives restart");

            var after = File.ReadAllLines(path);
            var beforeTicks = ticks;
            foreach (var line in after)
            {
                if (!line.StartsWith("persist\t", StringComparison.Ordinal)) continue;
                var updated = long.Parse(line.Split('\t')[4], CultureInfo.InvariantCulture);
                Assert.True(updated >= beforeTicks, "timestamp not rewound");
            }
        }

        private static void LegacyState()
        {
            File.WriteAllText(WattPaths.DataFile("ble-battery-guard.txt"),
                "# header\r\nlegacy\t40\t40\ttrue\t0\r\n");

            BleBatteryGuard.ResetForTests();
            Assert.True(BleBatteryGuard.IsQuirked("legacy"), "legacy quirk loaded");
            Assert.Equal(40, BleBatteryGuard.Normalize("legacy", 60), "legacy baseline used");
        }
    }
}
