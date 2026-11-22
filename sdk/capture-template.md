# 新设备协议采集模板

把本文件复制成 `device-<型号>.md` 填写；连同 `--diagnose` 报告、原始报文和厂商资料一起
交给 AI/开发者。**不要只给设备名，让 AI 猜协议。**

## 1. 基本信息

- 设备名称/型号：
- 品牌：
- 连接方式：蓝牙经典 / BLE / 2.4G 接收器 / 有线
- USB VID:PID（2.4G/有线）：
- 接收器型号/固件：
- 设备固件：
- Windows 版本 / 蓝牙适配器：
- 厂商软件（名称/版本/是否能看电量）：
- 是否支持多设备共用一个接收器：

## 2. 诊断报告

运行：

```cmd
"%LOCALAPPDATA%\Programs\Watt\Watt.exe" --diagnose "%TEMP%\watt-diag.txt"
```

把报告中的这些段落贴进来：

- `== plugins ==`（确认没有内置插件覆盖它）
- `== bluetooth devices (AEP) ==`（蓝牙设备）
- `== HID collections (SetupAPI) ==`（找到设备的接口：VID/PID/usagePage/usage/in/out/feature）
- `== HID vendor input reports (listening) ==`（原始输入报文）
- 如适用：`classic Bluetooth HFP node battery`、`BLE GATT battery probe`

## 3. 接口地图

| 接口 | VID:PID | usagePage | usage | in 长度 | out 长度 | feature 长度 | 用途/备注 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| | | | | | | | |

## 4. 协议行为

- [ ] 被动上报：不操作也会周期性发送状态帧
- [ ] 需要主动查询：查询帧格式如下
- [ ] 需要先开会话/握手
- [ ] 只在设备唤醒/按键/移动时发送

查询帧（十六进制，含 report ID）：

```
...
```

响应帧（十六进制，含 report ID）：

```
...
```

字段说明：

| 偏移（从 0 开始） | 含义 | 示例值 | 备注 |
| --- | --- | --- | --- |
| | 电量 % | | |
| | 充电状态 | | 0=放电 1=充电 2=满电（按实际） |
| | 设备名/型号 | | |

## 5. 边界与异常

- 电量 0/1/255 是否为无效值：
- 充电位 bit/值：
- 设备休眠时是否响应查询：
- 接收器拔出后接口是否消失：
- 与其他软件同时读取的冲突：
- 协议来源（官方 WebHID/驱动/抓包/社区文档链接）：

## 6. 期望插件行为

- 插件 Id：
- Priority（建议第三方 ≥60）：
- 是否写设备：
- 设备 Id 规则（例如 `vendor:vid:pid` 或蓝牙 MAC）：
- 显示名称：
- DeviceKind：Keyboard / Mouse / Headphone / Other
