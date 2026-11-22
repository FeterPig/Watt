using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Watt.Tray
{
    /// <summary>
    /// Windows 11 stores notification-area icon placement in
    /// HKCU\Control Panel\NotifyIconSettings\&lt;id&gt;. New icons default to the overflow
    /// ("^") flyout; setting IsPromoted = 1 puts the icon on the primary taskbar next to
    /// the system icons. This is the same switch the Settings app toggles; changing it takes
    /// effect immediately (no Explorer restart). We only touch entries whose ExecutablePath
    /// is our own executable, never other apps.
    /// </summary>
    internal static class TrayPromotion
    {
        private const string SettingsKeyPath = @"Control Panel\NotifyIconSettings";
        private const string ExePathValue = "ExecutablePath";
        private const string PromotedValue = "IsPromoted";

        internal static string CurrentExecutablePath
        {
            get
            {
                try
                {
                    var entry = Assembly.GetEntryAssembly();
                    if (entry != null && !string.IsNullOrEmpty(entry.Location)) return entry.Location;
                }
                catch
                {
                    // fall through to the process path
                }

                try
                {
                    return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                }
                catch
                {
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Watt.exe");
                }
            }
        }

        /// <summary>Promotes every tray entry that belongs to this executable. Never throws.</summary>
        internal static bool EnsurePromoted()
        {
            try
            {
                var exePath = CurrentExecutablePath;
                if (string.IsNullOrEmpty(exePath)) return false;

                using (var root = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true))
                {
                    if (root == null) return false;

                    var promoted = false;
                    foreach (var id in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var entry = root.OpenSubKey(id, writable: true))
                            {
                                if (entry == null) continue;
                                var entryPath = entry.GetValue(ExePathValue) as string;
                                if (!IsSameExecutable(entryPath, exePath)) continue;

                                var current = entry.GetValue(PromotedValue);
                                if (!(current is int) || (int)current != 1)
                                {
                                    entry.SetValue(PromotedValue, 1, RegistryValueKind.DWord);
                                }
                                promoted = true;
                            }
                        }
                        catch
                        {
                            // One malformed entry must not hide the others.
                        }
                    }

                    return promoted;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSameExecutable(string registeredPath, string exePath)
        {
            if (string.IsNullOrEmpty(registeredPath)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(registeredPath), Path.GetFullPath(exePath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(registeredPath, exePath, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
