using System;
using System.IO;
using System.Linq;
using System.Threading;
using Watt.Plugins;

namespace Watt.Tests
{
    internal static class PluginHostTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("plugins/built-in plugins load with metadata", BuiltInPlugins);
            runner.Run("plugins/broken DLL is reported without breaking discovery", BrokenDll);
            runner.Run("plugins/Contracts and Support DLLs are skipped", ContractAssembliesSkipped);
            runner.Run("plugins/later directory overrides the same plugin id", LaterDirectoryWins);
        }

        private static string PluginDirectory(string project)
        {
            return Path.Combine(TestEnv.RepoRoot, "src", project, "bin", "Release", "net48");
        }

        private static string TestPluginDirectory(string project)
        {
            return Path.Combine(TestEnv.RepoRoot, "tests", "plugins", project, "bin", "Release", "net48");
        }

        private static void BuiltInPlugins()
        {
            var host = PluginHost.Load(new[]
            {
                PluginDirectory("Watt.Plugins.Windows"),
                PluginDirectory("Watt.Plugins.BleGatt")
            }, null);

            Assert.Equal(2, host.Plugins.Count, "two built-in plugins");
            Assert.Equal(0, host.Issues.Count, "no load issues");
            Assert.NotNull(host.Plugins.SingleOrDefault(p => p.Plugin.Id == "windows-aep"), "windows-aep");
            Assert.NotNull(host.Plugins.SingleOrDefault(p => p.Plugin.Id == "ble-gatt"), "ble-gatt");
            Assert.Equal(10, host.Plugins.Single(p => p.Plugin.Id == "windows-aep").Plugin.Priority,
                "AEP priority");
            Assert.Equal(5, host.Plugins.Single(p => p.Plugin.Id == "ble-gatt").Plugin.Priority, "BLE priority");
            Assert.True(host.SearchDirectories.Count >= 3, "app, user and extra plugin folders are searched");
        }

        private static void BrokenDll()
        {
            var directory = TestEnv.UniqueDirectory("plugins-broken");
            File.WriteAllText(Path.Combine(directory, "Broken.dll"), "this is not a managed assembly");

            var host = PluginHost.Load(new[] { directory }, null);
            Assert.Equal(0, host.Plugins.Count, "broken DLL is not loaded");
            Assert.Equal(1, host.Issues.Count, "one load issue reported");
            Assert.True(host.Issues[0].Message.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        host.Issues[0].Message.IndexOf("assembly", StringComparison.OrdinalIgnoreCase) >= 0,
                "issue describes the load failure");
        }

        private static void ContractAssembliesSkipped()
        {
            var directory = TestEnv.UniqueDirectory("plugins-contracts");
            File.Copy(typeof(IBatteryPlugin).Assembly.Location,
                Path.Combine(directory, "Watt.Plugin.Contracts.dll"));
            File.Copy(typeof(Watt.Plugins.Support.DeviceClassifier).Assembly.Location,
                Path.Combine(directory, "Watt.Plugin.Support.dll"));

            var host = PluginHost.Load(new[] { directory }, null);
            Assert.Equal(0, host.Plugins.Count, "contract DLLs are not plugins");
            Assert.Equal(0, host.Issues.Count, "contract DLLs are skipped without an issue");
        }

        private static void LaterDirectoryWins()
        {
            var first = TestPluginDirectory("OverrideA");
            var second = TestPluginDirectory("OverrideB");
            Assert.True(File.Exists(Path.Combine(first, "Watt.TestPlugin.OverrideA.dll")),
                "OverrideA test plugin is built");
            Assert.True(File.Exists(Path.Combine(second, "Watt.TestPlugin.OverrideB.dll")),
                "OverrideB test plugin is built");

            var host = PluginHost.Load(new[] { first, second }, null);
            Assert.Equal(1, host.Plugins.Count, "same Id is de-duplicated");
            Assert.True(host.Plugins[0].AssemblyPath.EndsWith("Watt.TestPlugin.OverrideB.dll",
                    StringComparison.OrdinalIgnoreCase),
                "later directory wins");

            var result = host.Plugins[0].Plugin.ReadAsync(host.Plugins[0].Context, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.Equal(1, result.Devices.Count, "test plugin read");
            Assert.Equal(22, result.Devices[0].BatteryPercent, "later plugin's value");
        }
    }
}
