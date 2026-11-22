using System.Threading;
using System.Threading.Tasks;
using Watt.Models;
using Watt.Plugins;

namespace Watt.Samples
{
    /// <summary>
    /// 最小可用的 Watt 电池插件：每次扫描都返回一台虚拟的 "Sample Keyboard"（42%）。
    ///
    /// 这个示例演示的是插件契约本身，不是真实设备的读取逻辑：
    ///   - 只继承 BatteryPluginBase，覆写 Id 与 ReadAsync；
    ///   - Id 必须稳定且全局唯一，宿主会把它写成设备行的 SourceId；
    ///   - Priority = 999 表示最低优先级（合并时数字越小越强）；
    ///   - ReadAsync 永不抛异常，用 PluginReadResult.Observed 返回观测结果；
    ///   - 不碰缓存 / UI / 文件，这些都由宿主负责。
    ///
    /// 安装方式：把 bin\Release\net48\Watt.Plugin.Sample.dll 复制到
    ///   %LOCALAPPDATA%\Watt\plugins\
    /// 然后重启 Watt.exe（或用 --selftest 验证）。
    /// </summary>
    public sealed class SamplePlugin : BatteryPluginBase
    {
        /// <summary>稳定的小写 kebab-case 标识；发布后不要再改，否则缓存/覆盖关系都会失效。</summary>
        public override string Id
        {
            get { return "sample-demo"; }
        }

        public override string DisplayName
        {
            get { return "Sample Demo Plugin"; }
        }

        public override string Description
        {
            get { return "Demo plugin: reports one fake keyboard at 42% for API testing."; }
        }

        public override string Version
        {
            get { return "1.0.0"; }
        }

        /// <summary>数值越小越强；真实插件建议 100 以上，内置插件是 10~50。</summary>
        public override int Priority
        {
            get { return 999; }
        }

        /// <summary>一次扫描的软预算，宿主会把它限制在 500~15000 ms。</summary>
        public override int SuggestedTimeoutMs
        {
            get { return 3000; }
        }

        /// <summary>本插件不向设备发任何报文（纯演示）。</summary>
        public override bool WritesToDevice
        {
            get { return false; }
        }

        public override Task<PluginReadResult> ReadAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            // 真实插件通常在这里：探测传输层是否存在 -> 枚举设备 -> 解析电量。
            // 传输层不在（接收器拔掉/没有蓝牙）时返回 PluginReadResult.Absent()；
            // 传输层在但没有要报告的设备时返回 PluginReadResult.Empty()。
            //
            // 这里每次都创建新的 PeripheralDevice 实例，避免宿主填充 SourceId 等字段时
            // 复用上一次扫描的对象（示例代码保持无状态，也便于理解）。
            var fakeKeyboard = Device(
                id: "sample:keyboard-1",
                name: "Sample Keyboard",
                kind: DeviceKind.Keyboard,
                percent: 42);

            return Task.FromResult(PluginReadResult.Observed(fakeKeyboard));
        }
    }
}
