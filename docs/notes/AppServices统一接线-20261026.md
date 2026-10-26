# AppServices 统一接线（2026-10-26，task-34 + task-36 最终版）

> 目标：8 个 `AppServices` 服务全部在 UI 主路径消费，消除“窗口直连 `BackendProbe`”与“服务层”双入口；不改文案/能力/下发顺序。
> 写作用域：`OriginalStyleWindow.xaml.cs`、`SettingsWindow.xaml.cs`、`Services/PerformanceService.cs`、`Services/ChargeLimitService.cs`（扩展接口/实现），本文档。
> 未改 `source/Core`、XAML、`MainWindow`、`AppServices`；未打包、未 kill 用户进程。
> 构建：`bin-alt/services36` ⇒ **0 错误**；非管理员隔离启动：宽屏/五页/设置均正常，无 XamlParse/Unhandled。

---

## 1. 最终状态（8/8 主路径已消费）

| 服务 | 最终状态 | 主路径接入点 | 保留直连/原因 |
|---|---|---|---|
| **PerformanceService** | **已接** | 模式切换 `:2469`（`SetModeResultAsync` 保留 `Superseded`）；托盘强冷 `:270`（`SwitchFanBoostRawAsync` 与原 GCU/Quick 路径 1:1）；当前模式 `:4066` | 无（自定义档/音效仍走各自业务类，非本服务职责） |
| **GpuService** | **已接** | 宝龙达切换 `:2901`（`SwitchOnBaolongda`）；pending 标记/清除 `:2895/2905/2932/2955/2959`；刷新 `:2951`；CanSwitch/SwitchAsync/RequestRestart 已在 task-34 接入 | `RefreshGpuModeAsync` 后的 `CurrentGpuMode` 仍读 probe（服务无对应只读属性所需精确时序；只读、无下发差异） |
| **ChargeLimitService** | **已接** | 阈值启停 `:2027/2044/2080`（扩展 `EnableThresholdRaw/DisableThresholdRaw`，保留 `TryEnable/allowInitialDischarge/SaveConfig` 原顺序）；滑杆/EC 直写 `:2116/2118/2140/2148/2152`（task-34） | 无（阈值校验/文案/放电实验仍在窗口，符合“服务只做 EC/配置”边界） |
| **DisplayService** | **主路径已接，部分高级路径保留** | 刷新率 `:408/3004`、亮度 `:2291`、自动刷新 `:3017` | 校色 ICC/色彩增强/LCD Overdrive/MiniLED/FHD/HDR 仍直连：涉及自定义 ICC、确认框、整页刷新/状态文案，服务方法只覆盖部分分支；强行替换会改下发顺序/文案，按“不能等价先保留”处理 |
| **LightingService** | **已接** | 所有通道效果（Keyboard/Lightbar/Logo/Hinge/Sync）经窗口包装 `Set*EffectServiceAsync` → 服务；所有通道电源 `SetLightPowerServiceAsync` → 服务；自动关灯 `:957/966`（task-34） | 通道设置落盘/GCU/HID 分流仍由窗口决定（服务方法只下发，1:1 包装）；属服务职责边界，不是双入口 |
| **BatteryService** | **已接** | 读档位 `:1029`；写档位 `:2985`（task-34） | 无 |
| **TrayMenuService** | **已接** | 设置页加载/保存 `SettingsWindow.xaml.cs:339/360`（task-34） | 目录遍历仍 `TrayMenuConfig.Catalog`（类型适配未改） |
| **BackendStatusService** | **已接** | 状态判断 `:3968/3970/3977/3982`（task-34） | 文案/tooltip 保持窗口原串 |

> “主路径已消费”指用户实际点击/展示路径走服务；各服务的边界内直接写控件/格式化仍属 View 职责。

---

## 2. 本次扩展的服务（仅 WPF Services，不改 Core）

### `Services/PerformanceService.cs`
- 接口新增：
  - `Task<MechrevoService.ModeSwitchResult> SetModeResultAsync(int mode)` —— 原样返回 `SwitchModeResultAsync` 的 `Ok/Failed/Superseded`，窗口保留 `Superseded` 早退/回读逻辑。
  - `Task<bool> SwitchFanBoostRawAsync(bool on)` —— 原样 `probe.SwitchQuickAsync("fanboost", on)`，**不**改宝龙达分支，保持托盘原行为。
- 原 `SetModeAsync`/`SetFanBoostAsync` 未改，供其他调用方使用。

### `Services/ChargeLimitService.cs`
- 接口新增：
  - `ServiceResult EnableThresholdRaw(int lowPercent, int highPercent)` —— 仅 `BatteryLimitWriter.TryEnable(..., allowInitialDischarge:true)`，成功/失败 detail 通过 `ServiceResult.Detail` 返回；**不**写 `BatteryLimitRestore.SaveConfig`（窗口保持原顺序）。
  - `ServiceResult DisableThresholdRaw()` —— 仅 `BatteryLimitWriter.TryDisable`。
- 原 `SaveThreshold/Disable` 未改。

### 窗口侧 Lighting 包装（`OriginalStyleWindow.xaml.cs`）
- 新增 6 个包装：
  - `SetKeyboardEffectServiceAsync` / `SetLightbarEffectServiceAsync` / `SetLogoEffectServiceAsync` / `SetHingeEffectServiceAsync` / `SetSyncEffectServiceAsync` / `SetLightPowerServiceAsync`；
  - 有 `_services` 时走 `ILightingService`，否则回退原 `_backendProbe` 直连；返回值仍为 `bool`，调用点/顺序/状态文案不变。
- 全工程替换：`_backendProbe.Set*EffectAsync`、`_backendProbe.SetLightPowerAsync` 调用点改为包装（共 20+ 处，见 §1）。

---

## 3. 关键改动行

### `OriginalStyleWindow.xaml.cs`
- `:270` 托盘强冷：`Performance.SwitchFanBoostRawAsync`
- `:408/3004` 刷新率：`Display.SetRefreshRateAsync`
- `:957/966` 灯光自动关灯：`Lighting.LightIdleSeconds/SetLightIdleSeconds`
- `:1029` 电池档位读取：`Battery.CurrentMode`
- `:2027/2044/2080` 充电阈值：`ChargeLimit.EnableThresholdRaw/DisableThresholdRaw`
- `:2116/2118/2140/2148/2152` 滑杆/EC 直写：`ChargeLimit.SetSliderEnabled/SetEcDirectWrite`
- `:2122/2131/2140/2149/2158` 灯光效果/电源包装
- `:2291` 亮度：`Display.SetBrightness`
- `:2469` 性能模式：`Performance.SetModeResultAsync`
- `:2901/2905/2932/2955/2959` 显卡宝龙达/pending：`Gpu.SwitchOnBaolongda/MarkRestartPending/ClearRestartPending`
- `:2951` 显卡刷新：`Gpu.RefreshAsync`
- `:2985` 电池写档：`Battery.SetModeAsync`
- `:3017` 自动刷新：`Display.SetAutoRefreshAsync`
- `:3968/3970/3977/3982` 后端状态：`BackendStatus`
- `:4066` 性能配置行：`Performance.CurrentMode`

### `SettingsWindow.xaml.cs`
- `:339/360` 托盘加载/保存：`TrayMenu.LoadEnabled/SaveEnabled`

---

## 4. 构建与启动验证

```powershell
& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\tools\dev\wpf-p0\WpfP0.csproj' -c Release --no-incremental -o 'D:\openLM\tools\dev\wpf-p0\bin-alt\services36'
⇒ 0 错误
```

非管理员隔离启动（`__COMPAT_LAYER=RunAsInvoker`、唯一实例、隔离 config/log，`last_window_mode=wide`）：
- 宽屏启动成功（`L-Mechrevo · 宽屏`）；
- 五页 `NavHome/NavScreen/NavLighting/NavCustom/NavSwitches` 逐一点击，进程存活；
- 设置页打开（「设置」），托盘配置走 `TrayMenu` 服务；
- 日志无 `XamlParseException`/`UnhandledException`/`NullReferenceException`；
- 未写硬件、未真实切电源；测试实例已关闭，用户进程未触碰。

---

## 5. 剩余风险

1. **Display 高级路径仍直连**：校色/色彩增强/LCD/MiniLED/FHD/HDR 含 ICC 自定义、确认框、整页刷新与状态文案；服务现有方法不能保证下发顺序/文案 1:1。后续若要收敛，需给 `IDisplayService` 增加“Core”方法（只做 probe 下发，配置/UI 留在窗口）再替换。
2. **Performance 强冷双通道**：服务里仍保留 `SetFanBoostAsync`（支持宝龙达）与新增 `SwitchFanBoostRawAsync`（原 GCU/Quick）；托盘用 Raw 保持原行为，其他调用方不要混用。
3. **ChargeLimitRaw 不写 config**：窗口仍负责 `BatteryLimitRestore.SaveConfig`；调用方必须保持此顺序，否则限充恢复会丢配置。
4. **Lighting 包装 fallback**：`_services == null` 时回退直连，保证后端 attach 前不崩；服务方法会吞异常返回 false，原直连多数在 try/catch 中，状态表现一致。
5. 未改 `MainWindow`/`Pages`：宽屏展示仍经 `Wide*` 门面，服务消费在窄窗/设置页；如需彻底消除门面内直连，下一阶段再做。
