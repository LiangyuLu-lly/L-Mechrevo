# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 11:50] 修「重启以应用」的目标显示错误（棕色高亮挂错按钮）
================================================================================
用户报告：「不过LM还是显示切的独显 没有回读说是」
并明确了界面语义（重要，记下来）：
  **棕色高亮 = 待生效（下次重启会切成的模式）** ；**蓝色高亮 = 当前实际模式** ✅
  -> 现在的界面是「重启以应用 + 独显直连(棕色)」= 在说"下次重启会切独显直连" ❌
     而实际待生效是【核显模式】 ❌ **与实际不符** ✅

【真值（按 §9.2 教训先取证再改）】
```
config: gpu_restart_pending = '1'
        gpu_restart_target  = '2'          <- 目标是【独显直连】❌
        gpu_restart_tick    = '28274265'
NVRAM : @0x62 = 2 (amd-decoded=IgpuOnly)   <- 实际待生效是【核显模式】✅
route : Direct                             <- 当前实际是【独显直连】✅
```
成因：
- `gpu_restart_target=2` 是用户 **11:05:01** 自己切独显时 `MarkPending(2)` 留下的 ✅
- 之后**我方**从外部发 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU`（11:08）✅ -> LM 不知道 ✅
  -> target 没更新，仍指向 2 ❌
- 代码：`SettingsForm:7345-7352`
  `restartPending=true` -> 取 `GpuRestartVerifier.PendingTarget`
  -> 而 `PendingTarget` 只来自 LM 自己写的配置（`MarkPending`），**不读 NVRAM** ❌
  -> `num=2` -> 棕色挂在 2 上 ❌

【修法（tools\\patch_pending_target_from_nvram.py，2 项锚点；只读，不写配置）】
1. `UefiDisplayModeDiagnostics` 新增：
   - `TryReadRoute(bool isAmd, out GpuRoute? route)` —— 与 `Describe()` 同变量/偏移
     （`UniWillVariable` / `{9f33f85c-...}` / `buffer[98]`），需提权
   - `TryGetPendingRoute(...)` —— **带 5 秒缓存**（UI 每次刷新都会问，不能每次调固件）
2. `GpuRestartVerifier.PendingTarget`：当 **NVRAM 待生效路由 != 当前实际路由** 时以 NVRAM 为准
   （返回 `ToGpuMode(NVRAM 路由)`），否则沿用原配置值
   -> 于是 `num = 0`（核显模式）-> 棕色挂到【核显模式】✅ 与实际一致 ✅

【验证】
- 编译 0 错误；产物 1936384 字节 @11:12:35；替换完成 11:12:35
- LM 重启后正常（PID 1404 Responding=True）✅，无异常日志 ✅
- 读取正常：`11:12:58 GPU route at startup: ... NVRAM display byte @0x62=2 (amd-decoded=IgpuOnly)` ✅
- **预期界面：棕色高亮在【核显模式】+「重启以应用」** ✅（待用户目视确认）
- config 未被改动（`gpu_restart_target` 仍 = 2 ✅ —— 修复是**只读**的 ✅）
- 备份：backup\\UefiDisplayModeDiagnostics_20261005_pre_pendingroute.cs、
        backup\\GpuRestartVerifier_20261005_pre_pendingroute.cs

【顺带确定的界面语义（写进文档备查）】
| 颜色 | 含义 |
|---|---|
| **棕色**（`UiVisualStyle.Warn`，`:7358`） | **待生效**：下次重启会切成的模式 |
| **蓝色**（默认） | **当前实际模式** |
- 只有一个按钮同时被 `Activated`（`:7362-7364`），颜色区分状态 ✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
