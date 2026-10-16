# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 02:16] 修复 GPUPowerSavingSettings.json 0 字节覆盖 + 体积分析
- 用户三条：①「修复」（指附录 A.5 那个隐患）②问我的上下文机制 ③问 30 系与 50 系完整包是否共用文件（想缩体积）
- **修复**：把 `payload\\30\\AiStoneService\\MyControlCenter\\GPUPowerSavingSettings\\GPUPowerSavingSettings.json`
  （0 字节）**移出**到 `backup\\gcu_payload_20261005_0215\\GPUPowerSavingSettings.json.from30patch`
  -> 安装时将使用底座的 301 字节版本 ✅
  -> 30 系补丁文件数 **118 → 117** ✅
  （原因：`Install-Gcu.ps1` 的 `Copy-Tree` 用 `Copy-Item -Recurse -Force` 会覆盖）
- **体积分析（实测）**：
  | payload | 文件 | 占用 |
  |---|---|---|
  | 底座 GCU（完整 50 系） | 271 | **48.14 MB** |
  | 30 补丁 | 117 | 18.73 MB |
  | 40 三件套 | 3 | **16.55 MB** |
  | 50 三件套 | 3 | **19.15 MB** |
  | 合计 | — | **102.57 MB** |
  | 官方已装(30系，对照) | 279 | 44.17 MB |
- ✅ **回答用户核心问题：30 与 50 大量共用，不会爆炸** ——
  底座 48.14MB/261 文件被所有机型共用；30 补丁只覆盖 12 个、新增 106 个（94 个是 .NET 运行时）
- ⭐ **真正的大头是 40/50 三件套 35.7 MB，而每台机器只用一套**：
  Inno 会把两者都压进同一个 EXE -> 每个用户白下 ~17 MB
- **优化建议（写进文档附录 B）**：
  1. 三件套拆成独立附带包/按机型下载 -> 省 **~17 MB**（低风险）
  2. 删 `EntityFramework.xml`(3.51) + `EntityFramework.SqlServer.xml`(0.15) -> 省 **3.66 MB**（**零风险**，
     仅 IntelliSense 文档）
  3. 按机型拆 `oled_care.scr`(12.35MB) / `rtknrai.exe`(2.48) / `DisplayInfo.dll`(2.43) /
     `SQLite.Interop.dll`(1.91) -> 省 **~19 MB**（中风险；这些**官方 30 系包里没有**，属 50 系包特有）
  4. 底座里被 30 补丁覆盖的 3 个大文件（ACPIDriverDll 3.93/UWDiskInfo 2.63/GPUInfoDLL 2.47 ≈ 9MB）
     -> 仅对 30 系冗余，但底座要服务 40/50，难拆
  目标：只做 1+2 ≈ **82 MB**；再做 3 ≈ **63 MB**
- **另外**：向用户解释了上下文机制 —— 压缩由系统自动进行、我看不到也不会收到通知；
  以后不再说"上下文已用尽"，改为直接继续或先说"我先记录一下"
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
