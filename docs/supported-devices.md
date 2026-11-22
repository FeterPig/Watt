# 支持设备与数据来源

Watt 默认只使用 Windows 与蓝牙标准提供的数据源，不读取任何厂商私有 2.4G 协议、厂商 HID 协议或厂商查询帧。所有来源由插件提供，宿主按优先级合并、去重并缓存。

## 默认内置来源

| 插件 Id | 优先级 | 超时 | 写设备 | 数据来源 |
| --- | --- | --- | --- | --- |
| `windows-aep` | 10 | 3000 ms | 否 | Windows 蓝牙 AEP 属性（`System.Devices.BatteryLife` / `BatteryPlusCharging`），并对经典蓝牙耳机回退读取 HFP 设备节点电量 |
| `ble-gatt` | 20 | 6000 ms | 否 | 已连接 BLE 设备的标准 Battery Service（`0x180F / 0x2A19`） |

优先级数字越小越优先；第三方插件建议使用 ≥ 60。

## Windows AEP

- 通过 `BluetoothDevice` / `BluetoothLEDevice` 的配对选择器枚举设备，读取 `System.Devices.BatteryLife` 和 `System.Devices.BatteryPlusCharging`；
- `BatteryPlusCharging` 的 101–200 表示“电量 = 值 − 100 且充电中”，201 表示未知；
- `BatteryLife` 的 101 表示未知；
- 设备被 Windows 枚举（即使已休眠/断开）就保留其行，电量没有新值时由宿主用缓存补全；
- 这是与「设置 → 蓝牙和设备」一致的数据源，覆盖大部分标准蓝牙键盘、鼠标和耳机。

## 经典蓝牙 HFP 节点电量

部分蓝牙耳机（本机实测 EDIFIER Fit900NB）只在 HFP 网关设备节点上暴露电量，Windows AEP 属性和 BLE GATT 都读不到。`windows-aep` 插件会回退枚举 `BTHENUM\*` 设备节点，读取：

```
DEVPKEY_Device_BatteryLevel
{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2
```

按设备 MAC 归并，只对当前已连接的经典蓝牙设备采用。方法来源：[VigilWatt `ClassicDevNodeReader.cs`](https://gitee.com/YGWG-LX/vigil-watt/blob/main/src/VigilWatt.Core/Ble/ClassicDevNodeReader.cs)（MIT © 2026 evren），本项目按其文档独立实现。

## BLE GATT

- 只处理已配对的 BLE 设备；
- 设备 AEP 已有电量时跳过，避免和 `windows-aep` 重复；
- 设备未连接时跳过，不主动唤醒休眠设备；
- 连接后读取标准 Battery Service `0x180F` / characteristic `0x2A19`，只接受 0–100。

## 休眠与离线行为

- 蓝牙设备仍在 Windows 配对列表里（休眠/断开）→ 保留最后一次电量；
- 设备被取消配对/不再被 Windows 枚举 → 下一次扫描后从列表消失；
- 接收器/蓝牙适配器不存在 → 连续 3 次扫描后清理该插件的缓存设备；
- 缓存保留 30 天；读不到新值时沿用旧值展示，不做任何提示文案；
- BLE 设备休眠时不会被强制唤醒，只显示缓存值。

## 未内置的设备

厂商私有 2.4G dongle、厂商 HID 协议、需要发送厂商查询帧的设备**默认都不读取**。如果需要支持，按 [插件开发文档](plugins.md) 编写一个 net48 插件 DLL，自行处理该设备的协议、稳定 Id、电量解析和读写安全，然后放到：

- `%LOCALAPPDATA%\Watt\plugins`（推荐，升级/重装 Watt 不会覆盖）；
- 或 `<安装目录>\plugins`；
- 开发调试时也可用 `Watt.exe --plugin-dir <dir>` 临时加载。

插件可以完全独立于本项目发布；Watt 只负责调度、缓存和显示。

## 扩展新设备

1. 运行 `Watt.exe --diagnose report.txt`，查看报告中的 AEP、HFP、HID 集合和原始 HID 报文；
2. 确认目标设备不属于 AEP/HFP/BLE GATT 已覆盖的标准来源；
3. 参考 [plugins.md](plugins.md) 与 `sdk/sample/Watt.Plugin.Sample/` 实现插件；
4. 把 DLL 放进 `%LOCALAPPDATA%\Watt\plugins`，重启 Watt；
5. 用 `--plugins` / `--selftest` / `--diagnose` 验证，并按 `sdk/ACCEPTANCE.md` 验收。
