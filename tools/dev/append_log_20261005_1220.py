# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 12:20] 做「核显切换动作采集工具」（给其他机主用，零依赖）+ 查极光Pro 诊断包
================================================================================
用户要求：「或者你写一个脚本 抓一下LM在50系机型上冷切换核显做了什么」
          → 追加：「我是给其他机主用的 不能是简单的python脚本」

【① 查极光Pro 诊断包（前一条要求）—— 结论：没有记录】
- 盘上**没有**名为「极光XPro」的包；只有 `JiguangPro Series GM6PQ0X`（极光Pro 2024）。
  用户澄清：极光X Pro 是 **2025 机型**，记错了 ✅
- `jiguang_pro2024_unpacked`：**日志级别 Off** ✅ -> **0 个日志文件** ✅ -> 切核显动作**完全没记录** ❌
- 顺带全盘搜「切核显证据」（tools\\hunt_igpu_switch_evidence.py）：
  * 只有**本机（翼龙15Pro）**有 `GpuMode -> 0` 记录（2026-09-28 共 4 次，另 10-05 多次）
    且 `dgpu=` 字段正是 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU` ✅ -> 独立佐证该命令正确 ✅
  * 另一台 `CANGLONG Series`（集显模式=是）日志里**全是 mode 2**，从未切核显 ✅
  * 蛟龙16S：无切核显 ✅

【② 诊断包到底记录什么（代码确认）】
- `系统信息.txt` 总是有；`配置\\*.json` 总是有
- **`日志\\log.txt` 仅当日志级别 ≠ Off**（`MaxLogBytes=10MB`，超出只留末尾 1MB）
- **`日志\\内存日志缓冲.txt` 总是有**，但只是 `CrashRingBuffer(65536)` = **内存**，
  **重启即丢** ⚠️ -> 而显卡模式切换**恰恰要重启** -> 默认 Off 下**拿不到动作** ❌
- 默认级别 = Off（环境变量 LMECHREVO_LOG_LEVEL ?? 配置 log_level ?? Off）

【③ 产出：给机主的零依赖工具包】
`D:\\openLM\\tools\\LM-核显切换采集工具.zip`（6.7 KB，4 个文件）
  采集核显切换.bat   <- 机主双击（纯 ASCII + 自提权：net session 检查 -> Start-Process -Verb RunAs）
  只采集.bat         <- 重启系统后再采一次（不重启 LM、不交互）
  collect.ps1        <- 实际逻辑（**UTF-8 带 BOM**，中文才正常；10 KB）
  说明.txt           <- 给机主的操作说明（含隐私说明）
做法要点：
- **不做抓包**（GCU 的 MQTT client_id 白名单只认 `UWPClient_2`，LM 占用 -> 物理上做不到）
  -> 改为读 **LM 自己的 log.txt**（磁盘文件，**重启也不丢** ✅）
- 三步流程：①开全量日志(setx)+重启 LM ②引导点「核显模式」**先别重启**就采集
  ③问是否重启电脑，重启后再用 `只采集.bat` 采第二份（校验是否真的切换成功）
- 报告内容：机型/BIOS/CPU/GPU + `dgpu_generation`/`gpu_mode`/pending 三键，
  再把日志按 **7 类分桶**（①LM 实际发出的动作 ②冷切换载荷序列 ③模式回读
  ④路由/NVRAM ⑤能力位 ⑥核显省电 ⑦机型硬件），并把「切到核显(mode 0)」**单独成节**
- 生成器：`tools\\gen_igpu_capture_tool.py`（生成器 + 后续 `patch_igpu_tool_collectonly.py`
  补 `-CollectOnly`；⚠️ 生成器未同步该开关，重生成会覆盖 -> 以后直接改 igpu-capture 目录里的文件）

【验证】
- `collect.ps1` 通过 **PowerShell 语法检查** ✅、**带 BOM** ✅；`.bat` **纯 ASCII** ✅
- **本地实测** `-CollectOnly`：生成报告到桌面 ✅
  报告实测含：
    `机型 yilong15 Pro Series GM5HG0A / BIOS N.1.10MRO28 / RTX 4060 / Gen40`
    `① LM 实际发出的动作`：`SwitchGpuMode(0) -> DGPU_DIRECT_CONNECT_TOGGLE_IGPU` ✅
    `★ 重点：切到核显模式` 小节 ✅
- ZIP：6.7 KB，含 4 个文件 ✅

【⚠️ 由脚本输出暴露的一个待决风险（重要）】
报告里出现 **2026-10-03 12:52:24 `SwitchGpuMode(1) -> IGPU_ONLY_CONNECT_RB_OFF`** ✅
-> 证明**当 `supportsIgpuOnly=true` 时，mode 0/1 确实会走 `CreateGpuModePayload`** ✅
-> **50 系恒为 `supportsIgpuOnly=true`** -> 意味着我今天"第一类清理"里的
   `CreateGpuModePayload(0)`（`IGPU_ONLY_CONNECT_RB_ON` -> `TOGGLE_IGPU`）**会改变 50 系发出的命令** ⚠️
   （`MechrevoHw:427` 的判据改动对 50 系**无影响**，因为 Gen50 词汇表里两者都有 ✅）
-> **待用户决定**：是否把 `CreateGpuModePayload(0)` 那一行回退，等 50 系样本到手再改
   （本工具正是用来拿这个样本的 ✅）
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
