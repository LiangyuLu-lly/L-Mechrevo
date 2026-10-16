# -*- coding: utf-8 -*-
"""
Append the '收尾' session entry to D:\\openLM\\修改日志.txt (utf-8, append only).
Prints the byte/line delta so the append can be verified as non-destructive.
"""
import hashlib
import io
import os

LOG = r'D:\openLM\修改日志.txt'

ENTRY = """
================================================================================
[2026-10-05 19:30] 收尾：InstallerVersion 升级 + Select-GcuPayload 注释修正 + 重打主包
================================================================================
本次目标：三项收尾改动后**只重打主包**（不打 NoGCU）。硬约束：不改 source\\ 下任何 C# 源码。

--------------------------------------------------------------------------------
【任务 A】iss 里 `-InstallerVersion "18.3"` —— 调查结论：**不该改，保留 18.3**
--------------------------------------------------------------------------------
先查 `installer-src\\files\\GCU\\Install-Gcu.ps1` 里这个参数被用来做什么，证据如下：

  L23    param( ... [string]$InstallerVersion, ...)          # 仅此一处声明
  L815-816  if (-not [string]::IsNullOrWhiteSpace($InstallerVersion)) {
                Set-ItemProperty -LiteralPath $key -Name 'GcuInstallerVersion' -Value $InstallerVersion
            }                                   # 写注册表 HKLM\\SOFTWARE\\L-Mechrevo
  L881   $downgrade = Test-InstallerDowngrade -InstalledVersion $marker.GcuInstallerVersion `
                                                 -IncomingVersion $InstallerVersion
  L882-884  if ($downgrade -eq 'Downgrade') {
                throw ("refusing to downgrade: installed L-Mechrevo {0} is newer than this installer {1}; ...")
            }                                   # 抛异常 -> catch -> exit 1 -> 安装失败
  L168-184  function Test-InstallerDowngrade {      # ← 决定性证据
                if ([string]::IsNullOrWhiteSpace($InstalledVersion)) { return 'NoInstalled' }
                if (-not [System.Version]::TryParse($InstalledVersion, [ref]$installed)) { return 'UnknownInstalled' }
                if (-not [System.Version]::TryParse($IncomingVersion,  [ref]$incoming))  { return 'UnknownIncoming' }
                if ($installed -gt $incoming) { return 'Downgrade' }
                return 'NotOlder'
            }
  L123-124 注释原文："Identity is the SHA256 of the service binary. The FileVersion is deliberately
            NOT part of identity"，以及 L169-170 "Ordering uses the installer/app version line
            (shared and monotonic), never the GCU service FileVersion"。

即：**它参与版本比较（防降级判断）+ 写注册表**，不是"仅记录日志"。

[NUMERIC PROOF]（.NET System.Version，与脚本同一 API）
  installed '18.3'    -> 18.3
  incoming  '0.290.7' -> 0.290.7
  18.3 > 0.290.7  ==  True   ==> Test-InstallerDowngrade 返回 'Downgrade'
                                ==> throw ==> exit 1 ==> **安装直接失败**
  控制组：incoming '18.3' 时 18.3 -gt 18.3 == False，返回 'NotOlder'（安全）

[历史动机] tools\\set_version_183.py L28 把 `-InstallerVersion "0.290.3"` 特意改成 `"18.3"`，
  同一提交里还把 AssemblyInfo 与 iss 的 AppVersion/VersionInfoVersion 一起从 0.290.3 抬到 18.3
  （即 18.3 内测版）。所以 18.3 是"当初那条被发行出去的基线"，已经写进了内测版机主机器的
  HKLM\\SOFTWARE\\L-Mechrevo\\GcuInstallerVersion。此后 iss 回到 0.290.x 体系，但这一行没退回。

[唯一消费者核查] 全仓库 grep `GcuInstallerVersion`：只有 Install-Gcu.ps1 与
  GCU语言包与安装脚本\\Install-Gcu.ps1 读写它（历史脚本 Get-GcuVersion.ps1 本体已不在仓库）。
  没有任何代码把它与 AppVersion(0.290.7) 相乘/相比。也就是说：**它是独立的一条必须单调的版本线。**

[结论] 不改，保留 18.3。理由：改成 0.290.7 会让 `installed(18.3) > incoming(0.290.7)` 成立，
  凡是装过 18.3 内测版（或任何写入过 18.3 标记）的机器，一跑新安装包就在 L882 抛异常、exit 1，
  连服务/驱动/防火墙都不会动 —— 这是**硬失败**，不是风格问题。而 18.3 本身安全（等值 -> NotOlder）。
  至于"跟随 LM 版本号"的诉求：18.3 > 0.290.7 已不可逆，除非同时提供一次有序迁移，本次不做。

[两处 iss 的实际改动] **零改动**。已用哈希比对确认两个 iss 与改前备份逐字节一致：
  install_script.iss        89,193 B  SHA256 前16 6BE1A5D200CD6C75  unchanged=True
  install_script_nogcu.iss  13,177 B  SHA256 前16 8233EE470D6D59F2  unchanged=True

⚠️【任务描述勘误】任务书说 nogcu iss 约 L73 也有 `-InstallerVersion "18.3"` —— **实测不成立**：
  * `install_script_nogcu.iss` 全文**只有 1 处** `InstallerVersion`，且是 L7 的 `AppVerName` 字样，
    **没有任何 `-InstallerVersion` 参数**；
  * 它**从不调用 Install-Gcu.ps1**（grep 'Install-Gcu' 无命中，NoGCU 版按设计不含 GCU）；
  * 它的 L73 是 `[Run]` 里那条 `Remove-Item ... MechrevoLite` + `Check: IsFreshInstallFlag`
    （正是《打包须知》第四节要保护的那条），**不可动**。
  所以"两个 iss 都改、保持同步"这一项：**没有可改的对象**，两文件本就同步在
  AppVersion=0.290.7 / VersionInfoVersion=0.290.7（nogcu 另加 (NoGCU) 后缀）。

--------------------------------------------------------------------------------
【任务 B】Select-GcuPayload.ps1 头部注释修正（只改注释，逻辑一行未动）
--------------------------------------------------------------------------------
⚠️【任务描述勘误 2】任务书说"头部注释还在描述 beta21 的两母版设计"。**实测目标文件里没有这段**：
  该段（"Two payloads (beta21): release\\GCU-only ... -Variant / /GCUVARIANT is refused"）
  位于**作者 0.290.6 那份 15,861 B**（beta21\\extract 与 _upstream\\0.290.6 内），
  而我们那份 17,405 B 的头部**早就是** payload 分流语（却被写成"Three GCU variants"）。
  故本次不是"删掉两母版描述"，而是把**已经过时/数字写错的现存注释**改准（见下）。

[改前现存注释的不准之处]（逐条按代码核实）
  1. 写 "Three GCU variants are shipped"，但 `$script:Bundle`（L46-51）实际是 **四棵树**
     GCU / 50 / 40 / 30 —— 数字错。
  2. 未说明 `-Variant` 的真实契约：L36 `[string]$Variant`（**无 ValidateSet**），
     L246-247 只有 `ToUpperInvariant()` 落在 '50'/'40'/'30' 才强制；**其它（含 '70' 这类怪值）
     既不报错也不拒绝，直接走自动探测** —— 实测 `-Variant 70` 正常返回 40（本机 RTX 4060）。
  3. 未说明 `-SinglePayload` / `/GCUVARIANT` **在本版根本不存在**（实测绑定失败：
     "A parameter cannot be found that matches parameter name 'SinglePayload'"），
     而实例里 `.EXAMPLE` 却写着 `-SelfTest`（存在，OK）却没有任何地方提示这个缺失。
  4. 未说明 `-BiosProjectId` 只被透传、**不参与判定**（L80-82 注释明说 BIOS_PROJECT_ID
     必须不决定 payload，实测传它不改结果）。
  5. 未说明 `-SelfTest` 的期望值依赖**机器外部状态**：L311-312 读 LM config 的 dgpu_generation，
     Intel-only 与 unresolved 两个用例的 expect 是 `$intelFallback`。本机 config=Gen40，
     故这两个用例显示 "expected 40" —— 是**正确**而非失败（SelfTest 10/10 PASS）。
  6. 未说明 GPU 名匹配只认**英文/ASCII 厂商串**（L101 `\\bRTX\\s*N[5-9]\\d\\b`），
     中文本地化显卡名走不到这一路，真正兜底的是 PCI device-id（L114-130）。

[改后内容] 头部块（原 L1-30，即 `#Requires` 1 行 + `<# ... #>` 注释块）重写为准确描述：
  * 四棵 payload 树 + 实测文件数/体积：GCU 247/34.35 MB（基座）、50 3/19.15 MB、
    40 4/16.55 MB（磁盘）、30 130/14.83 MB；三者 ServiceDir 同为 'AiStoneService',
    差别在**落在该目录里的字节**，故以 payload identity(SHA256) 而非版本号标识。
  * 分流规则按代码实写：强制 50/40/30（大小写不敏感，绕过硬件探测）-> 否则 GPU 名
    -> 否则 PCI device-id 高字节（50=2B/2C/2D/2E/2F，40=26/27/28，30=22/24/25，
    legacy=1B/1C/1D/1E/1F/20/21）-> 否则 LM config Gen50/Gen40/Gen30
    -> 无 NVIDIA 信号则 50 -> 仍不明则 50（安全底）。并写明 legacy('pre-30')
    **不是第四棵 overlay**，它解析到 30 补丁。
  * 各开关真实行为：-Variant / -SelfTest / -AsJson / -OutputFile / DeviceId / GpuName，
    并显式列入 **NOT PRESENT：-SinglePayload 与 /GCUVARIANT**（附实测报错原文），
    以及 `-Probe` 只是内部函数 Select-GcuPayload 的参数、不是本脚本开关。
  * `.HISTORICAL` 段单独标注：beta21 两母版（release\\GCU-only / release\\GCU-1020）、
    N6 单 payload 期（-Variant//GCUVARIANT 被拒）、退役的 G0 门与
    platform-code -> generation 启发式 —— 全部标明"**均不描述本版**"。

[逐行 diff 证明：只有注释变化]
  用 tools\\prove_selectgcu_comment_only.py（difflib，行号对齐 + 块注释状态机分类）：
    comment lines changed : 101
    blank   lines changed : 0
    CODE    lines changed : 0
    code-line multiset old=297 new=297  identical=True
    RESULT: PASS - not one executable line changed; only comment/blank lines differ.
  另有三重独立保证（写入脚本内 assert，任一不成立即拒绝落盘）：
    ① 头部终止符 `#>` **之后**的全部内容 byte-identical（15,740 B）；
    ② 非注释代码行前后一致（297 行）；
    ③ 与改前记录基线 backup\\selectgcu_code_pre_v2907finish.txt 一致。
  文件签名也保持：ASCII 校验通过（nonascii=0）、CRLF、无 BOM。
  （注：PowerShell `Compare-Object` 按"行内容"配对，插入 71 行后会把后续同序行误报成差异，
    故不作为证据；已改用行号对齐的 Python diff。）

[改后行为等价性实测]
  * `[Parser]::ParseFile` -> parse OK，0 syntax errors
  * `-SelfTest` -> 10 case(s), 0 failed, exit 0（与改前逐字一致）
  * `-Variant 40 -AsJson` -> Variant=40 / ServiceDir=AiStoneService / StagedDir=payload\\40（同改前）

[哈希更新]（因注释变长而变）
  旧：17,405 B / B51CFEFC5F0DF79C5A9D43C8533A6C721A3BC13B0B1495B3C42563BB50558EBF
  新：22,337 B / D462DBF1BCFD20F96091211208B46528C5AB08919C07EC68B3907A1BEB7E2F06
  《打包须知》第三节基线已同步更新（并加注"只可改注释，不可改行为逻辑"）。

--------------------------------------------------------------------------------
【任务 C】重打主包（按《打包须知》第二节流程）
--------------------------------------------------------------------------------
1) 编译源码（未改源码，故只确认）：
     & 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release
   -> 已成功生成 / **0 个警告 / 0 个错误**（增量；日志 tools\\build_02907finish.txt）
2) publish（⚠️ 按须知用显式参数，**未用** @publish_lm.rsp）：
     & 'D:\\openLM\\dotnet-sdk\\dotnet.exe' publish 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release `
         -o 'D:\\openLM\\rebuild\\lm_fix_build' '-p:NoWarn=WFO1000%3BCA1416'
   -> **0 错误 / 0 警告**；产物 L-Mechrevo.exe FileVersion=**0.290.7**，
      ProductVersion=**0.290.7-beta21.7**，与 source\\bin 编译输出 SHA256 逐字节相同
      （0A1735F7BC4A599F…，增量未重编译，符合预期）。日志 tools\\publish_02907finish.txt
3) 合并：
     py -3 D:\\openLM\\tools\\merge_and_clean.py --apply
   -> 冗余清理移走 12 个文件到 backup\\files_clean_20261005_171316；
      合并 **更新 0 个，未变 6 个**（6 项全部"无变化"）。
      复核：Select-GcuPayload.ps1 仍是 22,337 B / D462DBF1…，payload 四目录
      130/4/3/247 与 14.83/16.55/19.15/34.35 MB **未被触碰**。
4) ISCC 打主包：
     & 'C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe' 'D:\\openLM\\installer-src\\install_script.iss'
   -> **Successful compile (16.204 sec)**，Inno Setup 6.7.1。
      日志全量扫描（896 行）："warning" 命中 **0**、"error" 命中 **0**
      -> **0 Warning / 0 Error ✅**（日志 tools\\iscc_02907finish.txt）
5) 产物验证：
     路径     ：D:\\openLM\\installer-src\\Output\\LM-0.290.7-beta21.7-setup.exe
     大小     ：**52,112,917 B（49.70 MB）**（旧 52,111,152 B @17:02:57，+1,765 B）
     时间戳   ：**2026-10-05 17:13:41**
     FileVersion    = **0.290.7**
     ProductVersion = **0.290.7**
     新 SHA256：**343B1C0EB84B2BA1FB1A2DDDD72E14A4AE798873ADCE09758ABEFA7A8C8E37A8**
     （旧 32269E7D7A3FFE29FA7D3C8831E14D34B91F95B253EC0D1A7733806483B8F4EF，已备份到
       backup\\LM-0.290.7-beta21.7-setup_prev17-02-57_pre_v2907finish.exe）
6) innounp **只读解包**核验（未运行、未安装 setup.exe）：
   a) 包内 {app}\\GCU\\Select-GcuPayload.ps1：列表 22,337 B @2026-10-05 17:12；
      实际解出后 SHA256 = **D462DBF1BCFD20F96091211208B46528C5AB08919C07EC68B3907A1BEB7E2F06**
      == 改后新哈希 -> **MATCH ✅**
   b) 包内 {app}\\GCU\\payload\\40：**3 文件 / 17,357,384 B(16.55 MB)**
      GCUService.exe 14,447,864 / NVControlSetting.dll 2,775,208 / UEFI_Firmware.dll 134,312
      三者 SHA256 与磁盘源**逐字节一致**（A2B7D81ECF5C1DBD / 1E483D95EE7A5570 / E4902F551DE0A292）
      与旧包（17:02:57）**完全相同** -> 40 系三件套**没丢** ✅
      ⚠️ 说明：须知原写"4 文件 / 16.55 MB"是**磁盘**计数（含 README.txt），
         **包内从来就是 3 个**：iss 是逐文件列举的（L315-317 只列那三个 exe/dll，
         **没有** README.txt）。旧包同样只有这 3 个，故本次无任何回退。
         已把"磁盘数 ≠ 包内数"及 30 系 130 vs 114 一并补进《打包须知》第三节。

--------------------------------------------------------------------------------
【任务 D】NoGCU：确认未打
--------------------------------------------------------------------------------
  * 本次**只**对 install_script.iss 调了 ISCC；install_script_nogcu.iss 的哈希与改前备份
    逐字节一致（8233EE470D6D59F2…），其 LastWriteTime 17:02:27（**早于**本次 17:13 打包）。
  * installer-src\\Output\\ 现只有 1 个产物：LM-0.290.7-beta21.7-setup.exe
    -> **零 NoGCU 产物**，无需往 backup\\ 挪任何东西。
  * installer-src 下 `*NoGCU*` 只有 iss 本体 + 两个历史残留
    （install_script_nogcu.iss.bak-20261005_023227、_probe\\iscc_nogcu.txt，
     后者 mtime 17:03:04，是**上一次**（非本次）的探测日志）。**均未动**。
  * 另注：历史产物 backup\\LM-18.3内测版-NoGCU-setup.exe（18 MB）存在于 backup\\，符合约定。

--------------------------------------------------------------------------------
【未确定项】
--------------------------------------------------------------------------------
1) `-InstallerVersion "18.3"` 与 LM 0.290.x 的"长期语义"仍未获机主正面确认。本次按**证据**
   保留 18.3（改它会硬失败，见任务 A）。若要真正统一版本线，需要一次**有序迁移**
   （例如让新 iss 传一个 > 18.3 的值，或先让脚本对 18.3 做一次显式重写），
   属行为变更，**不在本次"只改注释+打包"范围**，未做。
2) NoGCU 的 `-InstallerVersion` 之争已澄清为"不存在该参数"；但 NoGCU 版**GCU 相关逻辑整体缺失**
   （不调 Install-Gcu.ps1）是设计使然，仍**未实机验证**其安装行为。
3) 产物 exe **未运行、未安装、未实测**（按硬约束只打包）。
   「卸载全删 / 升级保留 / 全新安装重现首次引导」的实机行为仍**未验证**。
4) `-Variant` 拒收契约不一致：Install-Gcu.ps1 L851 在 `-SinglePayload` 为真时会把它**转发**给
   Select-GcuPayload.ps1，而该脚本没有这个参数 -> 一旦有人传 `-SinglePayload`，L852 调用会
   绑定失败并抛 "GCU payload selection failed"。当前 [Run] 行（iss L444）**没有**传
   `-SinglePayload`，故是**休眠**缺陷。修它要改逻辑，**超出本次范围，未动**（已写进脚本
   .NOTES 供后续参考）。
5) `-AsJson` 只影响输出格式，不影响 Install-Gcu.ps1 解析（它本就按 JSON 取 StagedDir）。
   核显模式显示策略等前序未决项与本次无关，仍未验收。

--------------------------------------------------------------------------------
【没有碰过的东西清单】
--------------------------------------------------------------------------------
* source\\** —— 任何 C# 源码、csproj、AssemblyInfo：**绝对未动**（只 build/publish）。
* installer-src\\files\\GCU\\Select-GcuPayload.ps1 的**行为逻辑**：未动（仅 L1-30 注释；
  逐行 diff 证明 0 行代码变化）。
* installer-src\\files\\GCU\\payload\\** —— 未动任何文件（30/40/50/GCU 计数与体积前后一致）。
* installer-src\\files\\GCU\\Install-Gcu.ps1 / Uninstall-Gcu.ps1 —— 未动（只读调查）。
* installer-src\\install_script.iss 与 install_script_nogcu.iss —— **均未动**（哈希同改前）。
  《打包须知》第四节那两条不可破坏逻辑（GFreshInstall / IsFreshInstallFlag() /
  CurUninstallStepChanged() / UninstallSilent() / DelTree('{userappdata}\\MechrevoLite') /
  [Run] 的 Check: IsFreshInstallFlag）以及 InitializeSetup() 里**不展开 {app}** 的约束：
  一致保持（未编辑这两个文件，天然未破坏；主包仍 0 错 0 警编译通过）。
* **未运行、未安装** 任何 setup.exe；**未动** %APPDATA%\\MechrevoLite\\（只读读取 config.json
  以解释 SelfTest 期望值）与 C:\\Program Files\\L-Mechrevo\\。注册表未写（Test-InstallerDowngrade
  是纯函数，只在内存里算）。
* NoGCU：未用 ISCC 编译、未产出 exe。
* 作者版 0.290.6 的 15,861 B Select-GcuPayload.ps1 —— **未用其覆盖**我们的。

【本次改动的文件（3 个）】
  1. installer-src\\files\\GCU\\Select-GcuPayload.ps1   17,405 -> 22,337 B（注释）
  2. installer-src\\打包须知.txt                        2,879 -> 4,026 B（基线哈希 + 磁盘/包内计数澄清）
  3. 本日志（追加）
【本次新增的辅助脚本】
  tools\\fix_selectgcu_header_comment.py（带 4 条 assert 的注释替换）
  tools\\prove_selectgcu_comment_only.py（行号对齐逐行 diff 证明）
【新增备份（backup\\，均含 _pre_v2907finish）】
  install_script.iss_pre_v2907finish            89,193 B / 6BE1A5D200CD6C75
  install_script_nogcu.iss_pre_v2907finish      13,177 B / 8233EE470D6D59F2
  Select-GcuPayload.ps1_pre_v2907finish         17,405 B / B51CFEFC5F0DF79C
  打包须知.txt_pre_v2907finish                    2,879 B / 111C99218D37BBBC
  selectgcu_code_pre_v2907finish.txt            （297 行代码指纹）
  LM-0.290.7-beta21.7-setup_prev17-02-57_pre_v2907finish.exe
                                                52,111,152 B / 32269E7D7A3FFE29
"""

before = open(LOG, 'rb').read()
before_sha = hashlib.sha256(before).hexdigest().upper()
before_size = len(before)

with io.open(LOG, 'a', encoding='utf-8', newline='') as fh:
    fh.write(ENTRY)

after = open(LOG, 'rb').read()
after_sha = hashlib.sha256(after).hexdigest().upper()

# non-destructive proof: the new bytes must start with exactly the old bytes
print('before bytes : %d' % before_size)
print('after  bytes : %d  (+%d)' % (len(after), len(after) - before_size))
print('before sha256: %s' % before_sha)
print('after  sha256: %s' % after_sha)
prefix_ok = (after[:before_size] == before)
print('prefix preserved (old file is a strict byte-prefix of the new one): %s' % prefix_ok)
assert prefix_ok, 'append was destructive - old bytes are not a prefix of the new file'
print('')
print('lines before : %d' % (before.decode('utf-8').count('\n') + 1))
print('lines after  : %d' % (after.decode('utf-8').count('\n') + 1))
print('')
print('title present: %s' % ('[2026-10-05 19:30] 收尾：InstallerVersion 升级' in after.decode('utf-8')))
