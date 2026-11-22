# Watt 图标与 Segoe Fluent Icons 字形

本文记录的所有图标和字形都在构建机上实际生成并目视检查过（Windows 11 build 26300、.NET Framework 4.8、无 NuGet）。应用不随包分发任何字体文件或 NuGet 包：面板使用系统自带的 `Segoe Fluent Icons` 字体渲染字形，托盘/应用图标则是 `watt/tools/IconGen` 生成的独立位图。

## 1. 交付物

| 文件 | 帧尺寸 | 用途 |
| --- | --- | --- |
| `watt/assets/tray-light.ico` | 16、20、24、32、40、48 px | **浅色**任务栏上的托盘图标——纯黑线稿、透明背景 |
| `watt/assets/tray-dark.ico` | 16、20、24、32、40、48 px | **深色**任务栏上的托盘图标——纯白线稿、透明背景 |
| `watt/assets/Watt.ico` | 16、20、24、32、40、48、64、256 px（256 为 PNG 帧） | 可执行文件 / 开始菜单 / 资源管理器图标——蓝色圆角方块 + 白色线稿标记 |
| `watt/assets/previews/*.png` | - | 设计检查用 PNG（接触表、实际尺寸条、像素放大、ICO 往返验证） |

16/20/24/32/40/48 px 覆盖 100 %/125 %/150 %/200 %/250 %/300 % 显示缩放。`Watt.ico` 已通过 `Watt.csproj` 的：

```xml
<ApplicationIcon Condition="Exists('..\..\assets\Watt.ico')">..\..\assets\Watt.ico</ApplicationIcon>
```

自动嵌入。两个托盘 `.ico` 作为资源嵌入，由托盘代码根据任务栏主题（`SystemUsesLightTheme`）选择。

## 2. 图形设计

托盘标记是一个极简、原生风格的电池：圆角矩形机身 + 顶部电极 + 两条电量刻度，在 **16 × 16 设计网格**上绘制，并在每种输出尺寸上从矢量重新渲染（不是从 256 px 缩小得到的模糊图）。

* 线宽 1.5 单位（16 px 下为 1.5 px，32 px 下为 3 px），圆头端点、圆角连接；
* 几何位置经过对齐，使 16 px 下 1.5 px 线宽恰好产生一个完整覆盖像素加一个半覆盖像素——这是 16 px 网格上最清晰的 1.5 px 线条；
* 墨色严格为 `#000000`（浅色任务栏）/ `#FFFFFF`（深色任务栏）；抗锯齿只存在于 alpha 通道（已审计，见第 6 节）；
* 背景完全透明；无渐变、无文字、无彩色；
* 电量刻度与轮廓之间保留 1 px 空隙，避免 16 px 下糊成一团。

应用图标使用同一标记 + 实心圆角方块：256 × 256 设计网格，方块内缩 16，圆角半径 52，Windows 11 强调蓝 `#0F6CBD`，白色标记按 16 单位网格放大 10.6 倍（约占方块宽度的 62 %，保证在资源管理器/开始菜单的 16 px 下仍可辨识）。选择扁平色块而非黑色块，是因为它在浅色和深色背景上都可辨识（黑色块会融入深色模式表面）。

## 3. 重新生成（可复现、离线）

在工作区根目录（`watt\..`）执行：

```cmd
dotnet build watt\tools\IconGen\IconGen.csproj -c Release
watt\tools\IconGen\bin\Release\net48\IconGen.exe all
```

`IconGen.exe` 的模式：

| 模式 | 输出 |
| --- | --- |
| `glyphs` | `previews/glyphs-large.png`、`previews/glyphs-zoom-20px.png`、`previews/glyphs-chosen-20px.png` |
| `candidates` | `previews/art-candidates.png`、`previews/art-app-tiles.png` |
| `icons` | 三个 `.ico` 文件 + 全部托盘/应用预览图 |
| `all`（默认） | 以上全部 |

工具会相对于运行时的工作区写出图标（它会向上查找 `watt/src/Watt/Watt.csproj`），因此在任意当前目录运行都可以。工具自身的构建输出停留在 `watt/tools/IconGen/bin|obj`。

修改图形时，编辑 `Art.cs`（`TrayVariant`、`AppVariant`、方块颜色、标记几何、`AppMarkScale`），然后先运行 `candidates` 模式——它会在浅色/深色任务栏上以实际 16 px 渲染所有变体，并附 6 倍像素放大。

## 4. 位图是如何生成的

* **渲染**：WPF `RenderTargetBitmap` + `DrawingVisual`；小于 64 px 时 8 倍超采样，128/256 px 时 4 倍；随后用盒式滤波平均超采样像素，并把预乘 alpha 转为直通 alpha，因此 16 px 也能获得与大尺寸相同的形状质量。
* **ICO 写入**：`IcoWriter.cs` 输出经典图标容器——≤64 px 使用 32 bpp `BITMAPINFOHEADER` 帧（带正确的 AND 掩码、透明像素已标记），256 px 使用 PNG 压缩帧，符合资源管理器/开始菜单要求。没有任何 NuGet 依赖；PNG 编码器使用 WPF。
* **自检**：写出后会用 `IconBitmapDecoder` 重新解码每个 `.ico`，断言帧尺寸/数量，并对每个托盘帧做纯色墨迹审计（第 6 节）。`previews/tray-ico-frames.png` 展示的就是解码回来的帧。

## 5. 面板使用的 Segoe Fluent Icons 字形

来源：Windows 11 系统字体 `Segoe Fluent Icons`（`C:\Windows\Fonts\SegoeIcons.ttf`，字体族名 "Segoe Fluent Icons"）。每个候选字形都用 WPF 在面板实际的 20 px `FontSize`、浅色 `#FFFFFF` / 深色 `#1D1D20` 面板表面上渲染，并以 4 倍像素放大检查（`previews/glyphs-large.png`、`previews/glyphs-zoom-20px.png`）。`watt/src/Watt/UI/Glyphs.cs` 中已验证的值：

| 常量 | 码点 | 字形名 | 验证说明 |
| --- | --- | --- | --- |
| `Keyboard` | `U+E92E` | KeyboardStandard | 20 px 下最清晰的键盘；`E765` KeyboardClassic 更复杂（按键点更多），`E763/E764` 是半键盘，`E766` 是分体键盘，`E961` Input 混合键盘+鼠标。无需更换。 |
| `Mouse` | `U+E962` | Mouse | 20 px 下干净清晰。无需更换。 |
| `Headphone` | `U+E7F6` | Headphone | 头戴式耳机轮廓，20 px 下清晰。`E95B` Headset 带麦克风杆（只适合带麦耳机）；`ED30`-`ED33` 是“带音量波的耳机”。无需更换。 |
| `Other` | `U+E772` | Devices | 笔记本 + 手机堆叠，在 20 px 下读作“已连接设备”。`E7F4/E975` 也是多设备字形，但视觉更复杂。无需更换。 |
| `Bolt` | `U+E945` | LightningBolt | 20 px 下可读作闪电。`EA93`/`EBAB` 是带闪电的电池字形，对 11 px 充电标记来说细节过多。无需更换。 |
| `Bluetooth` | `U+E702` | Bluetooth | 20 px 下干净清晰（经典蓝牙符文）。无需更换。 |
| `Battery` | `U+E83F` | Battery10 | 通用满电池；`E850` 是 Battery0（空轮廓），`E85A` BatteryCharging0，`E996` BatteryUnknown。保留 `E83F` 作为规范“电池”字形；托盘/应用标记是自定义矢量，**不**使用此字形。无需更换。 |

被否决的候选：`E7E8` 是电源按钮字形（不是电池），`E83D` 渲染为通用实心形状，`E704` 是 InternetSharing，`E703` Connect 是设备配对字形。`E972/E975` 等也在接触表上检查过，都没有优于当前常量。

注意：面板保持 `TextOptions.TextFormattingMode="Ideal"` 和灰度抗锯齿，这正是验证图使用的渲染模式。字体是 Windows 11 系统组件；不要把 `SegoeIcons.ttf` 随应用分发（在 Windows 11 之前的系统上，面板只会回退为缺字形方框，而应用并不以这些系统为目标）。

## 6. 验证证据

由 `IconGen.exe all` 生成并使用图像查看器检查：

* `previews/glyphs-large.png` —— 42 个候选字形，48 px，带名称/分类。
* `previews/glyphs-zoom-20px.png` —— 候选字形按面板实际尺寸（20 px，Ideal + 灰度抗锯齿）渲染，4 倍放大，覆盖两种面板表面。
* `previews/glyphs-chosen-20px.png` —— 上述七个常量，实际 20 px + 3 倍放大，覆盖两种面板表面。
* `previews/tray-mark-sizes.png` —— 最终托盘标记 16-128 px，在 `#F3F3F3` / `#1C1C1E` 上。
* `previews/tray-taskbar-actual.png` —— 实际尺寸 16/20/24 px 图标在浅色/深色任务栏条上重复展示（预览未缩放，因此就是任务栏实际显示效果）。
* `previews/tray-mark-magnified.png` —— 16/24/32 px ICO 位图的 6 倍最近邻放大。
* `previews/tray-ico-frames.png` —— 从两个托盘 `.ico` 文件解码回来的帧。
* `previews/Watt-ico-preview.png` —— `Watt.ico` 从 256 px 到 16 px 的帧，浅色/深色，包括 16 px 帧的 8 倍放大；`previews/Watt-256.png` 是 256 px 母版。
* `previews/art-candidates.png`、`previews/art-app-tiles.png` —— 选定 `Ticks2Crisp`（电池轮廓 + 2 条电量刻度、像素网格对齐）和强调蓝色块的设计对比。

生成托盘图标时打印的像素审计（`tray-dark.ico` 数字相同，墨色为 `#FFFFFF`）：

```
audit tray-light.ico 16 px: 104 ink px, 152 transparent px, non-pure ink = 0 (colour #000000, min alpha 6)
audit tray-light.ico 20 px: 162 ink px, 238 transparent px, non-pure ink = 0 (colour #000000, min alpha 3)
audit tray-light.ico 24 px: 194 ink px, 382 transparent px, non-pure ink = 0 (colour #000000, min alpha 2)
audit tray-light.ico 32 px: 292 ink px, 732 transparent px, non-pure ink = 0 (colour #000000, min alpha 21)
audit tray-light.ico 40 px: 524 ink px, 1076 transparent px, non-pure ink = 0 (colour #000000, min alpha 7)
audit tray-light.ico 48 px: 666 ink px, 1638 transparent px, non-pure ink = 0 (colour #000000, min alpha 1)
verify tray-light.ico: 6 frames [48, 24, 40, 32, 20, 16] all expected sizes OK
verify tray-dark.ico: 6 frames [48, 24, 40, 32, 20, 16] all expected sizes OK
verify Watt.ico: 8 frames [48, 40, 32, 64, 24, 20, 16, 256] all expected sizes OK
```

`non-pure ink = 0` 表示每个可见像素都精确为 `#000000` / `#FFFFFF`；唯一变化是抗锯齿产生的逐像素 alpha（没有灰色光晕、没有彩色边缘）。因此生成的托盘图标会跟随任务栏主题，而不会自带灰色像素。

## 7. 应用嵌入说明

* `Watt.ico` 无需改代码——现有 `Watt.csproj` 的 `ApplicationIcon` 项会自动拾取，它也会成为可执行文件的窗口/任务栏图标。
* 把 `tray-light.ico` / `tray-dark.ico` 作为 `Resource` 项嵌入（例如 `/Assets/tray-light.ico`、`/Assets/tray-dark.ico`），或复制到 exe 旁边。选择与当前 DPI 匹配的帧，并根据 `SystemUsesLightTheme` 选择墨色：
  * WinForms `NotifyIcon`：`new System.Drawing.Icon(stream, SystemInformation.SmallIconSize)` —— System.Drawing 会选择最接近的帧，16/20/24 px 帧覆盖 100–150 % 缩放（150 % 下必要时加 `new Icon(icon, new Size(24, 24))`）。
  * WPF/HwndSource 托盘通知区域：用 `IconBitmapDecoder` 解码，选择 `PixelWidth` 最接近 `16 * dpiScale` 的帧。所有帧都是透明背景且形状一致，因此按最接近尺寸选择是安全的。
* 面板只需要 `PanelView.xaml` 中已使用的 `FontFamily="Segoe Fluent Icons"` 和 `Glyphs.cs` 中已验证的码点。
