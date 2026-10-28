# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 12:55] 修「待生效」残留：NVRAM 待生效路由 == 实际路由 时视为没有待生效
================================================================================
起因：用户实测 8 次切换后发现界面仍显示「重启以应用」+ 棕色高亮在独显直连，但实际无待生效变更
```
config: gpu_restart_pending=1, gpu_restart_target=2
NVRAM : @0x62=1 (Direct)  ==  实际路由 Direct
```
成因：先切核显(target=0) 再切回独显(target=2)，**两次都没重启**，NVRAM 跟着回到 1；
     而 `gpu_restart_pending` 只在「重启后校验」时才清（TryTakeRebootTarget 要求 RebootHappened）。

改动（tools\\patch_pending_satisfied.py，全部在 MechrevoLite.Gpu\\GpuRestartVerifier.cs）：
- 新增 `internal static bool PendingAlreadySatisfied()`（:23）
  条件：配置 pending=1 **且** NVRAM 待生效路由可读 **且** != Unknown **且** == 实际路由
  （NVRAM 读取走 TryGetPendingRoute，带 5 秒缓存，不会频繁调固件）
- `PendingTarget` 开头加守卫：满足则返回 -1（:46）
- `HasPendingThisBoot()` 开头加守卫：满足则返回 false（:101）
  -> `SettingsForm.HasPendingGpuRestart()` 直接转调它，于是：
     * 棕色高亮消失（restartPending=false -> 用默认蓝色 = 当前实际模式）
     * 「重启以应用」消失
     * 核显省电行可见性判断更准确（它要求 !HasPendingGpuRestart()）
- 与 2026-10-05 12:00 那次的修法**互补**：
  12:00「NVRAM 与实际**不同** -> 以 NVRAM 为准」；本次「NVRAM 与实际**相同** -> 视为无待生效」
- 「真的有待生效」时行为不变：刚点完切换未重启 -> NVRAM=目标 != 实际 -> 仍棕色 + 重启提示 ✅

【⚠️ 补丁踩坑记录（留给以后）】
v1 补丁锚点 0/0 全部未命中 —— 原因是我的「行尾回退逻辑」只试了一种行尾：
  锚点用 LF 写，文件是 **CRLF**，而回退分支只把锚点从 CRLF 转 LF（方向反了），导致两种都没试。
v2 改为：**读出来统一规范化成 LF 匹配，最后按原行尾写回**（并保留 utf-8-sig BOM）。
-> 以后所有 patch 脚本都用这个模式，别再写"双向猜测行尾"。

【部署与预期】
- 编译 0 错误 ✅；产物 1929216 字节 @12:52:41；替换完成 12:52:41；LM PID 18020 Responding=True ✅
- 当前真值：pending=1/target=2，但 NVRAM=1(Direct) == 实际路由 Direct
- **预期界面**：无棕色、无「重启以应用」、独显直连按蓝色高亮 ✅（待用户目视确认）
- 备份：backup\\GpuRestartVerifier_20261005_pre_satisfied.cs
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
