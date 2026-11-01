# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-06 00:20] 记录：会话文件策略切到 workspace-write 时遇到 DSH 沙箱权限失败，已按技能修复并验证
================================================================================
现象：会话文件策略改为 workspace-write 后，第一条命令直接失败：
  `Error: SetNamedSecurityInfoW failed (Win32 5): grantWrite(D:\\openLM)`
  -> DSH 无法给工作区授予写权限 -> 所有命令都会失败

处理（按技能 diagnose-windows-sandbox-acl，一次不受限运行，已获用户批准）：
  & '<skill>\\scripts\\diagnose-windows-sandbox-acl.ps1' -Path 'D:\\openLM' -AllowRoot 'D:\\openLM' -Out 'D:\\openLM-acl-recovery'
  结果：VERDICT=PRECONDITION（D:\\openLM 缺 WRITE_OWNER）-> 备份 DACL -> 补当前用户 FullControl 允许项
        -> 验证通过：before {writeDac:true, writeOwner:false} / after {writeDac:true, writeOwner:true}
        -> SUMMARY FIXED=0 GRANTED=1 REFUSED=0 RESTORED=0
        -> 未删除任何拒绝项、未改所有者、未改文件内容；子树扫描 packageSources=[]（无可清理的包权限项，扫描 truncated=true）
  报告：D:\\openLM-acl-recovery\\acl-report-f629eca9a7df40b9ac4b18a1dfb83a53.jsonl
  备份/回滚脚本：D:\\openLM-acl-recovery\\acl-backup-be73529b5c6a449ba6cb804712e6c92e.json(.ps1)
  回滚命令（如需）：
    pwsh -NoProfile -File 'D:\\openLM-acl-recovery\\acl-backup-be73529b5c6a449ba6cb804712e6c92e.json.ps1' -Path 'D:\\openLM' -AllowRoot 'D:\\openLM' -Restore 'D:\\openLM-acl-recovery\\acl-backup-be73529b5c6a449ba6cb804712e6c92e.json'

复验（重跑原操作，全部通过）：
- 工作区写入探针：写入并清理成功 ✅（该路径已恢复）
- 部署一致性：C:\\Program Files\\L-Mechrevo\\L-Mechrevo.dll 与 source\\bin\\Release 产物**逐字节同大小**：
  2,062,336 字节 @ 2026-10-06 00:08:37（0.290.7 / 0.290.7-beta21.7）；LM PID 27184 responding=True
- 每核 CO 逻辑在位（二进制关键字全中）：cpu_co_core0 / cpu_co_auto_apply / cpu_co_clean_exit /
  cpu_co_guard_armed / cpu_co_guard_verdict / cpuTuneReset / cpuTuneAutoApply / cpuTuneGuardStatus ✅
- 保底机制**正在正确运行**（日志实证）：
    [cpu-co] shutdown guard: already decided this boot -> first_run (boot 2026-10-05 09:17)
    [cpu-co] startup auto-apply: shutdown guard = first_run (first run ... -> treated as clean)
    [cpu-co] startup auto-apply: OFF (cpu_co_auto_apply); nothing written
  -> 首次运行不误判为异常 ✅、开关默认关 -> 不写 ✅、同一次开机只判一次 ✅
- 配置：cpu_co_guard_armed=1 / cpu_co_guard_boot=2026-10-05 09:17 / cpu_co_guard_verdict=first_run；
  **没有 cpu_co_auto_apply**（开关默认关）✅；无 crash.txt ✅

⚠️ 一次**自查方法的错误**（记录以免误导）：
  我用"读 resources.dll 的字节搜中文"来判断中文卫星资源是否含新键，结果报"缺失"。
  这是**假阴性** —— .resources 是二进制格式，不能搜字节。**既有教训（.resx 要用
  System.Resources.ResourceReader 校验、不搜字节）我又犯了一次**。
  实际卫星：63,488 字节 @00:08:37（与本次部署同刻），且子代理已用正确方法核对过键；
  **结论：卫星资源正常，无需处理** ✅

用户决定：**不再做台式机验证**（「不要台式机验证了 你直接写进去逻辑没问题就行」）
  -> 每核 CO 产品化 + 界面精简 + 非正常关机保底：**均已写入并部署，逻辑按设计与日志实证运行** ✅
  -> 未做端到端实机验证的部分（台式机上的应用/重置/开机自动应用真实写入）**由用户接受为不再验证** ✅

遗留（与本次无关）：
- 合并主线 ⑦B Intel（含第三方签名内核模块，待用户定）、⑧ 遥测管线（决策③）、⑨ 旧机型后端
- 0.290.7 安装包（installer-src\\Output\\LM-0.290.7-beta21.7-setup.exe）**早于**今天的后续改动
  -> 若要发布需重新打包（用户未要求）
- 未打包、未打 NoGCU
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
