using System.Threading;
using System.Threading.Tasks;
using Watt.Models;
using Watt.Plugins;

namespace Watt.TestPlugins
{
    /// <summary>Test-only plugin used to prove that a later plugin directory overrides an
    /// earlier one with the same Id. Never shipped with Watt.</summary>
    public sealed class OverridePluginA : BatteryPluginBase
    {
        public override string Id
        {
            get { return "override-test"; }
        }

        public override string DisplayName
        {
            get { return "Override test A"; }
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
                Device("override:device", "Override Device", DeviceKind.Keyboard, 11, false, null, true)));
        }
    }
}
