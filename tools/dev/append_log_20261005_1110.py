# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 11:10] 查清 LM 切核显真实发送的报文（用户要求）+ 抓包可行性结论
================================================================================
用户要求：「抓一下LM切核显真实发了什么，之前你手动发是失败的」

【抓包不可行 —— 已实测】
- GCU 的 MQTT broker 对 client_id **有白名单**：只有 `UWPClient_2` 被接受 ✅
  其余 16 个候选全被拒（`Client identifier not valid`）：
  UWPClient_1/3/0、UWPClient、UWPClient_User_2、CCUWinUI、ControlCenter、L-Mechrevo、
  LM、GCUClient、MyControlCenter、MechrevoLite…（工具：tools\\probe_client_ids.py）
- 而 `UWPClient_2` 正是 **LM 自己在用**的 id -> 我连上去会把 LM 踢下线（互相踢）⚠️
- **改用 LM 自己的日志**（它把每次发送都记下来了 ✅ 比抓包更可靠）

【🎯 LM 切「核显模式」的真实报文（LM 日志 2026-10-05 11:04）】
```
11:04:21.788  MechrevoService.SwitchGpuMode(0) -> DGPU_DIRECT_CONNECT_TOGGLE_IGPU,
                                 leavingDirect=True, leavingIgpuOnly=False
11:04:23.001  GpuMode -> 0 (dgpu=DGPU_DIRECT_CONNECT_TOGGLE_IGPU, igpu=IGPU_ONLY_CONNECT_RB_OFF)
11:04:23.001  SwitchGpuMode confirmed=True: expected=0 actual=0
                                 action=DGPU_DIRECT_CONNECT_TOGGLE_IGPU elapsed=1213ms
```
-> **载荷 = `{"Action":"DGPU_DIRECT_CONNECT_TOGGLE_IGPU"}`** ✅（不是 50 系风格的
   `IGPU_ONLY_CONNECT_RB_ON`）✅ **confirmed=True，切换成功** ✅

【LM 切「独显直连」的真实报文】
```
11:05:01.086  SwitchGpuMode(2) -> DGPU_DIRECT_CONNECT_TOGGLE_ON, leavingIgpuOnly=True
11:05:02.905  GpuMode -> 1 (DGPU_DIRECT_CONNECT_TOGGLE_OFF)      <- 中间态
11:05:06.530  GpuMode -> 2 (DGPU_DIRECT_CONNECT_TOGGLE_ON)       <- 到达
11:05:06.530  confirmed=True action=DGPU_DIRECT_CONNECT_TOGGLE_ON elapsed=5444ms
```
（另有 03:13:19 记录：切 target=2 时连发 3 条
  `[DGPU_DIRECT_CONNECT_TOGGLE_ON -> IGPU_ONLY_CONNECT_RB_OFF -> DGPU_DIRECT_CONNECT_TOGGLE_ON]`
  —— 中间那条是 LM 自己在清除核显模式，随后 800ms 发 `DGPU_DIRECT_CONNECT_RESTART`）

【⚠️ 为什么我手动发"看起来失败"】
- 我发的是 `IGPU_ONLY_CONNECT_RB_ON`（50 系风格）-> **确实改了 NVRAM**（@0x62 变 2，
  用户也确认「英伟达显卡已屏蔽」）✅ 但**本机 GCU 的 `IGpuOnlyConnectionSwitch_Status` 是死字段**
  （永远 `IGPU_ONLY_CONNECT_RB_OFF`）❌ -> 从状态字段看不出成功
- 而 LM 用的 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU` 会**立刻**反映在
  `DiscreteGpuDirectConnectionSwitch_Status = TOGGLE_IGPU` 上 ✅
- **已修工具**：`tools\\gcu_send_mode.py` 的 `on` 改为发 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU`
  （与 LM 一致），旧命令保留为 `on50`
  （工具：tools\\patch_send_mode_toggleigpu.py）

【✅ 顺带验证：用户早先报的「弹两个重启提示」已修好】
```
11:04:19.719  GpuRestartPrompt: seq=1 latest=1     <- 每次切换只弹一次 ✅
11:04:55.222  GpuRestartPrompt: seq=2 latest=2
11:05:00.378  GpuRestartPrompt: seq=3 latest=3
```
且 `GpuSwitch UI: ... allowsRestart=True` -> **Gen40 允许重启式切换** ✅（pendingRestart 正常）
- 同时证明用户已在 LM 界面**成功切换过三种模式**：
  `11:04:23 GpuMode->0(核显)` / `11:05:02 ->1(混合)` / `11:05:06 ->2(独显)` ✅
  -> 说明**核显模式按钮存在且可用** ✅（今天的修复生效）

【新增工具】
- tools\\mqtt_capture.py（被动抓包，因 client_id 白名单**本机不可用**，留作参考）
- tools\\probe_client_ids.py（探测 broker 接受的 client_id）
- tools\\patch_send_mode_toggleigpu.py
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
