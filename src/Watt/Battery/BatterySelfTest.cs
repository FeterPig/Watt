using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Watt.Models;
using Watt.Plugins;

namespace Watt.Battery
{
    /// <summary>
    /// Headless diagnostics wired to <c>Watt.exe --selftest &lt;file&gt;</c> / <c>--plugins &lt;file&gt;</c>.
    /// Loads the same plugin set as the tray app, runs one scan with a hard limit and writes a
    /// small JSON document so new plugin/device support can be verified without the UI.
    /// </summary>
    internal static class BatterySelfTest
    {
        private const int HardTimeoutMs = 20000;

        internal static void Run(string outputFile, IEnumerable<string> pluginDirectories)
        {
            var devices = new List<PeripheralDevice>();
            var plugins = new List<PluginInfo>();
            var issues = new List<PluginLoadIssue>();
            string error = null;
            var started = DateTime.UtcNow;

            try
            {
                var host = PluginHost.Load(pluginDirectories, null);
                plugins.AddRange(EnumeratePluginInfo(host));
                issues.AddRange(host.Issues);

                using (var service = new BatteryService(host.Plugins))
                {
                    service.Start();
                    var refresh = service.RefreshAsync(true);
                    var finished = false;
                    try
                    {
                        finished = refresh.Wait(HardTimeoutMs);
                    }
                    catch (AggregateException exception)
                    {
                        error = Describe(exception);
                        finished = true;
                    }
                    catch (Exception exception)
                    {
                        error = Describe(exception);
                    }

                    if (!finished) error = "timeout: battery scan did not finish within " + HardTimeoutMs + " ms";

                    var snapshot = service.Devices;
                    if (snapshot != null) devices.AddRange(snapshot);
                }
            }
            catch (Exception exception)
            {
                error = Describe(exception);
            }

            var elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            WriteFileSafely(outputFile, BuildJson(devices, plugins, issues, error, elapsedMs));
        }

        internal static void WritePluginList(string outputFile, IEnumerable<string> pluginDirectories)
        {
            var started = DateTime.UtcNow;
            var builder = new StringBuilder();
            builder.AppendLine("Watt battery plugins");
            builder.AppendLine("search order (later overrides earlier by Id):");
            try
            {
                var host = PluginHost.Load(pluginDirectories, null);
                foreach (var directory in host.SearchDirectories) builder.AppendLine("  " + directory);

                builder.AppendLine();
                builder.AppendLine("loaded: " + host.Plugins.Count);
                foreach (var info in EnumeratePluginInfo(host))
                {
                    builder.AppendLine(string.Format(
                        "  {0}  v{1}  priority={2} timeout={3}ms writes={4}",
                        info.Id, info.Version, info.Priority, info.SuggestedTimeoutMs, info.WritesToDevice));
                    if (!string.IsNullOrEmpty(info.Description))
                        builder.AppendLine("      " + info.Description);
                    builder.AppendLine("      assembly: " + info.AssemblyPath);
                }

                if (host.Issues.Count > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine("load issues: " + host.Issues.Count);
                    foreach (var issue in host.Issues)
                    {
                        builder.AppendLine("  " + issue.Path);
                        builder.AppendLine("      " + issue.Message);
                    }
                }
            }
            catch (Exception exception)
            {
                builder.AppendLine("ERROR: " + Describe(exception));
            }

            builder.AppendLine();
            builder.AppendLine("elapsedMs: " + (int)(DateTime.UtcNow - started).TotalMilliseconds);
            WriteFileSafely(outputFile, builder.ToString());
        }

        private static IEnumerable<PluginInfo> EnumeratePluginInfo(PluginHost host)
        {
            var result = new List<PluginInfo>();
            foreach (var loaded in host.Plugins)
            {
                var plugin = loaded.Plugin;
                result.Add(new PluginInfo
                {
                    Id = plugin.Id,
                    DisplayName = plugin.DisplayName,
                    Version = plugin.Version,
                    Description = plugin.Description,
                    Priority = plugin.Priority,
                    SuggestedTimeoutMs = plugin.SuggestedTimeoutMs,
                    WritesToDevice = plugin.WritesToDevice,
                    AssemblyPath = loaded.AssemblyPath
                });
            }
            return result;
        }

        private static string BuildJson(List<PeripheralDevice> devices, List<PluginInfo> plugins,
            List<PluginLoadIssue> issues, string error, int elapsedMs)
        {
            var builder = new StringBuilder();
            builder.Append("{\n  \"devices\": [");
            for (var i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                if (i > 0) builder.Append(',');
                builder.Append("\n    {");
                builder.Append("\"id\": ").Append(JsonString(device.Id)).Append(", ");
                builder.Append("\"name\": ").Append(JsonString(device.Name)).Append(", ");
                builder.Append("\"kind\": ").Append(JsonString(device.Kind.ToString())).Append(", ");
                builder.Append("\"percent\": ")
                    .Append(device.BatteryPercent.HasValue
                        ? device.BatteryPercent.Value.ToString(CultureInfo.InvariantCulture)
                        : "null")
                    .Append(", ");
                builder.Append("\"charging\": ").Append(device.IsCharging ? "true" : "false").Append(", ");
                builder.Append("\"connected\": ")
                    .Append(device.IsConnected.HasValue
                        ? (device.IsConnected.Value ? "true" : "false")
                        : "null")
                    .Append(", ");
                builder.Append("\"source\": ").Append(JsonString(device.SourceId)).Append(", ");
                builder.Append("\"address\": ")
                    .Append(device.Address == null ? "null" : JsonString(device.Address));
                builder.Append('}');
            }
            builder.Append("\n  ],\n  \"plugins\": [");
            for (var i = 0; i < plugins.Count; i++)
            {
                var plugin = plugins[i];
                if (i > 0) builder.Append(',');
                builder.Append("\n    {");
                builder.Append("\"id\": ").Append(JsonString(plugin.Id)).Append(", ");
                builder.Append("\"version\": ").Append(JsonString(plugin.Version)).Append(", ");
                builder.Append("\"priority\": ").Append(plugin.Priority.ToString(CultureInfo.InvariantCulture)).Append(", ");
                builder.Append("\"timeoutMs\": ").Append(plugin.SuggestedTimeoutMs.ToString(CultureInfo.InvariantCulture)).Append(", ");
                builder.Append("\"writes\": ").Append(plugin.WritesToDevice ? "true" : "false").Append(", ");
                builder.Append("\"assembly\": ").Append(JsonString(plugin.AssemblyPath));
                builder.Append('}');
            }
            builder.Append("\n  ],\n  \"pluginIssues\": [");
            for (var i = 0; i < issues.Count; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append("\n    {");
                builder.Append("\"path\": ").Append(JsonString(issues[i].Path)).Append(", ");
                builder.Append("\"message\": ").Append(JsonString(issues[i].Message));
                builder.Append('}');
            }
            builder.Append("\n  ]");

            if (!string.IsNullOrEmpty(error))
                builder.Append(",\n  \"error\": ").Append(JsonString(error));
            builder.Append(",\n  \"elapsedMs\": ").Append(elapsedMs.ToString(CultureInfo.InvariantCulture));
            builder.Append("\n}\n");
            return builder.ToString();
        }

        private static string JsonString(string value)
        {
            if (value == null) return "null";

            var builder = new StringBuilder(value.Length + 8);
            builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }

        private static string Describe(Exception exception)
        {
            if (exception == null) return "unknown error";

            var aggregate = exception as AggregateException;
            if (aggregate != null)
            {
                var flattened = aggregate.Flatten();
                if (flattened.InnerExceptions.Count == 1) return Describe(flattened.InnerExceptions[0]);
                return flattened.ToString();
            }

            return exception.GetType().Name + ": " + exception.Message;
        }

        private static void WriteFileSafely(string outputFile, string contents)
        {
            if (string.IsNullOrWhiteSpace(outputFile)) return;

            try
            {
                var directory = Path.GetDirectoryName(outputFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(outputFile, contents, new UTF8Encoding(false));
            }
            catch
            {
                // The self test has no other reporting channel; failing to write is not fatal.
            }
        }
    }
}
