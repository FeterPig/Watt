# Watt 自动化测试

在仓库根目录运行完整测试套件：

```cmd
tests\run-tests.cmd
```

脚本会构建宿主、内置插件和测试运行器，然后执行 `Watt.Tests.exe`。任何测试失败时脚本以非零退出码结束。也可以传入过滤词只运行一部分测试：

```cmd
tests\run-tests.cmd battery
tests\run-tests.cmd ui/
tests\run-tests.cmd plugins/
```

## 覆盖范围

| 范围 | 示例 |
| --- | --- |
| 电量服务 | 并行扫描、超时、异常、absent 计数、合并优先级、新读数与缓存、手动刷新、缓存归属、释放 |
| 持久化 | `last-known.json` 往返、30 天过期、损坏缓存、原子写入、设备偏好、恢复隐藏设备 |
| 插件宿主 | 内置插件元数据、损坏 DLL 隔离、跳过 Contracts/Support、同 Id 覆盖顺序 |
| 插件 SDK 辅助库 | 设备分类、地址/名称规范化、Provider 转换、HID 枚举 |
| BLE guard | 计数器特征学习、放电、充电、时间戳持久化 |
| 自启/主题 | 用内存存储测试 Run 键的引号与修复逻辑；主题读取、刷新、启动生命周期 |
| UI | 面板行、低电量/充电状态、空状态/加载状态、编辑模式、删除、刷新反馈、滚动、辅助功能名称 |
| 诊断 | 快照渲染、报告写入失败、单实例、托盘图标 |

## 隔离方式

测试运行器在接触任何服务之前，会把 `WATT_DATA_DIR` 指向 `.test-out\test-data\` 下的临时目录，因此测试不会读写 `%LOCALAPPDATA%\Watt` 的真实数据。UI 测试在 STA 线程创建 `Application`，但不会显示窗口或托盘图标。插件测试会加载真实的内置 DLL，以及 `tests\plugins\` 下两个仅用于测试的小插件。

部分注册表写入（例如修改主题）被有意排除在测试套件之外：受限的测试进程可能只有 `HKCU\Software` 的读取权限。测试改为覆盖读取/刷新路径，并通过可注入的存储来测试 `StartupService`。
