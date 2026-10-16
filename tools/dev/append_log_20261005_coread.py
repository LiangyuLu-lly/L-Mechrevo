# -*- coding: utf-8 -*-
# 追加本次操作记录到 D:\openLM\修改日志.txt 末尾（utf-8 追加，不覆盖；行尾沿用 CRLF）
import io, os

LOG = r'D:\openLM\修改日志.txt'
assert os.path.exists(LOG)

ENTRY = u"""
================================================================================
[2026-10-05 23:00] 实现 CO 回读（只读 0xE1 等）+ 新增台式机用 SMU CO 往返验证工具

【用户原话（本轮需求）】
「修复 我这个机器不支持降压写了也没有用,但是我有一个AMD的台式机是支持的
  如果想要测试可以单独写一个脚本来验证」
即：① 把 CO 回读修好；② 另外写一个独立脚本/工具，给那台支持降压的 AMD 台式机做验证。
⚠️ 用户明确要求：**绝对不要在本机（这台 8745H 笔记本）执行任何写入测试** ——
   本机不支持降压、写了也没用。本轮严格遵守：本机只跑"只读"路径，一次写入都没有发生。

【任务 A：CO 回读（纯只读）】
1) source\\PawnIO\\RyzenSmuService.cs（+136 / -2）
   * 新增**只读**命令表 CurveReadCommand(cpu)（每核一条 GetDldoPsmMargin）：
       Phoenix / Phoenix2 / HawkPoint        -> PSMU 0xE1
       Cezanne                               -> PSMU 0xC3
       Rembrandt / Mendocino                 -> PSMU 0x2F
       StrixPoint / StrixHalo / KrackanPoint -> PSMU 0xAF
       Raphael / GraniteRidge / DragonRange  -> PSMU 0xD5
       ShimadaPeak                           -> PSMU 0xA3
       其余（Renoir / Lucienne / VanGogh / Picasso / RavenRidge / Zen1 / Zen2 ...）-> null = 不可回读
       （上游对 Zen1/Zen2 自己标了 "// Not sure" / "Works sometimes"，**故意不采用**；
         KrackanPoint2 也刻意不列，因为上游表里没有它的读命令，不猜 ID）
   * 新增 SupportsCurveRead(cpu)。
   * 新增 ReadCurveMargin(int coreIndex, out uint[] response)（public）+ 内部
     ReadCurveMarginRaw(uint arg0, out uint[] response)（供台式机工具测 arg0 编码）。
   * 新增 SendPsmuRaw(cmd, args, out response)：**把 MailboxRaw 已经读回的 6 个 response word
     带出来**（老代码 SendPsmu 把它们丢了）。SendPsmu 改为委托给它，语义不变。
   * 新增 DecodeCurveMargin(raw)：32 位有符号 -> 20 位有符号 -> 16 位有符号，取最像 CO 步数
     （-50..50）的那个；都不像则 null。原因：上游 MakePsmMarginArg 编 16 位，而 LM 自己的
     EncodeCurve 编 20 位，具体哪一段进 response 未实测（本机读到 0，三种解一致）。
   * ⚠️ 写入路径**逐字未改**（用 SHA256 逐方法比对过备份）：SetCoAll / SetOcClock / DisableOc /
     SetCoGfx / SetThm / SetAllLimits / SendMp1 / MailboxRaw / GetMp1Addrs / GetPsmuAddrs /
     EncodeCurve —— 11 个方法哈希全部与 pre_coread 备份一致。
2) source\\PawnIO\\CpuInfo.cs（+66 / -0，**纯新增**，不动 ⑦A 的 SkuTuningRejection/ClassifyUvSkuTier）
   * 新增 CpuInfo.PhysicalCoreCount：GetLogicalProcessorInformationEx(RelationProcessorCore)
     记录条数（本机实测 = 8；Environment.ProcessorCount 是 16，含 SMT，不能用）。
     失败退回 Environment.ProcessorCount。
3) source\\MechrevoLite.Hardware\\CpuTuningOption.cs（+16 / -1）
   * 新增两个**可选**尾参（默认 null，位置参数调用不受影响）：
     CoreReadback（逐核回读值）、ReadbackNoteKey（Current 为 null 时 UI 用哪个 resx 键）。
4) source\\MechrevoLite.Hardware\\RuntimeCpuTuning.cs（+62 / -5）
   * Probe() 里**只读一次**：有 curve 命令时逐核 ReadCurveMargin(0..PhysicalCoreCount-1)，
     8 核 = 8 条命令；填 Current（全核一致时）+ CoreReadback（逐核值）。
     * 平台没有读命令 -> Current=null + ReadbackNoteKey=CpuTuneNoReadCmd（不伪造 0）
     * 有命令但读失败/解不出 -> Current=null + CpuTuneReadFailed（Detail 里带 core 号、
       SMU 状态与原 hex）
     * 逐核不一致 -> Current=null（数值框回退 0），CoreReadback 仍带逐核值，UI 显示范围
   * **绝不放进轮询/定时刷新**：只在 Probe（窗口打开 / 点"检测能力" / --cpu-tuning-probe）时读。
   * amd-clock（固定频率）带上 CpuTuneClockNoReadback（固定频率确实没有 Getter，说清楚原因）。
   * 被 SKU 层级锁掉的 amd-co **照样读**：能不能读与能不能写是两把锁；CanWrite 仍只由
     SkuTuningRejection 决定（本机 R7 依然 CanWrite=false）。
5) source\\MechrevoLite\\CpuTuningForm.cs（+63 / -15）
   * 新增 ReadbackText(option) 五态文案（键名沿用 CpuTune* 体系，代码里不硬编码中文）。
   * 不可用选项不再用一句"不可用"盖掉读数：显示"不可用: 原因" + 换行 + "回读: …"。
   * 默认选中项优先取"有逐核回读值"的那一条（其次才是"第一条可写的"）——这样打开窗口
     第一眼就是"回读: 0 CO"，而不是固定频率那条的"不可回读"；**不影响 CanWrite / Apply**。
   * _current 标签加 Name="cpuTuneCurrent"（证据工具用）。
6) 文案（MechrevoLite.Properties.Strings.resx + Strings.zh-CN.resx，各 +20 / -2，中英同步）
   新增：CpuTuneReadbackValue = "{0} CO（SMU 当前生效值，重启后失效）"
         CpuTuneReadbackZero  = "0 CO（未检测到偏移；也可能是本次开机的默认值）"
         CpuTuneReadbackMixed = "逐核不一致（{0} ~ {1} CO，共 {2} 核；逐核明细见下方说明）"
         CpuTuneNoReadCmd     = "不可回读（该平台没有回读命令）"
         CpuTuneReadFailed    = "不可回读（读取失败）"
         CpuTuneClockNoReadback = "不可回读（固定频率缺少独立回读）"
   改写总述：CpuTuneSmuReady / CpuTuneSmuPartial 里"CO 和固定频率缺少独立设置回读" ->
         "固定频率缺少独立回读；CO 可回读（读的是 SMU 当前状态，不是上次写入记录）。
          成功仍仅表示 SMU 接受命令。"
   注：Strings.cs（生成的设计器）里本来就没有 CpuTune* 属性，代码走
       Strings.ResourceManager.GetString(key)，所以只改两个 resx（与既有 CpuTune* 键的处理一致）。
7) source\\MechrevoLite.UI\\UiAuditRunner.cs（+11 / -4）
   * AMD fixture 补上逐核回读值；新增 CpuTune-AMD-NoReadCmd fixture（覆盖"无读命令"文案）；
     fixture 数组长度 22 -> 23。
8) --cpu-tuning-probe 输出：因为 Probe() 填了 Current/CoreReadback，JSON 自动带上回读值
   （JSON 序列化整个 CpuTuningProbe，未改序列化代码）。

【任务 A 本机验收（只读，已通过）】
部署（提权跑 替换LM.ps1，未加 -Wait；轮询确认 DLL 2008064 字节与源一致；LM 已重启 PID 23240，
日志无异常）后，提权运行已部署的 L-Mechrevo.exe：
* --cpu-tuning-probe D:\\openLM\\diagnostics\\cpu-tuning-probe-coread2.json 得到：
    "Detail": "Phoenix; SMU 0x4C6500; CO readback per core [0, 0, 0, 0, 0, 0, 0, 0]
               (SMU current state, this boot only); amd-co unavailable: R7 tier ..."
    amd-co: "Current": 0, "CanWrite": false, "CoreReadback": [0,0,0,0,0,0,0,0]
    amd-clock: "Current": null, "ReadbackNoteKey": "CpuTuneClockNoReadback"
  -> **8 核全部读回 0，且从"不可回读"变成了有回读值**（对比上一轮同机 JSON 里 Current=null）。
* 真实窗口截图（用 tools\\uvshot 渲染真实窗体 + 真实探针数据，只读、不点 Apply/Restore）：
    D:\\openLM\\diagnostics\\cpu-tuning-window-coread2.png（打开即选中有回读的 CO 那条）
    D:\\openLM\\diagnostics\\cpu-tuning-window-coread2-co-selected.png
    D:\\openLM\\diagnostics\\custom-mode-entry-coread2.png
    文字证据 D:\\openLM\\diagnostics\\cpu-tuning-evidence-coread2.txt 里：
      readbackText = 不可用: R7 tier does not expose UV (tier 9 only: ...)
                     ||  回读: 0 CO（未检测到偏移；也可能是本次开机的默认值）
      apply enabled with unavailable item selected = False   （不可用选项仍然不能写）
      IsSupportedUV = False / IsSupportedClock = True / EntryVisible = True
  即：用户要求的「打开 CPU 调节应显示 回读: 0 CO（8 核都是 0），而不是"不可回读"」**已达成**；
  同时明确保留了"本机 R7 不能降压"这个事实（不可用 + 原因照旧显示，Apply 仍灰）。

【任务 B：给 AMD 台式机的独立验证工具（重点交付物）】
1) CLI：L-Mechrevo.exe --smu-co-roundtrip [margin] [--yes] [--out <path>]
   实现 source\\MechrevoLite\\SmuCoRoundtrip.cs（新文件，722 行）+ Program.cs 里一个 case（+7 行）。
   流程：前置条件（管理员 / PawnIO / AMD / 模块初始化）-> 打印写入路径与回读路径 ->
   写前逐核读（hex + 十进制）-> arg0 编码探针（裸核号 vs core<<20，写前基线）-> 【仅 --yes】写入
   （**只用现有 SetCoAll**）-> 等 750ms -> 写后逐核读 -> 编码探针（写后判定）-> 判定 ->
   finally 恢复（SetCoAll(0) + 若平台有固定频率命令则 DisableOc()）-> 恢复后逐核读并核对是否全 0
   -> 打印结论 + 落盘报告。
   判定：CONSISTENT（每核差值都等于写入值）/ NOT-CONSISTENT（写后没变化，或只部分变化）/
         UNKNOWN（读命令不可用、读失败、SMU 拒绝写入、或只读模式未写入）；
         编码：BARE / SHIFTED / UNDETERMINED（两种编码都成功且值相同 -> 分不出来，如实写 UNDETERMINED）。
   退出码：0=CONSISTENT 或只读正常；1=前置条件/异常；2=NOT-CONSISTENT；3=UNKNOWN。
   兜底：try/catch/finally 保证"只要写过就一定走恢复"；Ctrl+C 有专门分支先恢复再退出；
         每段读有 30s 超时报错上限；报告默认写 %USERPROFILE%\\Desktop\\SMU-CO-roundtrip-<时间>.txt
         （失败退 %TEMP%）；控制台输出**只用 ASCII**（避免 cmd 代码页乱码），报告文件 UTF-8(带 BOM)。
   ⚠️ 硬约束遵守：**没有新增/修改任何写命令**——写入只用现有 SetCoAll，恢复只用现有 SetCoAll(0)/
      DisableOc()；本文件不含任何新的 SMU 命令 ID（读命令表在 RyzenSmuService 里，是只读表）。
   ⚠️ 没有 --yes 就绝不写入（默认只读）。
2) 启动器与说明（独立目录，可整个拷走）：D:\\openLM\\tools\\smu-co-roundtrip\\
     一键验证-CO往返-会写入.bat   （纯 ASCII .bat，自提权，调 --smu-co-roundtrip -5 --yes，
                                    结尾打印退出码含义、桌面报告完整路径并提示回传）
     只读检查-不写入.bat          （纯 ASCII .bat，自提权，**不带 --yes**，先做安全检查）
     说明.txt                     （UTF-8 带 BOM：前置条件 / 会做什么 / 风险与自动恢复 / 怎么读结果 /
                                    常见问题 / 文件布局与开发者备注）
   布局：把 LM 编译输出整个文件夹放到 .bat 旁边的 lm\\ 子目录（也支持 exe 放 .bat 旁边、
         或已安装在 C:\\Program Files\\L-Mechrevo\\）。找不到 exe 会给明确指引而不是静默失败。
   没装 PawnIO 时 CLI 明确报错退出并给出 https://pawnio.eu/ 与 LM 内"安装缺失驱动"的指引。
3) 本机**只读**验证（用户允许，且**一次写入都没做**）：
   * 提权跑真实启动器（只读那个 .bat，用 cmd + stdin=NUL 模拟双击）：
       Using: ...\\source\\bin\\Release\\...\\L-Mechrevo.exe
       write allowed: NO / core0..core7 status=OK(0x01) resp=[0x00000000 x6] -> CO 0
       编码探针：裸核号 OK -> CO 0；core<<20 OK -> CO 0（全 0，故 UNDETERMINED，与预期一致）
       write performed: NO / round trip: UNKNOWN（note: read-only run）/ 退出码 0
       报告落盘：C:\\Users\\xiaohan\\Desktop\\SMU-CO-roundtrip-20261005-211942.txt（4668 字节）
   * 参数分支验证：--help（退出 0）、--out <路径>（报告写到指定路径）、margin=5 越界（拒绝，退出 1）。
   * ⚠️ 本机**从未**带 --yes 运行过。--smu-co-roundtrip 的写入分支只做了编译层面验证。
4) 留待台式机才能确定的两件事（也是这个工具存在的理由）：
   未确定 1：全核写（Phoenix MP1 0x4C / Raphael PSMU 0x07）能否被每核读（0xE1 / 0xD5）看到
             -> 看 CONSISTENT / NOT-CONSISTENT。
   未确定 2：每核读的 arg0 是裸核号还是 core<<20 -> 看 BARE / SHIFTED / UNDETERMINED。
   （本机全 0 且两种编码都返回 OK，所以这两个问题在本机**无法**判定 —— 用户判断正确。）

【本机没碰的东西 / 保护自检】
* 写入路径：上述 11 个方法逐字未改（SHA256 比对一致）；没有给 CO 开任何强制写路径。
* 清单 B 全部未动（grep 自检 + LastWriteTime 自检）：igpu_only_ui（FirstRunGuideForm /
  ModelScopedConfig / SettingsForm / MechrevoHw）、PendingAlreadySatisfied（GpuRestartVerifier）、
  ResolveGpuModeStatus（MechrevoHw）、ModeProfileConfig（CustomModeForm / SettingsForm /
  MechrevoHw / ModeProfileConfig.cs）、EcChargeLimit（SettingsForm / BatteryControl /
  ChargeLimitMonitor / BranchConvergenceMap / EcChargeLimit.cs）、PowerSleepGuard（SettingsForm /
  PowerSleepGuard.cs）—— 这些文件一个都没进本次改动清单。
* ⑦A 的 UV 层级过滤未回退：CpuInfo.cs 本次是 **+66 / -0 纯新增**（只加了 PhysicalCoreCount），
  SkuTuningRejection / ClassifyUvSkuTier 原样；命名层级自检仍 PASS=33 FAIL=0；探针仍是
  IsSupportedUV=False（R7 不给 UV）、IsSupportedClock=True。
* installer-src\\ 未动（递归时间戳自检为空）；**未打包、未打 NoGCU**；仓库根没有新包。
* 改前备份：D:\\openLM\\backup\\20261005_pre_coread\\（9 个 .cs/.resx +
  Program_LM_pre_coread.cs / uvshot_Program_pre_coread.cs），附 SHA256；
  其中 Program.cs 一开始与 uvshot 的 Program.cs 撞名被覆盖，已用
  backup\\20261005_pre_smuread\\src\\Program.cs（与改后仅差本次 +7 行）修正并留档。

【编译 / 部署】
* 编译：& 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release
  -> 0 错误（1234 个既有风格警告，均为 CS8632/CS0649 之类历史警告）。
* 部署：Start-Process powershell ... -File D:\\openLM\\替换LM.ps1 -Verb RunAs（**未加 -Wait**），
  轮询确认 C:\\Program Files\\L-Mechrevo\\L-Mechrevo.dll 与源一致（2008064 字节）、
  zh-CN 卫星资源已同步（否则新增中文键会回退英文）、LM 已重启且进程存活、日志无异常。
* 证据工具 tools\\uvshot\\Program.cs 只加了日志（把"回读"那一行的文字也写进证据文本），
  未改其行为；其输出与截图见上面"任务 A 本机验收"。

【本轮未确定项】
1. 台式机上"全核写 -> 每核读"是否一致（工具已就绪，等用户在那台机器上跑）。
2. 每核读 arg0 的编码（同上）。
3. 16/20/32 位三种 CO 解码哪一种才是该平台真实语义（本机读数全 0，无法区分；
   工具会把原始 hex 全部打出来，台式机跑完可据此确认）。
4. BIOS 里设的 CO 是否同样体现在每核读上（本机 BIOS 没有该选项，无法验证）。
5. Renoir / Lucienne / VanGogh 等上游无读命令的代号仍然不可回读（本轮不猜 ID、不盲试）。
================================================================================
"""

with io.open(LOG, 'r', encoding='utf-8', newline='') as f:
    existing = f.read()
assert '实现 CO 回读（只读 0xE1 等）' not in existing, 'entry already appended?'

text = ENTRY.replace('\r\n', '\n').replace('\n', '\r\n')
with io.open(LOG, 'a', encoding='utf-8', newline='') as f:
    f.write(text)

with io.open(LOG, 'r', encoding='utf-8', newline='') as f:
    after = f.read()
print('before bytes(chars) =', len(existing))
print('after  chars        =', len(after))
print('appended chars      =', len(after) - len(existing))
print('tail check          =', repr(after[-80:]))
