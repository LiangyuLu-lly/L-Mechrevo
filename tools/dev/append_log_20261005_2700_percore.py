# -*- coding: utf-8 -*-
"""2026-10-05 percore-product: 把本轮改动追加到 D:\\openLM\\修改日志.txt **末尾**（utf-8 追加，不覆盖）。"""
import io

ENTRY = """
================================================================================
[2026-10-05 27:00] 产品化每核 CO：每核读写 + 重置(每核写0) + 保存 + 开机自动应用开关(默认关)
================================================================================
用户原话：
  「可以写进产品了 点重置是每个核心写0 然后允许开机自动应用和保存超频设置」

【背景】上一轮台式机实测（D:\\Downloads\\SMU-CO-corewrite-20261005-230824.txt）已确认：
  PSMU 0x7C 每核读可用（8 核全 status=OK，读回 32 位有符号，本机基线 -15）；
  MP1 0x35 每核写成立（只写核 0 = -5 -> 只有 core0 从 -15 变 -5，其余 7 核不变 -> CORE-WRITE-OK）；
  恢复（逐核写回 0 + SetCoAll(0) + DisableOc()）已核实。本轮把它**做进产品**（用户已批准）。

--------------------------------------------------------------------------------
一、改动清单（文件 -> 改了什么）
--------------------------------------------------------------------------------
1. source\\PawnIO\\RyzenSmuService.cs
   * CurveReadCommand：新增 `case CpuCodeName.Vermeer: return 124u;`（0x7C）
     —— 依据写清：Zen3Settings 从 Zen2Settings.cs:35 继承 Rsmu.SMU_MSG_GetDldoPsmMargin = 0x7C
     （原文 `// Not sure`）+ 台式机实测 CANDIDATE-OK + 读回是 **32 位有符号**（DecodeCurveMargin 第 1 档）。
     Matisse **仍然不列**（真 Zen2 桌面未实测；且 CurveCommand(Matisse)=null，放开读也换不来可写能力，
     不放开不会造成回归）—— 结论与理由都写在方法注释里。
   * 新增 `CurvePerCoreCommand` / `SupportsPerCoreCurve` / `EncodePerCoreCurveArg` / `SetCoPerCore`
     —— 每核写白名单**只有 Vermeer**，其余代号返回 null -> SetCoPerCore 直接 UnknownCmd（一条命令都不发）。
   * ReadCurveMargin 的 arg0 改为按代号分派：新增 `EncodeCurveReadArg`（Vermeer = core<<20，其余 = 裸核号）。
     ⚠️ 这是台式机实测暴露出来的差异：写后逐核回读证明 SMU 认 core<<20 字段（核 0 被写成 -5 后，
     用 core<<20 读核 1 仍是 -15，没有跟着变 -5）；而本机 Phoenix 一直是裸核号。**其它代号行为一字未变。**
2. source\\MechrevoLite.Hardware\\CpuTuningOption.cs
   * 新增可选尾参 `bool PerCoreWrite = false`（默认 false = 旧行为；只用于 UI 门控与展示）。
3. source\\MechrevoLite.Hardware\\RuntimeCpuTuning.cs
   * Probe()：算 `perCoreWrite = (skuRejection == null) && SupportsPerCoreCurve(代号)`，
     写进选项并在 Detail 里加一句 `per-core CO write: ... available/not available`（probe 输出可见）。
   * 新增：ApplyPerCore / ResetAllCores / SaveSettings / SetAutoApply / LoadSavedPerCore / LoadSavedAllCore /
     ApplySavedOnStartup(+simulate) / WriteAllCoresZero / EnsurePerCoreWrite / ReadPerCore。
   * Restore()：amd-co 这一项在"每核路径可用"的代号上，先逐核写 0，再走既有 SetCoAll(0)（语义未改）。
4. source\\MechrevoLite\\CpuTuningForm.cs
   * 新增每核 CO 区：N 个每核输入框（N = 实读到的每核值个数，8 核机 = 8；2 列 4 行布局）+
     "进入时实读"说明行 + 「逐核应用」/「重置（每核写 0）」/「保存设置」三个按钮 +
     「开机自动应用这些 CO 值」开关 + 红色警告段。
   * 首次探针后把**当前实读的每核值**填进输入框，并把这份"进入前的值"记在内存里（_perCoreBaseline），
     用于说明行与「重置」的二次确认文案；之后不再自动刷新（不覆盖用户手改）。
   * 「重置」= 二次确认（写明"包括你打开窗口前就存在的偏移，进入时实读：xxx"）-> 逐核写 0 -> 全核 SetCoAll(0)
     兜底 -> 回读 -> **并把保存的每核值一并清零**（否则开着自动应用的机器下次启动会把刚清掉的偏移写回来）。
   * 「应用」（全核）成功时把全核值一并保存；开机自动应用开关打开时把当前输入框的值一并保存。
   * 门控：每核那一组（8 个输入 + 3 个按钮 + 开关）只在 "选中 amd-co AND CanWrite AND PerCoreWrite" 时可编辑；
     本机（R7 APU）整组置灰并在说明行给出原因（SKU 层级）。
   * 窗口 430->560 高，并在打开/首次填充后把滚动条拉回顶部（内容变长后 WinForms 会自己滚到底部按钮）。
5. source\\MechrevoLite\\ModelScopedConfig.cs
   * ExactMachineScopedKeys 5 -> 15 项：新增 cpu_co_core0..7 / cpu_co_all / cpu_co_auto_apply；
     并给 IsMachineScopedKey 加 `cpu_co_` 前缀规则（覆盖 12/16 核 Vermeer 的 core8..15）。
6. source\\MechrevoLite\\Program.cs
   * 新增 `--cpu-co-auto-apply [--yes]`：开机自动应用的手动诊断入口（不带 --yes = 只检查并打印，**不写**）。
   * 开机流程：deferredInitialization 里 SetAutoModes 之后调度一次 ScheduleCpuCoAutoApply()
     （延迟 7 秒 -> RuntimeCpuTuning.ApplySavedOnStartup()；一次性、异常全吞进日志、不弹窗）。
7. source\\MechrevoLite.Properties.Strings.resx / .Strings.zh-CN.resx / MechrevoLite.Properties\\Strings.cs
   * 新增 19 个 CpuTune* 键（中英同步，文本级插入，未做全量 XML 重写）；
   * CpuTuneScope（总述）改写：不再写"不在开机时自动重放"，改为说明"全核手动 + 每核有开机自动应用开关且默认关"。
8. tools\\smu-co-tool\\src\\CoreWriteRunner.cs
   * 产品读表有 Vermeer 后，`--read-candidate vermeer` 不再被"读表非空"整条拒绝：**同一条命令 -> 视为确认**
     （不安装覆盖，走产品读表；现有 .bat 全部继续可用）；候选命令与产品表**不一致**时仍拒绝。
9. tools\\smu-co-tool\\src\\ReadProbeRunner.cs
   * CANDIDATE-OK 的 recommendation 文案更新（旧文案「可加入 CurveReadCommand(Vermeer) = 0x7C；加入后仍需…」
     已过时 —— 0x7C 已经进产品表了）。
10. tools\\uvshot\\Program.cs（**只读取证工具**，不是产品）
   * phase 1 增加每核面板取证：8 个输入框的名字/值/启用状态/范围、三个每核按钮与自动应用开关的
      启用与勾选状态，并把每核网格滚进视野多截一张图。

--------------------------------------------------------------------------------
二、新增配置键与默认值（都按机型作用域）
--------------------------------------------------------------------------------
  * cpu_co_core0 .. cpu_co_coreN —— 每核 CO 值（N = 物理核数；8 核 5700X = 0..7）。默认**不存在** = 视为 0。
  * cpu_co_all                 —— 全核输入框那一份值。默认不存在 = 0。
  * cpu_co_auto_apply          —— 开机自动应用开关。**默认关**（键不存在 = 关）。
    写入时机：显式「保存设置」/ 全核「应用」成功 / 逐核应用成功 / 「重置」/ 开关切换（开=确认后保存当前输入）。
  * 开机自动应用**只重放每核表**（cpu_co_all 只做持久化与回显）：每核是逐核精确值，
    再叠一次全核写（不同 SMU 表）会把逐核值盖掉，用户"按逐核保存"的意图会被静默否定。

--------------------------------------------------------------------------------
三、每核写的实现要点（命令 / 位宽 / arg0 / 限定）
--------------------------------------------------------------------------------
  * 命令 ID：**MP1 0x35**（Mp1Smu.SMU_MSG_SetDldoPsmMargin；上游 Zen3Settings.cs:22，Vermeer 走的就是它）。
  * 通道：MP1 邮箱（GetMp1Addrs(Matisse 家族)，Matisse/Vermeer 共用）。
  * margin：**16 位二补码**（-5 -> 0x0000FFFB）。注意 LM 自己的 EncodeCurve 是低 20 位，两者不同，
    所以每核写**没有**复用 EncodeCurve，而是单独打包。
  * arg0 = (ccd<<28) | ((core%8)<<20) | (margin & 0xFFFF)；无 APOB 映射时沿用上游假设 ccd = core/8
    （8 核单 CCD 的 5700X 上 ccd 恒 0；arg0 会逐条打进日志，可核对）。
  * 范围：与 EncodeCurve 一致 -40..0；UI 侧按选项的 Minimum/Maximum（CO = -30..0）。
  * **桌面限定**：CurvePerCoreCommand 白名单只有 Vermeer（真机实测过的这一型）。
  * **APU 一律拒绝**：上游 APU 每核写把裸核号塞进随后被自己掩掉的字段，上游自己都没验证 -> 不实现；
    Matisse-only（真 Zen2 桌面）、Zen4 桌面（Raphael/GraniteRidge/DragonRange）、Zen5 等一律未批准。
    拒绝是"返回 UnknownCmd + 一条命令都不发"，不是"发出去让固件拒绝"。

--------------------------------------------------------------------------------
四、「重置 = 每核写 0」与「开机自动应用（默认关）」的落地与安全措施
--------------------------------------------------------------------------------
  * 重置：二次确认（MessageBox YesNo/Warning）-> 逐核 MP1 0x35 写 0 -> 既有 SetCoAll(0) 兜底 ->
    逐核回读 -> 保存的每核值清零。确认文案明确写"会把所有核心（包括你原有的偏移）清零"，
    并把进入时实读的基线值列出来（本机全 0；用户那台 5700X 是 8 核全 -15）。
  * 开机自动应用：默认关；打开时二次确认；每次启动（deferredInitialization -> 延迟 7 秒）检查开关，
    写前**必须**过能力检查（amd-co CanWrite + PerCoreWrite），不满足就**跳过并写日志**、
    不弹窗、异常全吞；日志逐条记录：写了哪些值、每条命令的状态与 arg0、回读结果、成功/失败、耗时。
  * 三道锁叠加：① 开关默认关；② EnsurePerCoreWrite（CanWrite + 代号白名单）；
    ③ SetCoPerCore 自身对非白名单代号直接 UnknownCmd（**一条命令都不发**）。
  * 本机（R7 8745H / Phoenix，SKU 层级锁掉 UV）实测：开关打开也只会打印
    `startup auto-apply skipped: amd-co is not writable (SKU tier / probe rejection); no write command sent (22 ms)`。

--------------------------------------------------------------------------------
五、本机只读验收证据（**本机全程没有写入**）
--------------------------------------------------------------------------------
  产物：source L-Mechrevo.dll 2,054,144 B / zh-CN satellite 62,976 B；
        部署态 C:\\Program Files\\L-Mechrevo\\ 两文件哈希与产物**完全一致**；
        LM 进程 PID 26676（23:40:36 起）存活；重启后日志无异常。
  编译：`dotnet build source\\L-Mechrevo.csproj -c Release` -> **0 错误**（1235 个既有 warning，全在无关文件）。
  probe（部署态、提权）：StatusKey=**CpuTuneSmuPartial**；amd-co CanWrite=false / PerCoreWrite=false；
        Detail 带 `per-core CO write: not available for this codename/SKU (all-core only)`。
  窗口取证（uvshot 渲染真实窗口 + 真实 probe，**只读**）：
        per-core input count = **8**（cpuTuneCore0..7，value=0 = 本机实读值，enabled=False，range=[-30..0]）；
        perCoreInfo = 每核 CO 不可用（当前不允许写入）: R7 tier does not expose UV (...)；
        cpuTunePerCoreApply / cpuTuneReset(「重置（每核写 0）」) / cpuTuneSave 全部 enabled=False；
        cpuTuneAutoApply = found，**checked=False**，enabled=False，文案"开机自动应用这些 CO 值"；
        旧按钮 `apply enabled with unavailable item selected = False`（应用仍禁用）。
        截图：cpu-tuning-window-percore*.png / custom-mode-entry-percore.png。
  开机自动应用（部署态真实启动日志）：
        `[cpu-co] startup auto-apply: OFF (cpu_co_auto_apply); nothing written`
  能力拦截证据（用**临时配置文件**把开关打开，不碰安装态配置）：
        模拟：`ON (SIMULATE, --yes not given)` -> `skipped: amd-co is not writable ...; no write command sent (23 ms)`（exit 0）
        真跑：`ON; checking capability ...`        -> `skipped: amd-co is not writable ...; no write command sent (22 ms)`（exit 1）
        ⚠️ 两次都**没有发出任何写命令**（能力检查在写之前拦下）；本机安装态 config.json 里没有任何 cpu_co 键。
  开机自动应用钩子的**缺陷与修正**（本轮自己发现）：最初把钩子放在 SetAutoModes 末尾，实测开机日志里
        **完全没有 [cpu-co] 行** —— 因为 SetAutoModes 开头有节流（wakeup=10s / init=3s），开机时先有一次
        "Monitor Power On" 唤醒调用通过节流，紧接着的 init:true 调用被节流直接 return false，钩子永不执行。
        已改挂到 deferredInitialization（启动流程里只跑一次、不受节流影响）+ 一次性保护，重新部署后日志出现。
  小工具（tools\\smu-co-tool，链接产品源码）：重建 app-fd 4 文件 349,084 B / app-sc 1 文件 11,145,661 B；
        提权只读回归：`SmuCoTool.exe -5` -> 8 核全 OK、CO 0、`write performed: NO`、exit 0；
        `--probe-read vermeer --dry-run` -> 16 条 getter 计划、exit 0；
        `--probe-core-write --read-candidate vermeer --dry-run` -> exit 0（新的"确认同一命令"文案可见）；
        `--probe-core-write ... --read-candidate vermeer --yes` -> 本机 CPUID 预过滤 REFUSED、
        **PawnIO 都没打开**、exit 1（"本机绝不写入"的守卫证据）；
        `--guard-selftest` -> 10/10 期望符合、mismatch 0、exit 0。

--------------------------------------------------------------------------------
六、未确定项 / 没碰过的东西
--------------------------------------------------------------------------------
  * 未确定：
    1) `--read-candidate vermeer` 的"确认同一命令"分支**无法在本机实测**（本机 APU 被 CPUID 预过滤拒绝）；
       需要在那台 5700X 上跑一次 `每核写验证-会写入.bat`（它会带 --read-candidate vermeer）确认输出为
       "CONFIRMS the same command id, no override installed" 且不再 REFUSED。
    2) 恢复语义：本轮「重置」= 逐核写 0 + 清空保存值；**没有**实现"恢复到进入前基线"。
       基线值只在内存里 + 二次确认文案里提示。是否需要"一键恢复基线"由用户决定。
    3) 每核写与"模式自带 UV 全核写"（ModeControl.SetRyzen / cpu_uv）的交互：本轮靠既有 OwnsCurve 保护
       （每核写成功后 OwnsCurve=true -> 模式控制不再覆盖）。未在台式机上实测这一交互。
    4) 4 个每核输入框的排版在 16 核（5900X/5950X）上会变 8 行，未做 DPI/窄窗实测。
  * 没碰过的东西：
    * 清单 B（igpu_only_ui / PendingAlreadySatisfied / ResolveGpuModeStatus / ModeProfileConfig /
      EcChargeLimit / PowerSleepGuard）与 ⑦A 的 UV 层级过滤（CpuInfo.SkuTuningRejection / ClassifyUvSkuTier）
      —— 一行未动（CpuInfo.cs 未在改动清单里）。
    * 既有写入语义：SetCoAll / ClockCommand / Publish / SetOcClock / DisableOc / SetCoGfx / SetThm 未改；
      每核写是**并列的新方法**。Restore() 只是在 amd-co 那一支前面多走一遍逐核 0。
    * ①~⑥ 已合并内容未回退。
    * **未打包、未打 NoGCU、installer-src\\ 一字未动。**
    * 本机（笔记本）**没有执行任何写入**：没有点应用/重置，没有在安装态配置里打开自动应用开关。

--------------------------------------------------------------------------------
七、备份与产物
--------------------------------------------------------------------------------
  * 改前备份：D:\\openLM\\backup\\20261005_2700_pre_percore\\（13 个待改文件 + pre-change-hashes.txt）
    改后哈希：同目录 post-change-hashes.txt（11 个文件确认 CHANGED，UiAuditRunner.cs 未改）。
  * 证据目录：D:\\openLM\\diagnostics\\percore-20261005-2700\\
    （probe JSON、窗口截图、uvshot 取证文本、工具 5 项回归输出、自动应用拦截日志与 transcript、
      log-before-deploy.txt、_check_deployed_strings.py 的部署态字符串核对）。
  * 新增脚本：tools\\add_percore_resx.py / tools\\add_percore_strings_cs.py / tools\\percore_hash_report.py /
    tools\\_add_bom.py（PowerShell 证据脚本加 BOM，避免 PS 5.1 按 ANSI 读中文注释）。
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as handle:
    handle.write(ENTRY)
print('appended to 修改日志.txt, bytes =', len(ENTRY.encode('utf-8')))
