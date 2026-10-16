# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 07:10] 按用户要求连发 5 次核显指令 + 确认 NVRAM 未写（重启无效）
- 用户原话：「帮我发送切核显指令 然后我重启」
- 做法：连发 **5 次** `{"Action":"IGPU_ONLY_CONNECT_RB_ON","SetToWMIEC":"OK"}`（每次间隔 2 秒，
  模仿官方 50 系控制台"每 2 秒重发、count>60 放弃"的行为，见
  `CCUWinUI.decompiled.cs:53527-53580`）-> 每发一次都回读状态
- **结果：5 次全部无效** ✅
  * `IGpuOnlyConnectionSwitch_Status` 始终 = `IGPU_ONLY_CONNECT_RB_OFF`
  * `IGpuOnlyConnectionSwitch_Support` 始终 = `NotSupport`
  * 之后重启 LM 读 NVRAM：`@0x62=0 (amd-decoded=Hybrid)` —— **与之前完全一致，没被写入** ⚠️
    （02:58:14，与此前 02:56:40 / 02:54:48 三条相同）
- **结论：系统重启后也不会变成核显模式** ✅（NVRAM 是"待生效意图"的落脚点，它没变）
  -> 重启无害但也无益；`payload\\40`（GCUService 1.0.2.70）确实未实现该功能
- 新增工具 **`tools\\post_reboot_check.py`**：重启后一条命令自检
  （GCU 状态 + NVRAM 最后 3 条 + 明确结论）—— 已试跑通过 ✅
- 救援命令（万一黑屏）：`cd D:\\openLM; py -3 tools\\gcu_send_mode.py recover`
  （发 `DGPU_DIRECT_CONNECT_TOGGLE_ON` 切回独显直连；本机已证明该指令有效）
- 风险说明：因为该 40 系 GCU 对核显指令**完全无响应**（NVRAM 都没写），
  黑屏风险**很低** ✅ —— 但仍建议保留救援命令
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
