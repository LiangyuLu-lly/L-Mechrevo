# -*- coding: utf-8 -*-
"""2026-10-05 uifix: 把本轮改动追加到 D:\\openLM\\修改日志.txt **末尾**（utf-8 追加，不覆盖）。"""
import io

ENTRY = """
================================================================================
[2026-10-05 27:30] 每核 CO 界面精简（按钮合并/重置去括号/提示精简）+ 新增非正常关机保底
================================================================================
用户原话：
  「重复按钮不要(逐核应用和重置和底下按钮不是重复了 重置里面括号也不要 还有提示要简洁明了 增加保底机制
    如果检测到上次非正常关机 LM不会开机自动应用并提示」

【本轮范围】上一轮把每核 CO 做进产品后，用户看了窗口截图提了四条；本轮只落这四条，**不新增任何写命令**，
  也不放宽任何能力检查。硬约束依旧：本机（Phoenix / R7 8745H，amd-co 被 SKU 层级锁掉）绝不写入。

--------------------------------------------------------------------------------
一、四条各自怎么落的
--------------------------------------------------------------------------------
① 去掉重复按钮 —— 全窗口只剩**一排**动作按钮。
   前：每核区「逐核应用 / 重置（每核写 0）/ 保存设置」+ 底部「检测能力 / 应用 / 恢复」= 两排、语义重复。
   后：底部一排「检测能力 / 应用 / 重置」（+ 能力不足时才出现的「安装缺失驱动」，本机不可见）。
       * 「逐核应用」「保存设置」删除：它们并进「应用」—— 每核路径可用（amd-co + CanWrite + PerCoreWrite，
         只有桌面 Vermeer）时，「应用」= 逐核写入这 8 个值，**成功后一并保存**；否则走原来的全核/单项应用
         路径（既有语义一字未改）。
       * 「重置（每核写 0）」与「恢复」合并成一个「重置」（Name 仍为 cpuTuneReset）：
         行为 = 逐核写 0 + 全核 SetCoAll(0) 兜底 + 退出固定频率（仅本轮写过 amd-clock 才 DisableOc）
         + 清空已保存的每核值。实现上直接复用旧的 ResetAllCores()（逐核 0 + 全核兜底 + 清保存值，
         **实现与日志格式一字未改**），没有每核写路径时退回旧 Restore 的 amd-co 分支（只发 SetCoAll(0)）。
         ⚠️ **绝无两个都会清零的按钮**：全窗口只有这一个清零入口。
       * 「重置」的启用 = 旧「重置」条件 ∪ 旧「恢复」条件（每核可编辑 或 本轮有变更）。
         本机：amd-co 不可写 + 无变更 -> 保持禁用（验收要求）。
       * 每核区最后只剩：8 个每核输入框 + 「开机自动应用这些 CO 值」开关 + 说明/警告文字。
② 「重置」不再带括号：CpuTuneResetAllCores 的值从「重置（每核写 0）」改成「重置」。
   旧键 CpuTunePerCoreApply / CpuTuneSaveSettings / CpuTuneRestore / CpuTuneRestored 已无引用 ->
   从两个 resx 与 Strings.cs 里删掉（不留死文案）。
③ 提示精简：
   * 红色警告（CpuTuneAutoApplyWarning）压到 **2 行**：
     「打开后每次开机都会真的写入这些 CO 值（不是只读）；只对本次开机有效，重启由固件重来。不确定就不要打开。」
     其余细节全部移到 tooltip（新键 CpuTuneAutoApplyWarningTip，挂在警告文字与开关上）。
   * 总述（CpuTuneScope）压到 **2 行**：「本次开机全局生效，切换性能模式时保留。「应用」写入并保存当前值；
     「重置」把所有核心 CO 清零并退出固定频率。」
   * 二次确认框照旧保留**安全信息**：两个框都必须列出「将写入：{0}」与「当前基线（进入时实读）：{1}」
     （基线标签点明来源 —— 它记的是打开窗口时实读到的值，不会因为一次「重置」而变）。
   * 新增保底状态行（见④）；成功文案 CpuTuneResetDone 改成合并后的语义。

--------------------------------------------------------------------------------
二、④ 保底机制（上次非正常关机 -> 不自动应用 + 提示）
--------------------------------------------------------------------------------
【判定方式】选了用户建议里的**首选方案：自记标记**（不引入 EventLog 依赖；Kernel-Power 41 / 6008 的
  交叉核对作为"可选加强"**没有做**，理由：需要新增 System.Diagnostics.EventLog 或慢速 WMI 查询，
  而自记标记已覆盖崩溃/蓝屏/断电/被任务管理器结束这四类，见下面"未确定项"）。
  * 正常退出写标记：Program.OnExit（ApplicationExit）与 SystemEvents_SessionEnding（注销/关机/重启）
    都调 CpuCoAutoApplyGuard.MarkCleanExit(...)，幂等；
  * 下次启动**读取后立即清除**（AppConfig.Remove + Flush）；
  * 读到"没有标记"且**曾经武装过** -> 判定 abnormal；**首次安装/首次运行**（没有 armed 键）-> first_run，
    **不算异常**（用户明确要求，实测本机第一次启动就是 `armed=False cleanExitMarker=False -> first_run`）；
  * 复用既有模式：配置键 + AppConfig.Is/Set/Remove（与 GpuRestartVerifier 的 gpu_restart_pending 同一套）、
    "这份判定是不是本次开机的"（同 RebootHappened 思路，但用"开机时刻"字符串而不是 TickCount64，
    避免"上次开机跑得久、这次跑得快"的歧义）、以及 Logger.FlushCrashBuffer —— 进程内落过崩溃现场就
    **绝不写**"干净退出"标记（宁可在下次启动按非正常关机处理）。
【标记写在哪】新增 4 个**按机型作用域**的配置键（cpu_co_ 前缀 -> ModelScopedConfig.IsMachineScopedKey
  自动按机型，且已显式登记进 ExactMachineScopedKeys，与 cpu_co_coreN / cpu_co_auto_apply 同一把锁）：
    cpu_co_clean_exit / cpu_co_guard_armed / cpu_co_guard_boot / cpu_co_guard_verdict
  判定结论按"开机身份"（UtcNow - TickCount64，取到分钟）缓存，**每次开机只算一次**：
  同一次开机内再有进程来问（例如正在跑的 LM + `--cpu-co-auto-apply`）直接复用结论，
  不会把"读取后立即清除"重复消耗、从而不会把一次正常开机误判成非正常关机。
【判定为异常时的完整行为】
  * **一条写命令都不发**：判定发生在 Probe()/PawnIO **之前**（本机日志可证：跳过时没有
    "checking capability before touching the SMU"，也没有任何 SMU/PawnIO 行）；
  * 日志（无论开关是否打开都会写）：
    `[cpu-co] shutdown guard: armed=True cleanExitMarker=False -> abnormal (boot ...)`
    `[cpu-co] startup auto-apply: shutdown guard = abnormal (last shutdown was NOT clean ...)`
    `[cpu-co] startup auto-apply SKIPPED: previous shutdown was not clean ... the saved per-core CO values
     were NOT written this boot ... Switch location: custom mode -> CPU tuning -> startup auto-apply`
  * 轻量提示（**不弹窗、不轰炸**）：沿用既有 ToastForm.ShowNotice，**只在开关为开且真的跳过了**这一次才提示
    （一次开机最多一条）；开关本来就是关的 -> 只留日志，不打扰用户；
  * 窗口里一行状态（新控件 cpuTuneGuardStatus，红色，平时完全不显示）：
    「上次非正常关机，本次未自动应用。开关：自定义模式 → CPU 调节。」
    tooltip 里写清原因与关掉它的位置，并说明"正常关机一次以后，这行提示就不会再出现"；
  * `--cpu-co-auto-apply` 诊断入口同步更新：新增一行
    `cpu-co shutdown guard: lastShutdown=<clean|first_run|abnormal> (...) wouldSkip=<YES|no>`。

--------------------------------------------------------------------------------
三、改动清单（文件 -> 改了什么）
--------------------------------------------------------------------------------
1. source\\MechrevoLite.Hardware\\CpuCoAutoApplyGuard.cs（**新增**，约 200 行）
   * CpuCoShutdownVerdict 枚举（NotEvaluated/Clean/Abnormal/FirstRun）+ 判定/写标记/只读 UI 访问；
   * 开机身份 BootIdentity(utcNow, tickCount64) + 每次开机只判定一次（按 boot 缓存）；
   * VerdictOverride（internal，证据工具/自检注入用，只影响判定与显示，**不打开任何写路径**）。
2. source\\MechrevoLite.Hardware\\RuntimeCpuTuning.cs
   * 新增 ResetAll()：「重置」的合并实现（复用 ResetAllCores + 旧 Restore 的 amd-clock 分支）；
   * AutoApplySaved()：**执行前**先过保底判定；异常 -> 跳过 + 日志 + Toast；开关关 -> 只记日志；
   * 新增静态 GuardVerdict / SkippedByGuard / LastAutoApplyDetail 供 UI 与 CLI 读取。
3. source\\MechrevoLite\\CpuTuningForm.cs
   * 删掉每核区的按钮排与三个按钮字段；底部按钮排改成 probe/apply/reset，并给按钮补上 Name
     （cpuTuneProbe / cpuTuneApply / cpuTuneReset / cpuTuneInstallDriver，验收按 Name 取证）；
   * 「应用」按面板状态分派（每核可编辑 -> 逐核写 + 一并保存；否则原路径）；
   * 新增 ConfirmReset()/ResetConfirmText()/AutoApplyConfirmText()/BaselineText()（纯字符串方法便于取证，
     取证工具只读文本、绝不弹窗）；ResetAll 成功后清零输入框（**不动 _perCoreBaseline**：它记的是
     "进入窗口时实读"，说明行的字面意思就是它）；
   * 新增 cpuTuneGuardStatus 状态行 + UpdateGuardStatus()；新增 _tips（ToolTip）承载被移出的警告细节；
   * ScrollToTop() 补一次延迟拉回（实测第一屏会停在每核输入框那一段，现在回到窗口顶部）。
4. source\\MechrevoLite\\Program.cs
   * OnExit / SystemEvents_SessionEnding 写"干净退出"标记（ApplicationExit 里在 AppConfig.Shutdown 之前）；
   * `--cpu-co-auto-apply` 增加 shutdown guard 一行输出（判定 + 是否会跳过）。
5. source\\Logger.cs
   * 新增 `internal static bool CrashFlushed`（FlushCrashBuffer 里置位），供保底机制决定能不能写干净标记。
6. source\\MechrevoLite.ModelScopedConfig.cs
   * 4 个新键登记进 ExactMachineScopedKeys + 注释写明"按机型作用域"。
7. source\\MechrevoLite.Properties.Strings.resx / Strings.zh-CN.resx / MechrevoLite.Properties\\Strings.cs
   * 改 7 个键的值（ResetAllCores/Scope/AutoApplyWarning/AutoApplyConfirm/ResetConfirm/ResetConfirmTitle/
     ResetDone）、删 4 个死键、加 4 个新键（AutoApplyWarningTip/GuardSkipped/GuardSkippedTip/GuardSkipToast），
     en 与 zh-CN 同步，Strings.cs 同步增删属性。
8. tools\\uvshot\\Program.cs（证据工具，非产品代码）
   * 追加：一排按钮核对、旧按钮不存在核对、重置文案有无括号、每核网格"8 框 0 按钮"、
     警告/总述/状态行的行数、两个二次确认框的**文本**（反射调用纯字符串方法，不弹窗）、
     保底状态行的注入显示（VerdictOverride=Abnormal + 临时配置开开关）与截图；
   * 收尾改成 Environment.Exit(0)（实测 Application.Run 在有些机器状态下不返回，会留下挂着的窗口进程）。
9. tools\\uifix_resx.py / tools\\uifix_resx2.py（一次性文案脚本，文本级改 <data> 块，不做全量 XML 重写）。

--------------------------------------------------------------------------------
四、本机只读验收证据（**本机绝不写入**）
--------------------------------------------------------------------------------
证据目录：D:\\openLM\\diagnostics\\uifix-20261005-2730-final3\\
  * 窗口取证（真实 probe + 真实 zh-CN 卫星资源，只读控件属性，绝不点应用/重置）：
    cpu-tuning-evidence-uifix.txt / cpu-tuning-window-uifix*.png / custom-mode-entry-uifix.png
      - action-row button count = 4（检测能力 / 应用 / 重置 / 隐藏的"安装缺失驱动"），**只有一排**；
      - cpuTunePerCoreApply present = False；cpuTuneSave present = False；cpuTuneRestore present = False；
      - reset button text = "重置" hasParentheses = False；
      - per-core grid found = True numericInputs = 8 buttons = 0（8 框全 0、enabled=False）；
      - cpuTuneAutoApply checked=False、enabled=False；cpuTuneApply enabled=False（本机不可写，按钮禁用）；
      - 警告 approxLines=2.12（实际 2 行）、总述 approxLines=2.12（2 行），tooltip 里是细节长文；
      - 两个二次确认框文本（只读取字符串）：都含「将写入：0, 0, ... 」与「当前基线（进入时实读）：0, 0, ... 」；
      - 保底状态行：默认（开关关）visible=False；注入 abnormal + 开关开 -> visible=True，
        文本「上次非正常关机，本次未自动应用。开关：自定义模式 → CPU 调节。」（见 cpu-tuning-window-uifix-guard.png）。
  * 保底机制两条路径（CLI，日志为证，全部单次运行）：
    ① 标记缺失 -> 非正常关机 -> 跳过并提示（guard-01/02/03-abnormal-*）
       `armed=True cleanExitMarker=False -> abnormal` -> `startup auto-apply SKIPPED ... not written this boot`
       -> `lastShutdown=abnormal wouldSkip=YES`；`--yes` 退出码 1，**没有任何能力检查/SMU 行**（一条命令都没发）；
       第二步复用缓存：`already decided this boot -> abnormal; clean-exit marker not re-read`（不会重复消耗标记）。
    ② 标记存在（正常退出）-> 照常走能力检查（guard-04/05-clean-*）
       `armed=True cleanExitMarker=True -> clean` -> `checking capability before touching the SMU` ->
       `skipped: amd-co is not writable (SKU tier / probe rejection); no write command sent`，退出码 0；
       跑完配置里 cpu_co_clean_exit 已被消耗掉、只留 cpu_co_guard_verdict="clean"。
    ③ 首次运行（guard-06-firstrun）：`armed=False cleanExitMarker=False -> first_run` -> 当作干净，照常走检查，
       **不误判为异常**。
  * 真机（部署态）实测：LM 启动日志第一行就是
    `[cpu-co] shutdown guard: armed=False cleanExitMarker=False -> first_run (boot 2026-10-05 09:17)`，
    随后 `startup auto-apply: OFF (cpu_co_auto_apply); nothing written`；安装态配置里**没有** cpu_co_auto_apply，
    只多了 cpu_co_guard_armed / cpu_co_guard_boot / cpu_co_guard_verdict 三个键（开机自动应用开关仍默认关）。
  * 部署：dotnet build 0 错误；用既有 替换LM.ps1 提权替换 DLL + zh-CN 卫星资源（00:08:37），
    重启后进程存活（PID 27184）、无 crash.txt、日志无异常；部署态资源里新键 CpuTuneGuardSkipped /
    CpuTuneGuardSkipToast / CpuTuneGuardSkippedTip / CpuTuneAutoApplyWarningTip 均在、旧键
    CpuTunePerCoreApply / CpuTuneSaveSettings / CpuTuneRestore 均不在。

--------------------------------------------------------------------------------
五、小工具回归（tools\\smu-co-tool，重建 + 只读回归）
--------------------------------------------------------------------------------
  * build.ps1 重建：app-fd 4 文件 349,084 B；app-sc 11,145,661 B（与上一轮一致，源码未改）。
  * tool-regress（提权，5 步）：
    tool-01-readonly-8core        -> 8 核全 status=OK、CO 0、`write performed: NO`、exit 0；
    tool-02-probe-read-dryrun     -> exit 0；
    tool-03-corewrite-dryrun      -> exit 0；
    tool-04-corewrite-guard-refused -> 本机 CPUID 预过滤 REFUSED、**PawnIO 都没打开**、exit 1（守卫证据）；
    tool-05-guard-selftest        -> 10/10 期望符合、mismatches 0、exit 0。

--------------------------------------------------------------------------------
六、台式机验证步骤（本轮重点：开机自动应用 + 非正常关机保底）
--------------------------------------------------------------------------------
准备：桌面 Vermeer（如 5700X）上装好本 DLL；`--cpu-tuning-probe` 里应能看到
  `per-core CO write: MP1 0x35 per core available (Vermeer, measured CORE-WRITE-OK)`。
1. 界面对照：自定义模式 -> CPU 调节。确认只有一排按钮（检测能力/应用/重置）、重置无括号、
   红色提示 2 行、每核 8 框填着实读值、开关默认关。
2. 手动路径：把 8 个每核框填成想要的负值 -> 点「应用」-> 应看到逐核写日志
   `[cpu-co] per-core write coreN = x CO -> OK ...` + 回读校验 + `[cpu-co] saved: per-core [...]`；
   再点「重置」-> 二次确认（列出将写入 0 与当前基线）-> 逐核 0 + 全核兜底 + 退出固定频率的日志。
3. 打开「开机自动应用」：二次确认框会列出将写入的值与当前基线；确认后配置里
   cpu_co_auto_apply=1、cpu_co_coreN=你填的值。
4. **正常关机一次**（或托盘退出），确认配置里出现 `cpu_co_clean_exit: 1`，并且日志有
   `[cpu-co] clean-exit marker written (...)`。
5. 再开机：日志应为 `armed=True cleanExitMarker=True -> clean` + 能力检查通过 +
   `startup auto-apply writing per-core CO [...]`；窗口里**没有**保底状态行。
6. **保底验证**：开机自动应用成功之后，用任务管理器**结束 L-Mechrevo 进程**（或断电/蓝屏），
   再开机。预期：
     * 日志 `armed=True cleanExitMarker=False -> abnormal` + `startup auto-apply SKIPPED ...`；
     * 一条 Toast（仅开关为开时）「上次非正常关机，未自动应用 CO」；
     * 自定义模式 -> CPU 调节 窗口里出现红色一行「上次非正常关机，本次未自动应用。开关：自定义模式 → CPU 调节。」；
     * SMU 里**没有**被重新写入（可用 `只读检查-不写入.bat` / `SmuCoTool.exe -5` 回读确认仍是本次开机的值）；
     * 之后正常关机一次，再开机即恢复正常自动应用（状态行消失）。
7. 不想再自动写入：在窗口里把开关关掉（或清掉 cpu_co_auto_apply），保底提示也不再出现。
8. 诊断入口：`L-Mechrevo.exe --cpu-co-auto-apply`（不带 --yes 只打印计划）会先打印
   `cpu-co shutdown guard: lastShutdown=... wouldSkip=...`，可直接看到这次的判定。

--------------------------------------------------------------------------------
七、未确定项 / 没碰过的东西
--------------------------------------------------------------------------------
  * 未确定 / 已知边界：
    1) 保底判定是**按开机**的：同一次开机内 LM 被强杀后**再次启动 LM**（机器没重启）会复用本次开机
       已判定的结论，不会跳过。跨重启（崩溃/蓝屏/断电/被任务管理器结束 -> 重启）才是这套机制覆盖的场景
       （也正是"开机自动应用"会产生崩溃循环的场景）。要覆盖"同一开机内强杀后重启 LM"需要再加
       "会话存活标记 + 单实例互斥体判别"，本轮**没做**（依据不足，且会给 CLI 诊断入口带来误判风险）。
    2) 系统时间在**同一次开机内**被大幅调整（NTP/手动/DST）会让"开机身份"变化 -> 该次判定会重算，
       可能出现一次**保守的**误判（跳过自动应用并提示），下一次正常重启即恢复。
    3) 关机过程中若 SessionEnding 已写标记、之后关机被卡住并被强制断电，则下次开机会看成"正常"——
       这类"卡住的关机"本轮不识别（EventLog 6008/41 交叉核对是用户给的可选加强，未做）。
    4) `Restore()` 保留但 UI 不再直接调用（合并进 ResetAll）；它是既有恢复语义的实现，留作参考/将来的
       CLI 与自检入口。RuntimeCpuTuning.LastAutoApplyDetail 目前主要是写到日志旁白的诊断状态。
    5) 12/16 核 Vermeer（5900X/5950X）上每核网格会变成 4 列 / 8 行的排版，未做 DPI/窄窗实测（沿用上一轮结论）。
  * 没碰过的东西：
    * 清单 B（igpu_only_ui / PendingAlreadySatisfied / ResolveGpuModeStatus / ModeProfileConfig /
      EcChargeLimit / PowerSleepGuard）与 ⑦A 的 UV 层级过滤（CpuInfo.cs 未在改动清单里）；
    * 既有写入语义：SetCoAll / ClockCommand / SetOcClock / DisableOc / Publish 等一字未改；
      每核写路径（SetCoPerCore / EncodePerCoreCurveArg）一字未改；ResetAll 只是把既有动作编排到一起；
    * ①~⑥ 已合并内容未回退；
    * **未打包、未打 NoGCU、installer-src\\ 一字未动。**
    * 本机（笔记本）**没有执行任何写入**：没有点应用/重置，没有在安装态配置里打开自动应用开关；
      保底机制的两条路径全部用**临时配置**（LMECHREVO_CONFIG_FILE 指向证据目录）验证。

--------------------------------------------------------------------------------
八、备份与产物
--------------------------------------------------------------------------------
  * 改前备份：D:\\openLM\\backup\\20261005_2730_pre_uifix\\（9 个待改文件 + pre-change-hashes.txt）；
    改后哈希：同目录 post-change-hashes.txt（9 CHANGED / 2 NEW / UiAuditRunner.cs UNCHANGED）。
  * 证据目录：D:\\openLM\\diagnostics\\uifix-20261005-2730-final3\\
    （probe JSON、窗口截图 5 张、控件取证文本（含两个二次确认框文本）、保底两条路径 6 步日志、
      小工具 5 项回归输出、_run_uifix_evidence.txt / _run_guard_only.txt 两份 transcript）。
  * 新增脚本：tools\\uifix_resx.py（改 7 键 + 删 4 键 + 加 4 键 + Strings.cs 同步）、
    tools\\uifix_resx2.py（二次确认框基线标签写清来源）；
    证据脚本：diagnostics\\uifix-20261005-2730-final3\\run_uifix_evidence.ps1 / run_guard_only.ps1 /
    regress_uifix.ps1 / run_all_elevated.ps1。
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as handle:
    handle.write(ENTRY)
print('appended to 修改日志.txt, bytes =', len(ENTRY.encode('utf-8')))
