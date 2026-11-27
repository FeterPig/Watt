using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Watt.Battery;
using Watt.Models;
using Watt.Plugins;

namespace Watt.Tests
{
    internal static class BatteryServiceTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("battery/fresh reading is displayed, attributed and cached", FreshReading);
            runner.Run("battery/sleeping device keeps last-known value", SleepingDevice);
            runner.Run("battery/percent is clamped to 0..100", PercentClamp);
            runner.Run("battery/devices are sorted keyboard, mouse, headphone, other", SortOrder);
            runner.Run("battery/device without value and without cache stays hidden", UnknownDeviceHidden);
            runner.Run("battery/absent transport keeps rows for two scans, removes on third", AbsentThreeScans);
            runner.Run("battery/an observed scan resets the absent counter", AbsentCounterResets);
            runner.Run("battery/timeout keeps cache and does not count as absent", TimeoutKeepsCache);
            runner.Run("battery/throwing plugin keeps cache and does not count as absent", ThrowKeepsCache);
            runner.Run("battery/plugin returning null task keeps cache", NullTaskKeepsCache);
            runner.Run("battery/lower priority number wins for fresh readings", PriorityWins);
            runner.Run("battery/fresh value beats a cached higher-priority value", FreshBeatsCached);
            runner.Run("battery/merged winner owns the cache entry", CacheOwnerIsWinner);
            runner.Run("battery/two addressed devices with the same name stay separate", SameNameDifferentAddress);
            runner.Run("battery/addressed and address-less observations merge by name", AddresslessMerge);
            runner.Run("battery/manual refresh removes an explicitly disconnected device", ManualRemovesDisconnected);
            runner.Run("battery/manual refresh keeps a device with unknown connection state", ManualKeepsUnknown);
            runner.Run("battery/manual refresh keeps a cache refreshed by another plugin", ManualFreshFromOtherPlugin);
            runner.Run("battery/manual refresh drops an absent plugin cache immediately", ManualAbsentRemoves);
            runner.Run("battery/concurrent non-manual refreshes share one scan", ConcurrentRefreshesShared);
            runner.Run("battery/manual refresh waits for the in-flight scan and rescans", ManualWaitsForInflight);
            runner.Run("battery/dispose clears devices and completes refresh calls", DisposeStopsScanning);
            runner.Run("battery/no plugins yields an empty snapshot", NoPlugins);
        }

        private static void FreshReading()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa:bb:cc:dd:ee:ff", "MX Master 3S", DeviceKind.Mouse, 55)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                var devices = BatteryServiceFactory.Scan(service);
                Assert.Equal(1, devices.Count, "one device");
                Assert.Equal("MX Master 3S", devices[0].Name, "name");
                Assert.Equal(55, devices[0].BatteryPercent, "percent");
                Assert.Equal("p1", devices[0].SourceId, "source");

                DateTime savedAt;
                string owner;
                var cached = LastKnownStore.TryGet("bt:aa:bb:cc:dd:ee:ff", out savedAt, out owner);
                Assert.NotNull(cached, "cache entry");
                Assert.Equal(55, cached.BatteryPercent, "cached percent");
                Assert.Equal("p1", owner, "cache owner");
                Assert.True((DateTime.UtcNow - savedAt).TotalSeconds < 5, "cache timestamp is current");
            }
        }

        private static void SleepingDevice()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60, false, "01:00:00:00:00:01", true)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                BatteryServiceFactory.Scan(service);
                plugin.Handler = token => Task.FromResult(Devices.Observed(
                    Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, null, false,
                        "01:00:00:00:00:01", true)));

                var devices = BatteryServiceFactory.Scan(service);
                Assert.Equal(1, devices.Count, "sleeping device keeps its row");
                Assert.Equal(60, devices[0].BatteryPercent, "cached percent");
                Assert.Equal(true, devices[0].IsConnected, "connection state carried through");
            }
        }

        private static void PercentClamp()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("d:high", "High", DeviceKind.Other, 150),
                Devices.Device("d:low", "Low", DeviceKind.Other, -3)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                var devices = BatteryServiceFactory.Scan(service);
                var high = devices.Single(d => d.Id == "d:high");
                var low = devices.Single(d => d.Id == "d:low");
                Assert.Equal(100, high.BatteryPercent, "above 100 clamped");
                Assert.Equal(0, low.BatteryPercent, "below 0 clamped");

                DateTime savedAt;
                string owner;
                Assert.Equal(100, LastKnownStore.TryGet("d:high", out savedAt, out owner).BatteryPercent,
                    "cache clamped too");
            }
        }

        private static void SortOrder()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("d:4", "Z Other", DeviceKind.Other, 10),
                Devices.Device("d:3", "Headphone", DeviceKind.Headphone, 10),
                Devices.Device("d:2b", "B Mouse", DeviceKind.Mouse, 10),
                Devices.Device("d:2a", "A Mouse", DeviceKind.Mouse, 10),
                Devices.Device("d:1", "Keyboard", DeviceKind.Keyboard, 10)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                var devices = BatteryServiceFactory.Scan(service);
                Assert.Sequence(new[] { "Keyboard", "A Mouse", "B Mouse", "Headphone", "Z Other" },
                    devices.Select(d => d.Name), "sort order");
            }
        }

        private static void UnknownDeviceHidden()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("d:unknown", "No reading", DeviceKind.Keyboard, null)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                Assert.Equal(0, BatteryServiceFactory.Scan(service).Count, "no row without a value");
            }
        }

        private static void AbsentThreeScans()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                BatteryServiceFactory.Scan(service);
                plugin.Handler = token => Task.FromResult(Devices.Absent());

                Assert.Equal(1, BatteryServiceFactory.Scan(service).Count, "first absent keeps cache");
                Assert.Equal(1, BatteryServiceFactory.Scan(service).Count, "second absent keeps cache");
                Assert.Equal(0, BatteryServiceFactory.Scan(service).Count, "third absent removes device");

                DateTime savedAt;
                string owner;
                Assert.Null(LastKnownStore.TryGet("bt:01", out savedAt, out owner), "cache removed");
            }
        }

        private static void AbsentCounterResets()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                BatteryServiceFactory.Scan(service);
                plugin.Handler = token => Task.FromResult(Devices.Absent());
                BatteryServiceFactory.Scan(service);
                BatteryServiceFactory.Scan(service);

                plugin.Handler = token => Task.FromResult(Devices.Observed(
                    Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, null, false, null, true)));
                BatteryServiceFactory.Scan(service);

                plugin.Handler = token => Task.FromResult(Devices.Absent());
                Assert.Equal(1, BatteryServiceFactory.Scan(service).Count, "counter reset: first absent keeps");
                BatteryServiceFactory.Scan(service);
                Assert.Equal(1, service.Devices.Count, "two absents after reset still keep");
                BatteryServiceFactory.Scan(service);
                Assert.Equal(0, service.Devices.Count, "third consecutive absent removes");
            }
        }

        private static void TimeoutKeepsCache()
        {
            var plugin = new FakePlugin("p1", 10, 500);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                BatteryServiceFactory.Scan(service);

                var never = new TaskCompletionSource<PluginReadResult>();
                plugin.Handler = token => never.Task;

                var watch = Stopwatch.StartNew();
                var devices = BatteryServiceFactory.Scan(service);
                watch.Stop();

                Assert.True(watch.ElapsedMilliseconds >= 400, "host waited for the plugin budget");
                Assert.Equal(1, devices.Count, "timed-out plugin keeps its cached row");
                Assert.Equal(60, devices[0].BatteryPercent, "cached value survives timeout");
            }
        }

        private static void ThrowKeepsCache()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                BatteryServiceFactory.Scan(service);
                plugin.Handler = token => { throw new InvalidOperationException("boom"); };
                var devices = BatteryServiceFactory.Scan(service);
                Assert.Equal(1, devices.Count, "throwing plugin keeps cache");
                Assert.Equal(60, devices[0].BatteryPercent, "cached value survives exception");
            }
        }

        private static void NullTaskKeepsCache()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                BatteryServiceFactory.Scan(service);
                plugin.Handler = token => null;
                var devices = BatteryServiceFactory.Scan(service);
                Assert.Equal(1, devices.Count, "null task keeps cache");
                Assert.Equal(60, devices[0].BatteryPercent, "cached value survives null task");
            }
        }

        private static void PriorityWins()
        {
            var low = new FakePlugin("ble", 5);
            low.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa", "Mouse", DeviceKind.Mouse, 70, true, "aa:bb:cc:dd:ee:ff")));
            var high = new FakePlugin("aep", 10);
            high.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa", "Mouse", DeviceKind.Mouse, 80, false, "aa:bb:cc:dd:ee:ff")));

            using (var service = BatteryServiceFactory.Create(high.Loaded(), low.Loaded()))
            {
                var devices = BatteryServiceFactory.Scan(service);
                Assert.Equal(1, devices.Count, "merged into one row");
                Assert.Equal(70, devices[0].BatteryPercent, "lower priority number wins");
                Assert.Equal(true, devices[0].IsCharging, "winner's charging state wins");
                Assert.Equal("ble", devices[0].SourceId, "winner's source is reported");
            }
        }

        private static void FreshBeatsCached()
        {
            var low = new FakePlugin("ble", 5);
            low.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa", "Mouse", DeviceKind.Mouse, 70, false, "aa:bb:cc:dd:ee:ff")));
            var high = new FakePlugin("aep", 10);
            high.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa", "Mouse", DeviceKind.Mouse, 80, false, "aa:bb:cc:dd:ee:ff")));

            using (var service = BatteryServiceFactory.Create(high.Loaded(), low.Loaded()))
            {
                var first = BatteryServiceFactory.Scan(service);
                Assert.Equal(70, first[0].BatteryPercent, "first scan uses BLE");

                low.Handler = token => Task.FromResult(Devices.Observed(
                    Devices.Device("bt:aa", "Mouse", DeviceKind.Mouse, null, false, "aa:bb:cc:dd:ee:ff", true)));

                var second = BatteryServiceFactory.Scan(service);
                Assert.Equal(80, second[0].BatteryPercent, "fresh AEP value beats the cached BLE value");
                Assert.Equal("aep", second[0].SourceId, "fresh source owns the row");
            }
        }

        private static void CacheOwnerIsWinner()
        {
            var low = new FakePlugin("ble", 5);
            low.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa", "Mouse", DeviceKind.Mouse, 70, false, "aa:bb:cc:dd:ee:ff")));
            var high = new FakePlugin("aep", 10);
            high.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa", "Mouse", DeviceKind.Mouse, 80, false, "aa:bb:cc:dd:ee:ff")));

            using (var service = BatteryServiceFactory.Create(high.Loaded(), low.Loaded()))
            {
                var devices = BatteryServiceFactory.Scan(service);
                Assert.Equal(70, devices[0].BatteryPercent, "displayed BLE value");

                DateTime savedAt;
                string owner;
                var cached = LastKnownStore.TryGet("bt:aa", out savedAt, out owner);
                Assert.Equal(70, cached.BatteryPercent, "cache stores the displayed winner, not the last writer");
                Assert.Equal("ble", owner, "cache owner is the winning plugin");
            }
        }

        private static void SameNameDifferentAddress()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:one", "MX Keys", DeviceKind.Keyboard, 80, false, "11:11:11:11:11:11"),
                Devices.Device("bt:two", "MX Keys", DeviceKind.Keyboard, 40, false, "22:22:22:22:22:22")));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                var devices = BatteryServiceFactory.Scan(service);
                Assert.Equal(2, devices.Count, "two real MAC addresses must never merge by name");
            }
        }

        private static void AddresslessMerge()
        {
            var addressless = new FakePlugin("vendor", 5);
            addressless.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("vendor:1", "Same Device", DeviceKind.Mouse, 50)));
            var addressed = new FakePlugin("aep", 10);
            addressed.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa", "Same Device", DeviceKind.Mouse, 50, false, "aa:bb:cc:dd:ee:ff")));

            using (var service = BatteryServiceFactory.Create(addressless.Loaded(), addressed.Loaded()))
            {
                var devices = BatteryServiceFactory.Scan(service);
                Assert.Equal(1, devices.Count, "address-less observation merges by name");
                Assert.Equal("aa:bb:cc:dd:ee:ff", devices[0].Address, "address filled from the other source");
            }
        }

        private static void ManualRemovesDisconnected()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                BatteryServiceFactory.Scan(service);
                plugin.Handler = token => Task.FromResult(Devices.Observed(
                    Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, null, false, null, false)));

                var devices = BatteryServiceFactory.Scan(service, manual: true);
                Assert.Equal(0, devices.Count, "manual refresh removes the disconnected row");
                DateTime savedAt;
                string owner;
                Assert.Null(LastKnownStore.TryGet("bt:01", out savedAt, out owner), "cache removed");
            }
        }

        private static void ManualKeepsUnknown()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                BatteryServiceFactory.Scan(service);
                plugin.Handler = token => Task.FromResult(Devices.Observed(
                    Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, null)));

                var devices = BatteryServiceFactory.Scan(service, manual: true);
                Assert.Equal(1, devices.Count, "unknown connection state keeps the cached row");
                Assert.Equal(60, devices[0].BatteryPercent, "cached value");
            }
        }

        private static void ManualFreshFromOtherPlugin()
        {
            var disconnected = new FakePlugin("aep", 10);
            disconnected.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa", "Mouse", DeviceKind.Mouse, null, false, "aa:bb:cc:dd:ee:ff", false)));
            var fresh = new FakePlugin("ble", 5);
            fresh.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:aa", "Mouse", DeviceKind.Mouse, 40, false, "aa:bb:cc:dd:ee:ff", true)));

            using (var service = BatteryServiceFactory.Create(disconnected.Loaded(), fresh.Loaded()))
            {
                var devices = BatteryServiceFactory.Scan(service, manual: true);
                Assert.Equal(1, devices.Count, "a fresh reading from another plugin wins over a disconnect");
                Assert.Equal(40, devices[0].BatteryPercent, "fresh value displayed");

                DateTime savedAt;
                string owner;
                Assert.NotNull(LastKnownStore.TryGet("bt:aa", out savedAt, out owner), "fresh cache not deleted");
            }
        }

        private static void ManualAbsentRemoves()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60)));

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                BatteryServiceFactory.Scan(service);
                plugin.Handler = token => Task.FromResult(Devices.Absent());
                var devices = BatteryServiceFactory.Scan(service, manual: true);
                Assert.Equal(0, devices.Count, "manual refresh drops an absent plugin's cache immediately");
                DateTime savedAt;
                string owner;
                Assert.Null(LastKnownStore.TryGet("bt:01", out savedAt, out owner), "cache removed");
            }
        }

        private static void ConcurrentRefreshesShared()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = async token =>
            {
                await Task.Delay(200).ConfigureAwait(false);
                return Devices.Observed(Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60));
            };

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                var first = service.RefreshAsync(false);
                var second = service.RefreshAsync(false);
                Assert.True(ReferenceEquals(first, second), "concurrent refreshes share the in-flight task");
                Task.WaitAll(first, second);
                Assert.Equal(1, plugin.Calls, "plugin was invoked once");
                Assert.Equal(1, service.Devices.Count, "one device");
            }
        }

        private static void ManualWaitsForInflight()
        {
            var gate = new TaskCompletionSource<PluginReadResult>();
            var plugin = new FakePlugin("p1", 10);
            var calls = 0;
            plugin.Handler = token =>
            {
                var call = Interlocked.Increment(ref calls);
                if (call == 1) return gate.Task;
                return Task.FromResult(Devices.Observed(
                    Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 50)));
            };

            using (var service = BatteryServiceFactory.Create(plugin.Loaded()))
            {
                var first = service.RefreshAsync(false);
                Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) >= 1, 3000),
                    "first scan started");

                var manual = service.RefreshAsync(true, true);
                gate.SetResult(Devices.Observed(Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 50)));

                first.GetAwaiter().GetResult();
                manual.GetAwaiter().GetResult();

                Assert.Equal(2, plugin.Calls, "manual refresh waited and ran a second scan");
                Assert.Equal(1, service.Devices.Count, "device visible after manual refresh");
            }
        }

        private static void DisposeStopsScanning()
        {
            var plugin = new FakePlugin("p1", 10);
            plugin.Handler = token => Task.FromResult(Devices.Observed(
                Devices.Device("bt:01", "Keyboard", DeviceKind.Keyboard, 60)));

            var service = BatteryServiceFactory.Create(plugin.Loaded());
            BatteryServiceFactory.Scan(service);
            Assert.Equal(1, service.Devices.Count, "device before dispose");

            service.Dispose();
            Assert.Equal(0, service.Devices.Count, "dispose clears the snapshot");
            Assert.True(service.RefreshAsync(true).IsCompleted, "refresh after dispose completes immediately");
        }

        private static void NoPlugins()
        {
            using (var service = BatteryServiceFactory.Create())
            {
                Assert.Equal(0, service.Devices.Count, "no devices");
                Assert.Equal(0, BatteryServiceFactory.Scan(service).Count, "empty scan");
            }
        }
    }
}
