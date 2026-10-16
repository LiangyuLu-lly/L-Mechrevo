# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 11:40] 🎯 决定性实验：核显模式命令【真的有效】—— 结论与更正
================================================================================
背景：用户指出「IGPU_ONLY_CONNECT_RB_ON 像是核显省电模式开」✅ —— **完全正确** ✅
  （代码证实：`MechrevoService.SetIGpuOnlySaveMode()` 发的就是
    `IGPU_ONLY_CONNECT_RB_{ON,OFF,AUTO}`，被 SettingsForm:7523 的【核显省电】行调用 ✅）

【实验设计（不动 BIOS，不用重启系统）】
  ① 基线：NVRAM @0x62 = 1 (Direct)，GCU 状态 = DGPU_DIRECT_CONNECT_TOGGLE_ON
  ② 发 **LM 同款命令** `{"Action":"DGPU_DIRECT_CONNECT_TOGGLE_IGPU"}`（tools\\gcu_send_mode.py on）
  ③ 提权重启 LM —— 它会重新读 NVRAM 并打日志
  ④ 看新的 `GPU route at startup ... NVRAM display byte @0x62=?`

【结果 🎯】
```
发送后 GCU 状态: DiscreteGpuDirectConnectionSwitch_Status
                 DGPU_DIRECT_CONNECT_TOGGLE_ON -> DGPU_DIRECT_CONNECT_TOGGLE_IGPU   ✅ 被接受
11:08:25  GPU route at startup: panel=Discrete dgpu=Present route=Direct;
          NVRAM display byte @0x62=2 (len=180, amd-decoded=IgpuOnly)                  ✅✅ NVRAM 真写了
```
（`route=Direct`、NVIDIA 仍在 = **尚未生效，需重启** ✅ 符合"待生效意图"模型）

【结论】
1. ✅ **`DGPU_DIRECT_CONNECT_TOGGLE_IGPU` 真的有效** —— 它把待生效意图写进 NVRAM
   （AMD 编码 IgpuOnly=2）✅ -> **核显模式按钮是真能用的** ✅
2. ❌ `IGPU_ONLY_CONNECT_RB_ON`（**核显省电**命令）在本机**无效** ✅
   -> 我之前"手动发送成功"的测试**选错了命令** ✅；用户最初说"手动发是失败的"**是对的** ✅
3. ✅ 因此今天恢复的判据 `CanOfferIgpuOnly => CanOfferGpuModeSwitch && SupportsDgpuDirect`
   **是正确的** ✅ —— 按钮**显示且可用** ✅（保留，不撤）
4. ✅ `IGpuOnlyConnectionSwitch_Support = NotSupport` 说的是「**核显省电**不支持」✅
   —— 与"能不能切核显模式"**无关** ✅（对应 §9.6 的区分：核显省电是 50 系及更新的功能）
5. ⚠️ 值得留意（语义混淆，非当前 bug）：
   `CreateGpuModePayload(0)` 把【核显模式】映射成【核显省电】命令
   `{"Action":"IGPU_ONLY_CONNECT_RB_ON","SetToWMIEC":"OK"}` ——
   平时不走这条路（只在 `supportsIgpuOnly=true` 时被 `CreateGpuSwitchPayload` 调用），
   现象上无问题，但语义是混的 ✅

【抓包方案最终判定：不可行（已彻底确认）】
- 第一个后台抓包 job 连上了但 **4 分钟内收到 0 条消息**（期间用户在点模式切换 ✅）
  -> broker **不把 LM 的 publish 转发给其它订阅者**（或只转发本会话）✅
- 且 client_id 白名单只认 `UWPClient_2`（LM 占用）✅
- -> **抓包不可行；LM 自己的日志才是可靠来源** ✅（本次全程靠它 ✅）

【⚠️ 机器当前状态（重要）】
- **NVRAM @0x62 现在是 2 = IgpuOnly（待生效）** ✅ —— **下次重启系统就会进入核显模式** ⚠️
  - 想进去：直接重启 ✅
  - 不想：用 `py -3 tools\\gcu_send_mode.py dgpu`（回独显直连）或 `dgpuoff`（回混合）覆盖意图 ✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
