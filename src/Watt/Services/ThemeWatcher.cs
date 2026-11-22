using System;
using System.Globalization;
using System.Threading;
using Microsoft.Win32;

namespace Watt.Services
{
    /// <summary>
    /// Tracks the Windows light/dark preference for apps and the taskbar by reading
    /// HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize.
    ///
    /// The values (AppsUseLightTheme / SystemUsesLightTheme) are REG_DWORD 1 for light and
    /// 0 for dark. Missing keys or values mean "light", which is also what Windows reports
    /// on a fresh profile.
    ///
    /// <see cref="Start"/> must be called on the thread that owns the UI (normally the WPF
    /// dispatcher thread): it captures the current SynchronizationContext so
    /// <see cref="Changed"/> can be raised on the UI thread. SystemEvents fires on its own
    /// thread; if no SynchronizationContext is present the event is raised on that thread.
    /// </summary>
    public sealed class ThemeWatcher : IDisposable
    {
        /// <summary>Default registry key holding the Windows theme preferences.</summary>
        public const string DefaultPersonalizeKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        /// <summary>Value name for the app (window) theme.</summary>
        public const string AppsUseLightThemeValue = "AppsUseLightTheme";

        /// <summary>Value name for the taskbar / system theme.</summary>
        public const string SystemUsesLightThemeValue = "SystemUsesLightTheme";

        private readonly object _gate = new object();
        private readonly string _personalizeKeyPath;

        private SynchronizationContext _sync;
        private bool _started;
        private bool _disposed;

        public ThemeWatcher()
            : this(DefaultPersonalizeKeyPath)
        {
        }

        /// <summary>Creates a watcher for a custom Personalize key (used by tests).</summary>
        public ThemeWatcher(string personalizeKeyPath)
        {
            _personalizeKeyPath = string.IsNullOrEmpty(personalizeKeyPath)
                ? DefaultPersonalizeKeyPath
                : personalizeKeyPath;

            bool appsAreLight;
            bool taskbarIsLight;
            ReadTheme(out appsAreLight, out taskbarIsLight);
            AppsAreLight = appsAreLight;
            TaskbarIsLight = taskbarIsLight;
        }

        /// <summary>True when apps (windows) use the light theme.</summary>
        public bool AppsAreLight { get; private set; }

        /// <summary>True when the taskbar / system surfaces use the light theme.</summary>
        public bool TaskbarIsLight { get; private set; }

        /// <summary>Raised on the UI thread when either preference changes.</summary>
        public event EventHandler Changed;

        /// <summary>
        /// Starts listening and raises <see cref="Changed"/> once so the subscriber can
        /// apply the current theme immediately. Call once, on the UI thread.
        /// </summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_disposed || _started) return;
                _started = true;
                _sync = SynchronizationContext.Current;
            }

            try
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                SystemEvents.SessionSwitch += OnSessionSwitch;
            }
            catch (Exception ex)
            {
                ServiceLog.Error("ThemeWatcher subscribe", ex);
            }

            RefreshCore();
            RaiseChanged();
        }

        /// <summary>
        /// Re-reads the registry and raises <see cref="Changed"/> if a value changed.
        /// Safe to call from any thread and safe after Dispose (no-op).
        /// </summary>
        public void Refresh()
        {
            lock (_gate)
            {
                if (_disposed) return;
            }

            if (RefreshCore())
            {
                RaiseChanged();
            }
        }

        /// <summary>Reads the current preferences without raising events.</summary>
        public void ReadCurrent(out bool appsAreLight, out bool taskbarIsLight)
        {
            ReadTheme(out appsAreLight, out taskbarIsLight);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _sync = null;
            }

            try
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            catch (Exception ex)
            {
                ServiceLog.Error("ThemeWatcher unsubscribe", ex);
            }
        }

        private bool RefreshCore()
        {
            bool appsAreLight;
            bool taskbarIsLight;
            ReadTheme(out appsAreLight, out taskbarIsLight);

            bool changed;
            lock (_gate)
            {
                if (_disposed) return false;
                changed = appsAreLight != AppsAreLight || taskbarIsLight != TaskbarIsLight;
                AppsAreLight = appsAreLight;
                TaskbarIsLight = taskbarIsLight;
            }

            return changed;
        }

        private void ReadTheme(out bool appsAreLight, out bool taskbarIsLight)
        {
            // Windows defaults to light when the personalization key or values are absent.
            appsAreLight = true;
            taskbarIsLight = true;

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(_personalizeKeyPath, false))
                {
                    if (key == null) return;
                    appsAreLight = ReadBool(key, AppsUseLightThemeValue, true);
                    taskbarIsLight = ReadBool(key, SystemUsesLightThemeValue, true);
                }
            }
            catch (Exception ex)
            {
                ServiceLog.Error("ThemeWatcher read", ex);
            }
        }

        private static bool ReadBool(RegistryKey key, string valueName, bool fallback)
        {
            try
            {
                var value = key.GetValue(valueName);
                if (value == null) return fallback;
                return Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
            }
            catch (Exception ex)
            {
                ServiceLog.Error("ThemeWatcher read " + valueName, ex);
                return fallback;
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Theme changes usually arrive as General/Color/VisualStyle; refresh on any
            // category because a registry read is cheap and never blocks the UI.
            Refresh();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            Refresh();
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            Refresh();
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;

            SynchronizationContext sync;
            lock (_gate)
            {
                if (_disposed) return;
                sync = _sync;
            }

            if (sync == null)
            {
                InvokeHandler(handler);
                return;
            }

            try
            {
                sync.Post(delegate { InvokeHandler(handler); }, null);
            }
            catch (Exception ex)
            {
                ServiceLog.Error("ThemeWatcher post to UI", ex);
                InvokeHandler(handler);
            }
        }

        private void InvokeHandler(EventHandler handler)
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ServiceLog.Error("ThemeWatcher Changed handler", ex);
            }
        }
    }
}
