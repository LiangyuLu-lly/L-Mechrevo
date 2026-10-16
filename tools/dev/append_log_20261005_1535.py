# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 15:35] 排查上游 0.290.6：**是作者分支的正式版，覆盖安装会让我们的 19 项改动全部消失**
================================================================================
用户提供：D:\\Downloads\\L-Mechrevo-0.290.6-setup.exe（58,693,365 B @15:20）
产出报告：D:\\openLM\\_upstream\\0.290.6\\什么个情况.md（32,749 B）
解包产物：D:\\openLM\\_upstream\\0.290.6\\extract\\（493 文件 / 161.4 MB）
反编译产物：D:\\openLM\\_upstream\\0.290.6\\decomp\\（352 .cs / 2.29 MB，ilspycmd 全量仅 7.9 秒）
方法：innoextract 1.9 **失败**（Inno 6.4.0 太新）-> 用工作区已有
      D:\\openLM\\innounp-267\\innounp.exe（v2.67.11，支持 6.4.0 u）成功 ✅（无需联网）

【结论（逐条实测）】
1. ⚠️ **这是"作者分支"的正式版，不是我们分支的升级**
   反编译产物里我们的自定义实现逐个 grep 命中数**全为 0**
   （PowerSleepGuard / IccHelper / igpu_only_ui / calib_custom_on / ModeProfileConfig / DisplayColorGamut…）
2. ⚠️ **AppId 逐字一致**（{{8F4E2C71-9B3A-4D6E-A1C2-7E5B9D0F3A64}，DefaultDirName={autopf}\\L-Mechrevo）
   -> 真覆盖安装；我们的 L-Mechrevo.dll（1,936,384 B）会被作者的（**2,206,208 B，+13.9%**）整文件替换
   -> 版本号保护**不拦**（0.290.3 < 0.290.6）-> **19 项改动会全部消失** ⚠️
3. 0.290.6 正文很小：主要是**打包合并 + 日志/遥测管线 + 界面裁切**
   （原文大意："合并 0.290.3 至 0.290.5 的模式切换、驱动、旧机型、充电上限和 CPU 调整修复"）
4. ✅ **GCU 服务本体不会降级**：官方 payload\\50 的 GCUService.exe = 1.2.0.0 / 17,110,576 B /
   2026-06-23 15:45，与本机**已部署**的版本+大小+时间戳**完全一致**
   ⚠️ 但官方 GCU 包只剩 payload\\{50,1020}；Select-GcuPayload.ps1 把"非 1020"一律判为 50
   -> 我们特制的 payload\\40（GCUService 1.0.2.70 三件套）与 payload\\{30,GCU} 会变成
      **无人引用的死文件**（不被删，但不再生效）⚠️
5. ✅ **用户数据安全**：官方 [UninstallDelete] 只删 {commonappdata}\\L-Mechrevo\\logs 与
   {app}\\GCU\\{AiStoneService,UniwillService,UWACPIDriver,ACPIDriver,state-backup,gcu-registry-*.reg}
   **完全不碰 %APPDATA%\\MechrevoLite\\** ✅（作者还专门实现了 GCU 用户状态备份/还原）
   -> 风险只在软件层（我们的改动丢），不在数据层

【未完成（子代理明确声明没编造）】
- **文件级对比没做**：ilspycmd 按**类型**落文件（MechrevoService.cs 而非源文件名），
  与我们 source\\ 的按源文件组织**不能一对一映射**；且"19 个改动文件"在工作区里没有成文清单
- **建议下一步（预估 0.5~1 小时）**：把**已部署的 0.290.3 DLL** 只读复制出来同样反编译（约 8 秒），
  两个 decomp 目录做**按类型 diff** -> 得到"0.290.6 到底改了什么 / 与我们的改动哪里会撞"

【⚠️ 升级前的安全建议】
- 无论是否升级，**先整目录备份 %APPDATA%\\MechrevoLite\\**
  （我们自定义的配置键如 igpu_only_ui 作者版本不认识；大概率被忽略而非删除，
    但若作者的配置保存是"整对象重写"，可能在某次保存时丢弃 ⚠️ —— 此项**未验证**）
- 我们的当日改动已有可复原存档：D:\\openLM\\_checkpoints\\LM-20261005-改动存档.zip
  （19 个改动源文件 + 日志快照 73 条 + MANIFEST）

【干净性确认】本次排查**未**修改/删除/运行：D:\\openLM\\source\\（最新写入 14:47:29 早于排查）、
  installer-src\\、C:\\Program Files\\L-Mechrevo\\（只读）、%APPDATA%\\MechrevoLite\\config.json（只读）；
  **未安装 0.290.6**、未编译、未打包、未部署、未联网下载工具 ✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
