using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Watt.Battery;
using Watt.Models;
using Watt.Plugins;

namespace Watt.Tests
{
    internal sealed class FakePlugin : BatteryPluginBase
    {
        private readonly string _id;
        private int _calls;

        internal FakePlugin(string id, int priority = 100, int timeoutMs = 5000)
        {
            _id = id;
            Priority = priority;
            SuggestedTimeoutMs = timeoutMs;
        }

        public override string Id
        {
            get { return _id; }
        }

        public override string DisplayName
        {
            get { return _id; }
        }

        public override int Priority { get; }

        public override int SuggestedTimeoutMs { get; }

        internal Func<CancellationToken, Task<PluginReadResult>> Handler { get; set; }

        internal int Calls
        {
            get { return Volatile.Read(ref _calls); }
        }

        public override Task<PluginReadResult> ReadAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var handler = Handler;
            if (handler == null) return Task.FromResult(PluginReadResult.Empty());
            return handler(cancellationToken);
        }

        internal LoadedPlugin Loaded(string dataRoot = null)
        {
            return new LoadedPlugin
            {
                Plugin = this,
                Context = new FakeContext(dataRoot ?? TestEnv.DataRoot),
                AssemblyPath = "fake://" + _id
            };
        }
    }

    internal sealed class FakeContext : IPluginContext
    {
        internal FakeContext(string dataDirectory)
        {
            DataDirectory = dataDirectory;
            Logger = new NullLogger();
        }

        public string DataDirectory { get; private set; }
        public IPluginLogger Logger { get; private set; }

        private sealed class NullLogger : IPluginLogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message, Exception exception) { }
        }
    }

    internal static class Devices
    {
        internal static PeripheralDevice Device(string id, string name, DeviceKind kind, int? percent,
            bool charging = false, string address = null, bool? connected = null)
        {
            return new PeripheralDevice
            {
                Id = id,
                Name = name,
                Kind = kind,
                BatteryPercent = percent,
                IsCharging = charging,
                Address = address,
                IsConnected = connected
            };
        }

        internal static PluginReadResult Observed(params PeripheralDevice[] devices)
        {
            return PluginReadResult.Observed(devices);
        }

        internal static PluginReadResult Absent()
        {
            return PluginReadResult.Absent();
        }

        internal static PluginReadResult Empty()
        {
            return PluginReadResult.Empty();
        }
    }

    internal static class BatteryServiceFactory
    {
        internal static BatteryService Create(params LoadedPlugin[] plugins)
        {
            return new BatteryService(plugins);
        }

        internal static IReadOnlyList<PeripheralDevice> Scan(BatteryService service, bool manual = false)
        {
            var task = service.RefreshAsync(true, manual);
            task.GetAwaiter().GetResult();
            return service.Devices;
        }
    }
}
