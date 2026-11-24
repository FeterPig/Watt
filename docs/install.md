# 构建、安装与卸载

Watt 2.2.0 起不再使用 PowerShell 安装脚本。仓库只负责构建；安装/卸载入口只在构建产物
`dist\Watt-2.2.0-win-x64\` 中出现，由 C# 程序 `Watt.Setup.exe` 完成实际操作，无需管理员权限，
也不需要修改 PowerShell 执行策略。

## 构建要求

- Windows 10/11 x64；
- .NET SDK 9（用于构建；运行只需要 .NET Framework 4.8，Windows 11 自带）；
- Windows 10/11 SDK 的 `Windows.winmd`，默认路径
  `C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.26100.0\Windows.winmd`，
  可用 `-p:WinmdPath=...` 覆盖；
- 不需要 NuGet 源；构建完全离线。

## 从源码构建

在仓库根目录执行：

```cmd
build.cmd
```

`build.cmd` 会：

1. 构建 `src\Watt\Watt.csproj`（宿主，连带构建 Contracts/Support）；
2. 构建 `src\Watt.Setup\Watt.Setup.csproj`（安装/卸载程序）；
3. 构建 `src\Watt.Plugins.*` 下的所有内置插件；
4. 生成发行版目录：

   ```
   dist\Watt-2.2.0-win-x64\
     Watt.exe
     Watt.exe.config
     Watt.Plugin.Contracts.dll
     Watt.Plugin.Support.dll
     Watt.Setup.exe
     plugins\
       Watt.Plugins.Windows.dll
       Watt.Plugins.BleGatt.dll
     README.md
     docs\
   ```

5. 如果系统有 `tar.exe`，同时生成 `dist\Watt-2.2.0-win-x64.zip`。

构建失败时脚本返回非零退出码；没有单独的“只编译不打包”参数，需要时直接 `dotnet build`
单个工程即可。

## 安装（普通用户）

下载并解压发行版后，有两种方式。

### 方式一：双击 Watt.Setup.exe（推荐）

双击发行版根目录的 `Watt.Setup.exe`，控制台会显示交互菜单：

```
Watt.Setup 2.2.0 - per-user installer/uninstaller

  1. 安装 / Install
  2. 卸载 / Uninstall
  0. 退出 / Exit

请选择 / Select:
```

选择 `1` 安装、`2` 卸载、`0` 退出。安装/卸载结束后会提示按回车关闭窗口。

### 方式二：命令行

也可以直接运行 `Watt.Setup.exe install`：

```cmd
Watt.Setup.exe install
```

`Watt.Setup.exe` 会：

1. 停止已运行的 Watt；
2. 保留安装目录本身，覆盖运行时文件并替换 `plugins\` 文件夹；
3. 注册当前用户登录自启（`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `Watt` 值）；
4. 创建开始菜单快捷方式 `Watt.lnk`；
5. 启动 Watt。

安装是 per-user 的，不需要管理员权限，不写系统级目录，也不需要 PowerShell。

### 安装参数

参数直接传给 `Watt.Setup.exe install`，例如：

```cmd
Watt.Setup.exe install --install-dir D:\Apps\Watt
Watt.Setup.exe install --no-start
Watt.Setup.exe install --no-autostart --no-shortcut
```

| 参数 | 作用 |
| --- | --- |
| `--install-dir DIR` | 自定义安装目录（默认 `%LOCALAPPDATA%\Programs\Watt`） |
| `--no-start` | 安装后不启动 Watt |
| `--no-autostart` | 不写 HKCU Run 自启项 |
| `--no-shortcut` | 不创建开始菜单快捷方式 |
| `--no-stop` | 不停止已运行的 Watt（仅自动化/测试用） |

也可以直接运行 `Watt.Setup.exe install [参数]`。

### 安装后的目录

| 路径 | 内容 |
| --- | --- |
| `%LOCALAPPDATA%\Programs\Watt\Watt.exe` | 主程序 |
| `%LOCALAPPDATA%\Programs\Watt\Watt.Plugin.Contracts.dll` | 插件契约 |
| `%LOCALAPPDATA%\Programs\Watt\Watt.Plugin.Support.dll` | 插件辅助库 |
| `%LOCALAPPDATA%\Programs\Watt\plugins\*.dll` | 内置插件 |
| `%LOCALAPPDATA%\Watt\last-known.json` | 电量缓存 |
| `%LOCALAPPDATA%\Watt\plugins\*.dll` | 第三方插件（建议放这里） |
| `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Watt.lnk` | 开始菜单快捷方式 |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `Watt` 值 | 登录自启 |

## 卸载

在发行版目录中双击 `Watt.Setup.exe` 选择「2. 卸载」，或执行：

```cmd
Watt.Setup.exe uninstall
```

默认会先确认，然后：

- 停止 Watt；
- 删除 HKCU Run 自启值；
- 删除开始菜单快捷方式；
- 删除 `%LOCALAPPDATA%\Programs\Watt`；
- 删除 `%LOCALAPPDATA%\Watt`（缓存和第三方插件数据）；
- 清理该程序对应的 Windows 通知区域设置。

非交互或保留数据：

```cmd
Watt.Setup.exe uninstall --yes --keep-data
```

| 参数 | 作用 |
| --- | --- |
| `--yes` | 跳过确认 |
| `--keep-data` | 保留 `%LOCALAPPDATA%\Watt`（缓存、用户插件） |
| `--install-dir DIR` | 指定要删除的安装目录；默认从 HKCU Run 值推断，否则用默认目录 |
| `--no-autostart` / `--no-shortcut` / `--no-notify` | 分别跳过注册表/快捷方式/通知区域清理 |
| `--no-stop` | 不停止主程序（仅测试用） |

## 自启注册

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    Watt = "C:\Users\<you>\AppData\Local\Programs\Watt\Watt.exe"
```

可用以下命令查看：

```cmd
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Watt
```

## 常见问题

- **`dotnet` 找不到**：安装 .NET SDK 9，并确保 `dotnet` 在 PATH 中。
- **构建提示找不到 `Windows.winmd`**：安装 Windows 10/11 SDK，或用
  `dotnet build ... -p:WinmdPath=...` 指定。
- **安装时报“payload is incomplete”**：必须从 `dist\Watt-2.2.0-win-x64` 发行版目录运行
  `Watt.Setup.exe`，不要从源码目录运行。
- **安装/卸载提示注册表访问被拒绝**：确认当前用户对 HKCU 有写权限；程序不会请求管理员权限。
- **托盘图标在二级托盘里**：Watt 会重试并重新注册图标；如果升级后 Explorer 完全没有生成
  Watt 的设置项，Watt 会在约 15 秒后自动重启一次 Explorer 来恢复一级托盘图标。若仍无效，
  检查 `HKCU\Control Panel\NotifyIconSettings` 中是否有 `ExecutablePath` 指向
  `...\Programs\Watt\Watt.exe` 的项，以及 `IsPromoted` 是否为 1。
- **卸载后缓存还在**：卸载默认删除 `%LOCALAPPDATA%\Watt`；使用 `--keep-data` 时会保留。
- **企业策略禁止 exe**：`Watt.Setup.exe` 是普通 .NET Framework 控制台程序，未签名；如被
  AppLocker/SmartScreen 拦截，需要按组织策略放行。
