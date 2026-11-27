# 电量数据来源研究笔记

本文记录 Watt 默认数据源（Windows AEP、经典蓝牙 HFP、BLE GATT）的实现依据和边界。厂商私有 2.4G/HID 协议不在默认支持范围内。

## 1. Windows AEP 属性

Windows 会把已配对蓝牙设备的电量暴露为设备属性：

| 属性 | 含义 |
| --- | --- |
| `System.Devices.BatteryLife` | 电量百分比；`101` 表示未知 |
| `System.Devices.BatteryPlusCharging` | 0–100 = 电量；101–200 = 电量 = 值 − 100 且充电中；201 = 未知 |

读取方式：用 `BluetoothDevice.GetDeviceSelectorFromPairingState(true)` 和
`BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)` 枚举设备，再读取上述属性。
这是与「设置 → 蓝牙和设备」一致的数据来源，也是覆盖设备最多的默认来源。

注意 Windows 可能缓存已断开设备的值，因此休眠设备显示的可能是最后一次系统读数；Watt 自己的
`last-known.json` 会在此基础上保证设备休眠/重启后仍然可见。

## 2. BLE GATT Battery Service

标准 BLE 电池服务：

- Service UUID：`0000180f-0000-1000-8000-00805f9b34fb`（`0x180F`）
- Characteristic UUID：`00002a19-0000-1000-8000-00805f9b34fb`（`0x2A19`）
- 值：第一个字节为 0–100 的百分比

实现约束：

- 只处理已配对的 BLE 设备；
- 已连接设备即使 AEP 已有电量也直接读取；GATT 是实时读数，优先级高于 Windows AEP 缓存；
- 设备 `IsConnected == false` 时跳过，不主动唤醒休眠设备；
- 每一步 WinRT 异步调用都有超时，读取使用 `BluetoothCacheMode.Uncached`；
- .NET Framework 4.8 目标机上没有可用的 `System.Runtime.WindowsRuntime`，因此
  `Watt.Plugin.Support/WinRtAsync.cs` 提供 `IAsyncOperation<T> → Task<T>` 适配器。

### 2.1 BLE Battery Level 自增计数器

已确认某些 BLE 外设的 `0x2A19` 特征不是稳定电量，而是**每次读取返回上一次值 +1**。
例如 `EWEADN X23 Pro`（address `c9:8c:17:34:19:f8`，model `47-BLE-1.0`）连续读取：

```text
53, 54, 55, 56, 57, 58, ...
```

特点：

- 读取间隔无关，每读一次就上涨；
- 会超过 100（例如 117）；
- 设备不充电时也一样；
- CCCD Notify 不提供有效通知，无法用通知绕过。

`ble-gatt` 插件用 `BleBatteryGuard` 处理这类设备：

- 连续出现两次 `+1` 时识别为计数器；设备正在充电时上涨是正常方向，guard 不会在充电期间学习该特征；
- 锁定识别前的第一个电量为基线，之后不采用继续上涨的值；
- 出现更低的读数时更新基线（按真实放电处理）；
- 对已识别的设备优先读取 **Windows GATT 缓存**（`BluetoothCacheMode.Cached`），
  这与系统设置页显示的是同一份缓存值，不会触发设备的读取计数器；
- 缓存不可用时才回退到 Uncached，并由 guard 兜底；
- 状态保存在 `%LOCALAPPDATA%\Watt\ble-battery-guard.txt`，重启后继续生效；
- 移除该文件可让 Watt 重新学习（设备重新连接、实际电量变化后可重置基线）。

这不是 Windows AEP/HFP 的问题；同一个鼠标的 `ryunix-kyupro` 2.4G 读数保持稳定，BLE 行才是异常来源。

### 2.2 被动监听型 2.4G 插件的 `IsConnected`

`ryunix-kyupro` 通过 2 秒监听窗口捕获 X23 Pro 的 vendor 报文：

- 捕获到有效报文：返回 `IsConnected = true`；
- 接收器存在但本轮没捕获到报文（鼠标休眠/未活动）：返回 `BatteryPercent = null`、
  `IsConnected = null`；
- 接收器不存在：返回 `Absent()`。

这里不能用 `IsConnected = false` 表示“没收到报文”。宿主的手动刷新会清理明确
`IsConnected == false` 的缓存；如果被动插件把空闲误报成断开，设备行就会在每次手动刷新时
交替出现/消失。`false` 仅保留给能够确认设备已断开的主动查询型插件。

## 3. 经典蓝牙 HFP 节点电量

有些经典蓝牙耳机只在 HFP 网关设备节点上暴露电量。方法是通过 SetupAPI 枚举 `BTHENUM\*`
设备节点，读取设备属性：

```
DEVPKEY_Device_BatteryLevel
{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2
```

按设备 MAC 归并，只对当前已连接设备采用。该逻辑位于 `windows-aep` 插件的 HFP 回退路径中，
读取结果写入同一个设备行。方法来源：[VigilWatt `ClassicDevNodeReader.cs`](https://gitee.com/YGWG-LX/vigil-watt/blob/main/src/VigilWatt.Core/Ble/ClassicDevNodeReader.cs)（MIT © 2026 evren），本项目按其文档独立实现。

## 4. 设备分类

面板只区分键盘、鼠标、耳机、其他四类。分类顺序：

1. 名称关键词（耳机/键盘/鼠标的常见型号词）；
2. 蓝牙 Class-of-Device 的 major/minor 位；
3. 无法判断时使用 `Other`。

多来源合并时，类型可以由其他来源补全。

## 5. 合并、缓存与超时

- `BatteryService` 每 60 秒并行调用所有插件，单插件超时由 `SuggestedTimeoutMs` 决定（宿主限制 500–15000 ms）；
- 插件返回 `Absent()`：传输不存在，连续 3 次后删除该插件名下缓存；
- 插件返回 `Observed` + `BatteryPercent = null`：设备在但暂时无读数，宿主用 `last-known` 补全；
- 超时/异常：保留该插件缓存行，不按 absent 处理；
- 合并按规范化蓝牙地址优先、名称兜底；Priority 小者优先，新读数优先于旧缓存；
- `last-known.json` 保留 30 天。

## 6. 已知限制

- 从未上报过电量、也没有缓存的设备不会显示；
- Windows AEP 可能返回系统缓存值，无法保证是实时值；
- BLE 设备未连接时不主动唤醒，因此休眠时只显示缓存；
- 经典蓝牙耳机的电量取决于耳机固件是否在 HFP 节点暴露该属性；
- 厂商私有 2.4G/HID 协议默认不支持，需要按 [plugins.md](plugins.md) 编写插件。
