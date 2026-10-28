# -*- coding: utf-8 -*-
r"""落实用户规矩：NoGCU 版只在其明确要求时才打包
- 把本次误产的 NoGCU 包从 installer-src\Output 挪到 backup\
- 在 installer-src 下写一份「打包须知.txt」持久记录该约定（含其它打包约定）
- 追加修改日志
"""
import io
import os
import shutil
import datetime

OUT = r'D:\openLM\installer-src\Output'
BAK = r'D:\openLM\backup'
NOTE = r'D:\openLM\installer-src\打包须知.txt'
LOG = r'D:\openLM\修改日志.txt'

NOTE_TEXT = """L-Mechrevo 打包须知（约定）
================================================================================
最后更新：2026-10-10

【一、NoGCU 版：默认不打包】
- **除非用户明确要求，否则不要打包 `install_script_nogcu.iss`。**
  用户原话：「NOGPU(应为 NoGCU)我不提就不要打包这个版本」。
- 若误产出，请把它从 `installer-src\\Output\\` 挪到 `D:\\openLM\\backup\\`，
  保持 Output 目录只留主包。
- ⚠️ **nogcu 包暂不随主包升版：目前只更新 GCU 版本的包**（主 / GCU 包 = 0.291.7-beta22.7）。
  `install_script_nogcu.iss` **保持不动**，现停在 **0.291.5 / beta22.5**；
  `install_script_LMEXP_gen30on40b.iss`（0.291.4 / beta22.4）同样不跟改。
  ——不要再试图"同步"nogcu 的版本号（2026-10-12 用户指示更正）。
  2026-10-09 已把 `install_script_nogcu.iss` 同步为 **0.291.5 / beta22.5**（本轮**未打**该包）。
- ⚠️⚠️ **`install_script_30gcu.iss` 已废弃归档，不要再打包它、也不要再同步它的版本号。**
  2026-10-09 按用户指示「30GCU专用那个实验归档废弃」移到
  `D:\\openLM\\archive\\30gcu_iss_20261009\\install_script_30gcu.iss`（归档前 127,221 B，
  备份 `backup\\20261009_144812_pre_caplight\\installer-src\\`）。
  原因：正式包自己就带 `payload\\30` 并由 `Select-GcuPayload.ps1` 在目标机按 dGPU 分流到 30 变体，
  「40 与 30 的关系」已简化为「base + 覆盖层」，不再需要独立的 30 专用 setup；同批实验的 40B 变体当时已删除
  （2026-10-09 晚按用户要求以最小覆盖层形式加回：`payload\\40B` 10 文件 / 26,740,424 B，见第三、五段）。
  ⚠️ 复活它之前必须先把它 `[Files]` 里 payload\\40 的 **73 行旧独立树**（g40full 形态，已撤销）与磁盘对齐
  —— 现在磁盘 `payload\\40` 只剩 **4** 个覆盖层文件，该 iss 有更多 Source 路径不存在，直接 ISCC 会失败。
  详细复活条件见归档文件顶部横幅。
  ⚠️ `install_script_LMEXP_gen30on40b.iss` 是另一次实验的产物，**不要跟着改**（版本号与文案保持原样）。
- 打包命令（仅在被要求时执行）：
    & 'C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe' 'D:\\openLM\\installer-src\\install_script_nogcu.iss'

【二、主包打包流程（今天验证可用）】
1. 编译源码（必须 0 错误）：
     & 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release
2. publish 到既有目录：
     & 'D:\\openLM\\dotnet-sdk\\dotnet.exe' publish 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release `
         -o 'D:\\openLM\\rebuild\\lm_fix_build' '-p:NoWarn=WFO1000%3BCA1416'
   ⚠️ 不要用 `dotnet publish @tools\\publish_lm.rsp`：本 SDK(10.0.401) 下 rsp 里的
      `-c Release`（空格）与 `;` 分隔的 NoWarn 都会报 MSB1001/MSB1006，
      必须用 `-c:Release` 与 `%3B` 转义（见 2026-10-05 18:00 日志的教训）。
3. 把 publish 产物合并进 installer-src\\files：
     py -3 D:\\openLM\\tools\\merge_and_clean.py --apply
4. ISCC 打包主包（必须 0 Warning / 0 Error）：
     & 'C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe' 'D:\\openLM\\installer-src\\install_script.iss'
     -> 产物：installer-src\\Output\\LM-<版本>-setup.exe

【三、绝不能动的东西】
- `installer-src\\files\\GCU\\Select-GcuPayload.ps1` —— 我们自己的 GCU 分流脚本
  ⚠️ 默认**只可改注释**；要改行为逻辑（白名单 / 载荷表 / 判据）必须由用户明确要求，
  改完必须更新下面的基线哈希。
  （2026-10-09 40back 基线（40B 回归）：**24,112 字节 / SHA256 全文
    EBF70EE54FDE2E15F367F2BE3586932BCC998E25CFCC50635E2DCD63B0C6C942**；
  ⚠️ **2026-10-12 按磁盘逐字节实测复核：24,617 字节 / SHA256 全文
    55DED68AE2C92B95BE8BCF19EFB2FC329BB8BE30066BFA498A110BA809145D3B**。
    本批改动：`$script:Bundle` 加 '40B' 条目、`$script:PayloadEntries` 加 '40B'、
    forced 白名单加 '40B'、SelfTest 加 1 例（forced 40B -> 40B，11/11 通过），其余只改注释；
    自动分流判据（Get-GpuGeneration / Read-LmConfigGeneration / 选择表）与 `payload\\40` 行为**一字未动**。
    上一版 g40slim：22,987 字节 / 5AC8306F…；再上一版：23,923 字节 / 0C63DEE0…；24,214 字节 / 8124735A…）
  ⚠️ 作者 0.290.6 里那份是 15,861 字节，**不要用它的覆盖我们的**。
- `installer-src\\files\\GCU\\payload\\` —— 特别是 `payload\\40`（我们的 40 系载荷，2026-10-10 起为**4 文件覆盖层**）
  与 `payload\\40B`（2026-10-10 起为**8 文件覆盖层 / 23,858,360 B**，见下方清单哈希条）。
  作者版只剩 payload\\{50,1020}，**不要因此删掉我们的 30/40/40B/GCU**。
  ⚠️ **磁盘文件数 ≠ 包内文件数**（2026-10-10 按 50old 底座重新实测，别再用旧数去核包内）：
    磁盘（files\\GCU\\payload\\）：30=163，40=**4**，40B=**8**，50=**7**，GCU=**97**
    包内（{app}\\GCU\\payload\\）：30=163，40=**4**，40B=**8**，50=**7**，GCU=**97**
      （包内计数 = iss `[Files]` 里逐文件列举的 Source 行数，可直接数 iss 得到，**不要去数磁盘**。
       2026-10-10 对齐后两者逐变体相等：`tools\\dev\\check_iss_files.py` 报 0 missing。）
  ★★ 2026-10-10（用户有意把 base 换成「老一点的 50 系 GCU」= 50old 世代）——**base 换代，以下所有
    base 锚定值全部作废，已按新磁盘复算**（复算函数与 Install-Gcu.ps1 的 Get-GcuPayloadIdentity /
    Merge-GcuPayloadManifest 逐字对齐，并先用 2026-10-09 检查点快照的 9 个旧值自证：9/9 逐位相同）：
    磁盘总览（变体=文件数/总字节）：30=163/27,406,159 · 40=4/17,491,696 · 40B=8/23,858,360 ·
      50=7/21,566,056 · **GCU=97/33,844,501**（五变体合计 279 文件 / 124,166,772 B）
    base 换代带来的构成变化（逐条实测）：
      · base 新增：`GCUUtil.exe` 30,888 B、`MyControlCenter\\NVControlSetting.dll` 2,775,224 B、
        `MyControlCenter\\AppSettings\\GamingMonitor.json` 36 B；
      · base 消失：`System.Data.SQLite.dll`、`SQLite.Interop.dll`、`SystemHelper.exe`、`rtknrai.exe`、
        `snynrai.dll`、`SQLiteDB\\GamdeMode.db(-shm/-wal)`、`UEFI_Firmware.dll`（AiStoneService 根下），
        以及 `UserFanTables\\PH4*/PH6*` 全部 25 组机型档（合计 150 条 [Files] 条目被删）；
      · base 仍是 50old 的 Uniwill 完整桥：`GCUBridge.exe` 2,125,496 B / 1.0.1.10（与官方 5.56.51.58 同字节），
        `OSDTpDetect.exe` 522,480 B / 0.0.0.0（50new 是 590,048 B / 2.0.0.1）。
      · 2026-10-10 16:32 另有一处 payload 变动（另一会话的代理所加，已实测确认）：
        `payload\\50\\AiStoneService\\MyControlCenter\\SQLiteDB\\GamdeMode.db`（20,480 B / SHA256
        `56815c3cad739c80eb30f31b6a74453743278a64e9c83a80608555e58c1c9576`，取自官方 50new 只读解包），
        其 `Source` 行已在主 iss 的 50 段（现为该段第 7 条）。同目录的 `GamdeMode.db-shm`（32,768 B）/`-wal`（0 B）
        是本机正在运行的 GCUBridge/GCUService 打开该库时生成的 SQLite 运行期临时件，**故意不列进 [Files]**
        （会随服务启停出现/消失，列进去会让清单漂移、并在没有 GCU 运行的机器上让 ISCC 编译失败）。
    清单哈希（entries / 字节）——**HKLM 的 `GcuPayloadSha256` 记的就是「合并」那一行**：
      base 自身      = **c5197181079a8bc5f2a6c2eb7ece6d524aa45a88976005c9a67130d405af6754**（entries=87 / 33,717,768 B）
      30 覆盖层自身  = 18ed2a6d7c2e5e4ae9dc8c7e68b330bf8abaa389fef8aedbd511c79e10ad7f96（entries=163，未变）
      base+30 合并   = **664011cabf99266500a7edadeac64f8389b36f02d1e33cb762e39c7e751960af**（entries=232 / 45,351,181 B）
      40 覆盖层自身  = **5eebd3c058a7c23640333baa05f95d6f5bf09f6ed921849f510825af968b8f77**（entries=4 / 17,491,696 B）
      base+40 合并   = **41c3b8f326e72265ccbc9d3a0de4ed5d1b5eeda9cab9ba2b31f4ac14cbeafae1**（entries=90 / 48,434,240 B）
      40B 覆盖层自身 = **792f0a2ccae563e2235dfb55a027916383f6fe967656812e3deba1de429ae615**（entries=8 / 23,858,360 B）
      base+40B 合并  = **1afe15ba3822708e691db66c63e490b881faa00c158d427bb879303df22006bb**（entries=90 / 45,936,128 B）
      50 覆盖层自身  = **49d7e824ac3c971ea32b1f491a084cf33c3dc551c189a0d411478ad64dc995e0**（entries=9 / 21,598,824 B）
      base+50 合并   = **e314d751c992a188db6a2c665031e873fe4ef3395a21a279a96c91ede49b3bae**（entries=93 / 49,893,392 B）
      （50 覆盖层自身 entries=7 而磁盘只有 6 个文件 —— `MyControlCenter\\AppSettings\\GamingMonitor.json`
        与 base 同名同路径，身份口径按「相对路径唯一」去重。）
    ⚠️ **上一版 base（50new 世代）锚定的旧值、以及补入 `GamdeMode.db` 之前的 50 值都已作废**，命中它们只说明「装的是旧批次载荷」：
      50 补 SQLiteDB 前：自身 3d5810de…（7）· base+50 581f4d46…（91）
      base 8d5bc85f…（237）· base+30 0fb5b357…（383）· 40 自身 aba3aa75…（5）· base+40 5a4b5d74…（240）·
      40B 自身 f08d297f…（10）· base+40B b3b7fbd9…（240）· 50 自身 292903b2…（4）· base+50 73c79808…（241）。
      这些旧值已一并登记进 `tools\\GCU抓包.ps1` 的 `$script:PayloadOldValues`（现共 16 条）。
    主 iss 各段行数：base **97** / 30 **163** / 40 **4** / 40B **8** / 50 **7**（逐变体 iss 行数 == 磁盘文件数）；
    `[Run]` 仍是 `-Variant Auto`，40B **只能**用显式 `-Variant 40B` 选中 —— 安装包不弹任何变体询问。
  （2026-10-09 g40slim 变更：用户把 40 覆盖层精简到 5 个文件 —— 删掉 README.txt，
    并把 `ACPIDriverDll.dll`、`DisplayInfo.dll`、`GPUInfoDLL.dll`、`OSDTpDetect.exe`、
    `RegistryUtils.dll`、`UWAirplane.dll`、`UWDiskInfo.dll`、`AppSettings\\GamingMonitor.json`
    这 8 个改由 base（`payload\\GCU\\AiStoneService\\`）提供。主 iss 的 40 段随之 13 行 → 5 行，
    与磁盘一一对应；40 段里的 README 说明与最后一处 "40B" 注释也已清掉。）
  （2026-10-09 g40full 变更（**已撤销，留档**）：用户往打包目录放了**完整 40GCU**，故 40 从"13 文件覆盖层"变为
    "73 文件完整独立树"；安装时**整树复制**（不再先铺 base payload\\GCU 再覆盖），base 树仍随包
    暂存、只提供 40 树没有的 `UWACPIDriver` 与 `logo\\`。40 树自带的 `AiStoneService\\install.bat`
    （`sc create GCUBridge` + `sc start`）在铺完后由 Install-Gcu.ps1 调用一次，**非致命**，
    且**不取代**我们自己那段注册（我们跑在它之后、是权威）：install.bat 的裸 `sc create` 非幂等
    （重装必然报 1073），我们的 `Install-Service` 才是 stop+delete+recreate 的可靠路径。
    40B 变体与安装时的询问逻辑**整体移除**（[Files] 行、[Code] 询问、[Run] 的 `{code:}` 取值）。
    另：50/30 仍是"base + 覆盖层"合并，未改。）
- `installer-src\\files\\GCU\\Install-Gcu.ps1`
  （2026-10-09 40back 基线（40B 回归）：**81,689 字节 / SHA256 全文
    0C334AEA61693C2D73B8F04189E9F2B81AE947838B4F4424DB4B9512F873930F**；
  ⚠️ **2026-10-12 按磁盘逐字节实测复核（重出包前）：86,003 字节 / SHA256 全文
    9F5747B344C936C2C12FCA5FB87A34BBCB199DBE836BD718951A8BDFAD857C2B**。
    本批改动：`-Variant` 的 `ValidateSet` 加 `'40B'`、`Get-GcuVariantFamily` 把 `'40B'` 归入 `'40'` 同族
    （两套覆盖层都不自带 UserPofiles，40 <-> 40B 切换时用户模式档保留）、相关注释同步；
    `Uninstall-Gcu.ps1` 本批**未改**（它按服务名 `GCUBridge` 枚举，与变体名无关，基线仍 8,804 字节 / 4A2460AF…）。）
- `installer-src\\files\\GCU\\Uninstall-Gcu.ps1`
  （2026-10-09 g40slim 基线：8,804 字节 / SHA256 全文
    4A2460AF545C7F552D47B581B1EDC9AB5AA757B1128A713FB267DB39375E687C；
  ⚠️ **2026-10-12 按磁盘逐字节实测复核：13,663 字节 / SHA256 全文
    BE1F2BE8287E82FAE9D39DAC77E7AB0185743D68EA53CF3D40065382CF27C3F7**。
    这批**未改**本文件；列出哈希是因为重出包时它必须逐字节随包，便于核对包内副本。）

【四、iss 里的两条不可破坏的逻辑】
- 卸载全删用户数据 / 升级保留 / 全新安装清残留使首次引导重现：
    `GFreshInstall` + `IsFreshInstallFlag()` + `CurUninstallStepChanged()`
    + `UninstallSilent()` + `DelTree('{userappdata}\\MechrevoLite')`
    + `[Run]` 里带 `Check: IsFreshInstallFlag` 的那条 PowerShell
- ⚠️ **不要在 `InitializeSetup()` 里展开 `{app}`**（此刻安装目录未确定，会抛
  "An attempt was made to expand the app constant before it was initialized."）。
  判定"是否已安装"请用注册表卸载项：见 `IsLmAlreadyInstalled()`。

【五、GCU 载荷分层与 -Variant 取值（2026-10-08 补；2026-10-09 g40slim 更新）】
- `files\\GCU\\payload\\` 下五棵树（`30` / `40` / `40B` / `50` / `GCU`）**都随包暂存**是**设计如此**：
  使用哪个变体在**目标机上按 dGPU 决定**（由 `Select-GcuPayload.ps1` 分流），打包时不做取舍。
  ⚠️ **底座树 `payload\\GCU` 必须保持纯净**（它是五棵树共用的底座）；变体覆盖层**只应作用于
  运行目录 `{app}\\GCU\\AiStoneService`，不得写回底座 `payload\\GCU`** —— 写回会污染所有变体。
- ⚠️ 2026-10-09 g40slim：**`payload\\40` 又是覆盖层**（与 50/30 同形：base 先铺、40 覆盖上去，
  同名以覆盖层为准）。g40full 那版"完整独立树 + `Standalone = $true`、整树复制"**已撤销**，
  `$script:PayloadEntries` 里已无 `Standalone` 字段，Install-Gcu.ps1 也不再读它。
- `Install-Gcu.ps1` 的 `-Variant` 取值集合**必须包含分流脚本可能传过来的所有值**
  （`Auto` / `50` / `40` / `30` / `40B`）。⚠️ 40B 不会被自动分流选中（Auto 在 40 系机器上仍是 40），
  只能由 `-Variant 40B` 显式指定。）⚠️ 少一个值，`[Run]` 传参时 PowerShell **参数绑定会直接失败**
  （脚本主体一行都不执行、退出码 1），表现为 GCU 静默装不上。
  ⚠️（原「30GCU 包强制传 -Variant 30」已随 30 专用 iss 归档而作废；`-Variant 30` 仍由
  正式包在目标机上按 dGPU 自动分流时使用，取值集合里 `'30'` 必须保留。）
- （2026-10-08）三个 iss 里原 `[Dirs]` 段的 `{app}\\ru\\` / `{app}\\sv\\` / `{app}\\zh-Hant\\`
  已删除：本包只带 `zh-CN` 资源，这三条不接收任何文件，只会在安装目录里留下空文件夹。
"""

LOG_ENTRY = """
[2026-10-05 19:00] 规矩落地：NoGCU 版默认不打包（并把本次误产品挪出 Output）
================================================================================
用户指示：「NOGPU(NoGCU)我不提就不要打包这个版本」

落实：
1. 把本次误产的 `installer-src\\Output\\LM-0.290.7-beta21.7-NoGCU-setup.exe`
   （18,121,789 B / SHA256 FC08FA27…）挪到 `D:\\openLM\\backup\\`
   -> 保持 `installer-src\\Output\\` 只留主包 ✅
2. 新建 `installer-src\\打包须知.txt`，把打包约定持久化（含：
   ①NoGCU 默认不打包 ②主包四步打包流程 + publish rsp 的踩坑警告
   ③绝不动 Select-GcuPayload.ps1 与 payload\\40 ④iss 里不可破坏的两条逻辑，
   特别是「不要在 InitializeSetup 里展开 {app}」）
3. 备注：`install_script_nogcu.iss` 的版本号仍与主包同步（已升到 0.290.7/beta21.7），
   以后被要求时可直接打。
- 未打包（本次仅整理产物与写约定）
"""


def main():
    # 1) 挪走 NoGCU 包
    moved = []
    if os.path.isdir(OUT):
        for name in os.listdir(OUT):
            if 'NoGCU' in name:
                src = os.path.join(OUT, name)
                dst = os.path.join(BAK, name)
                if os.path.isfile(dst):
                    os.remove(dst)
                shutil.move(src, dst)
                moved.append((src, dst, os.path.getsize(dst)))

    # 2) 写打包须知
    # ⚠️ 此文案必须与 installer-src\打包须知.txt 保持同步（NOTE_TEXT 是那份文件的权威副本，改文件时务必回头一起改 NOTE_TEXT）
    io.open(NOTE, 'w', encoding='utf-8-sig', newline='\r\n').write(NOTE_TEXT)

    # 3) 追加日志
    with io.open(LOG, 'a', encoding='utf-8', newline='') as f:
        f.write(LOG_ENTRY)

    print('挪走的 NoGCU 产物:')
    for s, d, n in moved:
        print('   %s  ->  %s  (%d 字节)' % (os.path.basename(s), d, n))
    if not moved:
        print('   （Output 里没有 NoGCU 产物）')
    print('\n打包须知: %s (%d 字节)' % (NOTE, os.path.getsize(NOTE)))
    print('日志追加: %d 字节' % len(LOG_ENTRY.encode('utf-8')))
    print('\nOutput 目录现状:')
    for name in sorted(os.listdir(OUT)) if os.path.isdir(OUT) else []:
        p = os.path.join(OUT, name)
        print('   %-46s %12d 字节  %s' % (name, os.path.getsize(p),
              datetime.datetime.fromtimestamp(os.path.getmtime(p)).strftime('%m-%d %H:%M:%S')))


if __name__ == '__main__':
    main()
