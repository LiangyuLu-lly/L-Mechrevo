# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 12:15] ✅ 第二类清理验收通过 + 用户澄清核显省电行与 mode 3 无关 + 挖出 mode 3 死代码
================================================================================
【验收：第二类清理（核显省电命令脱离三档）实测通过】
用户点了一次「混合模式」，日志（基线 67345 行 -> 67380 行）：
```
12:09:07.883  MechrevoService.SwitchGpuMode(1) -> DGPU_DIRECT_CONNECT_TOGGLE_OFF, leavingDirect=True   ★
12:09:09.398  GpuMode -> 1 (dgpu=DGPU_DIRECT_CONNECT_TOGGLE_OFF, igpu=IGPU_ONLY_CONNECT_RB_OFF)
12:09:09.398  SwitchGpuMode confirmed=True: expected=1 actual=1 action=DGPU_DIRECT_CONNECT_TOGGLE_OFF elapsed=1515ms
```
对比清理前（2026-10-03 12:52:24 记录）：`SwitchGpuMode(1) -> IGPU_ONLY_CONNECT_RB_OFF` ⚠️
-> **现在发 TOGGLE_OFF ✅，切换成功（confirmed=True）✅，全程没有发送 IGPU_ONLY_CONNECT_RB_OFF ✅**

⚠️ 唯一出现的 `IGPU_ONLY_CONNECT_RB_OFF` 在 **`GpuMode -> 1 (...)` 的 igpu= 字段**里 ——
   那是 **GCU 上报的 `IGpuOnlyConnectionSwitch_Status`（核显省电状态）** ✅，是**回读**不是发送 ✅，
   被 LM 解析为 `IgpuOnlySaveMode`（核显省电功能的正确用途）✅

【用户澄清（重要，更正我上一条的说法）】
「核显省电按钮做在这三个按钮下面一行的 判断逻辑是混合模式切支持核显省电 才会显示的
  不是上面三档按钮里面的AUTO」
-> 核显省电 = 三档**下面独立一行** ✅，显示条件 = `SupportsGpuHotSwap && GpuMode == 1
   && !HasPendingGpuRestart`（SettingsForm:3973/7547）✅
-> **不是**三档里的 mode 3/AUTO ✅ —— 我此前把两者混为一谈 ⚠️（已更正）

【顺带挖出：mode 3 / AUTO 整条线现在没有 UI 入口 = 死代码】
```
gpu_auto = 0（本机配置）
Program.cs:546   AppConfig.Set("gpu_auto", (logicalMode == 3) ? 1 : 0);   <- 选 mode 3 才置 1
Program.cs:538   if (AppConfig.Is("gpu_auto") && hw.SupportsIgpuOnly) ... <- 启动时据此决定自动切
Program.cs:883   if (AppConfig.Is("gpu_auto")) ... AutoGPUMode(...)
GPUModeControl.cs:444  SwitchAutomaticGpuMode(plugged)
```
- mode 3 的**唯一 UI 入口**是本轮删掉的 `ButtonOptimized_Click` / `menuOptimized` ✅
  -> **现在 mode 3 无法从界面选中** = 死代码 ✅
  涉及：`CreateGpuModePayload(3)`(IGPU_ONLY_CONNECT_RB_AUTO)、`CreateGpuRestartTargetPayloads(3)`、
        `SwitchAutomaticGpuMode`、`GPUModeControl.AutoGPUMode`、`gpu_auto` 键、
        `Program.cs:538/546/883`、`GPUModeControl:444`
- ⚠️ **行为风险**：`gpu_auto` 是**持久化配置键**，老版本用户可能已存 `=1`
  -> `Program.cs:538` 会在启动时读它 -> 那些用户会**继续按电源自动切显卡模式**
  -> 所以"删掉自动切换"对他们属**行为变更**（本机 gpu_auto=0，不受影响）
- 两个方案待用户选：
  A. 连自动切换功能一起删（同时清/迁移 gpu_auto 键，避免幽灵行为）
  B. 只删 mode 3 在"档位表"里的部分，保留 SwitchAutomaticGpuMode/AutoGPUMode 自动切换功能
- 注意：核显省电行的 `...RB_AUTO` 与 mode 3 的 `...RB_AUTO` **是同一个命令**，
  但核显省电行走的是 `SetIGpuOnlySaveMode` ✅ -> 删 mode 3 **不影响**核显省电行 ✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
