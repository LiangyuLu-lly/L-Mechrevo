# -*- coding: utf-8 -*-
"""repack9 步骤⑪：生成本轮修改日志条目文本（只写 TEMP 暂存文件；实际追加由 pwsh 用
   [IO.File]::AppendAllText + UTF8Encoding($false) 完成）。行尾与最近若干条一致：**LF**。"""
import io, os, sys, datetime
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

TMP = os.path.join(os.environ["TEMP"], "r9_logentry.txt")
now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

ENTRY = "\n\n" + """
[@NOW@ 037:00] 补署名（点名内嵌 LMechrevo.IntelMSR.bin）并按 0.291.6 出包 + 源码包 r9
用户原话：「补+打包」（=「补掉署名缺口，然后正式出包」）。本轮**唯一功能性改动**就是补两份 notices，
其余全是核对 / 打包 / 验证动作。

① 补署名缺口（只改 installer-src\\files\\THIRD_PARTY_NOTICES,1.txt 与 ,2.txt）：
   两份都新增一段点名 **LMechrevo.IntelMSR.bin**：`5,324 B` / SHA-256
   `D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F` / `LGPL-2.1-or-later`
   / 上游 `namazso/PawnIO.Modules`，并写明「与 RyzenSMU.bin 同一上游、同一份许可文本
   （COPYING.PawnIO.txt 或 LICENSES\\LGPL-2.1.txt）」。英文版插在 `RyzenSMU.bin` 段之后、
   驱动段之前；中文版同位置。措辞**照抄** THIRD-PARTY-NOTICES.md §1.2 B4 / §1.3 与
   Strings.AboutLicensesUsePawnIOIntelMsr（en / zh-CN 两份 resx）的口径（AboutLicensesForm 里
   那一行就是 `PawnIO.Modules IntelMSR.bin`），未自创说法。
   保真：实测两份都是 **UTF-8 无 BOM + 纯 LF**（不是 CRLF），按此写回：4,599 -> 5,022 B（LF 80 -> 86）/
   4,527 -> 4,978 B（LF 77 -> 83）；文件末尾原本就无换行，未添加。
② 把仓库根 THIRD-PARTY-NOTICES.md **纳入安装包**（[Files] 加一条，129,423 -> 129,835 B，CRLF 保真）：
   理由：随包的 `{app}\\LICENSE`（根 LICENSE 横幅）与 `{app}\\LICENSES\\README.md:4` 都写着
   "..\\THIRD-PARTY-NOTICES.md"，不纳入则目标机上这两处引用悬空；它也是 B4 IntelMSR 的权威清单
   （含 5,324 B / SHA-256），粒度比两份 .txt 细，仅 38,177 B。ATTRIBUTION-PATCH-PLAN.md 属内部
   工作文档，**不纳入**。只加 [Files] 一条，不动 [Icons]/[CustomMessages]/任何行为逻辑。
③ 构建产物核对：D:\\openLM\\source\\bin\\Release\\net10.0-windows10.0.19041.0\\
   `L-Mechrevo.dll` 2,594,304 B / 6A9EE40E… / 23:19:39，`L-Mechrevo.exe` 170,496 B / 4AF6EFCB… /
   FileVersion 0.291.6（ProductVersion 0.291.6-beta22.6），卫星 83,456 B / CBF5B3D5…；
   source\\ 下最新文件 23:19:28（两份 resx）< 产物 23:19:39 ⇒ **无未构建改动**，未重新编译。
④ 刷新 installer-src\\files\\（publish 到 rebuild\\lm_fix_build -> tools\\merge_and_clean.py --apply，
   旧 -> 新）：`L-Mechrevo.dll` 771EF2A4…(2,580,480) -> **6A9EE40E…(2,594,304)**；
   `zh-CN\\L-Mechrevo.resources.dll` 2D2CF581… -> **CBF5B3D5…**；
   exe / deps.json / runtimeconfig.json / dll.config 四项逐字节未变（publish 与 build 输出三件同哈希）。
⑤ 旧包备份：D:\\openLM\\backup\\20261010_pre_repack9\\LM-0.291.6-beta22.6-setup.exe
   68,460,550 B / A1669816B4456532C88E55B9AB786B91C9510037268FA4226872CF82B6B990D5 / 2026-10-10 22:36:05
   （与备份副本逐字节同哈希）。
⑥ ISCC 编译校验（⛔ 不污染 Output\\）：`ISCC.exe /O%TEMP%\\iscc_r9_check install_script.iss`
   => **0 错误 / 0 警告**（20.110 s），临时产物已删；`tools\\dev\\check_iss_files.py` => A 类
   （.iss 引用但磁盘不存在）**0 项**，B 类（两个 .iss 都没引用）5 项（L-Mechrevo.pdb、GCU\\gcu-variants.json、
   GamdeMode.db-shm、GCU变体目录说明.md 等，均为既有且故意不打包项）。
⑦ 正式出包（ISCC 默认输出目录，同版本 0.291.6 / beta22.6）：
   D:\\openLM\\installer-src\\Output\\LM-0.291.6-beta22.6-setup.exe
   **68,479,145 B / B3530BD644A70C7CE3E50A771784154E79B44E8B163F6E0C502E2DDD9FA46FE3 / 2026-10-10 23:35:27**
   （ISCC 报 0 错误 / 0 警告；setup.exe 自身 FileVersion 0.291.6）。
⑧ innounp 真解包核对（`innounp.exe -x -q -d%TEMP%\\r9_unpack`，324 条目 = {app} 下 323 + install_script.iss）：
   * 三件 == 当前构建产物：主 DLL 6A9EE40E…（2,594,304）、exe 4AF6EFCB…、卫星 CBF5B3D5…；
     exe FileVersion **0.291.6** / ProductVersion 0.291.6-beta22.6；
   * 逐变体文件数/总字节**全部与参考口径一致**：30=163/27,406,159 · 40=4/17,491,696 · 40B=8/23,858,360 ·
     50=7/21,566,056 · GCU=97/33,844,501 ⇒ 合计 **279 / 124,166,772**；
   * `payload\\50\\...\\SQLiteDB\\` 只有 `GamdeMode.db`（20,480 B），全包扫描 **0** 个 `-shm`/`-wal`；
   * 根 `LICENSE`(35,739) + `LICENSES\\` 三件（GPL-3.0.txt / LGPL-2.1.txt / README.md）+ 新增的
     `THIRD-PARTY-NOTICES.md`(38,177) 均在包内且与磁盘逐字节相同；
   * **两份 THIRD_PARTY_NOTICES,*.txt 现在都点名 LMechrevo.IntelMSR.bin**（并含 5,324 / D6ED85D6… /
     LGPL-2.1-or-later / namazso/PawnIO.Modules；两份都仍点名 RyzenSMU.bin），包内副本与磁盘逐字节相同；
     ⚠️ innounp 按**源文件名**落盘（`THIRD_PARTY_NOTICES,1.txt` / `,2.txt`），DestName 是安装时才生效
     ⇒ 目标机上是 `{app}\\THIRD_PARTY_NOTICES.txt`（后装的 ,2 覆盖 ,1，与本轮之前一致）；
   * 卫星里本轮新文案 **UTF-8 字节实读成功**（⚠️ 按 resx 键名逐条比对，未用 UTF-16 检索）：
     `CpuTuneFreqNote`（新增键，「这两条不是一回事：FMax 是 PBO 的加速上限…」）、
     `CpuTuneSummaryPerCoreOff`（→「每核 CO 写入不可用（该代号没有每核写命令）」）、
     `CpuTunePerCoreCodenameOnly`（→「该代号在 SMU 参考中没有每核写命令…」）；
     主 DLL 里 `CpuTuneValueCo` → "All-core CO offset (legacy; this input box has been removed)"、
     `AboutLicensesUsePawnIOIntelMsr` 英/中两条均在；
   * `GCU\\Install-Gcu.ps1` 含 `-RemoveVendorConsole`/`-KeepVendorConsole`、`GCU\\Uninstall-Gcu.ps1` 含
     `-LmOwnedOnly`，两者与磁盘副本逐字节相同；`COPYING.PawnIO.txt` + `Install-PawnIO.ps1` 在包内。
⑨ 源码包 r9：D:\\openLM\\dist\\LM-0.291.6-beta22.6-source-20261010-r9.zip
   **12,625,147 B / 662820D251A71BB29CB564D36404F9D9B7EAEC705BB767E82DEA96807EE2D699 /
   419 条目（395 文件 + 24 目录）**。口径同 r8（r8 条目集合 + 本轮新增两份 THIRD_PARTY_NOTICES,*.txt），
   包内 395 个文件与磁盘**逐条哈希一致（0 处不同）**；含本轮改动：RyzenSmuService.cs(80,454) ·
   CpuTuningForm.cs(114,440) · RuntimeCpuTuning.cs(86,123) · CpuTuningOption.cs(1,594) ·
   UiAuditRunner.cs(60,894) · IntelOcMailbox.cs / IntelOcDomain.cs / LMechrevo.IntelMSR.bin(5,324) ·
   两份 resx(133,229 / 129,359) · Strings.cs · AboutLicensesForm.cs · THIRD-PARTY-NOTICES.md ·
   ATTRIBUTION-PATCH-PLAN.md · install_script.iss(129,835) · 两份 notices(5,022 / 4,978)；
   ⛔ 不含厂商 payload 二进制 / lm_pkg_files / diagnostics / bin·obj / 日志 / 用户配置 / 崩溃转储
   （禁入项扫描 0 命中）。build.ps1 参考哈希已刷新到当前 DLL/satellite/exe 并保留 r8/r7 旧值；
   SOURCE-README.md 已更新到 r9（含本轮 6 条增量说明）。旧源码包（初版/r2/r4/r5/r6/r7/r8）**全部保留**。
⑩ 清理（先列内容确认是脚手架，再删）：仓库根 `tmp_percorenolog\\`（app 部署副本 + host/resxhost/tablehost
   三个审计小程序 + audit\\ PNG/JSON，共 126 文件 / 89,827,330 B）与 `tmp_uiaudit_intel\\`（app2 + host +
   audit\\ + logs\\ + 构建日志，67 文件 / 36,006,848 B）——两者的最终 UI 截图早已提升到仓库根
   （CpuTuning-AMD.png 111,468 B 等，逐字节同源）⇒ 已删；`%TEMP%\\smudbg_z138\\`（SMUDebugTool 参考工具
   临时副本 13 文件）与 11 个 `%TEMP%\\dsh-spill-*\\` 已删，删除后复核均为 exists=False。
   ⛔ 未动 diagnostics\\ · _upstream\\ · archive\\ · backup\\（仅新建）· dist\\ 里的任何东西。
⑪ 边界：**未安装/未运行安装包、未卸载、未动 installer-src\\files\\GCU\\payload\\ 内容（只读）、
   未从 archive 恢复任何文件、未写注册表/EC/NVRAM、未执行任何 SMU 命令、未写任何 MSR、未连 13688**、
   未装驱动、未启停 GCU 服务；未动 source\\ 业务逻辑（本轮只改两份 notices，另在 install_script.iss
   加一条文件清单）；未动 backup\\(除新建 20261010_pre_repack9 与 merge_and_clean 的 files_clean_*)、
   archive\\、_upstream\\、diagnostics\\、待办.txt。
   ⚠️ 未做/不确定：`打包须知.txt` 里 Install-Gcu.ps1 的基线（81,689 B / 0C334AEA…）与当前包内/磁盘
   副本（86,003 B / 9F5747B344C936C2C12FCA5FB87A34BBCB199DBE836BD718951A8BDFAD857C2B）不符 ——
   与 r8 包内副本**逐字节相同**，属本轮之前就存在的文档漂移，本轮未动该文件，仅如实登记。
   修改日志按字节追加（[IO.File]::AppendAllText + UTF8Encoding($false)，未用 Add-Content）。
""".replace("@NOW@", now)

io.open(TMP, "w", encoding="utf-8", newline="").write(ENTRY)
data = io.open(TMP, "rb").read()
print("staged =", TMP)
print("bytes  =", len(data), " CRLF =", data.count(b"\r\n"), " LF =", data.count(b"\n"))
print("head   =", ENTRY.split("\n")[1])
