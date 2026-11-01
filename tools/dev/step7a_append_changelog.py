# -*- coding: utf-8 -*-
"""Append the 2026-10-05 20:30 step-7A entry to 修改日志.txt (utf-8, append only)."""
import io
import sys

LOG = r"D:\openLM\修改日志.txt"

ENTRY = """[2026-10-05 20:30] 合并第⑦A步（AMD CPU 调校）+ 强制显示降压入口调试开关
================================================================================
【任务】把作者 0.290.6 的 CPU 调校整块（清单 A-28/A-29/A-32/A-33 AMD 侧/A-41 CLI/A-27 夹具）
搬进 source\\，并加一个**只影响入口可见性**的调试强制开关，然后 build + deploy（**不打包**）。

--------------------------------------------------------------------------------
【0】改前备份（backup\\，命名含 _pre_step7a）—— 8 个文件
  RyzenSmuService_20261005_pre_step7a.cs              14,430 B
  CpuInfo_20261005_pre_step7a.cs                       2,498 B
  ModeControl_20261005_pre_step7a.cs                  19,717 B
  CustomModeForm_20261005_pre_step7a.cs               74,648 B
  Program_20261005_pre_step7a.cs                      62,626 B
  UiAuditRunner_20261005_pre_step7a.cs                43,545 B
  MechrevoLite.Properties.Strings_20261005_pre_step7a.resx        92,186 B
  MechrevoLite.Properties.Strings.zh-CN_20261005_pre_step7a.resx  90,020 B

--------------------------------------------------------------------------------
【一】搬了什么（逐项对照 decomp\\0.290.6）

1) PawnIO\\RyzenSmuService.cs（我们 653 -> 790 行；与上游语义一致，只多 2 处注释）
   * 新增 `_execute` 委托字段 + `public RyzenSmuService()` + `internal RyzenSmuService(CpuCodeName, Func<..>)`
     测试构造（A-35 的"无驱动也能测命令表"）；`ReadReg/WriteReg` 改走 `_execute`。
   * `SetCoAll`：**删掉** 老的 `switch (Family)`（Renoir/Mobile/StrixPoint/StrixHalo/Raphael），
     改成 **按 CpuCodeName 查表** `CurveCommand(_cpu)`，未命中 -> `SmuStatus.UnknownCmd`（拒绝瞎猜）。
   * **新增**：`CurveCommand` / `ClockCommand` / `SupportsClock` / `MaximumClock` /
     `SetOcClock(int mhz)` / `DisableOc()`。
     - `Vermeer -> (PSMU, 11)`（补齐 Vermeer CO）；`Matisse/Vermeer -> (MP1, 36/38/37)` 固定频率；
       `MaximumClock`：Matisse/Raphael=6500，其它=4500。
     - `SetOcClock` 失败会先 `DisableOc()` 回滚，回滚也失败才抛 `"SMU clock rejected: …"`。
   * 验收：`git diff --no-index source\\PawnIO\\RyzenSmuService.cs decomp\\…` 只剩我加的注释与
     反编译器噪声（`|| 1 == 0`、`? true : false`），**无功能差异**。

2) PawnIO\\CpuInfo.cs（84 -> 183 行）—— **决策② 落地**
   * 新增 `internal static CpuCodeName DetectedAmdCodeName`：用 `X86Base.CpuId` 解
     family/model/stepping，再查作者那张完整 codename 表（SummitRidge…StrixHalo，共 38 个 case）。
   * `IsSupportedUV()`：**字符串白名单整段删除**，改为
     `return RyzenSmuService.CurveCommand(DetectedAmdCodeName).HasValue;`
     （原白名单：RYZEN AI MAX / Ryzen AI 9 / Ryzen 9 / 4900H / 4800H / 4600H）
   * `IsSupportedUViGPU()`（`Name.Contains("6900H")`）**按上游保持字符串判据**，未动。

3) 新增类型（纯新增，4 个 .cs）：
   MechrevoLite.Hardware\\CpuTuningOption.cs    record(Id, LabelKey, Minimum, Maximum, Unit, Current, CanWrite)
   MechrevoLite.Hardware\\CpuTuningProbe.cs     record(Cpu, StatusKey, Detail, IReadOnlyList<CpuTuningOption>)
   MechrevoLite.Hardware\\CpuTuningResult.cs    record(Success, Verified, Detail)
   MechrevoLite.Hardware\\RuntimeCpuTuning.cs   单例 + SyncRoot 全局锁 + Probe/Apply/Restore/HasChanges/OwnsCurve
     - **AMD 分支**：作者原样（`amd-co` -30..0 CO；`amd-clock` 1000..MaximumClock MHz；`_denied` 会话内禁用；
       `_changed` 记录；`OwnsCurve`）。
     - **Intel 分支**：⚠️ **故意留桩**（第⑦B 步才做）—— 不引入 `IntelOcMailbox`/`IntelOcDomain`，
       也不引入二进制 `LMechrevo.IntelMSR.bin`。Intel 机型 Probe 返回
       `CpuTuneUnsupported` + 详情 `"Intel runtime tuning is not part of this build (step 7B pending)."`，
       **绝不**去碰 MSR 0x150。`IsHybrid()` 一并删掉（只被 Intel 分支用到）。

4) 新增界面 MechrevoLite\\CpuTuningForm.cs（406 -> 426 行，作者实现 + 强制显示标记）
   选项下拉 + 数值框 + 回读标签 + 按钮（检测能力 / 应用 / 恢复 / 安装缺失驱动）+ PawnIO 官网链接；
   `ApplyProbe` 决定"安装缺失驱动"是否可见；按钮可用性**只**由 `CanWrite` / `HasChanges` /
   `Program.UiAuditMode` 决定（未改）。三态结果文案沿用作者：`CpuTuneVerified`（回读一致）/
   `CpuTuneAccepted`（SMU 已接受、未独立验证）/ `CpuTuneFailed`。

5) **入口（用户该从哪里点开）** —— `MechrevoLite\\CustomModeForm.cs`
   * 作者实现：`flowLayoutPanel2`（"恢复默认 / 风扇曲线"那一行）**无条件**再加一个
     `Name="buttonCpuTuning"` 的按钮，点击 `new CpuTuningForm().ShowDialog(this)`。
   * 我们的接法：同一个位置、同一个 Name；字段名 `_cpuTuningButton`；宽度计入 `_minContentWidth`（val2）。
     ⚠️ **唯一偏离作者**：按钮 **Visible 改为受能力判定控制** =
     `RuntimeCpuTuning.EntryVisible`（见【二】）。作者是"永远显示"。
   * 打开路径：托盘/主窗 →「设置」→「性能模式 / 自定义模式」行 → 打开 **自定义模式** 窗口 →
     底部按钮行第 3 个「**CPU 调节**」→ 弹窗。

6) `MechrevoLite.Mode\\ModeControl.cs`（C-11 互锁）
   * `SetUV()` 整体包 `lock (RuntimeCpuTuning.SyncRoot)`，并加 `RuntimeCpuTuning.OwnsCurve` 短路；
   * `SetRyzen()` 里 CPU UV 那一段同样包锁 + `!OwnsCurve` 前置。
   => 手动在 CPU 调节窗口设过 CO 后，**切模式不会把它覆盖掉**。

7) `MechrevoLite\\Program.cs`（A-41）
   * 新增 CLI：`L-Mechrevo.exe --cpu-tuning-probe [输出.json]`
     （无参数时按作者 `Console.WriteLine`；因本程序是 WinExe 无控制台，**额外**把 JSON 落到
      `%TEMP%\\lm-cpu-tuning-probe.json` 并写日志，避免"什么都没看到"）；
   * 退出清理加一行 `RuntimeCpuTuning.Instance.Dispose();`（放在 `modeControl?.Dispose()` 之后）。

8) `MechrevoLite.Properties.Strings.resx` + `Strings.zh-CN.resx`
   * 追加 **27 个作者 `CpuTune*` 键**（英文取 decomp 的 resx，中文取 `_zhcn_decomp` 的 zh-CN resx）
     + **我们自己的 2 个键** `CpuTuneForcedMark` / `CpuTuneForcedNotice`。
   * 键数 711 -> **740**（en 与 zh-CN 都是 740，逐个校验无缺失）；BOM/XML 结构完好（[xml] 解析通过）。
   * 纯追加脚本：`tools\\step7a_append_cputune_resx.py`（只增不改，已存在则跳过）。

9) `MechrevoLite.UI\\UiAuditRunner.cs`（A-27 补齐第⑥步欠的 3 个夹具）
   * 数组 19 -> **22**：新增 `CpuTune-Intel` / `CpuTune-AMD` / `CpuTune-NoDriver`（作者原文）。

--------------------------------------------------------------------------------
【二】任务二：强制显示降压入口（调试开关）

* 开关名：环境变量 **`LMECHREVO_FORCE_CPU_TUNING`**（沿用本工程 `LMECHREVO_*` 惯例；
  `LMECHREVO_LOG_LEVEL` / `LMECHREVO_CONFIG_FILE` / `LMECHREVO_MODEL_OVERRIDE` 同族；**不写用户配置**）。
  取值 `1` / `true` / `yes` / `on`（大小写不敏感）为开；其余/未设置为关。实现见
  `RuntimeCpuTuning.ForceEntry`（读一次缓存）。

* 用户可直接执行（任选其一）：
    临时（只对本次会话生效，推荐先用这个）：
      powershell -NoProfile -Command "$env:LMECHREVO_FORCE_CPU_TUNING='1'; Start-Process 'C:\\Program Files\\L-Mechrevo\\L-Mechrevo.exe'"
    或持久（用户级，之后**重启 LM** 生效）：
      setx LMECHREVO_FORCE_CPU_TUNING 1
      （取消：setx LMECHREVO_FORCE_CPU_TUNING 0，或 reg delete HKCU\\Environment /v LMECHREVO_FORCE_CPU_TUNING /f）
    注意：`setx` 只影响**之后新启动**的进程；已运行的 LM 必须重启（托盘退出后重新打开）。

* 开关**只**做两件事（严格按用户要求"只把入口可见性这一层打开"）：
  1. `RuntimeCpuTuning.EntryVisible` 直接返回 true -> 自定义模式里「CPU 调节」按钮显示出来；
  2. 可见标记：按钮文本与窗口标题后面加 `CpuTuneForcedMark`（中文"（强制显示）"），
     并在窗口顶部多一行**红色**提示 `CpuTuneForcedNotice`
     （"调试开关已启用：本窗口为强制显示，并非能力检测结果。应用/恢复按钮仍完全按检测结果决定……"）。

* **安全保证**（未改作者逻辑）：开关**不碰** `CanWrite`、不碰 `Apply/Restore` 的可用性。
  不支持时机型的行为：
    - `CpuTuneUnsupported` / `CpuTuneProbeFailed` -> `Options` 为空 -> 下拉空、数值框与「应用」**禁用**，
      状态栏显示"没有检测到支持的可写接口……"（即"当前机型不支持"）；
    - `CpuTuneDriverMissing` -> 只有「安装缺失驱动」可用，应用/恢复禁用；
    - `CpuTuneAdminRequired` -> 文案"驱动拒绝访问，请以管理员身份重启软件后重新检测"，全部写入禁用；
    - 只有 Probe 真的给出 `CanWrite:true` 的选项时，「应用」才可点。
  => **绝不可能**因为"强制显示"就对不支持的机器写电压/频率。

* ⚠️【重要实测结论，与用户预期相反，务必看】本机 **AMD Ryzen 7 8745H** 换成作者 CPUID 判据后
  **不是"仍不支持"，而是"支持"**：
    `L-Mechrevo.exe --cpu-tuning-probe` 实测输出（本机，管理员）：
      Cpu       = "AMD Ryzen 7 8745H w/ Radeon 780M Graphics"
      StatusKey = **CpuTuneSmuReady**
      Detail    = "**Phoenix**; SMU 0x4C6500"
      Options   = amd-co (CpuTuneCurve, -30..0, CO, CanWrite=true)
                  amd-clock (CpuTuneClock, 1000..4500, MHz, CanWrite=true)
    即 CPUID 把 8745H（family 19h / model 75h = 0x75）判为 **Phoenix**（作者表里 6516/6517 -> Phoenix），
    而 Phoenix 在 `CurveCommand`/`ClockCommand` 两张表里**都有**，所以
      - `CpuInfo.IsSupportedUV()` 现在 = **true**（旧字符串白名单下是 false）
      - 入口用**正常路径**就会显示，**不需要**强制开关；
      - 且窗口里「应用」在检测通过后是**可点**的 —— 点了会真的往 SMU 写 CO / 固定全核频率。
    => 如果只是想"看一眼界面"，**请不要点「应用」**（`--cpu-tuning-probe` 已证明：只读、无写入）。
    => 想验证强制开关本身生效：设 `LMECHREVO_FORCE_CPU_TUNING=1` 重启 LM 后，
       按钮会变成「CPU 调节（强制显示）」、窗口标题同样带「（强制显示）」并多一行红字。

--------------------------------------------------------------------------------
【三】任务三：编译 / publish / 部署

* 编译（**0 错误**）：
    & 'D:\\openLM\\dotnet-sdk\\dotnet.exe' build 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release -t:Rebuild
    -> 「1218 个警告 / **0 个错误**」（警告全是既有的 CS8632/CS0649 风格；我的新文件只贡献 CS8632，
       无 CS04xx 功能类警告）
* publish：
    & 'D:\\openLM\\dotnet-sdk\\dotnet.exe' publish 'D:\\openLM\\source\\L-Mechrevo.csproj' -c Release
        -o 'D:\\openLM\\rebuild\\lm_fix_build' '-p:NoWarn=WFO1000%3BCA1416'
    -> 成功；`lm_fix_build\\L-Mechrevo.dll` = **1,979,904 B** / 2026-10-05 17:24:50
       `lm_fix_build\\zh-CN\\L-Mechrevo.resources.dll` = 59,392 B / 17:24:50
* 部署（提权，未加 -Wait）：
    Start-Process powershell -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','D:\\openLM\\替换LM.ps1' -Verb RunAs
    轮询结果：
      C:\\Program Files\\L-Mechrevo\\L-Mechrevo.dll              1,946,624 B @16:51:02
        -> **1,979,904 B @17:24:50（已替换）**
      C:\\Program Files\\L-Mechrevo\\zh-CN\\L-Mechrevo.resources.dll  56,320 B -> **59,392 B @17:24:50（已同步）**
      脚本自动备份：C:\\Program Files\\L-Mechrevo\\L-Mechrevo.dll.bak-20261005-172523（1,946,624 B，旧版）
* 重启后状态：
    LM 进程 **L-Mechrevo PID=2268，StartTime 2026-10-05 17:25:23，存活**（WorkingSet ~174 MB）
* 二进制校验（新代码确实进了部署的 DLL）：
    UTF-16 字节搜索 C:\\Program Files\\L-Mechrevo\\L-Mechrevo.dll 命中：
      buttonCpuTuning / --cpu-tuning-probe / LMECHREVO_FORCE_CPU_TUNING /
      cpuTuneOption / CpuTuneSmuReady / amd-co / amd-clock /
      "Intel runtime tuning is not part of this build…"
    zh-CN 卫星资源命中："CPU 调节" / "强制显示" / "全核 Curve Optimizer" / "安装缺失驱动" / "未安装 PawnIO 驱动"
* 日志（%APPDATA%\\MechrevoLite\\log.txt）：新实例 17:25:24 起正常启动，
    `MechrevoHw connected: True`、`TrayGPU: …`、`GPU route at startup: …` 等按旧行为出现；
    **全文件搜索 Error/Exception/Unhandled/失败：0 条**（只有 `FanErrorInfo: {"FanErrorStatus":0}` 这类正常行）。
* CLI 复跑证据文件：`diagnostics\\cpu-tuning-probe-step7a.json`（上面那段 JSON）。

--------------------------------------------------------------------------------
【四】清单 B 保护自检（grep 证据，全部命中 = 我们的东西没丢）
  B-1 igpu_only_ui=4 / IgpuOnlySemantics=1 / ResolveGpuModeStatus=1
  B-2 PowerSleepGuard=2          B-3 IccHelper=4 / calib_custom_on=2 / CalibrationStartupForm=2
  B-4 ModeProfileConfig=4        B-5 DisplayColorGamut=4 / DisplayEffectEditorForm=2
  B-6 OfficialConsoleIsolation=3 B-7 ModelScopedConfig=1 / ModelScopeMigration=2
  B-8 BranchConvergenceMap=1     B-9 DisplayRouteMatrix=3
  B-10 DgpuGenerationProbe=2     B-11 LightChannelSettings=7 / LightingSettingsStore=7 / LiquidCoolingLightMenu=2
  B-12 GpuSwitchPolicy=3         B-13 ElevatedProcessKiller=2 / ExecutableTrust=4
  B-14 ScreenBlankController=3   B-15 DiagnosticPackExporter=3 / FunctionVerifier=1
  B-16 FirstRunGuideForm=3       B-17 PendingAlreadySatisfied=1
  B-20 onboarding_version=2      B-22 GcuCoexistence=4
  （"files=" 为命中文件数，全部 >0；未命中一项则视为回归）
* 被我们主动删掉的 **29 个类型**：逐个用 `(class|struct|record|enum|interface) <名字>` 精确查证
  -> **re-added = 0 / 29**（没有把作者的旧类型当"新增"塞回来）。
* 本次只改了 8 个既有文件 + 新增 5 个文件（source 树 mtime>=17:00 清单）：
  PawnIO\\RyzenSmuService.cs, PawnIO\\CpuInfo.cs, MechrevoLite.Hardware\\{CpuTuningOption,CpuTuningProbe,
  CpuTuningResult,RuntimeCpuTuning}.cs, MechrevoLite\\{CpuTuningForm,CustomModeForm,Program}.cs,
  MechrevoLite.Mode\\ModeControl.cs, MechrevoLite.Properties.Strings{,.zh-CN}.resx, MechrevoLite.UI\\UiAuditRunner.cs
  => `MechrevoHw.cs` / `MechrevoService.cs` / `PerfModeService.cs` / `SettingsForm.cs` / `GpuRestartVerifier.cs` /
     `GPUModeControl.cs` **一个字节都没动**（三档显卡模式 / 核显省电 / ResolveGpuModeStatus /
     PendingAlreadySatisfied / igpu_only_ui 那套天然未受影响）。

--------------------------------------------------------------------------------
【五】未做 / 明确跳过 / 没有碰的东西（诚实声明）

* **没有打包**（未跑 ISCC；installer-src\\Output\\ 未动）。**没有打 NoGCU**。
* **installer-src\\files\\GCU\\ 下一个字节都没动**（含 Select-GcuPayload.ps1 与 payload\\40）。
* **A-30 的驱动安装脚本没搬**：`CpuTuningForm` 里「安装缺失驱动」代码按作者保留了，
  但 `Install-PawnIO.ps1` **没有**放进 installer-src\\files\\ —— 该脚本会提权装内核驱动，
  属第⑦B 步（还需先审阅其 SHA256/签名校验逻辑）。本机 PawnIO 已装（`CpuTuneSmuReady` 已证明），
  所以这个按钮只有 driver-missing 机型才会出现，且现在点了会以非 0 退出码报"失败"。
* **Intel 侧整块（A-29 Intel 分支 / A-31 / A-32 Intel 闭环 / IntelOcMailbox / IntelOcDomain /
  LMechrevo.IntelMSR.bin 及其 csproj EmbeddedResource）＝第⑦B 步，本次故意不做**（高风险：
  第三方签名内核模块 + 直写 MSR 0x150）。csproj 未编辑。
* **没有动**：`MechrevoHw.cs`、`MechrevoService.cs`、`PerfModeService.cs`、`SettingsForm.cs`、
  `Logger.cs`、`UsageTelemetry.cs`、`GpuRestartVerifier.cs`、`GPUModeControl.cs`、
  `DisplayRoutePolicy.cs`、`WinPowerPlan.cs`、`RComboBox.cs`、`RCheckBox.cs`、`SettingsDialog.cs`、
  `FirstRunGuideForm.cs`、`install_script.iss`、`install_script_nogcu.iss`、`Select-GcuPayload.ps1`。
* 未实机验证"写入是否被固件接受"（**不打算替用户按「应用」**）；作者自己也写着
  "CPU 能力位和 SMU 接受命令不等于跨机型稳定性验收"。
* 本机 `CpuInfo.IsSupportedUV()` 由 false 变 true 是**决策②的预期后果**（用户 16:40 已拍板
  "按作者来（CPUID 判据）"），但它确实**扩大**了可写机型范围，属行为变更，已在上面【二】写清。
* 遗留提示：`RuntimeCpuTuning.Probe()` 只读；`Apply`/`Restore` 会写 SMU。若希望"以后即使
  检测通过也不许写"，需要产品决策（本次未加额外闸门，以免偏离作者实现）。

--------------------------------------------------------------------------------
【本次改动的文件清单（8 改 5 新 1 脚本 1 日志）】
  改：source\\PawnIO\\RyzenSmuService.cs、source\\PawnIO\\CpuInfo.cs、
      source\\MechrevoLite.Mode\\ModeControl.cs、source\\MechrevoLite\\CustomModeForm.cs、
      source\\MechrevoLite\\Program.cs、source\\MechrevoLite.UI\\UiAuditRunner.cs、
      source\\MechrevoLite.Properties.Strings.resx、source\\MechrevoLite.Properties.Strings.zh-CN.resx
  新：source\\MechrevoLite.Hardware\\CpuTuningOption.cs、CpuTuningProbe.cs、CpuTuningResult.cs、
      RuntimeCpuTuning.cs、source\\MechrevoLite\\CpuTuningForm.cs
  脚本：tools\\step7a_append_cputune_resx.py
  日志：本条目（末尾追加）
  产物：rebuild\\lm_fix_build\\（已 publish）、diagnostics\\cpu-tuning-probe-step7a.json

================================================================================
"""


def main():
    with io.open(LOG, "r", encoding="utf-8-sig") as handle:
        before = handle.read()
    assert "合并第⑦A步" not in before, "entry already present, refusing to duplicate"
    with io.open(LOG, "a", encoding="utf-8", newline="") as handle:
        handle.write(ENTRY)
    with io.open(LOG, "r", encoding="utf-8-sig") as handle:
        after = handle.read()
    print("before chars=%d after chars=%d delta=%d" % (len(before), len(after), len(after) - len(before)))
    print("head preserved=%s" % after.startswith(before[:200]))
    print("tail ok=%s" % after.rstrip().endswith("=" * 80))
    return 0


if __name__ == "__main__":
    sys.exit(main())
