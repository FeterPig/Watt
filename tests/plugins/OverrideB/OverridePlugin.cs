using System.Threading;
using System.Threading.Tasks;
using Watt.Models;
using Watt.Plugins;

namespace Watt.TestPlugins
{
    /// <summary>Test-only plugin with the same Id as <c>OverridePluginA</c> but a different value.
    /// Loading B after A must replace A.</summary>
    public sealed class OverridePluginB : BatteryPluginBase
    {
        public override string Id
        {
            get { return "override-test"; }
        }

        public override string DisplayName
        {
            get { return "Override test B"; }
        }

        public override int Priority
        {
            get { return 70; }
        }

        public override int SuggestedTimeoutMs
        {
            get { return 1000; }
        }

        public override Task<PluginReadResult> ReadAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(PluginReadResult.Observed(
                Device("override:device", "Override Device", DeviceKind.Keyboard, 22, false, null, true)));
        }
    }
}
