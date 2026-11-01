# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 12:40] 采集工具 v2：修正致命错误（取消不发动作）+ 适配自定义安装目录
================================================================================
用户反馈（关键更正）：
  1) ❌「点取消是不发动作」—— v1 的说明让机主「先点【取消】」是**致命错误** ⚠️⚠️
     点【取消】= 整个切换被取消 = **一条指令都不会发出** = 日志里什么都没有
     ✅ 正确：必须点【重启切换】（发命令 + 重启；`shutdown /r` 是**干净关机**，
        文件缓存一定落盘，所以日志不会丢 ✅）
  2) 对方用的是**老版本 LM 18.4** —— 提示框**只有【重启切换】和【取消】**，
     没有"不重启切换"的选项 ✅（与冷切换设计一致）
  3) 对方的安装位置是**自定义目录**：`D:\\Desktop\\系统工具\\控制台\\L-Mechrevo\\L-Mechrevo.exe`
     -> v1 只在 Program Files 里找 -> 会报"找不到 L-Mechrevo.exe" ❌

【日志路径核实（重要，与安装位置无关）】
`Logger.ResolveAppPath()`（Logger.cs:71-98）：
  ① 环境变量 `LMECHREVO_LOG_DIR`（最优先）
  ② 测试宿主 -> `<baseDirectory>\\test-logs`
  ③ **否则恒为 defaultDirectory** = `%APPDATA%\\MechrevoLite`（以 SYSTEM 运行则 `%ProgramData%\\MechrevoLite`）
  -> **`baseDirectory`（exe 所在目录）只在测试宿主用** ✅
  -> 所以日志**与安装位置无关**，仍在 `%APPDATA%\\MechrevoLite\\log.txt` ✅
     （本机实测确认存在 ✅）

【v2 工具改动】生成器 tools\\gen_igpu_capture_tool_v2.py -> tools\\igpu-capture\\
  文件：第一步-开日志并重启LM.bat / 第二步-采集.bat / collect.ps1 / 说明.txt
  1. **流程改为两阶段**（因为必然重启）：
     第一步 = 开全量日志 + 重启 LM + **指引**（不采集）
     第二步（系统重启后）= 只采集 + 出报告到桌面
  2. **说明里改成必须点【重启切换】**，并用红字强调"千万不要点取消"及其原因
  3. **exe 发现顺序**：正在运行的进程 -> 注册表卸载项 InstallLocation -> 常见位置
     -> **手动输入/拖拽**（找不到也能继续，只是不自动重启 LM）
  4. **日志多候选**：LMECHREVO_LOG_DIR -> %APPDATA% -> %ProgramData%，取**最新**的
  5. **采集模式放宽**（兼容老版本 18.4 的用词）：8 个分桶，新增
     「⑧ 核显模式相关（宽匹配）」；聚焦节也放宽
  6. 报告头部增加：日志文件全路径、配置文件路径、LMECHREVO_LOG_DIR 值

【v2 二次修正（UX 缺陷）】
  实测发现：`TrayGPU:` 每几秒刷一行 -> ★重点节"最后 60 条"**全被它淹没** ❌
  -> 排除 `TrayGPU`，并把**关键动作行**（SwitchGpuMode/TOGGLE_IGPU/IGPU_ONLY_CONNECT_RB_ON/
     payload(s)/target=0/route verify/NVRAM/OemDisplayMode/Setting/Control）**优先显示**
     （最多 60 条），其余核显相关行只显示最后 20 条 ✅
  工具：tools\\patch_igpu_tool_v2_focus.py

【验证】
- `collect.ps1` PowerShell **语法通过** ✅、**带 BOM** ✅；两个 `.bat` **纯 ASCII** ✅
- **本地实测** `-CollectOnly`：报告 48.7 KB 生成成功 ✅
  ★重点节实测输出真正的动作行 ✅：
    `GPU restart route applied for target=2: 3 payload(s) [DGPU_DIRECT_CONNECT_TOGGLE_ON -> IGPU_ONLY_CONNECT_RB_OFF -> DGPU_DIRECT_CONNECT_TOGGLE_ON]`
    `MechrevoService.SwitchGpuMode(2) -> DGPU_DIRECT_CONNECT_TOGGLE_ON, ...`
    `SwitchGpuMode confirmed=True: expected=2 actual=2 ... elapsed=1525ms`
    `GPU route verify: target=2 actual=Direct verdict=Applied; NVRAM display byte @0x62=1`
  （顺带再次印证：切 target=2 时连发 3 条载荷 + 校验 ✅）
- 打包：`D:\\openLM\\tools\\LM-核显切换采集工具.zip` **7.5 KB / 4 个文件** ✅
- v1 的 `采集核显切换.bat` / `只采集.bat` 已删除 ✅
- 未打包 LM 本体（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
