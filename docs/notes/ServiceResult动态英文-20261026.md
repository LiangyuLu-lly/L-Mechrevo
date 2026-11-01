# ServiceResult 动态英文收口（task-38 · 2026-10-26）

> 任务：把 `ServiceResult.Detail`（ChargeLimit / Display / Lighting / Performance / Gpu 等）与 `OriginalStyleWindow` / `SettingsWindow` 剩余交互状态串、`OverlayWindow` 电源来源全部改走 `WpfStrings`；中文保持原文。
> 写作用域：`tools/dev/wpf-p0/Services/*Service.cs`、`OriginalStyleWindow.xaml.cs`、`SettingsWindow.xaml.cs`、`OverlayWindow.xaml.cs`、`Resources/WpfStrings.*`、本文件。
> ⛔ 未改 `source/**`（Core）、未改 MainWindow、未打包、未 kill 用户进程。

---

## 1. 结论速览

| 项 | 数值 |
|---|---:|
| 新增资源键 | **196**（中性 `WpfStrings.resx` 英文 / `WpfStrings.zh-CN.resx` 中文原文 / `WpfStrings.cs` 属性各 196） |
| 资源总键数 | 930（中性/zh-CN 各 930；中性 resx 中文 value = **0**） |
| 占位符一致性 | 与 zh-CN 逐 key 对比不一致 **0** |
| `ServiceResult.*` 行内中文残留 | **0** |
| scoped 文件 `WpfStrings.*` 引用 | 499 处 |
| 构建 | **0 错误 / 144 警告**（输出 `bin-alt/en38`） |
| en-US 隔离启动 | 五页 + 设置 **CJK=0**；设置 StatusText 实测英文 |
| zh-CN 回归 | 页面/设置保持中文；状态串中文原文不变（实测「主题色已恢复默认」） |

改动统计（仅 scoped 12 文件）：

```text
OriginalStyleWindow.xaml.cs      274 +/-
OverlayWindow.xaml.cs             15 +/-
Resources/WpfStrings.cs          202 +
Resources/WpfStrings.resx        603 +
Resources/WpfStrings.zh-CN.resx  603 +
ChargeLimitService.cs             27 +/-
DisplayService.cs                 54 +/-
LightingService.cs                39 +/-
PerformanceService.cs             27 +/-
GpuService.cs                     19 +/-
ServiceModels.cs                   9 +/-
SettingsWindow.xaml.cs            78 +/-
12 files changed, 1688 insertions(+), 262 deletions(-)
```

---

## 2. 做法

1. 从 scoped 文件抽取**带中文的字面量 chunk**（排除注释；日志行单独标记，不当作 UI）。
2. 先与现有 zh-CN resx 的 value 做精确匹配：**复用 32 个既有键**（如「后端未连接」→`OriginalStyleWindow_xaml_Text_51`、「静音/狂暴/平衡/自定义」→`OriginalStyleWindow_Text_23/22`、`CustomModeWindow_Content_13`、`NavCustom_Content`、「强冷回读确认」→`QuickFanBoostOnConfirmed` 等），避免重复造键。
3. 未匹配的 196 个 chunk 新增键：**zh-CN value = 原字面量逐字**（含空格/全角括号/`\n`/百分号），中性 value = 人工英文；`WpfStrings.cs` 同步加属性。
4. 代码中按「最长 chunk 优先」精确替换 `"chunk"` → `WpfStrings.Key`；**拼接表达式结构不变**（如 `"停充 " + high + "% / 起充 "` 变成 `Key + high + Key`，中文输出逐字等价）。
5. `OverlayWindow` 电源来源不再依赖 Core 的本地化字符串：按 `PowerInputKind` 枚举在 WPF 侧映射新键（Core 未改）。

---

## 3. 改动明细

### 3.1 Services（ServiceResult.Detail / 标签）

| 文件 | 改动 |
|---|---|
| `ChargeLimitService.cs` | 阈值保存/关闭、EC 直写、无极限充滑块、异常/失败/后端未连接等 Detail 全部资源化（例：`ChargeUnsupported`、`ChargeRangeInvalid`、`ChargeOrderInvalid`、`ChargeSliderOn/Off`、`ChargeEcDirectOff`、`ChargeEcAuto`、`ChargeEnableExceptionPrefix`、`ChargeDisableExceptionPrefix`、`ClosingParen`） |
| `DisplayService.cs` | 亮度/刷新率/自动刷新率/过驱/校色/色彩增强的 Detail、选项标签（`SvcCalibrationDefault` 类复用既有键）与异常串全部资源化（`DisplayBrightnessSet/Unconfirmed`、`DisplayRefreshSent/Failed`、`DisplayCalibrationOn/Off/IccMissing`、`DisplayColorEnhancementOn/Off`、`DisplayColorProfile*` 等） |
| `LightingService.cs` | 自动关灯/离电关灯、键盘灯电源、通道灯效下发（键盘/灯带/Logo/铰链/同步）、异常/空值/失败后缀全部资源化（`LightAutoOff*`、`LightChannel*`、`LightKeyboard*`、`LightPower*`、`IsEmptySuffix`、`SentSuffix`、`ApplyFailedSuffix`、`ExceptionPrefix`） |
| `PerformanceService.cs` | 模式下发结果（`PerfModeSent/Failed/Superseded/ExceptionPrefix`）、强冷（`FanBoostOn/Off/Failed/ExceptionPrefix` + 既有 `QuickFanBoost*`） |
| `GpuService.cs` | 宝龙达/GCU 显卡模式、重启切换结果与异常（`GpuModeSent/Failed/ExceptionPrefix`、`GpuTargetModeRestart`、`GpuSwitchUnconfirmed`、`GpuRestartExceptionPrefix`、复用 `OriginalStyleWindow_xaml_Text_39/40/41`） |
| `ServiceModels.cs` | 性能模式显示名（`PerformanceModeLabel`）改走既有 `OriginalStyleWindow_Text_23/22`、`CustomModeWindow_Content_13`、`NavCustom_Content` |

### 3.2 `OriginalStyleWindow.xaml.cs`

- 校色/色彩增强开关与档位、刷新率、显卡模式、GPU 超频、电池档位、水冷泵/风扇/灯色、灯光通道、悬浮窗开关等 **StatusText 状态串**全部资源化。
- iGPU 省电安全提示（弹出框/警告/继续确认）与阈值校验错误资源化（`IgpuBlock*`、`IgpuWarning*`、`IgpuConfirmContinueSuffix`、`ChargeStartRangeError/ChargeStopRangeError`、`ChargeAppliedPrefix`、`ChargeNotEnabled`）。
- 灯光编辑器标题 `Logo 灯光/灯带灯光/铰链灯光/同步灯光` → `LightTitle*`。
- 文件对话框过滤器 → `ImageFileFilter` / `ZipFileFilter`。
- **快捷磁贴目录名（84 处）**：不再硬编码中文，改为 `QuickSwitchService.Label("key", "中文原文")`（en 显示英文键值，zh 保留原中文 fallback；列表/顺序/逻辑不变）。
- 电源来源回退（`电池供电`/`外接电源（EC …）`）→ `OverlayBatteryPower` / `DisplayPowerEcHexFormat` / `DisplayPowerEcWattFormat`。

### 3.3 `SettingsWindow.xaml.cs`

`Status(...)` 全部状态串资源化，涵盖：加载异常、悬浮窗状态/位置、主题色保存/恢复、语言保存、日志级别、开机自启、水冷菜单、核显模式/灯带/Logo 行显示、背景图应用/清除、备份导出/导入、显示响应加速、在线更新禁用/检查更新/最新版本、反馈/链接/许可证打开失败、托盘图标传感器开关（`StartupEnabled/Disabled`、`DisplayResponseOn/Off`、`TraySensorOn/Off`、`AccentSaved/Reset`、`LanguageSaved`、`WideBackground*`、`BackupExported*`、`SettingsImported`、`UpdateLatestPrefix` 等）。文件对话框过滤器同资源键。

### 3.4 `OverlayWindow.xaml.cs`（电源来源）

```csharp
string text = kind switch
{
    MechrevoLite.Hardware.PowerInputKind.Barrel        => string.Format(WpfStrings.OverlayBarrelWattsFormat, watts),
    MechrevoLite.Hardware.PowerInputKind.TypeC         => WpfStrings.OverlayUsbCPower,
    MechrevoLite.Hardware.PowerInputKind.BarrelAndTypeC=> WpfStrings.OverlayUsbCPower,
    MechrevoLite.Hardware.PowerInputKind.ExternalUnknown=> WpfStrings.OverlayExternalPower,
    _                                                  => WpfStrings.OverlayAcPower
};
```

- 新增 `OverlayBarrelWattsFormat`（`圆口 {0}W` / `AC power (barrel {0} W)`）、`OverlayBarrelPower`、`OverlayUsbCPower`、`OverlayExternalPower`。
- 中文 value 镜像 Core `Strings.PowerSource*`（`圆口 {0}W` / `圆口供电` / `Type-C 供电` / `外接电源`）；满充/充电中/放电中等沿用既有 `Overlay*` 键。
- 不再调用 `SettingsText.PowerSourceHeadline`（Core 原样未改）；枚举→资源映射无信息丢失。

### 3.5 中文保持原文

- 196 个新键的 zh-CN value 均为替换前字面量**逐字**（脚本写入，含全角括号/空格/`\n`/标点）。
- 拼接拆分为前缀/后缀键时保持顺序与原拼接等价（例：`限制充电已生效（停充 ` + high + `% / 起充 ` + low + `%）` → `ChargeAppliedPrefix + high + ChargeStartSeparator + low + "%" + ClosingParen`）。
- 中文布局零变化（未改 XAML 尺寸/字号/边距，仅替换字符串常量）。

---

## 4. 构建与验证

### 4.1 构建
```powershell
& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\tools\dev\wpf-p0\WpfP0.csproj' `
  -c Release --no-incremental -o 'D:\openLM\tools\dev\wpf-p0\bin-alt\en38'
# → 0 个错误 / 144 个警告（既有警告基线）
```

静态核对：
- 中性 `WpfStrings.resx` 中文 value = **0**；与 zh-CN 逐 key 占位符不一致 = **0**；cs 属性数 = 930。
- 6 个 service 文件中 `ServiceResult.*` 行含中文 = **0** 行。

### 4.2 en-US 隔离启动（非管理员、独立实例/配置/日志；未动用户配置）
`__COMPAT_LAYER=RunAsInvoker` + `L_MECHREVO_WPF_INSTANCE=en38-final-en` + 临时 config（`language=en-US`、`last_window_mode=wide`、`onboarding_version=1`、`log_level=all`）。

| 检查 | 结果 |
|---|---|
| 主窗 | `L-Mechrevo · Wide` |
| 五页导航 | 全部 clicked=true；可见命名控件 71/53/73/81/72；**CJK 均为 0**（含底部快捷磁贴/更多开关） |
| 设置窗 | `Settings`，73 个可见命名控件，**CJK=0** |
| 设置 StatusText | 点击 Default（恢复默认强调色）后实测 `0.293.6 beta23.6 · Accent color restored to default`，**CJK=0**（新键 `AccentReset`） |
| 异常 | 无 XamlParse/Unhandled；仅非管理员预期提示 |

### 4.3 zh-CN 回归（同程序、`language=zh-CN` 隔离配置）
- 主窗 `L-Mechrevo · 宽屏`；五页 clicked；页面/设置保持中文（CJK 46~64 为正常中文 UI）。
- 设置点「默认」后 StatusText = `0.293.6 beta23.6 · 主题色已恢复默认` —— 与替换前中文逐字一致。
- 快捷磁贴中文 fallback 保持原样（`触摸板/蓝牙/强冷/息屏（不睡眠）` 等）。

---

## 5. 剩余中文（非本轮 UI 状态，或设计保留）

| 位置 | 内容 | 原因 |
|---|---|---|
| `OriginalStyleWindow` 的 `Logger.WriteLine("[lid]/[power]/[startup]/[perfmode] …")` | 诊断日志中文 | 日志非 UI；按「只改展示」保留（en 下日志仍中文，诊断包内可读） |
| `MarkCleanExitForShutdown("窗口 Closed")` | 退出原因字符串 | 写入/日志用，非 StatusText |
| `SyncGpuDbForMode(mode, "WPF 切档/启动同步/后端模式同步")` | 同步原因字符串 | 内部 reason，非用户可见状态 |
| `LightingSettingsStore.SavePower(..., "用户点击「灯光」开关")` | 持久化 reason | 配置/诊断记录，非 StatusText |
| Dock 切换 `case "设置"/"悬浮窗"/"赞助"/"诊断"/"退出"` | Tag 匹配常量 | 逻辑判据（按钮 Tag 仍为中文，标签本身已资源化；改动会破坏点击匹配） |
| 快捷磁贴目录 `("touchpad", QuickSwitchService.Label(..., "触摸板"), …)` | 中文 fallback 参数 | 仅作为 zh 回退；en 实际显示英文（实测 CJK=0） |
| `MainWindow` / `CpuTuningWindow` / `CustomModeWindow` 等其它窗口 | 少量动态状态 | 不在 task-38 写作用域；建议后续按窗口批次收口 |
| `PeripheralsService.cs` 来源串「蓝牙」 | 外设来源 | task-19 范围；本轮未改（本次重点五服务） |

---

## 6. 未执行 / 风险

- **未做硬件写入操作**：充电阈值保存、刷新率/亮度下发、灯光下发、GPU 模式切换等会产生真实硬件动作，本轮仅做**安全操作**（切页、打开设置、恢复默认强调色）验证 UI/StatusText；这些按钮的状态串已全部资源化并由静态检查确认无中文，建议真机管理员回归时逐项点一次确认英文 StatusText。
- 未逐一截图英文长文本状态串（如 `Turn off lighting on battery enabled (applied on next lighting reconcile)`）；布局宽度未改，文字仅替换，长英文可能换行但不裁切（Q6 的 english long-layout 任务已处理窗口整体布局）。
- 用户实例/提权实例未触碰；本轮隔离实例均已结束。
- 验证脚本与临时配置/日志在 `%TEMP%\en38\`，未入库。

## 7. 复现

```powershell
# 构建
& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\tools\dev\wpf-p0\WpfP0.csproj' -c Release --no-incremental -o 'D:\openLM\tools\dev\wpf-p0\bin-alt\en38'

# en-US 隔离启动（独立实例/配置；示例）
$env:__COMPAT_LAYER='RunAsInvoker'
$env:L_MECHREVO_WPF_INSTANCE='en38-verify'
$env:LMECHREVO_CONFIG_FILE="$env:TEMP\en38\run-en2\config.json"
$env:LMECHREVO_LOG_DIR="$env:TEMP\en38\run-en2\log"
$env:LMECHREVO_LOG_LEVEL='all'
Start-Process 'D:\openLM\tools\dev\wpf-p0\bin-alt\en38\L-Mechrevo.WpfP0.exe'
```
