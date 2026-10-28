# 快捷开关英文动态标签收口（2026-10-26）

## 1. 背景与范围

task-31 列出的英文模式残留动态中文：**更多开关/底部快捷磁贴的 `强冷 · Off`、`触摸板 · On`、`触摸板切换键 · On`、`蓝牙 · Off` 等**。
根因：宽屏底部磁贴与「更多开关」磁贴的 label 来自迷你窗 `OriginalStyleWindow.QuickTileCatalog` / `WideMoreSwitches()` 的**硬编码中文 name**（`OriginalStyleWindow.xaml.cs:128-139`、`:868-...`），`MainWindow` 只拿到文本后拼 ` · StateOn/Off`，所以英文模式仍是中文名 + 英文状态。

本任务写作用域（严格未越界）：
- `tools/dev/wpf-p0/Services/QuickSwitchService.cs`
- `tools/dev/wpf-p0/MainWindow.xaml.cs`
- `tools/dev/wpf-p0/Resources/WpfStrings.cs` / `WpfStrings.resx` / `WpfStrings.zh-CN.resx`
- `docs/notes/quick-switch英文收口-20261026.md`（本文件）

未改 `OriginalStyleWindow.xaml.cs`（其它任务占用）、未改任何 Core 源码、未改逻辑/能力判断。

## 2. 改动

### 2.1 新增 WpfStrings 键（24 个，中性=英文 / zh-CN=原文）
`QuickTileToggled`、`QuickTileToggleFailed`、`MoreSwitchToggled`、`MoreSwitchToggleFailed`、`QuickSwitchedOnPrefix`、`QuickSwitchedOffPrefix`、`QuickBackendOffline`、`QuickUnsupported`、`QuickFanBoostBackendUnavailable`、`QuickFanBoostOnConfirmed`、`QuickFanBoostOffConfirmed`、`QuickFanBoostOnUnconfirmed`、`QuickFanBoostOffUnconfirmed`、`QuickResultOn`、`QuickResultOff`、`QuickUnconfirmed`、`QuickApplyFailed`、`IgpuSavingCurrentOn`、`IgpuSavingCurrentOff`、`IgpuSavingCurrentAuto`、`IgpuSavingCurrentUnknown`、`IgpuSavingCurrentUnsupported`、`AutoOption`。
- 中性 resx 用英文，zh-CN resx 值与原文逐字一致（如 `已开启：`、`强冷已开启（回读确认）`、`当前：开`、`自动`）。
- `WpfStrings.cs` 同步加同名属性（均走既有 `Get()`）。

### 2.2 `QuickSwitchService.cs`
- 新增 `internal static string Label(string key, string? fallback = null)`：按 key 从 WpfStrings 取当前语言显示名（31 个开关 key 全覆盖）。
  - **中文保持原文**：zh 下若调用方给了 label 就直接返回（迷你窗短名 `强冷/麦克风/息屏` 等保持原样）；没给 label 时沿用 key（旧的“快捷键+key”文案不变）。
  - 非 zh 下用资源译名；未登记 key 回退 label/key。
- 新增 `internal static string GroupTitle(string title)`：把后端未就绪时回退的硬编码中文组名（输入设备/键盘与热键/电源与系统/睡眠省电）映射到 `QuickGroup*` 资源；已是当前语言的组名原样返回。
- `SetAsync` 的下发结果/错误 Detail 全部资源化（`QuickBackendOffline` / `QuickUnsupported` / `QuickFanBoost*` / `QuickResultOn|Off` / `QuickUnconfirmed` / `QuickApplyFailed`），zh-CN 文案逐字不变；下发逻辑/能力判断零改动。

### 2.3 `MainWindow.xaml.cs`
| 位置 | 改动 |
|---|---|
| `:679` `QuickTile_Click` | 状态串改用 `QuickTileToggled/Failed` 资源；key 显示名走 `QuickSwitchService.Label` |
| `:689` `MoreSwitch_Click` | 同上，`MoreSwitchToggled/Failed` |
| `:702` `BuildMoreSwitchTiles` 组标题 | `QuickSwitchService.GroupTitle(group)` |
| `:713` 磁贴 label | `QuickSwitchService.Label(key, label)`（英文模式替换中文短名） |
| `:730` 点击后状态 | `QuickSwitchedOnPrefix/OffPrefix` + 本地化 label |
| `:749` `BuildQuickTiles`（底部快捷磁贴） | label 走 `QuickSwitchService.Label(key, label)`，state 文本与 `_quickTileButtons` 都存本地化 label |
| `:764` 磁贴状态文本 | 加 `TextWrapping=Wrap` + `TextAlignment=Center`：英文 label 更长（`Turn off display · Off`），避免底部 72px 磁贴裁字（中文单行不受影响） |
| `:1117-1122` `RefreshIgpuSaving` | `当前：开/关/自动/未知/机型不支持` → `IgpuSavingCurrent*` 资源 |
| `:1744/:1751` 水冷泵/风扇下拉 | `自动` → `AutoOption` 资源 |

## 3. 构建与运行验证

### 3.1 构建（0 错误）
```
& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\tools\dev\wpf-p0\WpfP0.csproj' -c Release --no-incremental -o 'D:\openLM\tools\dev\wpf-p0\bin-alt\qsen'
→ 0 个错误 / 144 个警告（均既有告警）
```

### 3.2 en-US 隔离启动（非管理员，独立实例 + 独立临时配置，未动用户配置）
`__COMPAT_LAYER=RunAsInvoker` + `L_MECHREVO_WPF_INSTANCE=qsen-accept-en2` + `LMECHREVO_CONFIG_FILE=%TEMP%\wpf_accept\qsen-en2-config.json`（内含 `language=en-US`、`onboarding_version=1`、`igpu_only_asked=1`）。

UIA 全量文本检查（证据 `%TEMP%\wpf_accept\qsen_verify_en2.txt`、`Q1_en2.png`、`Q2_en2.png`）：
- **首页：109 个名称，CJK=0**；底部磁贴显示 `Fan boost · Off`、`Webcam · On`、`Mic mute · Off`、`Win key lock · Off`、`Turn off display · Off`（原来 `强冷 · Off` 等）。
- **更多开关页：117 个名称，CJK=0**；分组 `Input devices` / `Keyboard & hotkeys` / `Power & system` / `Sleep & power saving`；磁贴 `Touchpad`、`WiFi`、`Bluetooth`、`Webcam`、`Mic mute`、`Numpad lock`、`Touchpad toggle`、`Fan boost`、`Turn off display`、`Disable Windows Update` … 全部英文。
- 截图确认底部磁贴两行换行后不裁字（`Turn off display · Off` 完整可读），更多开关页无裁字/布局回归。

### 3.3 zh-CN 回归（同一程序+隔离配置，`language=zh-CN`）
- 底部磁贴保持原文：`强冷 · 关`、`摄像头 · 开`、`麦克风 · 关`、`Win键锁 · 关`、`息屏 · 关`（证据 `qsen_verify_zh.txt`）。
- 更多开关页保持原文：`输入设备/触摸板/触摸板切换键/风扇强冷`、页脚 `更多开关 · 以磁贴形式排列的快捷开关`；逻辑/能力未变。

## 4. 剩余动态中文（不在本任务写作用域，如实列出）

| 位置 | 残留 |
|---|---|
| `MainWindow.xaml.cs:625-627,630` | `ApplyPage` 的页状态串：`首页 · 传感器 / 性能 / 显卡 / 显示`、`屏幕 · …`、`灯光 · …`、`就绪`（en 下切页时状态栏仍中文） |
| `MainWindow.xaml.cs:280-288` | 窄导航/侧栏 dock 的中英字面量（收起态的 `屏幕/灯光/自定义/悬浮窗/设置/赞助/诊断/退出`） |
| `MainWindow.xaml.cs:1284/1296/1311/1382/1489/2651/2667/2688/2733/2743` 等 | 校色/色彩增强/灯光/HID 编辑器的状态与错误串（非快捷开关，未收口） |
| `OriginalStyleWindow.xaml.cs:128-139`、`:868-...` | 迷你窗 `QuickTileCatalog` / `WideMoreSwitches` 回退目录的硬编码中文 name（宽屏已按 key 本地化；**迷你窗自身**磁贴仍是中文，文件被其它任务占用） |
| `OriginalStyleWindow.xaml.cs` 多处 `StatusText` | 校色/灯效/悬浮窗等中文状态（同上） |
| `Services/ChargeLimitService.cs:108-172`、`DisplayService.cs:134-303`、`LightingService.cs:241-307`、`PerformanceService.cs:133-150` | 服务层 `ServiceResult.Detail` 中文（会进 StatusText；不在本任务写作用域） |
| `SettingsWindow.xaml.cs:541/893/959-960` | 自启/响应加速/托盘传感器等中文状态 |
| `OverlayWindow.xaml.cs:112/156/172/177/183` | 悬浮窗 `充电中/已充满/未充电/放电中/外接电源` 等动态文案 |
| `CpuTuningWindow.xaml.cs:378`、`CustomModeWindow.xaml.cs:749` | 其它窗口动态状态 |
| `PeripheralsService.cs:129/223` | 外设电量来源串 `蓝牙`（task-19 范围；本次未改） |

> 以上均不在本任务写作用域（或属其它任务/其它窗口），本次未改；如需继续收口建议按窗口/服务分批任务化。

## 5. 复现/验证命令
```powershell
# 构建
& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\tools\dev\wpf-p0\WpfP0.csproj' -c Release --no-incremental -o 'D:\openLM\tools\dev\wpf-p0\bin-alt\qsen'

# en-US 隔离启动（独立实例/独立配置）
$env:__COMPAT_LAYER='RunAsInvoker'
$env:L_MECHREVO_WPF_INSTANCE='qsen-accept-en2'
$env:LMECHREVO_CONFIG_FILE="$env:TEMP\wpf_accept\qsen-en2-config.json"   # {"language":"en-US",...,"last_window_mode":"wide"}
Start-Process 'D:\openLM\tools\dev\wpf-p0\bin-alt\qsen\L-Mechrevo.WpfP0.exe'
```

## 6. 未确认点
- 仅验证了 en-US/zh-CN 两种语言；其它语言 resx 不存在，会走中性（英文）资源。
- 底部磁贴英文长标签采用两行换行；若后续增加更长译名可能仍需调整 tile 宽/字号（已截图确认当前 5 个默认磁贴不裁字）。
- 中文“原文保持”策略：zh 下优先沿用调用方 label（迷你窗短名与「更多开关」长名各自保留），仅状态前缀/结果/组名映射等新增资源在 zh 下与原文逐字一致。
