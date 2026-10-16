# -*- coding: utf-8 -*-
"""2026-10-06 01:10：把本次（D-1 执行 + Intel 搁置）追加到 修改日志.txt 末尾（utf-8 追加，不覆盖）。"""
import io
import os

LOG = r'D:\openLM\修改日志.txt'
TITLE = '[2026-10-06 01:10] 执行待办第2条（核显模式按钮收尾 D-1）+ Intel 调校搁置（保留上游代码，等原作者）'

BODY = """================================================================================
用户原话（本次只执行这两个决定）：
  「走2 英特尔让原作者修 搁置保留他的代码就行」
拆解：
  决定一 = 执行《待办-按功能整理.md》第三节「建议的下一步」第 2 条，即 D-1 核显模式按钮收尾；
           并在 D-1 的「三选一」里取【选项 2：保留现在的显示方式，但加一句提示与"试过就记住"】
           （没做选项 1 的受控实验 —— 用户硬约束：本机绝不切核显模式 / 不写 NVRAM）。
           注：本次「走2」的两种读法（第 2 条建议 / D-1 的选项 2）在这里指向同一件事，按后者落地。
  决定二 = 英特尔机型 CPU 调校（上游第⑦B步 / 待办 A-1）搁置：不实现、保留作者那份代码、等原作者修。

--------------------------------------------------------------------------------
一、D-1 做了什么（依据：《待办-按功能整理.md》D-1 原文「保留现在的显示方式，但加一句提示与
    "试过就记住"」+《核显模式判据-存档与结论.md》§6 的 igpu_route_verified 草图）
--------------------------------------------------------------------------------
判定优先级（MechrevoHw.CanOfferIgpuOnly，未改既有顺序，只**插**了一条）：
  1) Gen30 -> false                                        （原样，硬屏蔽）
  2) SupportsIgpuOnly（50 系可靠位）-> 原样                （原样）
  3) 【新增】用户未表态(igpu_only_answer 非 yes/no) 且 已有试过结论(igpu_route_verified 有值)
       -> 1 = 一直显示 / 0 = 不再显示
       **必须排在 show 分支之前**：答「不确定」时 fail-safe 落盘的就是 igpu_only_ui=show，
       否则「试过没用 -> 收起来」永远被它盖住。
  4) igpu_only_ui == hide -> false                        （原样）
  5) igpu_only_ui == show -> true                         （原样，用户明确表态优先于机器结论）
  6) ask（默认）-> CanOfferGpuModeSwitch && SupportsDgpuDirect （原样：代理规则）
新增只读属性 MechrevoHw.IgpuOnlyUiIsGuess = "这个按钮是不是猜出来的"（无可靠判据 + 用户未表态 + 还没试过
  + 按钮确实显示中）—— 只有它为 true 时才加那句提示。

提示文案（两个 resx + Strings.cs 中英同步，最终文本）：
  中文：本机可能不支持，可试一次
  英文：May be unsupported; try once
  「核显模式」按钮 tooltip 追加（中文原文，英文同义）：
    本机没有可靠的办法判断是否支持核显模式，这个按钮是按经验规则显示出来的。可以点一次试试：
    重启后如果真切到了核显模式，我会记住并一直显示它；如果没切过去，我会把它收起来
    （之后仍可在设置里重新打开）。

「试过就记住」链路（只读写 config.json，不发显卡指令、不写 NVRAM、不碰硬件）：
  * 点在「不确定」机型上点核显模式 -> SettingsForm.MarkGpuRestartPending(target=0) 时
    IgpuOnlyAttempt.MarkAttempt() 记 igpu_attempt_pending=1；
  * 下次开机 GpuRestartVerifier.RunAfterBootAsync 算出既有 verdict 后，多调一行
    IgpuOnlyAttempt.RecordBootVerdict(target, verdict)：
      target!=0（用户重启前改了档）-> 作废、不写结论；
      Applied   -> igpu_route_verified=1（以后一直显示）；
      NotApplied-> igpu_route_verified=0（不再猜、收起按钮）；
      Unconfirmed/NotPending -> 不写结论（无数据不硬猜）；
      无论哪种都把 igpu_attempt_pending 清 0。
  既有成员一个字未改：PendingAlreadySatisfied / PendingTarget / Evaluate / TryTakeRebootTarget 全部原样。

逐文件改动清单（改前备份：D:\\openLM\\backup\\20261006_0110_pre_d1\\，含 _manifest.json 的 SHA256）：
  1) source\\MechrevoLite.Hardware\\MechrevoHw.cs
     - CanOfferIgpuOnly 插入第 3 条判定（+14 行注释说明为什么必须排在 show 之前）
     - 新增公共只读属性 IgpuOnlyUiIsGuess
  2) source\\MechrevoLite.Gpu\\IgpuOnlyAttempt.cs（**新增文件**）
     - 键名常量 igpu_only_answer / igpu_route_verified / igpu_attempt_pending；
       AnswerIsUncertain / HasVerdict / TrySucceeded / RememberAnswer / RememberUnknownAnswer /
       MarkAttempt / RecordBootVerdict；文件头明确写"只读写 config，不碰硬件"
  3) source\\MechrevoLite.Gpu\\GpuRestartVerifier.cs（+3 行注释 + 1 行调用）
     - RunAfterBootAsync 在既有 verdict 落日志之后、弹 toast 之前调用 RecordBootVerdict
  4) source\\MechrevoLite\\SettingsForm.cs（3 处）
     - ApplyIgpuOnlyUiSwitch：拨开关 = 明确表态 -> IgpuOnlyAttempt.RememberAnswer(show)
     - MarkGpuRestartPending：target==0 且 IgpuOnlyUiIsGuess -> MarkAttempt()
     - GpuRowStatusText：路由正常且 IgpuOnlyUiIsGuess 时返回 Strings.IgpuOnlyUnverifiedHint
       （位置沿用显卡行右侧那个状态标签 labelGPUFan，不加新控件、不动任何布局尺寸）
     - VisualiseGPUMode：仅当 IgpuOnlyUiIsGuess 时给 buttonEco 的 tooltip 追加 IgpuOnlyUnverifiedTip
  5) source\\MechrevoLite\\FirstRunGuideForm.cs
     - PersistIgpuOnlyChoice：落盘规则与键名一字未动，只**多记**一个态度
       （有->answer=yes、没有->no、不确定/未选/X、Esc->unknown）；+ using MechrevoLite.Gpu
  6) source\\MechrevoLite\\ModelScopedConfig.cs
     - ExactMachineScopedKeys string[19] -> string[22]，加入上述 3 个新键（机器作用域：切不切得过去是硬件事实）
  7) source\\MechrevoLite.Properties.Strings.resx / Strings.zh-CN.resx
     - 新增 IgpuOnlyUnverifiedHint、IgpuOnlyUnverifiedTip（UTF-8 无 BOM + LF 原样保持；
       校验：两文件各 769 个 <data>、键集合完全一致、无重复；脚本 tools\\d1_add_igpu_strings.py 可复查）
  8) source\\MechrevoLite.Properties\\Strings.cs
     - 按 name 字母序新增 2 个访问器
新增/更新工具与文档：
  * tools\\d1_add_igpu_strings.py（文案插入，--check 可复查）
  * tools\\d1shot\\（**新只读取证小工具** D1IgpuShot，模式对齐已归档的 uvshot：fixture 能力位 + 临时配置
    + 真实 SettingsForm 渲染 + 控件属性导出 + 截图；phase 0 还把 RecordBootVerdict 的 4 条分支逐条自检）
  * tools\\d1_run_d1_evidence.ps1（7 个场景 runner）、tools\\d1_probe_elevated.ps1（提权 probe）、
    tools\\d1_intel_evidence.py（Intel 搁置证据生成）
  * 文档：核显模式受控实验-步骤.md（**新增**：需要一台允许切核显模式的 40 系机器；本机不执行）

--------------------------------------------------------------------------------
二、Intel（第⑦B步 / A-1）搁置 —— 只记录与标注，一个字没删
--------------------------------------------------------------------------------
* 保留：RuntimeCpuTuning 的 Intel 分支与 intel-* 选项判定、CpuTuningForm 的 Intel 命名注释、
  两个 resx 里的 Intel 资源键（如 CpuTuneIntelReady）、UiAuditRunner 的 CpuTune-Intel 合成夹具。
* 运行时行为核实（源码级，本机 AMD 走不到该分支）：
    Probe()   -> Intel 时返回 CpuTuneUnsupported，详情 "Intel runtime tuning is not part of this build
                 (step 7B pending)."；不发任何寄存器/邮箱操作
    Apply()   -> intel-* 一律 return Success=false 同一条说明
    Restore() -> intel-* 一律 throw NotSupportedException
    文件头注释明确：IntelOcMailbox + LMechrevo.IntelMSR.bin 是第 7B 步，**故意没进本构建**（不碰 MSR 0x150）
* 产物级检查（部署态 L-Mechrevo.dll）：IntelMSR / IntelOcMailbox / "MSR 0x150" 均**不存在**
  （utf8 与 utf16 都没有）；而 "Intel runtime tuning is not part of this build" 存在（说明拒绝说明是编译进去的）；
  csproj 里只有 AMD 的 LMechrevo.RyzenSMU.bin，没有 Intel 的 .bin
* 证据文件：D:\\openLM\\diagnostics\\d1-20261006-igpuhint\\intel-shelved-evidence.txt

--------------------------------------------------------------------------------
三、本机只读验收（工具：tools\\d1shot；临时配置；绝不写硬件）
--------------------------------------------------------------------------------
证据目录：D:\\openLM\\diagnostics\\d1-20261006-igpuhint\\
  * d1-evidence-s*.txt（7 个场景逐项属性取证 + 截图）、d1-scenario-summary.csv、d1-settings-s*.png
  * 结果（全部 PASS）：
      s1 判据不可靠 + 未表态      -> CanOffer=True  Guess=True  button=True  提示='本机可能不支持，可试一次' switch=True
      s2 判据不可靠 + 答"有"       -> CanOffer=True  Guess=False button=True  提示=''                       switch=True
      s3 能力位可靠(和本机同款)    -> CanOffer=True  Guess=False button=True  提示=''                       switch=True
      s4 试过且成功(verified=1)    -> CanOffer=True  Guess=False button=True  提示=''                       switch=True
      s5 试过且失败(verified=0)    -> CanOffer=False Guess=False button=False 提示=''                       switch=False
      s6 用户明确关掉(ui=hide)     -> CanOffer=False Guess=False button=False 提示=''                       switch=False
      s7 只有 attempt_pending      -> CanOffer=True  Guess=True  button=True  提示='本机可能不支持，可试一次' switch=True
  * 文案宽度自检（同 text-clipping 口径，用控件自身字体实测）：中文 152px / 英文 185px，控件宽 200px -> 都 PASS
  * 记录规则自检（调真实 IgpuOnlyAttempt.RecordBootVerdict，只动临时配置）4/4 PASS：
      Applied->1、NotApplied->0、Unconfirmed->不写、target=1->不写；四种情况 pending 都归 0
  * 实机日志观察（部署后重启，只读）：TrayGPU: IgpuOnly=True SupIgpu=True SupDgpu=True Switch=True IgpuStatus=False；
    GPU route at startup: panel=Discrete dgpu=Present route=Direct; NVRAM display byte @0x62=1
    -> 本机 GCU 当前把 SupIgpu 报成 True，LM 因此走"判据可靠"分支：本机界面上**不会**出现 D-1 那句提示
       （设计如此：判据可靠就不猜；s3 场景 1:1 复现了这一分支）。要看到提示需要 SupIgpu=False 的机器。
  * 没有做的事（硬约束）：本机**没有**切核显模式、**没有**写 NVRAM、没有点任何"应用/重置"、
    没有碰处理器调压；临时配置都在证据目录里，`%APPDATA%\\MechrevoLite\\config.json` 未被本工具改写。

--------------------------------------------------------------------------------
四、编译 / 部署 / probe 回归
--------------------------------------------------------------------------------
* 编译：& 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release
    -> **0 个错误 / 1248 个警告**（改动前基线同为 1248；新文件 IgpuOnlyAttempt.cs 贡献 0 警告）
    日志：diagnostics\\d1-20261006-0110\\build-d1b.txt
* 部署：Start-Process powershell ... -File D:\\openLM\\替换LM.ps1 -Verb RunAs（不加 -Wait）
    轮询确认替换：C:\\Program Files\\L-Mechrevo\\L-Mechrevo.dll -> 2065408 字节 @00:36:25
                 zh-CN\\L-Mechrevo.resources.dll -> 64000 字节 @00:36:25
    重启后进程存活：L-Mechrevo PID 1268（00:40:50 起）；新日志段无 ERROR/异常，
    开机 8 秒的 GPU 路由回读正常（见上）；未出现 [igpu-d1] 行（本机没有发生"试一次"，符合预期）
* probe：--cpu-tuning-probe（提权）exit=0
    StatusKey=CpuTuneSmuPartial；amd-co CanWrite=false（原因：R7 tier does not expose UV ...）；
    amd-clock CanWrite=true；与上一轮部署态 probe **逐字节一致**（Compare-Object = IDENTICAL）
    文件：diagnostics\\d1-20261006-igpuhint\\cpu-tuning-probe-d1.json（+ cpu-tuning-probe-before-d1.json 对照）

--------------------------------------------------------------------------------
五、文档改动（只改了这两个 + 本日志）
--------------------------------------------------------------------------------
1) D:\\openLM\\待办-按功能整理.md（**只改这一个既有文档**）：
   a. A-1 小节整体改写为「⏸ 搁置：不实现、保留上游代码、等原作者修，不需要你做任何事」，
      并写明现状行为（CpuTuneUnsupported + 说明 / Apply·Restore 拒绝 / 不碰寄存器）与证据文件路径；
   b. 第三节「建议的下一步」更新：第 2 条（D-1）标注 ✅ 已完成并说明做了什么、剩下一次**可选**真机实验；
      第 3 条（A-1）标注 ⏸ 已搁置、三选一关闭、前置调研取消；顺带把还等用户拍板的项收敛为 B-1 / B-2；
   c. 连带保持一致：表头加"最近更新"、第四节清单表 A-1/D-1 两行、四之二里 A-1 前置调研划掉、
      文末新增「六、2026-10-06 01:10 的更新记录」。
2) D:\\openLM\\核显模式受控实验-步骤.md（**新增**）：A 组=真切一次+重启+回读的完整步骤与判定表/回滚；
   B 组=零硬件风险的"失败分支"验证（只改 config + 只重启系统）；写明所需机器类型与逃生手段。

--------------------------------------------------------------------------------
六、未确定项 / 没碰过的东西
--------------------------------------------------------------------------------
* 未确定（不硬猜）：
  1) 「点一次 -> 重启 -> 回读」这条链路**没有**在本机走过（本机禁止切核显）——A 组实验未执行，
     已写成文档等一台允许切的机器；"成功分支（verified=1 -> 一直显示）"因此只有代码级 + s4 场景证据。
  2) 本机 GCU 把 SupIgpu 报成 True（与《核显模式判据-存档与结论.md》记的 False 不同）——本机因此走可靠分支、
     看不到 D-1 提示；这是既有"50 系/能力位可靠就不问"的规则，本次**没有**改它（只在下游加了提示）。
     为什么这台机器现在报 True 未查（疑似 GCU 版本/载荷与当时不同），不影响本次改动的正确性。
  3) 老版本升级上来的用户若此前答过"有"，当时只落了 igpu_only_ui=show、**没有** igpu_only_answer，
     于是会被当成"未表态"而看到 D-1 提示（拨一次设置开关或试一次即消失）。这是 fail-safe 方向，未特意迁移。
* 没碰过的东西（自检）：
  * 清单 B 全部原样：igpu_only_ui 机制本体（三选一落盘规则/键名）、PendingAlreadySatisfied、
    ResolveGpuModeStatus、ModeProfileConfig、EcChargeLimit、PowerSleepGuard、①~⑥ 已合并项、
    ⑦A 的 UV 层级过滤、每核 CO（含非正常关机保底）—— 均未改；
  * installer-src\\（一个字没动）、未打包、未打 NoGCU；
  * 没动任何显示路由逻辑、没动核显省电链（_igpuSaving*/SetIGpuOnlySaveMode）；
  * 没有删除任何 Intel 相关代码/资源/枚举；
  * 本机没有写入任何硬件设置（不切显卡模式、不写 NVRAM、不点 CPU 调校的应用/重置/自动应用）。
* 备份与临时文件：
  * 源码改前备份：D:\\openLM\\backup\\20261006_0110_pre_d1\\（8 个文件 + _manifest.json）
  * 证据/临时配置：D:\\openLM\\diagnostics\\d1-20261006-igpuhint\\（含 7 个 tmp-cfg-*.json，都在证据目录里）
  * 编译日志：D:\\openLM\\diagnostics\\d1-20261006-0110\\build-d1.txt / build-d1b.txt
  * 仓库根目录未新增临时文件。
"""

with io.open(LOG, 'r', encoding='utf-8', newline='') as fh:
    old = fh.read()

assert TITLE not in old, 'title already present, refusing to append twice'
with io.open(LOG, 'a', encoding='utf-8', newline='') as fh:
    fh.write('\n' + TITLE + '\n')
    fh.write(BODY)

print('appended:', TITLE)
print('old bytes =', len(old.encode('utf-8')), '-> new bytes =', os.path.getsize(LOG))
