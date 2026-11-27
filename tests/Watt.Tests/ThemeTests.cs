using System;
using Microsoft.Win32;
using Watt.Services;

namespace Watt.Tests
{
    internal static class ThemeTests
    {
        internal static void Register(TestRunner runner)
        {
            runner.Run("theme/reads the current personalization values", ReadTheme);
            runner.Run("theme/missing key defaults to light", MissingKey);
            runner.Run("theme/refresh is idempotent for unchanged values", RefreshIdempotent);
            runner.Run("theme/start raises once and dispose silences updates", StartAndDispose);
        }

        private static void ReadTheme()
        {
            bool apps;
            bool taskbar;
            using (var watcher = new ThemeWatcher())
            {
                watcher.ReadCurrent(out apps, out taskbar);
                Assert.Equal(apps, watcher.AppsAreLight, "apps value matches a fresh read");
                Assert.Equal(taskbar, watcher.TaskbarIsLight, "taskbar value matches a fresh read");
            }

            // Compare with the registry directly as well: the values must be 0/1 DWORDs.
            using (var key = Registry.CurrentUser.OpenSubKey(ThemeWatcher.DefaultPersonalizeKeyPath))
            {
                if (key == null) return;
                var expectedApps = Convert.ToInt32(key.GetValue(ThemeWatcher.AppsUseLightThemeValue) ?? 1) != 0;
                var expectedTaskbar =
                    Convert.ToInt32(key.GetValue(ThemeWatcher.SystemUsesLightThemeValue) ?? 1) != 0;
                Assert.Equal(expectedApps, apps, "apps value read from registry");
                Assert.Equal(expectedTaskbar, taskbar, "taskbar value read from registry");
            }
        }

        private static void MissingKey()
        {
            using (var watcher = new ThemeWatcher("Software\\Watt.Tests\\Theme\\does-not-exist-" +
                                                  Guid.NewGuid().ToString("N")))
            {
                Assert.True(watcher.AppsAreLight, "apps default light");
                Assert.True(watcher.TaskbarIsLight, "taskbar default light");
            }
        }

        private static void RefreshIdempotent()
        {
            using (var watcher = new ThemeWatcher("Software\\Watt.Tests\\Theme\\does-not-exist-" +
                                                  Guid.NewGuid().ToString("N")))
            {
                var raised = 0;
                watcher.Changed += delegate { raised++; };
                watcher.Refresh();
                watcher.Refresh();
                Assert.Equal(0, raised, "no event when nothing changed");
            }
        }

        private static void StartAndDispose()
        {
            using (var watcher = new ThemeWatcher("Software\\Watt.Tests\\Theme\\does-not-exist-" +
                                                  Guid.NewGuid().ToString("N")))
            {
                var raised = 0;
                watcher.Changed += delegate { raised++; };
                watcher.Start();
                Assert.Equal(1, raised, "start raises the current theme once");
                watcher.Refresh();
                Assert.Equal(1, raised, "refresh after start without a change is silent");
            }
        }
    }
}
