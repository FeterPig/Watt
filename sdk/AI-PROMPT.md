# 给 AI 的插件生成提示词模板

把下面内容复制给 AI，替换 `<>` 部分，并附上：`docs/plugins.md`、`sdk/sample/Watt.Plugin.Sample/`、`sdk/capture-template.md`
填写后的采集文档、`--diagnose` 报告。

---

你是 Watt 的插件开发者。Watt 是一个 .NET Framework 4.8 的 Windows 托盘程序，电量来源由
独立插件 DLL 提供，宿主负责并行调度、超时、last-known 缓存、按地址/名称合并和界面显示。

请先阅读随附的 `docs/plugins.md` 和 `sdk/sample/Watt.Plugin.Sample/SamplePlugin.cs` /
`sdk/sample/Watt.Plugin.Sample/Watt.Plugin.Sample.csproj`，然后为下面这台设备实现一个新插件：

- 设备：<品牌 型号>
- 连接方式：<蓝牙经典 / BLE / 2.4G 接收器>
- VID:PID / usagePage / usage / 报文长度：<...>
- 已知协议/报文：<查询帧、响应帧、字段偏移、充电位；或说明“仅被动上报”>
- 采集文档：<粘贴 capture template 内容或附件>
- 期望插件 Id：<vendor-device-2g4>
- 期望 Priority：<60>
- 期望 DeviceKind：<Keyboard/Mouse/Headphone/Other>
- 是否需要写设备：<否 / 只发只读查询帧>
- 显示名称来源：<HID product string / 固定型号名>

硬性要求：

1. 目标框架 `net48`，类库，AnyCPU；只引用 `Watt.Plugin.Contracts`（源码工程或安装目录的
   `Watt.Plugin.Contracts.dll`），需要 HID/WinRT 时再引用 `Watt.Plugin.Support`；
   不要引用 NuGet 包；
2. 实现 `IBatteryPlugin`（继承 `BatteryPluginBase`），`ReadAsync` 中绝不抛异常，
   必须在 `SuggestedTimeoutMs` 内返回；
3. 设备在线且有新读数时返回带 `BatteryPercent` 的 `PeripheralDevice`；
   设备在线但休眠/无新读数时返回 `BatteryPercent = null`（宿主会保留上次电量）；
   接收器/适配器不存在时返回 `PluginReadResult.Absent()`；
   不要在插件里持久化缓存或管理删除逻辑；
4. Id / 设备 Id 稳定；第三方 Priority 建议 ≥60，避免覆盖内置来源；
5. 只读优先；必须写查询帧时只发文档化的只读帧并限制频率，在代码注释中说明；
6. 使用 `context.Logger` 记录关键异常（不要打日志洪水）；
7. 生成完整的 `.csproj` 和 `.cs`，并给出 `dotnet build -c Release` 命令；
8. 如果协议事实不足以判断电量字节，请不要编造：先列出还需要采集哪些帧/操作。

交付后我会按 `sdk/ACCEPTANCE.md` 在真机上验收：`--plugins` 无加载错误、
`--selftest` 出现设备、休眠保留、拔接收器 3 次扫描后移除、无异常日志。
