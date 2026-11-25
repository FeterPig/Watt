# Watt 插件开发指南

适用版本：Watt 2.2.6，插件契约 `Watt.Plugin.Contracts` 1.0（net48）。

Watt 的所有电量来源都是插件：内置插件与第三方插件使用同一套公共契约，从同一批目录加载，由同一个宿主并行调度。你写一个插件，就等于给 Watt 增加一种新的“读取外设电量”的方法。

## 目录

1. [架构总览](#1-架构总览)
2. [插件从哪里加载、谁覆盖谁](#2-插件从哪里加载谁覆盖谁)
3. [安全警告](#3-安全警告)
4. [快速开始](#4-快速开始)
5. [API 参考](#5-api-参考)
6. [返回值、缓存与合并](#6-返回值缓存与合并)
7. [适配新设备](#7-适配新设备)
8. [构建](#8-构建)
9. [部署](#9-部署)
10. [诊断与测试](#10-诊断与测试)
11. [ABI 与版本兼容](#11-abi-与版本兼容)
12. [内置插件](#12-内置插件)
13. [排错清单](#13-排错清单)
14. [当前契约限制](#14-当前契约限制)
15. [参考](#15-参考)

---

## 1. 架构总览

```
Watt.exe（宿主）
├─ PluginHost          发现/加载插件 DLL，创建 IPluginContext
├─ BatteryService      每 60 秒并行调用所有插件，处理超时与缓存
├─ LastKnownStore      %LOCALAPPDATA%\Watt\last-known.json
├─ UI/Tray             悬浮窗、托盘图标、主题
├─ Watt.Plugin.Contracts.dll（公共契约，插件引用）
└─ Watt.Plugin.Support.dll（可选的 HID/WinRT/分类辅助，插件引用）
```

一次扫描：

```
定时器（约每 60 秒）/ 打开悬浮窗
        │
        ▼
BatteryService.ScanAsync
        │  并行调用每个插件的 ReadAsync(context, cancellationToken)
        ├── windows-aep
        ├── ble-gatt
        └── 你的插件
        │
        ▼
宿主处理结果：
  · 有电量 → 写入 last-known 缓存
  · 电量 null → 用该设备 Id 的缓存值补全（休眠设备保持可见）
  · HardwarePresent=false → 连续 3 次后删除该插件的缓存设备
  · 超时/异常 → 保留该插件缓存行，不计为 absent
        │
        ▼
合并去重（蓝牙地址 → 名称兜底；Priority 小者优先；新读数优先于旧缓存）
        │
        ▼
按 键盘/鼠标/耳机/其他 + 名称排序，交给 UI
```

设计原则：

- **内置 2 种通用来源**：Windows AEP 属性（含经典蓝牙 HFP 节点）和 BLE GATT；
- **扩展新设备不需要改主程序**：第三方插件 DLL 放进插件目录即可；
- **故障隔离到插件级**：一个插件抛异常、超时、加载失败，不影响托盘程序和其他插件；
- **缓存、去重、UI 语义只实现一次**：插件只报告“看到了什么”，不写缓存、不碰 UI、不管设备何时该消失。

---

## 2. 插件从哪里加载、谁覆盖谁

### 2.1 搜索目录与顺序

宿主按以下顺序搜索（后面的目录可以覆盖前面的）：

| 顺序 | 目录 | 说明 |
| --- | --- | --- |
| 1 | `<Watt 安装目录>\plugins` | 内置插件，随安装包发布 |
| 2 | `%LOCALAPPDATA%\Watt\plugins` | 第三方插件；升级/重装 Watt 不会覆盖 |
| 3 | `--plugin-dir <dir>` | 开发/测试时临时追加，可重复或用 `;` 分隔 |

### 2.2 加载规则

- 枚举每个目录下的 `*.dll`；跳过 `Watt.Plugin.Contracts.dll`、`Watt.Plugin.Support.dll`；
- 用 `Assembly.LoadFrom` 加载（失败时回退为字节加载）；只实例化 public、非抽象、有参数构造函数的 `IBatteryPlugin` 类型；
- 同一插件 **Id** 后加载的覆盖先加载的；被覆盖的旧实例会被丢弃；
- DLL 加载失败、类型构造失败只记录为加载问题，不影响其他插件；
- 插件实例在进程内运行，随 Watt 退出而结束；**没有热加载/卸载**，替换 DLL 后必须重启 Watt。

### 2.3 插件数据目录

宿主为每个插件创建：

```
%LOCALAPPDATA%\Watt\plugin-data\<插件 Id>\
```

通过 `IPluginContext.DataDirectory` 获取。可以写自己的状态，但**不要**把 last-known 缓存写到这里——那由宿主统一管理。

---

## 3. 安全警告

插件与 Watt 同进程、同权限运行，**没有沙箱**。插件可以访问当前用户能访问的任何文件、注册表和设备。

- 只安装可信来源的 DLL；
- 不要把从网上随便下载的插件放进 `plugins`；
- 插件 DLL 与 `Watt.Plugin.Contracts` 版本应匹配；
- 插件不需要联网时，不要在插件里发起网络请求。

---

## 4. 快速开始

仓库里有一个可构建的示例插件：[`sdk/sample/Watt.Plugin.Sample/`](../sdk/sample/Watt.Plugin.Sample/)。它永远返回一台虚拟的 `Sample Keyboard`（42%），用来验证“插件加载 → 扫描 → JSON 输出”整条链路。

```cmd
:: 在仓库根目录
dotnet build .\sdk\sample\Watt.Plugin.Sample\Watt.Plugin.Sample.csproj -c Release

:: 产物：
:: sdk\sample\Watt.Plugin.Sample\bin\Release\net48\Watt.Plugin.Sample.dll
```

把 DLL 放到用户插件目录并重启 Watt：

```cmd
set "PLUGINS=%LOCALAPPDATA%\Watt\plugins"
if not exist "%PLUGINS%" mkdir "%PLUGINS%"
copy /Y ".\sdk\sample\Watt.Plugin.Sample\bin\Release\net48\Watt.Plugin.Sample.dll" "%PLUGINS%"

:: 完全退出 Watt 后重新启动，或直接用诊断模式验证：
"%LOCALAPPDATA%\Programs\Watt\Watt.exe" --plugins "%TEMP%\plugins.txt"
"%LOCALAPPDATA%\Programs\Watt\Watt.exe" --selftest "%TEMP%\selftest.json"
type "%TEMP%\plugins.txt"
type "%TEMP%\selftest.json"
```

预期：

- `--plugins` 出现 `sample-demo`，没有加载问题；
- `--selftest` 的 `devices` 多出 `Sample Keyboard`（`percent=42`）；
- 示例是假数据，验证后从 `plugins` 删除 DLL 并重启。

> 从源码运行时，先执行 `.\build.cmd`，然后用
> `.\dist\Watt-2.2.6-win-x64\app\Watt.exe --selftest ...` 验证；也可以把 `--plugin-dir` 指向示例 DLL 所在目录。

---

## 5. API 参考

### 5.1 `IBatteryPlugin`

```csharp
public interface IBatteryPlugin
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    string Version { get; }
    int Priority { get; }
    int SuggestedTimeoutMs { get; }
    bool WritesToDevice { get; }
    Task<PluginReadResult> ReadAsync(IPluginContext context, CancellationToken cancellationToken);
}
```

| 成员 | 规则 |
| --- | --- |
| `Id` | **必填、跨版本稳定、全局唯一**，建议小写 kebab-case，例如 `vendor-device-2g4`。宿主用它在覆盖时去重，并写入设备行的 `SourceId`。发布后不要改。 |
| `DisplayName` | 诊断/`--plugins` 显示名称。 |
| `Description` | 一到两句说明。 |
| `Version` | 插件版本字符串，例如 `1.0.0`。仅展示，不参与兼容判断。 |
| `Priority` | 合并优先级，数字越小越强。内置插件使用 10（Windows AEP/HFP）和 5（BLE GATT）；第三方建议 ≥ 60。 |
| `SuggestedTimeoutMs` | 一次扫描的软预算；宿主限制在 500–15000 ms，超时不会强杀插件线程。 |
| `WritesToDevice` | 插件是否向设备发送报文（只读查询帧也算 `true`），只用于诊断展示。 |
| `ReadAsync` | 执行一次扫描。**绝不抛异常**；必须尊重 `CancellationToken`；在预算内返回。 |

### 5.2 `BatteryPluginBase`

推荐继承。只需实现 `Id` 和 `ReadAsync`，其余有默认值：

```csharp
public abstract class BatteryPluginBase : IBatteryPlugin
{
    public abstract string Id { get; }
    public virtual string DisplayName => Id;
    public virtual string Description => string.Empty;
    public virtual string Version => "1.0.0";
    public virtual int Priority => 100;
    public virtual int SuggestedTimeoutMs => 5000;
    public virtual bool WritesToDevice => false;
    public abstract Task<PluginReadResult> ReadAsync(IPluginContext context, CancellationToken cancellationToken);

    protected static PeripheralDevice Device(
        string id, string name, DeviceKind kind, int? percent,
        bool charging = false, string address = null);

    protected static PeripheralDevice Device(
        string id, string name, DeviceKind kind, int? percent,
        bool charging, string address, bool? connected);
}
```

### 5.3 `PluginReadResult`

```csharp
public sealed class PluginReadResult
{
    public bool HardwarePresent { get; }
    public IReadOnlyList<PeripheralDevice> Devices { get; }

    public static PluginReadResult Absent();   // 传输层不存在
    public static PluginReadResult Empty();    // 传输层在，但没有要报告的设备
    public static PluginReadResult Observed(params PeripheralDevice[] devices);
    public static PluginReadResult Observed(IEnumerable<PeripheralDevice> devices);
}
```

- `Absent()`：蓝牙无线电、接收器、HID 接口等**整个传输层**不可用；
- `Empty()`：传输层在，但本轮没有设备需要报告；
- `Observed(...)`：传输层在，返回观察到的设备列表；设备电量可以为 `null`。

### 5.4 `IPluginContext` / `IPluginLogger`

```csharp
public interface IPluginContext
{
    string DataDirectory { get; }   // %LOCALAPPDATA%\Watt\plugin-data\<Id>\
    IPluginLogger Logger { get; }
}

public interface IPluginLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception exception);
}
```

日志写入 `<Watt.exe 目录>\watt-startup.log`。`Error` 只记录异常类型和 Message，不记录堆栈；调试信息请写进 `message`。

### 5.5 `PeripheralDevice` / `DeviceKind`

| 字段 | 说明 |
| --- | --- |
| `Id` | 设备稳定标识。蓝牙设备建议用 `DeviceClassifier.BuildId(address, name, fallbackKey, prefix)` 得到 `bt:<mac>`；非蓝牙设备用厂商固定 Id，例如 `vendor:vid:pid`。**不要用会变化的路径/句柄/PID 拼 Id**。 |
| `Name` | 人类可读名称；名称也参与降级合并（规范化后长度 ≥4 且相同会被视为同一设备）。 |
| `Kind` | `Keyboard` / `Mouse` / `Headphone` / `Other`，决定图标。 |
| `BatteryPercent` | 0–100；设备在但没有新读数时填 `null`（宿主会补缓存）。 |
| `IsCharging` | 设备未上报充电状态时填 `false`。 |
| `IsConnected` | 当前传输连接状态：`true` 已连接、`false` 已配对但断开/休眠、`null` 未知。手动刷新会移除插件**明确报告为 `false`** 的缓存设备行；`null` 会保留（兼容未实现该字段的旧插件），自动刷新始终保留休眠设备的 last-known 值。 |
| `SourceId` | 不用填；宿主对有电量的观测统一覆写为插件 `Id`。 |
| `Address` | 只有真实 48 位蓝牙 MAC 才填，格式 `aa:bb:cc:dd:ee:ff`；其他传输填 `null`，不要编造。 |

### 5.6 可选工具库 `Watt.Plugin.Support`

不是必须，但内置插件都基于它：

| 类型 | 用途 |
| --- | --- |
| `DeviceClassifier` | `Classify(name, codMajor, codMinor)`、`FromClassOfDevice`、`SortRank`、`NormalizeName`、`NormalizeAddress`、`BuildId`、`ClampPercent` |
| `ProviderUtil` | `Empty`、`AwaitOrNullAsync(task, timeoutMs)`、`Observe(task)`、`TryGetInt/TryGetBool/TryGetString(properties, key)`、`FormatBluetoothAddress(ulong)` |
| `HidNative` | SetupAPI + HID 封装：`EnumerateAllPresentCollections()`、`OpenShared`、`OpenSharedOverlapped`、`OpenReadOverlapped`、`TryReadInputReport`、`TryWriteReport`、`CloseHandleSafe`；`HidInterfaceInfo` 提供 VID/PID/usage/报文长度/产品名 |
| `WinRtAsync` | `AsTask<T>(IAsyncOperation<T>)`，把 WinRT 异步操作转成 `Task<T>` |
| `HfpBatteryReader` | `ReadByMac()`：读取经典蓝牙 HFP 节点 `DEVPKEY_Device_BatteryLevel` |

示例（来自内置 Windows 插件）：

```csharp
var id = DeviceClassifier.BuildId(address, name, information.Id);
var kind = DeviceClassifier.Classify(name, codMajor, codMinor);
return new PeripheralDevice
{
    Id = id,
    Name = name,
    Kind = kind,
    Address = DeviceClassifier.NormalizeAddress(address),
    BatteryPercent = percent   // 可能是 null
};
```

---

## 6. 返回值、缓存与合并

### 6.1 `BatteryPercent = null`

表示“设备被观察到了，但这次没有新读数”（例如休眠、耳机关机但仍在配对列表、接收器暂时不应答）。

宿主会按设备 `Id` 查 `last-known.json`：

- 有缓存 → 用缓存的电量/充电状态补全并继续显示；
- 无缓存 → 这一行不显示（面板不支持“电量未知”占位）。

所以插件在设备休眠时应该**继续返回该设备**（`percent: null`），而不是直接不返回。只有传输层完全不可用时才返回 `Absent()`。

### 6.2 `HardwarePresent = false`

`PluginReadResult.Absent()` 表示该插件的整个传输层不存在。宿主会为它累计 absent 次数：

- 连续 **3 次** absent 后，删除该插件名下的全部缓存设备；
- 期间保留缓存行，避免接收器短暂抖动导致设备消失；
- **手动刷新**会立即删除该插件名下的缓存设备并重新加载列表。

手动刷新还会移除插件明确报告 `IsConnected == false` 的缓存设备行；自动刷新仍保留
`IsConnected == true` 但暂时无读数的休眠设备。`IsConnected` 为 `null` 的旧插件保持原缓存行为，
以免升级后误删仍在使用的自用插件设备。

### 6.3 连接状态与手动刷新

`PeripheralDevice.IsConnected` 是当前契约中**新插件应尽量提供**的字段：

- `true`：本轮确认设备可访问/已连接（例如 GATT 读取成功、HID 查询收到响应）；
- `false`：传输层存在，但设备当前不可达/休眠/未响应（例如接收器在、设备不应答）；
- `null`：无法判断；旧插件不设置时即为 `null`。

宿主的行为：

- 自动刷新（60 秒扫描）：无论 `IsConnected` 为何，`null` 电量都沿用 last-known，休眠设备保持可见；
- 手动刷新（面板按钮）：先等待当前扫描结束，再重新扫描并**重新加载整个列表**；
  - `IsConnected == false` 的缓存设备立即从列表/缓存移除；
  - `IsConnected == null` 的旧插件设备保留，避免误删；
  - 新观察到且带电量（或缓存）的设备会加入列表。

因此，新写 2.4G/私有协议插件时，建议在“收到新读数”时返回 `connected: true`，在“接口存在但
设备不响应”时返回 `connected: false`；不要为了省事把所有设备都标成 `true`，否则手动刷新
无法清理真正离线的缓存设备。

> **被动监听型插件的特例**：如果插件只是监听 HID/蓝牙输入报文，本轮没收到报文并不等于
> 设备已断开。这种情况必须返回 `IsConnected = null`，**不能返回 `false`**；`false` 会被手动
> 刷新视为明确断开并清理缓存，导致设备行在每次刷新时出现/消失。只有能确认设备已断开时
> 才返回 `false`。

### 6.4 超时/异常

- `ReadAsync` 超过 `SuggestedTimeoutMs`：宿主取消等待，**保留**该插件此前的缓存行，不计入 absent；
- `ReadAsync` 抛异常：宿主捕获并记录日志，同样保留缓存行；
- 插件线程不会因超时被强制终止，避免死循环、无超时 I/O。

### 6.5 合并规则

多个插件可能观察到同一台设备：

- 优先按规范化蓝牙地址合并，其次按规范化名称；
- 同一来源有多个结果时，Priority 数字小的优先；
- **新读数优先于旧缓存**（避免 Windows 缓存的旧值压过 GATT 新值）；
- 设备类型/名称/地址缺失时由其他来源补全；
- 最终只输出有电量（新读数或缓存）的设备。

---

## 7. 适配新设备

1. 先运行诊断，确认目标设备不是 AEP/HFP/BLE GATT 已覆盖的标准设备：

   ```cmd
   "%LOCALAPPDATA%\Programs\Watt\Watt.exe" --diagnose "%TEMP%\watt-diag.txt"
   ```

   报告包含：插件加载结果、蓝牙 AEP 设备与属性、HFP 节点电量、全部 HID 集合（VID/PID/usage/报文长度）、被动词 HID 原始报文、各插件读取结果。

2. 从 `HID collections` 找到目标接口，记录 VID/PID、usagePage/usage、in/out 报文长度；
3. 从 `HID vendor input reports` 观察设备活动时的原始报文，推断电量字段、充电位和帧格式；
4. 新建 net48 类库，引用 `Watt.Plugin.Contracts`（需要 HID/WinRT 时再引用 `Watt.Plugin.Support`）；
5. 实现 `IBatteryPlugin` / `BatteryPluginBase`，约定：
   - `Id` 稳定唯一；设备 `Id` 稳定；
   - 传输层不存在 → `Absent()`；传输层存在但设备休眠 → `Observed(percent: null)`；
   - 设备可访问/查询成功时设置 `IsConnected = true`，设备不响应时设置 `false`；
   - 只读优先；必须发查询帧时只发文档化的只读帧，限制频率，并在 `WritesToDevice` 中返回 `true`；
   - 不自己持久化/删除缓存；
6. 构建后把 DLL 放进 `%LOCALAPPDATA%\Watt\plugins`，重启 Watt，用 `--plugins` / `--selftest` / `--diagnose` 验证；
7. 按 [sdk/ACCEPTANCE.md](../sdk/ACCEPTANCE.md) 验收。

### 7.1 示例插件代码

完整工程见 [`sdk/sample/Watt.Plugin.Sample/`](../sdk/sample/Watt.Plugin.Sample/)，最小实现：

```csharp
using System.Threading;
using System.Threading.Tasks;
using Watt.Models;
using Watt.Plugins;

public sealed class MyPlugin : BatteryPluginBase
{
    public override string Id => "vendor-device-2g4";
    public override string DisplayName => "Vendor Device 2.4G";
    public override string Description => "Reads battery from my vendor HID device.";
    public override int Priority => 60;
    public override int SuggestedTimeoutMs => 4000;

    public override Task<PluginReadResult> ReadAsync(
        IPluginContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(PluginReadResult.Absent());

            // TODO: 枚举 HID 接口；不存在就 return Absent();
            // TODO: 读取/解析电量；设备休眠时 percent 传 null。

            var device = Device(
                id: "vendor:1234:5678",
                name: "Vendor Device",
                kind: DeviceKind.Keyboard,
                percent: 80,
                charging: false,
                address: null,
                connected: true);

            return Task.FromResult(PluginReadResult.Observed(device));
        }
        catch
        {
            return Task.FromResult(PluginReadResult.Absent());
        }
    }
}
```

### 7.2 设备 Id 建议

| 传输 | Id 建议 |
| --- | --- |
| 蓝牙 | `DeviceClassifier.BuildId(address, name, fallbackKey)` → `bt:<mac>` |
| 2.4G 接收器 | 厂商固定标识，例如 `vendor:vid:pid`；不要用接收器枚举顺序/路径 |
| 蓝牙耳机 HFP | 与 AEP 共用 MAC，便于合并 |

---

## 8. 构建

### 8.1 引用契约

仓库内开发（推荐直接引用工程）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>7.3</LangVersion>
    <AssemblyName>MyVendor.Plugin</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Watt.Plugin.Contracts\Watt.Plugin.Contracts.csproj" />
    <!-- 需要 HID/WinRT 时再加：
    <ProjectReference Include="..\..\src\Watt.Plugin.Support\Watt.Plugin.Support.csproj" />
    -->
  </ItemGroup>
</Project>
```

仓库外开发时引用安装目录的 DLL：

```xml
<Reference Include="Watt.Plugin.Contracts">
  <HintPath>C:\Users\<you>\AppData\Local\Programs\Watt\Watt.Plugin.Contracts.dll</HintPath>
  <Private>false</Private>
</Reference>
```

### 8.2 离线构建

构建机通常没有 NuGet 源：

```cmd
set "DOTNET_CLI_HOME=%CD%\.dotnet-home"
set "NUGET_PACKAGES=%CD%\.nuget-packages"
dotnet build .\sdk\sample\Watt.Plugin.Sample\Watt.Plugin.Sample.csproj -c Release
```

需要 WinRT 时还需要 Windows SDK 的 `Windows.winmd`（默认路径
`C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.26100.0\Windows.winmd`，
可用 `-p:WinmdPath=...` 覆盖）。只用 HID 的 2.4G 插件通常不需要。

产物在 `bin\Release\net48\<AssemblyName>.dll`。插件输出目录**不要**复制 Contracts/Support DLL，
运行时使用 Watt 自己那份。

---

## 9. 部署

```
%LOCALAPPDATA%\Programs\Watt\          # Watt 运行时
├─ Watt.exe
├─ Watt.exe.config
├─ Watt.Plugin.Contracts.dll
├─ Watt.Plugin.Support.dll
└─ plugins\
   ├─ Watt.Plugins.Windows.dll
   └─ Watt.Plugins.BleGatt.dll

%LOCALAPPDATA%\Watt\plugins\           # 第三方插件（推荐）
%LOCALAPPDATA%\Watt\last-known.json    # 电量缓存
```

- 把插件 DLL 放到 `%LOCALAPPDATA%\Watt\plugins`，完全退出 Watt 再启动；
- 不要覆盖 `<安装目录>\plugins` 里的内置 DLL（升级会被替换）；
- 删除插件 DLL 并重启即可移除插件；
- 第三方插件目录不会被 `install` 覆盖，但 `uninstall` 会删除 `%LOCALAPPDATA%\Watt`（可用 `--keep-data` 保留）。

---

## 10. 诊断与测试

### 10.1 命令行

| 参数 | 作用 |
| --- | --- |
| `--plugins <file.txt>` | 插件搜索目录、已加载插件、元数据、加载错误 |
| `--selftest <file.json>` | 加载插件、扫描一次并输出 JSON（设备 + 插件 + 加载问题） |
| `--diagnose <file.txt>` | 蓝牙 AEP/HFP、HID 集合、原始报文、各插件读取结果 |
| `--plugin-dir <dir>` | 额外插件目录，可重复或用 `;` 分隔 |

### 10.2 `--selftest` 输出

```json
{
  "devices": [
    { "id": "sample:keyboard-1", "name": "Sample Keyboard", "kind": "Keyboard",
      "percent": 42, "charging": false, "source": "sample-demo", "address": null }
  ],
  "plugins": [ { "id": "sample-demo", "version": "1.0.0", "priority": 999,
                 "timeoutMs": 3000, "writes": false, "assembly": "..." } ],
  "pluginIssues": [],
  "elapsedMs": 120
}
```

### 10.3 `--diagnose` 报告

重点看：

- `== plugins ==`：加载了哪些插件、每个插件 `hardwarePresent` / `devices` / 耗时；
- `== bluetooth devices (AEP) ==`：Windows 已知设备与电池属性；
- `== classic Bluetooth HFP node battery ==`：HFP 节点电量；
- `== HID collections (SetupAPI) ==`：VID/PID/usage/报文长度；
- `== HID vendor input reports (listening) ==`：原始输入报文。

### 10.4 日志

- `<Watt.exe 目录>\watt-startup.log`；
- 插件日志格式：`[plugin:<Id>] INFO/WARN/ERROR ...`；
- 宿主日志：`plugin loaded: ...`、`plugin issue: ...`、`plugin <id> timed out ...`、`battery plugins loaded: N, issues: M`。

---

## 11. ABI 与版本兼容

- 契约程序集：`Watt.Plugin.Contracts`，当前 1.0.0，未强命名，目标 net48；
- 插件在进程内通过程序集加载，无跨进程 ABI、无版本协商；
- `1.x` 内契约只做增量修改，不破坏已有成员；为 1.x 编译的插件可在后续 1.x Watt 上运行；
- 如果插件使用了更新版本才有的 API，在旧 Watt 上可能加载失败或运行时抛
  `MissingMethodException` / `TypeLoadException`；
- 不要把 Contracts/Support DLL 重命名或放进插件目录（宿主会跳过这两个文件名）；
- 插件保持 AnyCPU；宿主是 x64 进程，仅 x86 的原生依赖会失败；
- `Version` 属性只用于展示，宿主不据此判断兼容性。

---

## 12. 内置插件

| 插件 Id | 源工程 | Priority | 超时 | WritesToDevice | 说明 |
| --- | --- | --- | --- | --- | --- |
| `windows-aep` | `src/Watt.Plugins.Windows` | 10 | 3000 ms | `false` | Windows AEP 电池属性 + 经典蓝牙 HFP 节点电量 |
| `ble-gatt` | `src/Watt.Plugins.BleGatt` | 5 | 6000 ms | `false` | 标准 BLE Battery Service `0x180F / 0x2A19`；连接的设备会直接读取，GATT 新读数优先于 Windows AEP 缓存 |

优先级数字越小越强。第三方插件建议 ≥ 60；示例插件用 999。

---

## 13. 排错清单

1. **插件没出现**：检查目录、`--plugins` 的 `loaded`、日志里的 `plugin issue`；DLL 是否 net48/AnyCPU。
2. **插件出现但设备没显示**：设备是否返回了 `BatteryPercent`；休眠设备是否 `null` 且缓存有值；`Id` 是否跨扫描稳定。
3. **设备休眠后消失**：插件是否在休眠时继续返回该设备（percent null）；是否误返回 `Absent()`。
4. **设备拔掉后不消失**：插件是否一直返回 `HardwarePresent=true` 和旧设备；应返回 `Absent()` 或不再返回该设备。
5. **超时日志**：把阻塞 I/O 改成带超时的读；调大 `SuggestedTimeoutMs`（≤15000）；超时不会终止线程。
6. **同 Id 覆盖不生效**：Id 必须完全一致；用户目录在 app 目录之后加载；旧进程未退出会导致新插件不生效。
7. **插件日志看不到**：日志在 exe 同目录 `watt-startup.log`；`--plugins` / `--diagnose` / `--selftest` 也会写。
8. **FileLoadException / HRESULT 0x80131515**：.NET Framework 拒绝从受限/不受信路径加载程序集；把 DLL 移到普通用户目录（如 `%LOCALAPPDATA%\Watt\plugins`）再试。

---

## 14. 当前契约限制

1. `IPluginContext` 不暴露宿主/契约版本，插件无法运行时判断 ABI；
2. 没有 Dispose/卸载回调，插件实例活到进程退出；没有热重载；
3. 无法感知“用户刚打开面板”/force 刷新；
4. 插件之间无法协调“谁覆盖了哪些设备”（旧的 GATT covered-device 机制已不存在）；
5. `IPluginLogger.Error` 不记录堆栈；日志直写文件，无限流；
6. 同 Id 覆盖时被覆盖的实例也会被构造；同 Id 共用 DataDirectory；
7. 按名称合并可能误合并两台同名且无地址的设备；
8. 只有 `null` 电量且无缓存的设备不会显示（不支持“电量未知”占位）；
9. 超时不会终止插件线程；
10. `Absent()` 是插件级语义，不能表达“某台设备暂时不在”。

---

## 15. 参考

- [README.md](../README.md) — 项目概览与安装
- [supported-devices.md](supported-devices.md) — 默认数据源与休眠行为
- [install.md](install.md) — 构建/安装/卸载细节
- [battery-research.md](battery-research.md) — Windows AEP / BLE GATT / HFP 调研笔记
- 示例工程：[`sdk/sample/Watt.Plugin.Sample/`](../sdk/sample/Watt.Plugin.Sample/)
- 契约源码：[`src/Watt.Plugin.Contracts/`](../src/Watt.Plugin.Contracts/)
- 工具库源码：[`src/Watt.Plugin.Support/`](../src/Watt.Plugin.Support/)
- 开发资料：[`sdk/README.md`](../sdk/README.md)、[`sdk/AI-PROMPT.md`](../sdk/AI-PROMPT.md)、[`sdk/capture-template.md`](../sdk/capture-template.md)、[`sdk/ACCEPTANCE.md`](../sdk/ACCEPTANCE.md)

> **再次提醒**：插件是与 Watt 同进程、同权限的受信代码，安装前务必确认来源可信。Watt 不提供任何插件沙箱。
