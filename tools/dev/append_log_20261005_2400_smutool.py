# -*- coding: utf-8 -*-
"""Append one entry to D:\\openLM\\修改日志.txt (UTF-8, append only -- never truncate)."""
import io
import os

LOG = r"D:\openLM\修改日志.txt"

ENTRY = """
================================================================================

[2026-10-05 24:00] 新增独立小工具 smu-co-tool（只验 SMU CO 往返，无需 LM/GCU）
================================================================================

【用户原话】「台式机不是机械革命也没有GCU服务说是 单独验证一个功能没必要这样子吧 脚本优化一下」

【背景】上一轮把 tools\\smu-co-roundtrip\\ 做成了"完全便携版"：把 LM 的自包含单文件
（148,363,339 字节 ≈ 148.4 MB，含 WinForms + Windows Desktop 运行时）整个拷进 lm\\ 子目录。
用户指出目标机**不是机械革命笔记本、也没有 GCU 服务**，为了验证**一个功能**（SMU CO 读写往返）
搬整个 148 MB 的主程序属于过度设计，要求做小而专的独立工具。

【A. 新工程：tools\\smu-co-tool\\（独立，不引用也不修改产品源码）】
* 自己的 csproj：tools\\smu-co-tool\\src\\SmuCoTool.csproj，**不引用** source\\L-Mechrevo.csproj。
* 只**链接**（<Compile Include=... Link=...>，单一事实来源，不复制不粘贴）产品源码 7 个文件：
    source\\PawnIO\\PawnIOWrapper.cs   （\\\\?\\GLOBALROOT\\Device\\PawnIO + DeviceIoControl）
    source\\PawnIO\\RyzenSmuService.cs （PSMU 邮箱 0x3B10A20/0x3B10A80/0x3B10A88、MailboxRaw、
                                        SendPsmuRaw、ReadCurveMargin*、CurveReadCommand、
                                        CurveCommand(SetCoAll)、DecodeCurveMargin）
    source\\PawnIO\\CpuInfo.cs         （IsAMD / Name / DetectedAmdCodeName / PhysicalCoreCount）
    source\\PawnIO\\CpuCodeName.cs / CpuFamily.cs / SmuStatus.cs / PowerLimits.cs
  内嵌模块：source\\LMechrevo.RyzenSMU.bin → <EmbeddedResource LogicalName="LMechrevo.RyzenSMU.bin"/>
  （资源名与产品保持一致，所以 RyzenSmuService.Initialize(Assembly) 原样可用）。
* 工程自己写的文件只有 4 个：
    src\\Program.cs         入口 + --runtime-probe（给 .bat 探测运行时用）+ 退出码
    src\\RoundtripRunner.cs 往返逻辑（**精简重写**，没有链接产品的 SmuCoRoundtrip.cs）
    src\\HostStubs.cs       两个最小替身：MechrevoLite.AppConfig（永远返回默认值）、
                            Logger（WriteLine 空实现）。产品 Logger 的静态构造函数会解析
                            %APPDATA%\\MechrevoLite\\log.txt，替身故意什么都不做 ——
                            这就是"绝不读写 %APPDATA%\\MechrevoLite"的代码级保证。
* 目标框架 net10.0（**不是** net10.0-windows + WinForms）：发布只需 Microsoft.NETCore.App 10。
* **没有包含**：WinForms/任何 UI、GCU、机型适配、配置系统、文件日志系统、注册表写入。

【B. 两种发布（都产出、0 错误）】
* framework-dependent（默认）：app-fd\\ 共 4 个文件，**249,756 字节（约 244 KB / 0.238 MiB）**
    SmuCoTool.exe 162,304 / SmuCoTool.dll 86,528（内含 RyzenSMU.bin）/ .deps.json 475 / .runtimeconfig.json 449
    runtimeconfig.json 里只有 framework "Microsoft.NETCore.App" 10.0.0（无 WindowsDesktop）。
* self-contained 单文件：app-sc\\ 共 1 个文件，**11,113,528 字节（约 10.60 MiB）**
    命令：-r win-x64 --self-contained true -p:PublishSingleFile=true
          -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=true
          -p:EnableCompressionInSingleFile=true
* 体积阶梯（三种都实测只读跑通、退出码 0，取最小的一种发货）：
    不裁剪不压缩 73,606,865 B（70.2 MiB，>60 MB 所以要裁）
    只压缩       37,612,031 B（35.9 MiB）
    裁剪+压缩    11,113,528 B（10.6 MiB）  ← 发货
    旧便携版单文件 148,363,339 B（141.49 MiB）
* 两份合计 **11,363,284 字节（约 10.84 MiB）**，约为旧版的 **1/13**。
* 重建脚本：tools\\smu-co-tool\\build.ps1（两份一起出）。

【C. 启动器与说明】
* tools\\smu-co-tool\\只读检查-不写入.bat（纯 ASCII、CRLF、自动提权、**绝不含 --yes**）、
  tools\\smu-co-tool\\一键验证-CO往返-会写入.bat（纯 ASCII、CRLF、自动提权、带 `-5 --yes`）。
* 两个启动器都**优先用小的 app-fd**：先跑 `SmuCoTool.exe --runtime-probe`，输出里出现
  SMUCOTOOL-RUNTIME-OK 就用 app-fd；否则自动回退到 app-sc，并打印
  "[info] Falling back to the self-contained build ..."；运行时打印 `Build in use: <路径>`。
* 启动器里**不使用括号形式的 if 块**（曾踩到"块内未转义的 ) 提前闭合块"导致误报 not found），
  改成 goto 标签式，避免这一类 cmd 解析坑。
* tools\\smu-co-tool\\说明.txt（UTF-8 带 BOM）：写明"无需安装 LM / 无需 GCU / 不是机械革命也能用"、
  唯一硬前提 = PawnIO 驱动（https://pawnio.eu/ ，缺失时的报错文本与退出码 1）、4 步操作、
  风险与自动恢复、两个版本（小/大）怎么选、体积对比、构建命令、本轮实测摘要。

【D. 顺手修掉"报告每核重复行"（只在新工具里修，产品源码一字未改）】
* 产品 source\\MechrevoLite\\SmuCoRoundtrip.cs 的 ReadAllCores() 每核那行同时调用 W(line) 与
  F(line)，而 W 与 F 都往报告 StringBuilder AppendLine → 报告文件里每核那行重复一次。
* 新工具是独立工程、往返逻辑是精简重写的，发射契约改为：W = 控制台+报告（只追加一次）、
  F = 只进报告且放**另一段中文**，同一段文字永不既走 W 又走 F。
* 实测：新工具报告里 `core0:` 出现 1 次、`core7:` 出现 1 次、`^\\s*core\\d+:` 共 8 行（8 核），不重复。

【E. 验收（全部只读，本机 AMD Ryzen 7 8745H / Phoenix / SMU 0x004C6500 / 8 物理核）】
* 干净临时目录 D:\\openLM\\tmp_smutool_test\\（整个工具目录拷过去，13 个文件 / 11,432,325 字节），
  以管理员身份运行【只读检查-不写入.bat】：
    Build in use: D:\\openLM\\tmp_smutool_test\\app-fd\\SmuCoTool.exe   ← 自动选中小的那份
    codename : Phoenix (family Mobile) / smu version 0x004C6500 / physical cores 8
    write path : MP1 0x4C (all cores, arg=0x000FFFFB)   read path : PSMU 0xE1 per core
    core0..core7 : status=OK(0x01) resp=[0x00000000 x6] -> CO 0        （8 核全 0）
    arg0 编码探针：核1 裸核号 0x00000001 / core<<20 0x00100000 均 OK -> CO 0
    report file : C:\\Users\\xiaohan\\Desktop\\SMU-CO-roundtrip-20261005-214905.txt（UTF-8 带 BOM）
    [READ-ONLY] --yes was not given: NOTHING WAS WRITTEN.
    退出码：Exit code: 0 / SHELL_EXITCODE=0
* 回退路径实测（D:\\openLM\\tmp_smutool_test2\\ 只放 app-sc）：自动打印三行 [info] 并
  改用 app-sc，8 核全 0、桌面报告生成、退出码 0 —— 自包含（裁剪+压缩）版本也能跑通。
* 缺失路径实测（D:\\openLM\\tmp_smutool_test3\\ 两个 build 都不放）：打印结构说明并 **退出码 1**。
* 四种构建形态（fd / sc 不裁剪 / sc 压缩 / sc 裁剪+压缩）逐一实测只读通过、退出码全 0，
  且报告里每核只有 1 行。

【F. 无 LM / 无 GCU / 不依赖安装目录与 %APPDATA% 的证据】
* 三个目录运行前后快照（文件集合 / 字节数 / LastWriteTimeUtc 逐条比对）**零差异**：
    C:\\Program Files\\L-Mechrevo（660 条）  %APPDATA%\\MechrevoLite（5 条）  C:\\ProgramData\\MechrevoLite（2 条）
* 运行期间采样进程模块清单（并集 42 个模块）：非 C:\\Windows 的只有它自己
  D:\\openLM\\tmp_smutool_test\\app-fd\\SmuCoTool.exe + SmuCoTool.dll，
  其余全是 C:\\Program Files\\dotnet\\shared\\Microsoft.NETCore.App\\10.0.11 的 BCL；
  **L-Mechrevo 相关模块 = 0**，也没有 MechrevoLite 的任何 DLL。
* 代码级保证：本工具编译进去的是 HostStubs.cs 里的 Logger（空实现）与 AppConfig（返回默认值），
  产品的 Logger/AppConfig 根本没参与编译；工程里没有任何 GCU / 机型判断代码。
* 唯一共享的是机器级的 PawnIO 驱动（SMU 访问的必经通道）。

【G. 产品工程回归】`dotnet build source\\L-Mechrevo.csproj -c Release` → **0 错误**（1234 个既有警告）。

【H. 没有碰的东西】
* **source\\ 下一个产品源码文件都没改**：链接的 7 个 .cs + LMechrevo.RyzenSMU.bin +
  MechrevoLite\\SmuCoRoundtrip.cs + L-Mechrevo.csproj 的 SHA256 与 mtime 均保持本轮之前的值
  （最新 mtime 21:06:49，本轮从 21:36 之后才开始写文件）。只有 source\\bin\\ 与 source\\obj\\ 被
  约束 5 要求的回归 build 刷新（属生成物）。
* 没新增/修改任何 SMU 写入命令；**本机没有执行过任何带 --yes 的写入**（四次验收全是 -5 只读）。
* 没动清单 B（igpu_only_ui / PendingAlreadySatisfied / ResolveGpuModeStatus / ModeProfileConfig /
  EcChargeLimit / PowerSleepGuard）与 ⑦A 的 UV 层级过滤。
* 没打包（没打 installer）、没打 NoGCU、installer-src\\ 一个字没动。

【I. 旧目录 tools\\smu-co-roundtrip\\】
* 按用户要求**保留**：说明.txt、两个 .bat 原样未动（mtime 仍为 21:28:22 / 21:32:27）。
* 新增一个指路文件 `_旧版-历史版-请用新的-smu-co-tool.txt`（UTF-8 带 BOM），注明"旧版是历史版、
  新工具在 tools\\smu-co-tool\\、旧目录现状与体积对比"。
* **本轮没有删除旧目录里的任何东西。** 但需要记录一个客观事实：本轮**第一次**列该目录时
  （在做任何写操作之前），`lm\\` 子目录**已经是空的**——那个 148.4 MB 的 L-Mechrevo.exe 在本轮
  开始前就已不在（lm\\ 目录时间戳 2026/10/05 21:41:47）；全盘搜索 D:\\openLM\\tools 下
  已无任何 L-Mechrevo.exe。是否清理旧目录请用户确认新工具可用后再决定。

【J. 备份与证据】D:\\openLM\\backup\\20261005_pre_smutool\\
    build-sizes-and-hashes.txt（两种发布每个文件的字节数与 SHA256、体积阶梯）
    source-integrity.txt（10 个产品文件的 SHA256 + mtime）
    old-dir-state.txt（旧目录现状 + 全盘搜索 L-Mechrevo.exe 的结果）
    readonly-console.txt / modules.txt / snapshot-before.txt / snapshot-after.txt / snapshot-diff.txt
    fallback-console.txt / nobuild-console.txt / exitcodes.txt / 四种形态的 *.console.txt / *.report.txt
    desktop-report-fd.txt / desktop-report-sc.txt（两份桌面报告原件）
    acceptance.ps1 / extra.ps1（验收脚本）

【K. 未确定项】
* 台式机真机上的 CONSISTENT / NOT-CONSISTENT 与 BARE / SHIFTED 结论仍然未知（本机不支持降压，
  且用户明确禁止在本机写入）——这正是本工具要在那台 AMD 台式机上跑的原因。
* 裁剪后的写入路径（SetCoAll / DisableOc）没有在本机实测（不允许写入），但它与已验证的读路径
  走的是同一条 PawnIO ioctl 通路；若台式机上发现异常，可用 build.ps1 去掉 PublishTrimmed 重出。
================================================================================
"""


def main() -> None:
    if not os.path.exists(LOG):
        raise SystemExit("changelog not found: %s" % LOG)
    before = os.path.getsize(LOG)
    with io.open(LOG, "r", encoding="utf-8") as fh:
        head = fh.read(200)
    has_bom = head.startswith("\ufeff")
    # read the tail so we can guarantee we only append
    with io.open(LOG, "rb") as fh:
        fh.seek(max(0, before - 4))
        tail = fh.read()

    with io.open(LOG, "a", encoding="utf-8", newline="") as fh:
        fh.write(ENTRY)

    after = os.path.getsize(LOG)
    print("changelog : %s" % LOG)
    print("encoding  : utf-8%s" % (" + BOM" if has_bom else " (no BOM)"))
    print("size      : %d -> %d bytes (+%d)" % (before, after, after - before))
    print("tail before append: %r" % tail[-4:])


if __name__ == "__main__":
    main()
