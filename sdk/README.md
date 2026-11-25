# Watt 插件开发指南

Watt 的电量来源全部是插件：宿主加载 DLL 后并行调用，统一做超时、缓存、合并和界面显示。
给 Watt 增加一个新外设支持，只需要写一个 **.NET Framework 4.8** 类库 DLL，放进
`plugins` 目录并重启 Watt。

本目录是插件开发的**文档与模板**；示例工程在 [`sample/Watt.Plugin.Sample/`](sample/Watt.Plugin.Sample/)，契约源码在
[`src/Watt.Plugin.Contracts/`](../src/Watt.Plugin.Contracts/)，复用库在
[`src/Watt.Plugin.Support/`](../src/Watt.Plugin.Support/)。

## 目录

| 文件 | 用途 |
| --- | --- |
| [README.md](README.md) | 本文件：环境、构建、部署、调试 |
| [AI-PROMPT.md](AI-PROMPT.md) | 把文档和采集结果交给 AI 生成插件的提示词模板 |
| [capture-template.md](capture-template.md) | 新设备协议采集模板（VID/PID、报文、字段偏移） |
| [ACCEPTANCE.md](ACCEPTANCE.md) | 插件验收清单 |
| [`docs/plugins.md`](../docs/plugins.md) | 完整接口/生命周期/缓存语义文档（先读这个） |
| [`sample/Watt.Plugin.Sample/`](sample/Watt.Plugin.Sample/) | 可直接构建的示例插件 |

## 环境要求

- Windows 10/11 x64；
- .NET SDK 9（或能编译 `net48` 的较新 SDK）与 .NET Framework 4.8 targeting pack；
- 需要 WinRT 蓝牙 API 时，还要 Windows SDK 的 `Windows.winmd`
  （默认路径 `C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.26100.0\Windows.winmd`，
  可用 `-p:WinmdPath=...` 覆盖）；只用 HID 的 2.4G 插件通常不需要；
- 插件以进程内完全信任代码运行，只安装可信来源的 DLL。

## 5 分钟验证示例

在仓库根目录执行：

```cmd
:: 1) 构建示例（工程引用 src/Watt.Plugin.Contracts 源码，不需要预编译 SDK）
dotnet build .\sdk\sample\Watt.Plugin.Sample\Watt.Plugin.Sample.csproj -c Release
:: 产物：sdk\sample\Watt.Plugin.Sample\bin\Release\net48\Watt.Plugin.Sample.dll

:: 2) 完全退出 Watt（右键托盘 → 退出，或 taskkill /IM Watt.exe /F）
:: 3) 放到用户插件目录（升级 Watt 不会被覆盖）
set "PLUGINS=%LOCALAPPDATA%\Watt\plugins"
if not exist "%PLUGINS%" mkdir "%PLUGINS%"
copy /Y ".\sdk\sample\Watt.Plugin.Sample\bin\Release\net48\Watt.Plugin.Sample.dll" "%PLUGINS%"

:: 4) 用已安装的 Watt 验证
"%LOCALAPPDATA%\Programs\Watt\Watt.exe" --plugins "%TEMP%\plugins.txt"
"%LOCALAPPDATA%\Programs\Watt\Watt.exe" --selftest "%TEMP%\selftest.json"
type "%TEMP%\plugins.txt"
type "%TEMP%\selftest.json"
```

预期：`--plugins` 出现 `sample-demo`，`--selftest` 多出 `Sample Keyboard 42%`。
示例是假数据，验证后从 `plugins` 目录删除对应 DLL 并重启 Watt。

> 如果从源码运行而不是安装版：先运行 `.\build.cmd`，再用
> `.\dist\Watt-2.2.6-win-x64\app\Watt.exe --selftest ...` 验证；`--plugin-dir` 可直接指向示例插件输出目录。

## 适配新设备的流程

1. 运行 `Watt.exe --diagnose report.txt`，确认这是需要新插件的外设
   （标准蓝牙设备通常已由内置 `windows-aep` / `ble-gatt` 覆盖）；
2. 按 [capture-template.md](capture-template.md) 采集设备信息和原始报文；
3. 把 [docs/plugins.md](../docs/plugins.md)、[AI-PROMPT.md](AI-PROMPT.md)、
   [`sample/`](sample/Watt.Plugin.Sample/) 和采集结果交给 AI，或自己按示例实现；
4. 复制示例工程到仓库外（或新建类库），修改 `AssemblyName`，引用
   `Watt.Plugin.Contracts`（需要 HID/WinRT 时再引用 `Watt.Plugin.Support`），
   实现 `IBatteryPlugin` / `BatteryPluginBase`；
5. `dotnet build <你的工程>.csproj -c Release`，把生成的 DLL 放到
   `%LOCALAPPDATA%\Watt\plugins` 或 `<Watt 安装目录>\plugins`；
6. 重启 Watt，按 [ACCEPTANCE.md](ACCEPTANCE.md) 验收；用 `--plugins` / `--selftest` /
   `--diagnose` 排查。

## 引用哪个程序集

在仓库内开发示例/插件时，直接引工程：

```xml
<ProjectReference Include="..\..\..\src\Watt.Plugin.Contracts\Watt.Plugin.Contracts.csproj" />
<ProjectReference Include="..\..\..\src\Watt.Plugin.Support\Watt.Plugin.Support.csproj" />
```

在仓库外开发时，引用安装目录里的 DLL：

```xml
<Reference Include="Watt.Plugin.Contracts">
  <HintPath>C:\Users\<you>\AppData\Local\Programs\Watt\Watt.Plugin.Contracts.dll</HintPath>
  <Private>false</Private>
</Reference>
<Reference Include="Watt.Plugin.Support">
  <HintPath>C:\Users\<you>\AppData\Local\Programs\Watt\Watt.Plugin.Support.dll</HintPath>
  <Private>false</Private>
</Reference>
```

插件输出目录里**不要**复制 Contracts/Support DLL：运行时使用 Watt 自己那份，避免版本冲突。
Watt 升级导致契约版本变化时，重新用新版安装目录的 DLL 编译插件。

## 关键规则（详见 docs/plugins.md）

- `BatteryPercent = null`：观察到设备但当前没有新读数（休眠）→ 宿主用最后一次缓存值保留这一行；
- `PluginReadResult.Absent()`：传输不存在（接收器拔出/无蓝牙）→ 宿主连续 3 次后清理该插件缓存；
- `BatteryPercent = null` 时若设备仍有连接，建议同时设置 `PeripheralDevice.IsConnected = true`；
  设备明确不响应/离线时设为 `false`。手动刷新只会清理明确 `false` 的缓存行，旧插件的 `null`
  会保留以兼容。
- 插件**不要**自己持久化/删除缓存，宿主统一处理；
- 不要抛异常；一定在 `SuggestedTimeoutMs` 内返回；超时只会取消等待，不会强杀线程；
- 只读优先；必须发查询帧时只发文档化的只读帧，并限制频率，避免与厂商软件冲突；
- 不引用 NuGet 外部包；
- 插件是进程内代码，没有沙箱。

## 调试

- `--plugins <txt>`：搜索目录、已加载插件、元数据、加载错误；
- `--selftest <json>`：一次真实扫描的结果（设备 + 插件 + 加载错误）；
- `--diagnose <txt>`：蓝牙 AEP/HFP、HID 集合、原始报文、各插件读取结果；
- `--plugin-dir <dir>`：临时额外插件目录，适合开发时不动安装目录；
- 日志：`<Watt 安装目录>\watt-startup.log`，插件可用 `context.Logger` 写日志；
- 当前没有热加载：替换 DLL 前必须先退出 Watt，再重新启动。

## 契约版本

当前 Watt 2.2.6，插件契约 1.0。`Watt.Plugin.Contracts` 的公开 API 变化会提升契约版本；
插件应使用与目标 Watt 版本匹配的 Contracts/Support DLL 编译。
