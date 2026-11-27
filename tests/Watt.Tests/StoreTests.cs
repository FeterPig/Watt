using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Watt.Battery;
using Watt.Models;
using Watt.Plugins.Support;
using Watt.Services;

namespace Watt.Tests
{
    internal static class StoreTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("store/saveMany persists, clamps and owns entries", SaveManyRoundTrip);
            runner.Run("store/reset reloads the file like an app restart", PersistenceAcrossRestart);
            runner.Run("store/remove and remove-by-plugin only touch matching entries", RemoveSemantics);
            runner.Run("store/expired entries are ignored and forgotten", ExpiredEntries);
            runner.Run("store/corrupt cache does not break scanning", CorruptCache);
            runner.Run("store/save uses the supplied owner when SourceId is empty", SaveOwnerFallback);
            runner.Run("store/writes leave no temporary files behind", NoTemporaryFiles);
            runner.Run("paths/WATT_DATA_DIR overrides the data directory", DataDirectoryOverride);
            runner.Run("paths/invalid WATT_DATA_DIR values are ignored", InvalidDataDirectoryOverride);
            runner.Run("atomic/replace keeps content and creates directories", AtomicReplace);
            runner.Run("log/concurrent writes are not lost", ConcurrentLogWrites);
            runner.Run("log/rotation keeps one previous file", LogRotation);
        }

        private static void SaveManyRoundTrip()
        {
            var devices = new List<PeripheralDevice>
            {
                new PeripheralDevice
                {
                    Id = "bt:02", Name = "Mouse", Kind = DeviceKind.Mouse, BatteryPercent = 150,
                    IsCharging = true, Address = "02:00:00:00:00:02", SourceId = "ble"
                },
                new PeripheralDevice
                {
                    Id = "bt:01", Name = "Keyboard", Kind = DeviceKind.Keyboard, BatteryPercent = 40,
                    SourceId = "aep"
                },
                new PeripheralDevice
                {
                    Id = "bt:03", Name = "No value", Kind = DeviceKind.Other, BatteryPercent = null,
                    SourceId = "aep"
                }
            };

            LastKnownStore.SaveMany(devices, DateTime.UtcNow);

            DateTime savedAt;
            string owner;
            var high = LastKnownStore.TryGet("bt:02", out savedAt, out owner);
            Assert.Equal(100, high.BatteryPercent, "percent clamped");
            Assert.Equal(true, high.IsCharging, "charging persisted");
            Assert.Equal("ble", owner, "owner persisted");
            Assert.True((DateTime.UtcNow - savedAt).TotalSeconds < 5, "timestamp persisted");

            Assert.Equal("aep", LastKnownStore.TryGet("bt:01", out savedAt, out owner).SourceId,
                "source id filled from owner");
            Assert.Null(LastKnownStore.TryGet("bt:03", out savedAt, out owner), "null values are not cached");

            var snapshot = LastKnownStore.Snapshot();
            Assert.Sequence(new[] { "bt:01", "bt:02" }, snapshot.Select(d => d.Id), "snapshot sorted by id");
        }

        private static void PersistenceAcrossRestart()
        {
            LastKnownStore.SaveMany(new[]
            {
                new PeripheralDevice
                {
                    Id = "bt:01", Name = "Keyboard", Kind = DeviceKind.Keyboard, BatteryPercent = 44,
                    Address = "01:00:00:00:00:01", SourceId = "aep"
                }
            }, DateTime.UtcNow);

            LastKnownStore.ResetForTests();

            DateTime savedAt;
            string owner;
            var cached = LastKnownStore.TryGet("bt:01", out savedAt, out owner);
            Assert.NotNull(cached, "entry reloaded");
            Assert.Equal(44, cached.BatteryPercent, "percent reloaded");
            Assert.Equal("01:00:00:00:00:01", cached.Address, "address reloaded");
            Assert.Equal("aep", owner, "owner reloaded");
        }

        private static void RemoveSemantics()
        {
            LastKnownStore.SaveMany(new[]
            {
                new PeripheralDevice { Id = "a", Name = "A", Kind = DeviceKind.Other, BatteryPercent = 10, SourceId = "p1" },
                new PeripheralDevice { Id = "b", Name = "B", Kind = DeviceKind.Other, BatteryPercent = 20, SourceId = "p1" },
                new PeripheralDevice { Id = "c", Name = "C", Kind = DeviceKind.Other, BatteryPercent = 30, SourceId = "p2" }
            }, DateTime.UtcNow);

            LastKnownStore.Remove("a");
            DateTime savedAt;
            string owner;
            Assert.Null(LastKnownStore.TryGet("a", out savedAt, out owner), "removed");
            Assert.NotNull(LastKnownStore.TryGet("b", out savedAt, out owner), "other entry kept");

            LastKnownStore.RemoveByPlugin("P1");
            Assert.Null(LastKnownStore.TryGet("b", out savedAt, out owner), "plugin entries removed case-insensitively");
            Assert.NotNull(LastKnownStore.TryGet("c", out savedAt, out owner), "other plugin entries kept");

            LastKnownStore.ResetForTests();
            Assert.Null(LastKnownStore.TryGet("b", out savedAt, out owner), "removal persisted");
            Assert.NotNull(LastKnownStore.TryGet("c", out savedAt, out owner), "unrelated entry persisted");
        }

        private static void ExpiredEntries()
        {
            var ticks = DateTime.UtcNow.AddDays(-31).Ticks;
            File.WriteAllText(WattPaths.DataFile("last-known.json"),
                "{\"devices\":[{\"id\":\"old\",\"name\":\"Old\",\"kind\":\"Mouse\",\"percent\":50," +
                "\"charging\":false,\"address\":null,\"pluginId\":\"p\",\"savedAtTicksUtc\":" + ticks + "}]}");

            LastKnownStore.ResetForTests();

            DateTime savedAt;
            string owner;
            Assert.Null(LastKnownStore.TryGet("old", out savedAt, out owner), "expired entry ignored");
            Assert.Equal(0, LastKnownStore.Snapshot().Count, "expired entry not in snapshot");
            Assert.Equal(0, LastKnownStore.GetByPlugin("p").Count, "expired entry not returned for plugin");

            // TryGet removes the expired entry from the file.
            LastKnownStore.ResetForTests();
            var text = File.ReadAllText(WattPaths.DataFile("last-known.json"));
            Assert.False(text.Contains("\"old\""), "expired entry pruned on access");
        }

        private static void CorruptCache()
        {
            File.WriteAllText(WattPaths.DataFile("last-known.json"), "{ this is not json");
            LastKnownStore.ResetForTests();

            DateTime savedAt;
            string owner;
            Assert.Null(LastKnownStore.TryGet("anything", out savedAt, out owner), "corrupt cache ignored");
            Assert.Equal(0, LastKnownStore.Snapshot().Count, "empty snapshot");

            LastKnownStore.SaveMany(new[]
            {
                new PeripheralDevice { Id = "good", Name = "Good", Kind = DeviceKind.Other, BatteryPercent = 5, SourceId = "p" }
            }, DateTime.UtcNow);

            LastKnownStore.ResetForTests();
            Assert.NotNull(LastKnownStore.TryGet("good", out savedAt, out owner), "cache recovers after a save");
        }

        private static void SaveOwnerFallback()
        {
            var device = new PeripheralDevice
            {
                Id = "d", Name = "D", Kind = DeviceKind.Keyboard, BatteryPercent = 12
            };
            LastKnownStore.Save(device, "fallback", DateTime.UtcNow);

            DateTime savedAt;
            string owner;
            LastKnownStore.TryGet("d", out savedAt, out owner);
            Assert.Equal("fallback", owner, "explicit plugin id used when SourceId is empty");
        }

        private static void NoTemporaryFiles()
        {
            LastKnownStore.SaveMany(new[]
            {
                new PeripheralDevice { Id = "d", Name = "D", Kind = DeviceKind.Other, BatteryPercent = 9, SourceId = "p" }
            }, DateTime.UtcNow);

            var leftovers = Directory.GetFiles(TestEnv.DataRoot, "*.tmp", SearchOption.AllDirectories);
            Assert.Equal(0, leftovers.Length, "no temporary file left after an atomic write");
            Assert.True(File.Exists(WattPaths.DataFile("last-known.json")), "cache file exists");
        }

        private static void DataDirectoryOverride()
        {
            Assert.Equal(Path.GetFullPath(TestEnv.DataRoot), Path.GetFullPath(WattPaths.DataDirectory),
                "WATT_DATA_DIR is honored");
            Assert.Equal(Path.Combine(WattPaths.DataDirectory, "x.json"), WattPaths.DataFile("x.json"),
                "DataFile combines the root");
        }

        private static void InvalidDataDirectoryOverride()
        {
            var original = Environment.GetEnvironmentVariable(WattPaths.DataDirectoryVariable);
            try
            {
                Environment.SetEnvironmentVariable(WattPaths.DataDirectoryVariable, @"relative\cache");
                var path = WattPaths.DataDirectory;
                Assert.True(Path.IsPathRooted(path), "relative override ignored in favour of an absolute path");

                Environment.SetEnvironmentVariable(WattPaths.DataDirectoryVariable, "   ");
                Assert.True(Path.IsPathRooted(WattPaths.DataDirectory), "blank override ignored");
            }
            finally
            {
                Environment.SetEnvironmentVariable(WattPaths.DataDirectoryVariable, original);
            }
        }

        private static void AtomicReplace()
        {
            var path = Path.Combine(TestEnv.UniqueDirectory("atomic"), "nested", "file.bin");
            AtomicFile.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Assert.Sequence(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path), "first write");

            AtomicFile.WriteAllBytes(path, new byte[] { 9, 8 });
            Assert.Sequence(new byte[] { 9, 8 }, File.ReadAllBytes(path), "replace");

            AtomicFile.WriteAllText(path, "héllo", new UTF8Encoding(false));
            Assert.Equal("héllo", File.ReadAllText(path, Encoding.UTF8), "text write");

            Assert.Equal(0, Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length,
                "no temporary file left");
        }

        private static void ConcurrentLogWrites()
        {
            DeleteLogFiles();
            const int threads = 4;
            const int perThread = 50;

            var tasks = new Task[threads];
            for (var t = 0; t < threads; t++)
            {
                var index = t;
                tasks[t] = Task.Run(() =>
                {
                    for (var i = 0; i < perThread; i++)
                        LogFile.Write("concurrent-" + index + "-" + i);
                });
            }
            Task.WaitAll(tasks);

            var lines = File.ReadAllLines(LogFile.Path, Encoding.UTF8);
            Assert.Equal(threads * perThread, lines.Length, "every line was appended");
            for (var t = 0; t < threads; t++)
            {
                for (var i = 0; i < perThread; i++)
                    Assert.True(lines.Contains("concurrent-" + t + "-" + i), "line " + t + "-" + i + " present");
            }
        }

        private static void LogRotation()
        {
            DeleteLogFiles();

            LogFile.Write(new string('x', 1024 * 1024 + 1024));
            LogFile.Write("after-rotate");

            Assert.True(File.Exists(LogFile.Path + ".1"), "previous log kept as .1");
            Assert.True(File.ReadAllText(LogFile.Path, Encoding.UTF8).Contains("after-rotate"),
                "current log has the new line");
        }

        private static void DeleteLogFiles()
        {
            try
            {
                if (File.Exists(LogFile.Path)) File.Delete(LogFile.Path);
                if (File.Exists(LogFile.Path + ".1")) File.Delete(LogFile.Path + ".1");
            }
            catch
            {
                // ignore
            }
        }
    }
}
