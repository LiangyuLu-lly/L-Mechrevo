# AppServices 统一接线（2026-10-26）

> 任务：task-34。把 `AppServices` 中 8 个未被 UI 消费的服务接入窗口，消除“窗口直连 `BackendProbe`”与“服务层”双入口。
> 写作用域：`OriginalStyleWindow.xaml.cs`、`SettingsWindow.xaml.cs`（`MainWindow.xaml.cs`/`MainWindow.Pages.cs`/`AppServices.cs` 未改）。
> 未改 `source/Core`、XAML、资源；未打包、未 kill 用户进程。

---

## 1. 结论

**已接入 3 个服务（Battery / BackendStatus / TrayMenu）、部分接入 5 个（Performance / Display / ChargeLimit / Lighting / Gpu）。**
所有替换均保留原 `BackendProbe` 调用作为 `_services == null` 回退（服务在后端 attach 后创建）；不改文案/能力/事件，不改 UI 结构。

| 服务 | 状态 | 已接调用（file:line） | 未替换的直连/原因 |
|---|---|---|---|
| **PerformanceService** | 部分 | `OriginalStyleWindow.xaml.cs:4066` `RefreshPerfConfigVisibility(_services?.Performance.CurrentMode ...)`（仅只读当前模式） | 模式切换仍走 `SwitchModeResultAsync`：`SetModeAsync` 丢弃 `ModeSwitchResult.Superseded`（窗口对该结果有专门早退/回读逻辑，换服务会改行为）；托盘强冷仍直连 `SwitchQuickAsync("fanboost")`（服务 `SetFanBoostAsync` 增加宝龙达分支，非等价） |
| **DisplayService** | 部分 | 刷新率 `:406/2919`、亮度 `:2211`、自动刷新率 `:2932` | 校色/色彩增强/LCD Overdrive/MiniLED/FHD/HDR 仍直连：窗口含自定义 ICC、确认框、整页刷新与状态文本，服务方法只覆盖部分分支，非 1:1 |
| **BatteryService** | 已接 | 读当前档 `:1029`；写档位 `:2900` | 电池文案格式化仍在窗口（服务只给硬件字段，符合设计） |
| **ChargeLimitService** | 部分 | 滑杆开关 `:2118/2140`、EC 直写 `:2116/2148/2152`（`SetSliderEnabled`/`SetEcDirectWrite`） | 阈值启用/禁用仍直连 `BatteryLimitWriter.TryEnable/TryDisable`：窗口有 `TryReadChargeThreshold` 校验、`BatteryLimitRestore.SaveConfig`、放电实验/写回满充与状态文案，服务 `SaveThreshold/Disable` 细节文案与 EC 实验分支不覆盖 |
| **LightingService** | 部分 | 自动关灯秒数 `:955`（get）、`:964`（set） | 各通道灯效/电源/亮度/颜色仍直连 `BackendProbe`/`LightingSettingsStore`：服务仅封装部分通道（GCU/HID 分流、通道能力、写入回退在窗口） |
| **GpuService** | 部分 | `CanSwitch` `:2797`、`SwitchAsync` `:2864`、`RequestRestartAsync` `:2843` | 宝龙达分支 `SwitchGpuModeBaolongda` + `MarkGpuRestartPending` 仍直连：服务 `SwitchOnBaolongda` 不负责窗口的对话框/pending 标记；`RefreshGpuModeAsync` 仍直连 |
| **TrayMenuService** | 已接 | 设置页加载 `SettingsWindow.xaml.cs:339`、保存 `:360` | 目录遍历仍用 `TrayMenuConfig.Catalog`（服务 `Catalog` 是 `TrayMenuItem`，标签/顺序同源；仅类型适配未改，行为不变） |
| **BackendStatusService** | 已接 | `OriginalStyleWindow.xaml.cs:3968/3970/3977/3982`（非 GCU/已连接/重连判断） | 状态文案/tooltip 保持窗口原字符串（服务只提供布尔状态），无行为变化 |

---

## 2. 改动文件与关键行

### `tools/dev/wpf-p0/OriginalStyleWindow.xaml.cs`
- `:406` `WideSetRefresh`：`_services.Display.SetRefreshRateAsync(hertz)`，异常/空服务回退 `_backendProbe.SwitchRefreshRateAsync`。
- `:955/964` 灯光自动关灯：`_services.Lighting.LightIdleSeconds` / `SetLightIdleSeconds(seconds)`。
- `:1029` 电池档位读取：`_services.Battery.CurrentMode`。
- `:2116/2118/2140/2148/2152` 充电滑杆/EC 直写开关：`_services.ChargeLimit.SetSliderEnabled/SetEcDirectWrite`。
- `:2211` 亮度：`_services.Display.SetBrightness(value)`。
- `:2797/2843/2864` 显卡：`_services.Gpu.CanSwitch/SwitchAsync/RequestRestartAsync`。
- `:2900` 电池档位写入：`_services.Battery.SetModeAsync`。
- `:2919/2932` 刷新率/自动刷新：`_services.Display.SetRefreshRateAsync/SetAutoRefreshAsync`。
- `:3968/3970/3977/3982` 后端状态判断：`_services.BackendStatus`。
- `:4066` 性能配置行可见性：`_services.Performance.CurrentMode`（只读）。

### `tools/dev/wpf-p0/SettingsWindow.xaml.cs`
- `:339` 托盘勾选加载：`_owner.Services.TrayMenu.LoadEnabled()`，空服务回退 `TrayMenuConfig.Load()`。
- `:360` 托盘勾选保存：`_owner.Services.TrayMenu.SaveEnabled(selected)`，空服务回退 `TrayMenuConfig.Save(selected)`。

### 未改
- `MainWindow.xaml.cs`、`MainWindow.Pages.cs`、`AppServices.cs` 未改；`OriginalStyleWindow.Services` 属性原已存在（`:6449`），无需新增注入。
- 所有 XAML、资源、`source/` 未改。

---

## 3. 构建

```powershell
& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\tools\dev\wpf-p0\WpfP0.csproj' -c Release --no-incremental -o 'D:\openLM\tools\dev\wpf-p0\bin-alt\services34'
⇒ 0 错误
```

---

## 4. 非管理员隔离启动验证

- 环境：`__COMPAT_LAYER=RunAsInvoker` + `L_MECHREVO_WPF_INSTANCE=services34-verify` + 隔离配置（`last_window_mode=wide`、`onboarding_version=1`、`log_level=all`）/隔离日志；配置为真实 config 副本，未动用户配置。
- 实测：
  - 宽屏启动成功（`L-Mechrevo · 宽屏`），进程 Responding；
  - 五页 `NavHome/NavScreen/NavLighting/NavCustom/NavSwitches` 逐一点击，进程存活；
  - 设置页打开（窗口「设置」），托盘勾选加载路径走 `_owner.Services.TrayMenu`；
  - 日志无 `XamlParseException`/`UnhandledException`/`NullReferenceException`；仅预期非管理员 SMU/MQTT 提示；
  - 未真实切电源/不写硬件；测试实例验证后关闭，用户进程未触碰。

---

## 5. 剩余风险

1. **Performance 模式切换/强冷仍直连**：服务 `SetModeAsync` 丢失 `Superseded` 语义、`SetFanBoostAsync` 改变宝龙达路径；要接需先扩展服务接口（保留 `ModeSwitchResult`/`FanBoost` 能力位），属后续任务。
2. **ChargeLimit 阈值启用/禁用仍直连**：涉及 `BatteryLimitWriter`/`BatteryLimitRestore`/放电实验/满充写回和大量窗口状态文案；服务 `SaveThreshold/Disable` 只覆盖基础分支。
3. **Lighting 通道灯效/电源仍直连**：服务只封装 idle 与部分 probe 方法，GCU/HID 通道能力/回退仍在窗口。
4. **Display 校色/色彩增强/显示特性仍直连**：涉及 ICC 自定义与 UI 确认框，非纯服务可等价。
5. **Gpu 宝龙达分支/pending 仍直连**：服务 `SwitchOnBaolongda` 不含窗口的 dialog/pending 语义。
6. 本次未改 `MainWindow`/`Pages`：宽屏展示层仍经 `OriginalStyleWindow.Wide*` 门面，服务消费在窄窗/设置页完成；如需彻底消除双入口，后续可把 `Wide*` 门面内部全部改为服务调用（需逐项验证）。
