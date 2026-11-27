using System;
using System.IO;
using System.Linq;
using Watt.Battery;
using Watt.Diagnostics;
using Watt.Models;
using Watt.Plugins;
using Watt.Services;
using Watt.Tray;
using Watt.UI;

namespace Watt.Tests
{
    internal static class DiagnosticsTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("diagnostics/snapshot renders every panel state", SnapshotRendersAllStates);
            runner.Run("diagnostics/demo data is deterministic", DemoDataShapes);
            runner.Run("diagnostics/report writers fail loudly for an unusable path", ReportWritersFail);
            runner.Run("diagnostics/single instance acquires and releases", SingleInstanceGuard);
            runner.Run("diagnostics/tray icon assets load", TrayIcons);
            runner.Run("diagnostics/tray promotion does not throw for this process", TrayPromotionSafe);
        }

        private static void SnapshotRendersAllStates()
        {
            var directory = TestEnv.UniqueDirectory("snapshots");
            Snapshot.Render(directory);

            var files = Directory.GetFiles(directory, "*.png");
            Assert.Equal(8, files.Length, "eight panel states rendered");
            foreach (var file in files)
                Assert.True(new FileInfo(file).Length > 1000, Path.GetFileName(file) + " is a real image");
        }

        private static void DemoDataShapes()
        {
            Assert.Equal(3, DemoData.Three().Count, "three demo devices");
            Assert.Equal(2, DemoData.Two().Count, "two demo devices");
            Assert.Equal(1, DemoData.OneLow().Count, "one low device");
            Assert.Equal(0, DemoData.Empty().Count, "empty demo");
            Assert.Equal(DeviceKind.Headphone, DemoData.Three()[2].Kind, "headphone third");
            Assert.Equal(true, DemoData.Three()[2].IsCharging, "headphone charging");
            Assert.True(DemoData.OneLow()[0].BatteryPercent <= 15, "one low is low");
        }

        private static void ReportWritersFail()
        {
            var directory = TestEnv.UniqueDirectory("report-is-a-directory");
            Assert.False(BatterySelfTest.WritePluginList(directory, null), "plugins report write fails");
            Assert.False(BatterySelfTest.Run(directory, null), "selftest report write fails");
            Assert.False(BatterySelfTest.WritePluginList(null, null), "null plugins path fails");
        }

        private static void SingleInstanceGuard()
        {
            var suffix = Guid.NewGuid().ToString("N");
            var guard = new SingleInstance(@"Local\Watt.Tests." + suffix, @"Local\Watt.Tests.Activate." + suffix);
            try
            {
                Assert.True(guard.TryAcquire(), "first acquire");
                Assert.True(guard.IsAcquired, "acquired state");
                Assert.True(guard.TryAcquire(), "re-acquire on the owning thread is a no-op success");
            }
            finally
            {
                guard.Dispose();
            }

            Assert.False(guard.IsAcquired, "released on dispose");
            Assert.False(guard.TryAcquire(), "cannot acquire after dispose");
        }

        private static void TrayIcons()
        {
            using (var light = TrayAssets.LoadTrayIcon(false))
            using (var dark = TrayAssets.LoadTrayIcon(true))
            using (var app = TrayAssets.LoadAppIcon())
            {
                Assert.NotNull(light, "light tray icon");
                Assert.NotNull(dark, "dark tray icon");
                Assert.NotNull(app, "app icon");
                Assert.True(light.Width > 0, "icon has a width");
                Assert.True(dark.Width > 0, "dark icon has a width");
            }
        }

        private static void TrayPromotionSafe()
        {
            Assert.NotNull(TrayPromotion.CurrentExecutablePath, "current executable path");
            Assert.True(Path.IsPathRooted(TrayPromotion.CurrentExecutablePath), "executable path is absolute");
            // This must never throw regardless of whether Explorer has an entry for the test exe.
            TrayPromotion.EnsurePromoted();
            TrayPromotion.HasCurrentEntry();
        }
    }
}
