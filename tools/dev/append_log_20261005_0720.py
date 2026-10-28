# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 07:20] 🎯 重大更正 + 找到核显模式的【真正可靠判据】（实测证实）
- 用户反馈：「当前在核显模式 英伟达显卡已屏蔽」-> **我上一条"重启也不会生效"的结论是错的** ✅ 已致歉
- **实测证据链**：
  ```
  系统重启于 03:00:03
  02:58:14  route=Hybrid;  NVRAM @0x62=0 (amd-decoded=Hybrid)   <- 我读得太早，写入还没发生
  03:00:26  GPU route readback: panel=Integrated dgpu=Absent route=IgpuOnly   <- dGPU 消失
  03:00:34  route=IgpuOnly; NVRAM @0x62=2 (amd-decoded=IgpuOnly)  <- ✅ NVRAM 确实写成 2
  ```
  * `Win32_VideoController` 里 **NVIDIA 已不存在**（只剩 AMD 780M + GameViewer）✅
  * NVRAM 显示模式字节 `@0x62 = 2` = **IgpuOnly**（AMD 编码 Hybrid=0/Direct=1/Igpu=2）✅
  -> 我连发 5 次的 `IGPU_ONLY_CONNECT_RB_ON` **确实被接受并写入了 NVRAM** ✅
     只是写入时刻在我 02:58:14 那次读取**之后**（重启前后），所以我误判为"没写"
- 🎯 **关键发现：真正可靠的信号是 `DiscreteGpuDirectConnectionSwitch_Status`**
  | 字段 | 处于核显模式时 | 说明 |
  |---|---|---|
  | `IGpuOnlyConnectionSwitch_Support` | `NotSupport` ❌ | **该 GCU 变体不使用此字段** |
  | `IGpuOnlyConnectionSwitch_Status` | `IGPU_ONLY_CONNECT_RB_OFF` ❌ | 一直是 OFF，无意义 |
  | **`DiscreteGpuDirectConnectionSwitch_Status`** | **`DGPU_DIRECT_CONNECT_TOGGLE_IGPU`** ✅ | **真正的判据** |
  * 该动作词 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU` **本来就在** `DisplayRouteMatrix` 的
    Gen40 允许列表里 ✅ —— 只是**没有被用作能力判据** ❌
  * 观察到的完整取值：`TOGGLE_ON`=独显直连 / `TOGGLE_OFF`=混合 / **`TOGGLE_IGPU`=核显模式** ✅
- **因此 LM 的现有判据是错的**：它只看 `IGpuOnlyConnectionSwitch_*`（`SupIgpu=False`），
  于是**机器已经处于核显模式，LM 却认为不支持、不给按钮** ⚠️ -> 用户**无法从 UI 切回**
- **修正后的判据（待实现）**：
  ```
  支持核显模式 = 任一：
      S1  IGpuOnlyConnectionSwitch_Support == Support           （50 系/部分 40 系变体）
      S2  IGpuOnlyConnectionSwitch_Status 含 IGPU_ONLY_CONNECT_RB_ON
      S3  DiscreteGpuDirectConnectionSwitch_Status == DGPU_DIRECT_CONNECT_TOGGLE_IGPU   ★本机实测有效
      S4  NVRAM @0x62 == IgpuOnly（按平台解码：AMD=2 / Intel=1）  ★硬件真值
      且 当前 GCU 变体未被 DisplayRouteMatrix 标为 ProvenAbsent（Gen30）
  ```
  并以 **当前 `TOGGLE_IGPU` 状态**驱动按钮高亮 ✅
- **⚠️ 用户当前状态提醒**：机器在核显模式下，LM 界面**没有**切回按钮 ⚠️
  切回方式（需重启生效）：
  `py -3 tools\\gcu_send_mode.py dgpuoff`（回混合）/ `dgpu`（回独显直连）
- **待办（合并进《核显模式判据设计与三方对照.md》的更正）**：
  1) 更正"能力位不可靠"的旧结论：**问题不是位不靠谱，而是 LM 看错了字段** ✅
  2) 实现上面的 S1..S4 并集判据（S3 为主）✅
  3) 诊断包 `[能力位]` 段增加 `DiscreteGpuDirectConnectionSwitch_Status` 原值 ✅
- 新增工具：`tools\\post_reboot_check.py`（重启后自检，已试跑）
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
