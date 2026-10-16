# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 12:35] ✅ 方案 A 执行完毕并部署：删除「自动切换显卡模式」(AUTO/gpu_auto/mode 3)
================================================================================
用户选择方案 A：连自动切换功能一起删，并清理 gpu_auto 老配置键。

【执行方式】委派子代理按精确清单执行（跨 6 文件、10+ 处、含 expectedAutoRuntime 联动链），
            子代理已在其报告中给出逐文件清单；主代理负责复查 + 部署 + 验收。

【净减 238 行，逐文件】
| 文件 | 行数变化 | 要点 |
|---|---|---|
| MechrevoService.cs | 3534→3473 (-61) | 删 `GpuAuto=3` 常量、`CreateGpuModePayload` 的 `3 =>`(=IGPU_ONLY_CONNECT_RB_AUTO)、
  `SwitchAutomaticGpuMode`、`CreateGpuRestartTargetPayloads` 的 `case 3:`(-21)、`SwitchGpuMode` 的 `pluggedForAuto` 参数、
  `expectedAutoRuntime` 整链（confirmationAttempts 固定 7、删 CurrentGpuMode==3 的 AUTO 接受块与专属日志、TargetReached 的 auto 分支）、
  两处范围检查 `mode > 3` -> `mode > 2` |
| Program.cs | 2036→2025 (-11) | ①GpuModeChanged 删 gpu_auto 覆盖与 Set，改为**一次性迁移 `AppConfig.Remove("gpu_auto")`**
  ②RestoreAfterHardwareConnection 删 AutoGPUMode(250) 分支 ③AutoSetting 删 AutoGPUMode(1000) 分支 |
| SettingsForm.cs | 10675→10663 (-12) | 删 RefreshDeviceCapabilities 里的 gpu_auto 块（连带死变量 flag20）、
  ShowGpuRestartPromptAsync 两处 previousAuto 保存/置0/恢复、Resolve 去掉 GpuSwitchResult 实参 |
| GPUModeControl.cs | 496→354 (-142) | InitGPUMode/SetGPUMode 的 gpu_auto 逻辑全删、删整个 `AutoGPUMode`、
  **删 `TryApplyAutomaticHotSwitchAsync`（热切换残留！）**、删只被它调用的 `ScheduleGpuEco` |
| GpuSwitchPolicy.cs | 52→40 (-12) | 删只服务「上报模式=3(AUTO) 反推」的 `ResolveCurrentMode`，`Resolve` 去掉 automaticRuntime |
| ModelScopedConfig.cs | 1 行改 | ExactMachineScopedKeys 去掉 "gpu_auto"（string[4] -> string[3]） |

【主代理复查（全部通过）】
- `gpu_auto` 全仓只剩迁移语句 `Program.cs:541 AppConfig.Remove("gpu_auto");` ✅
- `AutoGPUMode` / `expectedAutoRuntime` 只剩说明注释 ✅
- `SwitchAutomaticGpuMode` / `pluggedForAuto` / `GpuAuto` = 0 处 ✅
- **性能模式未被误碰** ✅：`OperatingMode == 3` 6 处、`AutoPerformance` 5 处、
  `pendingPerformanceMode == 3` 1 处 —— 数量不变 ✅
- 编译 0 错误 ✅（子代理另做了 -t:Rebuild 全量重建复核，1178 条警告全是项目既有 ✅）

【部署与验收】
- 产物 1928704 字节 @12:29:38；替换完成 12:29:38；LM PID 6008 Responding=True ✅
- 启动**无任何异常**日志 ✅
- **`gpu_auto` 迁移已执行**：config.json 里该键**已不存在** ✅（老配置幽灵行为清除）
- 日志基线 67493 行（tools\\_clean3_marker.txt）—— 待用户点三档验证

【⚠️ 子代理标出的唯一行为差异点（E），待用户定夺】
`GpuSwitchPolicy.ResolveCurrentMode` 被删 -> 若某台设备**当前仍上报 AUTO(3)**（老状态），
`Resolve` 不再按 `GpuSwitchResult`(1/2) 把 3 反推成 0/1，而是直接走 **Restart**（弹重启框）。
对可选目标 0/1/2 的其它情形结果完全一致（target=3 已不可达）。
- 本机 `gpu_mode` 从未为 3（配置里 =0/1/2），**本机无影响** ✅
- 若要兼容「仍停在上报 3 的老机器」的显示，可回补这段**只读反推**逻辑（不恢复功能本身）

【有意保留未动（均属设备状态解析或独立功能）】
- `MechrevoHw.CanSwitchGpuMode` 的 `3 => SupportsIgpuOnly`、`ResolveGpuModeStatus`/`ResolveIgpuOnlySaveMode`
  把 `...RB_AUTO` 解析为 3、`RefreshGpuModeStatus`/`GPUModeControl` 里的 `<= 3` 回读与 `case 3:`
- `IgpuOnlySemantics.AutomaticRuntime`（已无调用者，同文件另 3 个方法本来也无调用者）
- `GpuSwitchPolicy.ShouldDeferUntilGcuConnection`、`GPUModeControl.IsPlugged()`、`GPUModeControl.SetGPUMode(int)`
  （已无调用点，作为 legacy 保留）
- `DisplayRouteMatrix.IgpuOnlyAuto`（核显省电 `SetIGpuOnlySaveMode("AUTO")` 仍在用 ✅）
- **核显省电链与全部性能模式代码：完全未碰** ✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
