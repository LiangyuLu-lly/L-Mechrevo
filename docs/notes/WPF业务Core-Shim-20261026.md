# WPF 业务 Core 兼容 Shims（task-12 · 2026-10-26）

> 目标：让 `source/MechrevoLite.Core.csproj` 从 **97 错误 / 179 警告** 收敛到 **0 错误**，为 WPF 改引用 Core 铺路。
>
> 约束遵守：只改 `source/MechrevoLite.Core.csproj`、新增 `tools/core-shims/`、本文档；**未改** `source/` 任何现有源码、未改 WPF、未改旧 `L-Mechrevo.csproj`、未打包、未 kill 用户进程。

---

## 1. 新增/改动文件

| 文件 | 类型 | 说明 |
|---|---|---|
| `tools/core-shims/CoreUiShims.cs` | 新增 | 旧 UI 入口/窗体的 Core 兼容 shim（命名空间 `MechrevoLite` / `.Helpers` / `.Mode`） |
| `source/MechrevoLite.Core.csproj` | 改动 | ① 显式 Link 编入 `CoreUiShims.cs`；② 纳入旧入口依赖的 `MechrevoLite\AsusACPI.cs`（纯 Win32/业务类型，非窗体） |
| `docs/notes/WPF业务Core-Shim-20261026.md` | 新增 | 本文档 |

未改任何业务源码；Core 仍是「显式 Include 业务目录 + 根文件」的探针工程。

---

## 2. 每个 shim 的职责/行为

### 2.1 `MechrevoLite.Program`（入口静态门面 shim）

- 提供业务实际引用的字段/属性：
  `acpi`、`hw`、`service`、`rgb`、`settingsForm`、`modeControl`、`toast`、
  `UiAuditMode`、`ReleaseVersion`、`ReleaseLabel`、`currentSource`、`PowerSource` 枚举。
- **纯逻辑保持原行为**：
  - `PerformanceKey()`：`(int)SystemInformation.PowerStatus.PowerLineStatus`（与旧入口一致）；
  - `ReleaseVersion`/`ReleaseLabel`：读 entry/自身程序集 `AssemblyInformationalVersion`；Label 保留完整串，Version 取 `+` 前段；
  - `currentSource`：简单的 `lastObservedSource` 后备（默认 `Barrel`）。
- **UI 入口动作在 Core 中安全 no-op**（未来由 WPF/UI 层实现并注册）：
  `StopVendorLightingIdleMonitorForBaolongda`、`ScheduleGpuOcShutdownGuard`、`ScheduleIdleBackendWarmup`、
  `RequestShutdownForUpdate`、`SetAutoModes`、`SetExternalLightingPowerAsync`、
  `RestoreKeyboardLightingAsync`、`SuspendLightingTemporarilyAsync`、`SyncKbPowerFromHardwareIfDiverged`。
- ⚠️ 该类同时解决 `Probe.dll` 全局 `internal Program` 的绑定歧义（原 CS0122 的根因之一）。

### 2.2 `MechrevoLite.Helpers.ToastForm`（通知 shim）

- `ShowFailure(string)` / `ShowNotice(string)`：只写 `Logger`，**不创建任何 WinForms 窗体**。
- 旧 ToastForm 的 UI 动画/计时器不进入 Core。

### 2.3 `MechrevoLite.SettingsForm`（UI 通知 shim）

- 提供业务实际调用的通知面：`VisualiseBattery(int)`、`VisualiseBatteryUnknown()`、
  `VisualiseBatteryFull()`、`VisualiseScreen(...)`，以及 `IsDisposed`/`InvokeRequired`/
  `Invoke(Action)`/`BeginInvoke(Action)`。
- 方法体为空/直接执行委托；**不创建控件**，也不承载任何业务判断。

### 2.4 `MechrevoLite.LightForm`（纯业务静态表 shim）

- 只补 `LightEffectColorMode(string)`，与旧实现**逐字一致**：
  `Single/Raindrop → 1`；`Breathing/Impact/ColorMarquee → 7`；其余 `0`。

### 2.5 `MechrevoLite.CustomModeForm`（纯业务范围计算 shim）

- 只补 `GpuOffsetUiRange(MechrevoHw?)`，与旧实现一致：
  - 默认 core `-900..2000`、mem `-1000..2000`；
  - 优先 `NvapiPStates.TryGetP0OffsetRange`；否则取 hw 的厂商范围；
  - 上下限非法时回退默认。
- 不包含任何 UI/控件逻辑。

### 2.6 `MechrevoLite.Mode.ModeControl`（业务协调器 shim）

- `SharedSmu`：与旧实现同样的进程内懒加载单例（`RyzenSmuService.Initialize(Assembly.GetExecutingAssembly())`，失败/未初始化返回 `null`）。
- `SyncExternalModeStatic(int)`：与旧实现逐字一致：`Modes.SetCurrent(mode)` + `AppConfig.Set("performance_mode", mode)`。
- 不包含 `SetModeLabel`/设置窗协调等 UI 部分。

---

## 3. 构建证据（0 错误）

```powershell
& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\source\MechrevoLite.Core.csproj' -c Release --no-incremental
```

实测末尾：

```
186 个警告
0 个错误
已用时间 00:00:02.38
```

产物：

```
source\bin\MechrevoLite.Core\Release\net10.0-windows10.0.19041.0\MechrevoLite.Core.dll
```

> 错误收敛轨迹：97 错误（task-11 基线）→ 加入 Program/SettingsForm/ToastForm/LightForm/CustomModeForm/ModeControl shims 后 **0 错误**。
> 警告 179 → 186（+7，均为既有业务代码的 CS0649/CS0169 等；本任务未新增业务源码，不把告警数当验收门槛）。

---

## 4. 仍是 no-op / 风险点（必须在 WPF 切 Core 前处理）

1. **`Program` UI 门面 no-op**：
   - `SetAutoModes`、`SetExternalLightingPowerAsync`、`RestoreKeyboardLightingAsync`、`SuspendLightingTemporarilyAsync`、
     `SyncKbPowerFromHardwareIfDiverged`、`StopVendorLightingIdleMonitorForBaolongda`、`ScheduleGpuOcShutdownGuard`、
     `ScheduleIdleBackendWarmup`、`RequestShutdownForUpdate` 在 Core 中不执行；
   - 若 WPF 直接引用 Core 运行，这些“启动自动应用/灯效恢复/关机调度”不会发生 ⇒ **行为差异**；
   - 建议：抽 `IUiHost`/`ILightingHost` 接口，Core 调接口，WPF 实现并注册；在此之前 WPF 仍应引用旧入口或双写适配。
2. **`Program.settingsForm` 为 null**：
   - Core 中业务访问 `Program.settingsForm` 时会判空；WPF 需自己的 `ISettingsHost` 注入，不能依赖此字段。
3. **`SettingsForm` shim 的 `Invoke/BeginInvoke` 直接同步执行**：
   - 旧 UI 的线程切换语义不同；WPF 侧不要复用该 shim 做跨线程 UI 更新，应改用 Dispatcher。
4. **`ToastForm` 无实际弹窗**：
   - Core 里只写日志；WPF 需用自有 Toast/通知实现替换。
5. **`ModeControl.SharedSmu` 与旧单例不是同一个实例**：
   - 若同一进程同时加载旧 UI 与 Core，可能出现两份 SMU 单例（模块/句柄归属风险）；WPF 切 Core 后必须只保留一份。
6. **`ReleaseVersion` 取 entry assembly 的 InformationalVersion**：
   - WPF 入口需确保该属性存在；否则显示 `0.0.0`（不崩溃）。
7. **`AsusACPI` 已编入 Core**：
   - 它是旧入口依赖的 Win32 ACPI 门面；若未来要求 Core 纯无 UI，需按拆分文档 Phase 1 再迁或抽接口。
8. **警告 +7**：均为既有代码告警，无新增业务源码；若要求“警告不增”，后续可在 csproj 里按既有 `NoWarn` 白名单处理。

---

## 5. 下一步（WPF 切 Core 的前置条件）

1. **Phase 2 接口化**：把 `Program` 的 UI 动作（第 4 节第 1 条）抽成 `IUiHost`/`ILightingHost`，Core 只依赖接口；WPF 实现。
2. **Phase 2 状态类**：`Program.hw/service/acpi/settingsForm` → Core `AppState`（推荐改名，彻底消除 Probe.dll 的 `Program` 歧义）。
3. **Phase 3 通知/设置/模式**：
   - `ToastForm` → `IToastService`；
   - `SettingsForm` → `ISettingsHost`（或事件/回调）；
   - `ModeControl` → Core `ModeCoordinator` + UI 壳。
4. **WPF csproj 改 `ProjectReference`**：`L-Mechrevo.csproj` → `MechrevoLite.Core.csproj`；旧 WinForms 工程保留 Core 引用作过渡。
5. **验收**：Core 0 错误（本任务已达成）+ WPF 0 错误 + 旧工程 0 错误；再做 UI 功能回归（自动应用/灯效/Toast/SMU）。
