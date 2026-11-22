using System;
using System.Diagnostics;
using System.Reflection;

namespace Watt.Services
{
    /// <summary>
    /// Manages the per-user autostart registration
    /// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run, value "Watt").
    /// No administrator rights are required: the key always lives under HKCU.
    ///
    /// Registry access is never allowed to throw into the app. A missing or unreadable
    /// key simply means "autostart is off"; a failed write is logged and reported as
    /// <c>false</c> from <see cref="EnsureEnabled"/>.
    ///
    /// The registry key path, value name, current-executable provider and the underlying
    /// store are all injectable for tests. In production the defaults are used:
    /// HKCU Run value "Watt" = quoted path of the currently running Watt.exe.
    /// </summary>
    public sealed class StartupService
    {
        /// <summary>Default per-user Run key.</summary>
        public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>Default value name written under the Run key.</summary>
        public const string DefaultValueName = "Watt";

        private readonly string _runKeyPath;
        private readonly string _valueName;
        private readonly Func<string> _exePathProvider;
        private readonly IRunKeyStore _store;

        public StartupService()
            : this(DefaultRunKeyPath, DefaultValueName, null, null)
        {
        }

        /// <summary>Creates a service for a custom Run key / value (used by tests).</summary>
        public StartupService(string runKeyPath, string valueName)
            : this(runKeyPath, valueName, null, null)
        {
        }

        /// <summary>
        /// Creates a service for a custom Run key / value and a custom executable path
        /// provider. When <paramref name="exePathProvider"/> is null the path of the
        /// currently running process is used.
        /// </summary>
        public StartupService(string runKeyPath, string valueName, Func<string> exePathProvider)
            : this(runKeyPath, valueName, exePathProvider, null)
        {
        }

        /// <summary>
        /// Full injection point used by tests: custom Run key / value, executable path
        /// provider and registry store. Null values fall back to the production defaults.
        /// </summary>
        public StartupService(string runKeyPath, string valueName, Func<string> exePathProvider, IRunKeyStore store)
        {
            _runKeyPath = string.IsNullOrEmpty(runKeyPath) ? DefaultRunKeyPath : runKeyPath;
            _valueName = string.IsNullOrEmpty(valueName) ? DefaultValueName : valueName;
            _exePathProvider = exePathProvider;
            _store = store ?? new RegistryRunKeyStore();
        }

        /// <summary>Absolute path of the executable that Enable() registers.</summary>
        public string ExePath
        {
            get
            {
                try
                {
                    if (_exePathProvider != null)
                    {
                        var injected = _exePathProvider();
                        if (!string.IsNullOrEmpty(injected)) return injected;
                    }

                    var processPath = Process.GetCurrentProcess().MainModule;
                    if (processPath != null && !string.IsNullOrEmpty(processPath.FileName))
                    {
                        return processPath.FileName;
                    }

                    var assemblyPath = Assembly.GetEntryAssembly();
                    if (assemblyPath != null && !string.IsNullOrEmpty(assemblyPath.Location))
                    {
                        return assemblyPath.Location;
                    }
                }
                catch (Exception ex)
                {
                    ServiceLog.Error("StartupService.ExePath", ex);
                }

                return null;
            }
        }

        /// <summary>Command line that Enable() would register (exe path, quoted).</summary>
        public string ExpectedCommand
        {
            get { return Quote(ExePath); }
        }

        /// <summary>Raw value read from the Run key, or null when no entry exists.</summary>
        public string RegisteredCommand
        {
            get
            {
                try
                {
                    var value = _store.ReadValue(_runKeyPath, _valueName);
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
                catch (Exception ex)
                {
                    ServiceLog.Error("StartupService.RegisteredCommand read", ex);
                    return null;
                }
            }
        }

        /// <summary>True when any command is registered for this app's Run value.</summary>
        public bool IsEnabled
        {
            get { return RegisteredCommand != null; }
        }

        /// <summary>Registers the currently running executable for autostart.</summary>
        public void Enable()
        {
            var exePath = ExePath;
            if (string.IsNullOrEmpty(exePath))
            {
                ServiceLog.Write("StartupService.Enable: cannot determine the current executable path");
                return;
            }

            WriteRegistration(exePath);
        }

        /// <summary>Registers <paramref name="exePath"/> for autostart.</summary>
        public void Enable(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
            {
                ServiceLog.Write("StartupService.Enable: empty executable path");
                return;
            }

            WriteRegistration(exePath);
        }

        /// <summary>Removes the autostart registration. Safe when nothing is registered.</summary>
        public void Disable()
        {
            try
            {
                _store.DeleteValue(_runKeyPath, _valueName);
            }
            catch (Exception ex)
            {
                ServiceLog.Error("StartupService.Disable", ex);
            }
        }

        /// <summary>
        /// Ensures the Run value points at <paramref name="expectedPath"/>.
        /// Writes it when it is missing or stale. Returns true only when the registry
        /// was actually changed, false when it already pointed at the expected path or
        /// when the write failed.
        /// </summary>
        public bool EnsureEnabled(string expectedPath)
        {
            if (string.IsNullOrWhiteSpace(expectedPath))
            {
                ServiceLog.Write("StartupService.EnsureEnabled: empty expected path");
                return false;
            }

            var wanted = Quote(expectedPath);
            var current = RegisteredCommand;
            if (string.Equals(NormalizeCommand(current), NormalizeCommand(wanted), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return WriteRegistration(expectedPath);
        }

        /// <summary>Quotes an executable path the way the Run key expects it.</summary>
        public static string Quote(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            var trimmed = exePath.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }

            return "\"" + trimmed + "\"";
        }

        private bool WriteRegistration(string exePath)
        {
            try
            {
                var command = Quote(exePath);
                if (!_store.WriteValue(_runKeyPath, _valueName, command))
                {
                    ServiceLog.Write("StartupService: store refused to write " + _valueName + " under " + _runKeyPath);
                    return false;
                }

                ServiceLog.Write("autostart registered: " + command);
                return true;
            }
            catch (Exception ex)
            {
                ServiceLog.Error("StartupService write", ex);
                return false;
            }
        }

        private static string NormalizeCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return string.Empty;
            var trimmed = command.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }

            return trimmed;
        }
    }
}
