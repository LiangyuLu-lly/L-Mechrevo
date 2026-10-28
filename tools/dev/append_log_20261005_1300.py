# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 13:00] ✅ 删除自动切换功能后的三档全面验收通过（用户实测 8 次切换）+ 一个待生效残留观察
================================================================================
用户目测界面正常，日志实证（基线 67493 -> 67624 行）：
```
12:49:31  GpuSwitch UI: target=1 route=Restart current=2 gen=Gen40 allowsRestart=True pendingRestart=True seq=1
12:49:31  GpuRestartPrompt: seq=1 latest=1
12:49:32  MechrevoService.SwitchGpuMode(1) -> DGPU_DIRECT_CONNECT_TOGGLE_OFF, leavingDirect=True
12:49:33  GpuMode -> 1 (...TOGGLE_OFF...) / SwitchGpuMode confirmed=True expected=1 actual=1 elapsed=1512ms   ✅ 混合
12:49:36  seq=2 target=0 -> SwitchGpuMode(0) -> DGPU_DIRECT_CONNECT_TOGGLE_IGPU
12:49:39  confirmed=True expected=0 actual=0 elapsed=1211ms                                                  ✅ 核显
12:50:03  seq=4 target=2 -> SwitchGpuMode(2) -> DGPU_DIRECT_CONNECT_TOGGLE_ON
12:50:08  confirmed=True expected=2 actual=2 elapsed=5146ms                                                  ✅ 独显直连
12:50:28  seq=5 target=0 -> TOGGLE_IGPU / confirmed=True 1202ms                                              ✅
12:50:32  seq=6 target=2 -> TOGGLE_ON
12:50:38  SwitchGpuMode(2) superseded after 4957ms  <- 去重生效 ✅
12:50:38  confirmed=True elapsed=26ms
12:50:41  seq=8 target=2 route=NoChange -> confirmed=True 1205ms                                             ✅
```
结论：
- **三档（核显0/混合1/独显直连2）全部工作正常** ✅，全部 `confirmed=True` ✅
- **再也没有发出 IGPU_ONLY_CONNECT_RB_OFF** ✅（日志里的 `igpu=IGPU_ONLY_CONNECT_RB_OFF` 全是 GCU 回读）
- **重启提示去重正常** ✅：`GpuRestartPrompt: seq=1..8 latest=N` 每次只一条 ✅；
  并捕到一次 `superseded after 4957ms`（重复请求被正确取代 ✅）
- `GPUModeControl.cs` 被删 142 行（含热切换残留）后，按钮行文/高亮/提示均正常 ✅

【观察：一个"待生效"残留（无害，但语义不严谨）】
```
config: gpu_restart_pending=1, gpu_restart_target=2, gpu_restart_tick=34611875
NVRAM : @0x62=1 (amd-decoded=Direct)   <- 等于【当前实际路由】Direct
```
-> LM 因此仍显示「重启以应用」+ 棕色高亮在独显直连 ⚠️
-> 但实际上**没有待生效的变更**（NVRAM 已等于实际路由）—— 属**残留标记**
成因：用户先切核显(target=0) 再切回独显(target=2)，**都没重启**，NVRAM 跟着回到 1；
     而 `gpu_restart_pending` 只在"重启后校验"时才清（TryTakeRebootTarget 要求 RebootHappened）
建议的**小修**（可选）：
     `GpuRestartVerifier.PendingTarget` / `HasPendingGpuRestart` 增加条件 ——
     **当 NVRAM 待生效路由 == 实际路由时，视为没有待生效**（返回 -1 / false）
     这样"切过去又切回来"或"已生效"的情况就不会残留棕色 + "重启以应用"
     （我 12:00 那次修的是"NVRAM 与实际**不同**时以 NVRAM 为准"，这次是把"**相同**时视为无待生效"补齐）
- 待用户决定是否做这个小修
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
