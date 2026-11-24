using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Watt.Battery;
using Watt.Diagnostics;
using Watt.Models;
using Watt.Plugins;
using Watt.Services;
using Watt.Tray;
using Watt.UI;

namespace Watt
{
    public partial class App : Application
    {
        private const string RunValueName = "Watt";

        private SingleInstance _singleInstance;
        private ThemeWatcher _themeWatcher;
        private StartupService _startupService;
        private TrayController _tray;
        private FloatingWindow _window;
        private PanelView _panel;
        private BatteryService _battery;
        private PluginHost _pluginHost;
        private DispatcherTimer _refreshTimer;
        private DispatcherTimer _autoCloseTimer;
        private DispatcherTimer _promoteTimer;
        private int _promoteAttempts;
        private bool _promotionConfirmed;
        private bool _explorerRestartAttempted;
        private HwndSource _taskbarMessageWindow;
        private int _taskbarCreatedMessage;
        private bool _refreshing;
        private bool _demoMode;
        private DateTime _lastRefreshUtc = DateTime.MinValue;

        internal static string LogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watt-startup.log"); }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Trace("OnStartup args=" + string.Join(" ", e.Args));

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            try
            {
                if (RunDiagnosticMode(e.Args))
                {
                    return;
                }

                StartTrayApplication(e.Args);
            }
            catch (Exception ex)
            {
                Trace("FATAL " + ex);
                Shutdown(1);
            }
        }

        // ---------------------------------------------------------------- diagnostics

        private bool RunDiagnosticMode(string[] args)
        {
            var snapshotDir = GetOption(args, "--snapshot");
            if (snapshotDir != null)
            {
                Trace("rendering snapshots to " + snapshotDir);
                Snapshot.Render(snapshotDir);
                Trace("snapshots done");
                Shutdown();
                return true;
            }

            var selfTestPath = GetOption(args, "--selftest");
            if (selfTestPath != null)
            {
                Trace("selftest -> " + selfTestPath);
                BatterySelfTest.Run(selfTestPath, GetPluginDirectories(args));
                Trace("selftest done");
                Shutdown(0);
                return true;
            }

            var diagnosePath = GetOption(args, "--diagnose");
            if (diagnosePath != null)
            {
                Trace("diagnose -> " + diagnosePath);
                HardwareDiagnostics.Run(diagnosePath, GetPluginDirectories(args));
                Trace("diagnose done");
                Shutdown(0);
                return true;
            }

            var pluginsPath = GetOption(args, "--plugins");
            if (pluginsPath != null)
            {
                Trace("plugins -> " + pluginsPath);
                BatterySelfTest.WritePluginList(pluginsPath, GetPluginDirectories(args));
                Trace("plugins done");
                Shutdown(0);
                return true;
            }

            if (HasFlag(args, "--panel"))
            {
                ShowPanelPreview(args);
                return true;
            }

            return false;
        }

        private void ShowPanelPreview(string[] args)
        {
            var dark = HasFlag(args, "--dark");
            var preview = new PanelView();
            preview.ApplyTheme(dark ? Theme.Dark : Theme.Light);
            preview.SetDevices(HasFlag(args, "--empty") ? DemoData.Empty() : DemoData.Three(), false);
            var window = new FloatingWindow(preview);
            window.ShowPanel();
            Trace("panel preview shown");

            var autoClose = GetOption(args, "--panel-seconds");
            double seconds;
            if (autoClose != null && double.TryParse(autoClose, out seconds))
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                timer.Tick += delegate
                {
                    timer.Stop();
                    window.Close();
                    Shutdown();
                };
                timer.Start();
            }
        }

        // ---------------------------------------------------------------- tray app

        private void StartTrayApplication(string[] args)
        {
            _demoMode = HasFlag(args, "--demo");

            _singleInstance = new SingleInstance();
            if (!_singleInstance.TryAcquire())
            {
                Trace("another instance is running, activating it");
                _singleInstance.NotifyExisting();
                _singleInstance.Dispose();
                Shutdown();
                return;
            }
            _singleInstance.ActivationRequested += OnActivationRequested;

            _themeWatcher = new ThemeWatcher();
            _themeWatcher.Changed += delegate { ApplyTheme(); };
            _themeWatcher.Start();

            _panel = new PanelView();
            _panel.SetDevices(new PeripheralDevice[0], true);
            _panel.RefreshRequested += OnManualRefreshRequested;
            _window = new FloatingWindow(_panel);
            ApplyTheme();

            _startupService = new StartupService(RunKeyPath, RunValueName);
            EnsureAutostartForInstalledCopy();

            _tray = new TrayController();
            _tray.Clicked += delegate { TogglePanel(); };
            _tray.ExitRequested += delegate { Shutdown(); };
            _tray.AutostartToggleRequested += delegate { ToggleAutostart(); };
            _tray.Show(TrayAssets.LoadTrayIcon(_themeWatcher.TaskbarIsLight), _startupService.IsEnabled);
            ApplyTheme();
            StartTaskbarMessageHook();
            ScheduleTrayPromotion();

            ScheduleAutoClose(args);

            if (_demoMode)
            {
                _window.UpdateDevices(DemoData.Three(), false);
                Trace("demo mode, using sample devices");
                return;
            }

            _pluginHost = PluginHost.Load(GetPluginDirectories(args), Trace);
            Trace("battery plugins loaded: " + _pluginHost.Plugins.Count +
                  ", issues: " + _pluginHost.Issues.Count);

            _battery = new BatteryService(_pluginHost.Plugins, Trace);
            _battery.Changed += delegate { Dispatcher.BeginInvoke(new Action(UpdateBatterySnapshot)); };
            _battery.Start();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _refreshTimer.Tick += delegate { RefreshBattery(false); };
            _refreshTimer.Start();

            RefreshBattery(true);
        }

        /// <summary>
        /// Windows 11 adds new notification-area icons to the overflow ("^") by default. Promote
        /// our own entry to the primary taskbar. Explorer writes the NotifyIconSettings entry
        /// only after the icon is added (and may do so a little later), so retry for a while; the
        /// registry change itself is applied live by Explorer.
        /// </summary>
        private void ScheduleTrayPromotion()
        {
            if (_promotionConfirmed) return;

            if (_promoteTimer == null && TrayPromotion.EnsurePromoted())
            {
                _promotionConfirmed = true;
                Trace("tray icon promoted to the primary taskbar");
                return;
            }

            if (_promoteTimer != null) return;
            _promoteAttempts = 0;
            _promoteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _promoteTimer.Tick += delegate
            {
                _promoteAttempts++;

                // An in-place upgrade can leave Explorer without a settings entry for the
                // new executable. Re-registering the icon makes Explorer create one, so the
                // normal promotion path below can set IsPromoted on the next tick.
                if (_promoteAttempts == 3 || _promoteAttempts == 12)
                {
                    if (_tray != null)
                    {
                        _tray.ReRegister();
                        Trace("tray icon re-registered to let Explorer create its settings entry");
                    }
                }

                // If Explorer never created a settings entry for this executable (observed after
                // some in-place upgrades), no amount of registry retries can promote the icon.
                // Restart Explorer once; its TaskbarCreated broadcast and fresh tray registration
                // make it create the entry, then the normal retry below promotes it.
                if (_promoteAttempts == 15 && !_explorerRestartAttempted &&
                    !TrayPromotion.HasCurrentEntry())
                {
                    _explorerRestartAttempted = true;
                    RestartExplorerForTray();
                }

                if (TrayPromotion.EnsurePromoted())
                {
                    _promotionConfirmed = true;
                    Trace("tray icon promoted to the primary taskbar (retry " + _promoteAttempts + ")");
                    _promoteTimer.Stop();
                    _promoteTimer = null;
                    return;
                }

                if (_promoteAttempts < 60) return;

                _promoteTimer.Stop();
                _promoteTimer = null;
                Trace("tray promotion not confirmed yet (will retry after Explorer restarts)");
            };
            _promoteTimer.Start();
        }

        private void RestartExplorerForTray()
        {
            Trace("tray settings entry is missing; restarting Explorer once to recreate it");
            try
            {
                foreach (var process in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        if (!process.HasExited) process.Kill();
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                Thread.Sleep(800);
                if (Process.GetProcessesByName("explorer").Length == 0)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Trace("Explorer restart for tray promotion failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Listen for Explorer's "TaskbarCreated" broadcast: after an Explorer restart the
        /// notification-area state is rebuilt, so promote the icon again when it comes back.
        /// </summary>
        private void StartTaskbarMessageHook()
        {
            try
            {
                var parameters = new HwndSourceParameters("Watt.TaskbarHook")
                {
                    Width = 0,
                    Height = 0,
                    WindowStyle = 0
                };
                _taskbarMessageWindow = new HwndSource(parameters);
                _taskbarMessageWindow.AddHook(TaskbarWindowProc);
                _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            }
            catch (Exception ex)
            {
                Trace("taskbar hook failed " + ex.Message);
            }
        }

        private IntPtr TaskbarWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
            {
                Trace("TaskbarCreated received, re-promoting tray icon");
                _promotionConfirmed = false;
                ScheduleTrayPromotion();
            }
            return IntPtr.Zero;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        private void ScheduleAutoClose(string[] args)
        {
            var closeAfter = GetOption(args, "--close-after");
            double seconds;
            if (closeAfter == null || !double.TryParse(closeAfter, out seconds)) return;
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _autoCloseTimer.Tick += delegate
            {
                _autoCloseTimer.Stop();
                Shutdown();
            };
            _autoCloseTimer.Start();
        }

        private void OnActivationRequested(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                Trace("activation requested, showing panel");
                if (_window == null) return;
                RefreshBattery(false);
                if (!_window.IsPanelVisible) _window.ShowPanel();
            }));
        }

        private void TogglePanel()
        {
            if (_window == null) return;

            if (!_window.IsPanelVisible)
            {
                if ((DateTime.UtcNow - _lastRefreshUtc).TotalSeconds > 10)
                {
                    RefreshBattery(false);
                }
            }
            _window.ToggleFromTray();
        }

        private void RefreshBattery(bool force)
        {
            if (_battery == null || _refreshing) return;
            _refreshing = true;
            _lastRefreshUtc = DateTime.UtcNow;
            try
            {
                var task = _battery.RefreshAsync(force);
                if (task == null)
                {
                    _refreshing = false;
                    return;
                }

                task.ContinueWith(
                    delegate { _refreshing = false; },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _refreshing = false;
                Trace("refresh failed " + ex);
            }
        }

        /// <summary>
        /// Manual refresh from the panel button. It always waits for the scan to finish before
        /// showing "已刷新" so the feedback reflects the new device snapshot, not just the click.
        /// </summary>
        private async void OnManualRefreshRequested(object sender, EventArgs e)
        {
            if (_panel == null || _battery == null) return;

            _panel.SetRefreshing(true);
            try
            {
                var task = _battery.RefreshAsync(true);
                if (task != null) await task.ConfigureAwait(true);

                UpdateBatterySnapshot();
                _panel.ShowRefreshResult(true);
                Trace("manual refresh completed");
            }
            catch (Exception ex)
            {
                _panel.ShowRefreshResult(false);
                Trace("manual refresh failed " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void UpdateBatterySnapshot()
        {
            if (_window == null || _battery == null) return;
            _window.UpdateDevices(_battery.Devices, _battery.IsScanning);
        }

        private void ApplyTheme()
        {
            if (_themeWatcher == null) return;
            var theme = _themeWatcher.AppsAreLight ? Theme.Light : Theme.Dark;
            if (_window != null) _window.ApplyTheme(theme);
            if (_tray != null) _tray.SetTheme(_themeWatcher.TaskbarIsLight);
        }

        private void ToggleAutostart()
        {
            if (_startupService == null || _tray == null) return;
            try
            {
                if (_startupService.IsEnabled) _startupService.Disable();
                else _startupService.Enable();
            }
            catch (Exception ex)
            {
                Trace("autostart toggle failed " + ex.Message);
            }
            _tray.SetAutostart(_startupService.IsEnabled);
        }

        private void EnsureAutostartForInstalledCopy()
        {
            try
            {
                // Only repair a registration that already exists (e.g. the install folder moved).
                // Never recreate it when the user unchecked "开机自启" from the tray menu.
                var exePath = ExecutablePath;
                if (!IsInstalledCopy(exePath)) return;
                if (!_startupService.IsEnabled) return;

                var expected = (_startupService.ExpectedCommand ?? string.Empty).Trim();
                var registered = (_startupService.RegisteredCommand ?? string.Empty).Trim();
                if (string.Equals(registered, expected, StringComparison.OrdinalIgnoreCase)) return;

                Trace("repairing autostart registration");
                _startupService.Enable(exePath);
            }
            catch (Exception ex)
            {
                Trace("EnsureAutostart failed " + ex.Message);
            }
        }

        private static bool IsInstalledCopy(string exePath)
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var expectedDir = Path.Combine(local, "Programs", "Watt");
                return exePath.StartsWith(expectedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string RunKeyPath
        {
            get { return @"Software\Microsoft\Windows\CurrentVersion\Run"; }
        }

        private static string ExecutablePath
        {
            get
            {
                try
                {
                    var module = Assembly.GetEntryAssembly();
                    if (module != null && !string.IsNullOrEmpty(module.Location)) return module.Location;
                }
                catch
                {
                }
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Watt.exe");
            }
        }

        // ---------------------------------------------------------------- lifetime

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_autoCloseTimer != null)
                {
                    _autoCloseTimer.Stop();
                    _autoCloseTimer = null;
                }
                if (_promoteTimer != null)
                {
                    _promoteTimer.Stop();
                    _promoteTimer = null;
                }
                if (_taskbarMessageWindow != null)
                {
                    try
                    {
                        _taskbarMessageWindow.RemoveHook(TaskbarWindowProc);
                        _taskbarMessageWindow.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }
                    _taskbarMessageWindow = null;
                }
                if (_refreshTimer != null)
                {
                    _refreshTimer.Stop();
                    _refreshTimer = null;
                }
                if (_battery != null)
                {
                    _battery.Dispose();
                    _battery = null;
                }
                if (_tray != null)
                {
                    _tray.Dispose();
                    _tray = null;
                }
                if (_themeWatcher != null)
                {
                    _themeWatcher.Dispose();
                    _themeWatcher = null;
                }
                if (_singleInstance != null)
                {
                    _singleInstance.Dispose();
                    _singleInstance = null;
                }
            }
            catch (Exception ex)
            {
                Trace("exit cleanup failed " + ex);
            }
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Trace("dispatcher exception " + e.Exception);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Trace("domain exception " + e.ExceptionObject);
        }

        // ---------------------------------------------------------------- helpers

        internal static void Trace(string message)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
                // diagnostics must never take the app down
            }
        }

        private static string GetOption(string[] args, string name)
        {
            if (args == null) return null;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        /// <summary>Extra plugin folders for development/testing: --plugin-dir a;b (repeatable).</summary>
        private static List<string> GetPluginDirectories(string[] args)
        {
            var result = new List<string>();
            if (args == null) return result;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "--plugin-dir", StringComparison.OrdinalIgnoreCase)) continue;
                var value = args[i + 1];
                if (string.IsNullOrWhiteSpace(value)) continue;
                foreach (var part in value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = part.Trim();
                    if (trimmed.Length > 0 && !result.Contains(trimmed)) result.Add(trimmed);
                }
            }
            return result;
        }

        private static bool HasFlag(string[] args, string name)
        {
            if (args == null) return false;
            foreach (var arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
