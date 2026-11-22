using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Watt.Plugins;

namespace Watt.Plugins
{
    /// <summary>One successfully instantiated plugin plus its per-plugin context.</summary>
    internal sealed class LoadedPlugin
    {
        internal IBatteryPlugin Plugin;
        internal IPluginContext Context;
        internal string AssemblyPath;
    }

    internal sealed class PluginLoadIssue
    {
        internal string Path;
        internal string Message;
    }

    /// <summary>Diagnostics view of a loaded plugin.</summary>
    internal sealed class PluginInfo
    {
        internal string Id;
        internal string DisplayName;
        internal string Version;
        internal string Description;
        internal int Priority;
        internal int SuggestedTimeoutMs;
        internal bool WritesToDevice;
        internal string AssemblyPath;
    }

    /// <summary>
    /// Discovers and instantiates battery plugins from DLLs:
    ///   &lt;app&gt;\plugins, %LOCALAPPDATA%\Watt\plugins, plus extra --plugin-dir folders.
    /// User-level folders are loaded later so they can override a built-in plugin by Id.
    /// A broken DLL only produces a load issue; it never stops the tray app.
    /// </summary>
    internal sealed class PluginHost
    {
        private readonly List<LoadedPlugin> _plugins = new List<LoadedPlugin>();
        private readonly List<PluginLoadIssue> _issues = new List<PluginLoadIssue>();
        private readonly List<string> _searchDirectories = new List<string>();

        internal IReadOnlyList<LoadedPlugin> Plugins { get { return _plugins; } }
        internal IReadOnlyList<PluginLoadIssue> Issues { get { return _issues; } }
        internal IReadOnlyList<string> SearchDirectories { get { return _searchDirectories; } }

        internal static PluginHost Load(IEnumerable<string> extraDirectories, Action<string> log, string dataRoot = null)
        {
            var host = new PluginHost();
            var directories = host.BuildSearchDirectories(extraDirectories);
            var loadedById = new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories)
            {
                bool exists;
                try
                {
                    exists = Directory.Exists(directory);
                }
                catch
                {
                    exists = false;
                }
                if (!exists) continue;

                string[] files;
                try
                {
                    files = Directory.GetFiles(directory, "*.dll");
                }
                catch (Exception ex)
                {
                    host._issues.Add(new PluginLoadIssue { Path = directory, Message = "cannot list folder: " + ex.Message });
                    continue;
                }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    try
                    {
                        LoadAssembly(file, dataRoot, host, loadedById, log);
                    }
                    catch (Exception ex)
                    {
                        host._issues.Add(new PluginLoadIssue { Path = file, Message = ex.GetType().Name + ": " + ex.Message });
                        SafeLog(log, "plugin load failed: " + file + " -> " + ex.Message);
                    }
                }
            }

            host._plugins.AddRange(loadedById.Values);
            host._plugins.Sort(delegate(LoadedPlugin left, LoadedPlugin right)
            {
                var byPriority = left.Plugin.Priority.CompareTo(right.Plugin.Priority);
                if (byPriority != 0) return byPriority;
                return string.Compare(left.Plugin.Id, right.Plugin.Id, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var plugin in host._plugins)
            {
                SafeLog(log, $"plugin loaded: {plugin.Plugin.Id} v{plugin.Plugin.Version} " +
                             $"(priority {plugin.Plugin.Priority}, timeout {plugin.Plugin.SuggestedTimeoutMs}ms, " +
                             $"writes={plugin.Plugin.WritesToDevice}) <- {plugin.AssemblyPath}");
            }
            foreach (var issue in host._issues)
            {
                SafeLog(log, "plugin issue: " + issue.Path + " -> " + issue.Message);
            }

            return host;
        }

        private static void LoadAssembly(string file, string dataRoot, PluginHost host,
            Dictionary<string, LoadedPlugin> loadedById, Action<string> log)
        {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "Watt.Plugin.Contracts.dll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "Watt.Plugin.Support.dll", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(file);
            }
            catch (Exception loadFromException)
            {
                // Some environments (including restricted automation folders) refuse LoadFrom
                // for files outside the app base; byte loading has no location and still resolves
                // the already-loaded Contracts/Support assemblies from the app base.
                try
                {
                    assembly = Assembly.Load(File.ReadAllBytes(file));
                }
                catch (Exception byteException)
                {
                    host._issues.Add(new PluginLoadIssue
                    {
                        Path = file,
                        Message = "load: " + loadFromException.Message + " / " + byteException.Message
                    });
                    return;
                }
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                var loaded = new List<Type>();
                if (ex.Types != null)
                {
                    foreach (var type in ex.Types)
                    {
                        if (type != null) loaded.Add(type);
                    }
                }
                types = loaded.ToArray();
            }

            foreach (var type in types)
            {
                try
                {
                    if (type == null || type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(IBatteryPlugin).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;

                    var plugin = (IBatteryPlugin)Activator.CreateInstance(type);
                    if (plugin == null || string.IsNullOrWhiteSpace(plugin.Id)) continue;

                    var context = CreateContext(plugin, dataRoot);
                    var loaded = new LoadedPlugin
                    {
                        Plugin = plugin,
                        Context = context,
                        AssemblyPath = file
                    };

                    // Later folders (user plugins) override built-ins with the same Id.
                    loadedById[plugin.Id] = loaded;
                    SafeLog(log, "plugin discovered: " + plugin.Id + " (" + type.FullName + ")");
                }
                catch (Exception ex)
                {
                    host._issues.Add(new PluginLoadIssue
                    {
                        Path = file + " :: " + type.FullName,
                        Message = ex.GetType().Name + ": " + ex.Message
                    });
                }
            }
        }

        private static IPluginContext CreateContext(IBatteryPlugin plugin, string dataRoot)
        {
            var root = string.IsNullOrEmpty(dataRoot) ? WattDataDirectory : dataRoot;
            var safeId = Sanitize(plugin.Id);
            var dataDirectory = Path.Combine(root, "plugin-data", safeId);
            try
            {
                Directory.CreateDirectory(dataDirectory);
            }
            catch
            {
                // A read-only location only affects plugins that want to persist state.
            }

            return new PluginContext(dataDirectory, new PluginLogger(plugin.Id));
        }

        private List<string> BuildSearchDirectories(IEnumerable<string> extraDirectories)
        {
            var result = new List<string>();
            AddDirectory(result, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins"));

            try
            {
                AddDirectory(result, Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Watt", "plugins"));
            }
            catch
            {
                // ignore
            }

            if (extraDirectories != null)
            {
                foreach (var directory in extraDirectories)
                {
                    if (!string.IsNullOrWhiteSpace(directory)) AddDirectory(result, directory.Trim());
                }
            }

            return result;
        }

        private void AddDirectory(List<string> result, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            foreach (var existing in result)
            {
                if (string.Equals(existing, directory, StringComparison.OrdinalIgnoreCase)) return;
            }
            result.Add(directory);
            _searchDirectories.Add(directory);
        }

        private static string WattDataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Watt");
            }
        }

        private static string Sanitize(string id)
        {
            var builder = new System.Text.StringBuilder(id.Length);
            foreach (var c in id)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '-' || c == '_')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('_');
                }
            }
            return builder.Length == 0 ? "plugin" : builder.ToString();
        }

        private static void SafeLog(Action<string> log, string message)
        {
            if (log == null) return;
            try
            {
                log(message);
            }
            catch
            {
                // logging must not break discovery
            }
        }

        private sealed class PluginContext : IPluginContext
        {
            internal PluginContext(string dataDirectory, IPluginLogger logger)
            {
                DataDirectory = dataDirectory;
                Logger = logger;
            }

            public string DataDirectory { get; private set; }
            public IPluginLogger Logger { get; private set; }
        }

        private sealed class PluginLogger : IPluginLogger
        {
            private readonly string _id;
            private readonly Action<string> _log;

            internal PluginLogger(string id)
            {
                _id = id;
                _log = null;
            }

            internal PluginLogger(string id, Action<string> log)
            {
                _id = id;
                _log = log;
            }

            public void Info(string message)
            {
                Write("INFO", message);
            }

            public void Warn(string message)
            {
                Write("WARN", message);
            }

            public void Error(string message, Exception exception)
            {
                Write("ERROR", message + (exception == null ? string.Empty : " | " + exception.GetType().Name + ": " + exception.Message));
            }

            private void Write(string level, string message)
            {
                var text = "[plugin:" + _id + "] " + level + " " + message;
                if (_log != null)
                {
                    _log(text);
                    return;
                }
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "watt-startup.log"),
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
