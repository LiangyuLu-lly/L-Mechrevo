// ==============================================================================================
// L-Mechrevo — AMD SMU / PawnIO service layer.
//
// 本文件属于 L-Mechrevo（GPL-3.0-only，见仓库根 LICENSE）。
//
// 派生声明 / Derived-work notice:
//   * 本文件的 SMU 命令表（CurveCommand / CurvePerCoreCommand / CurveReadCommand / ClockCommand /
//     Stapm / Fast / Slow / Thm / CoGfx）与邮箱地址表（GetMp1Addrs / GetPsmuAddrs）是**派生作品**：
//     它们与下列上游逐条同源（命令 ID / 通道 / 参数打包 / 调用顺序），
//     L-Mechrevo 只是把它们重写成按代号的 C# 表并补了守卫（逐条对照见 ATTRIBUTION-CO-ADDENDUM.md）：
//         seerge/g-helper   app/Pawn/RyzenSmu.cs  https://github.com/seerge/g-helper           (GPL-3.0)
//         irusanov/ZenStates-Core                 https://github.com/irusanov/ZenStates-Core    (GPL-3.0)
//         FlyGoat/RyzenAdj  lib/api.c             https://github.com/FlyGoat/RyzenAdj           (LGPL-3.0)
//     注：本仓库**保留了一份与 g-helper 上游逐字节相同**的参考副本
//         diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs
//         （22,317 B，SHA-256 07925B97DEF1A77D4A7F5D1D3B896032B0D10BF3DF635B0CCE6E603D5EEDDE35），
//         它就是这张表的来源文件。
//     ⚠️ 上述命令表**不是**"沿用 L-Mechrevo 自己的 0.290.6 实现"：0.290.6 的那张表本身就是这张表的
//        早期重排（逐条比对见 ATTRIBUTION-CO-ADDENDUM.md §3 末行），不能因为"我们自己以前也有"
//        就当成原创。
//     该部分按 GPL-3.0 分发（LGPL-3.0 与 GPL-3.0-only 相容）；许可文本见 LICENSES\GPL-3.0.txt，
//     第三方清单见 THIRD-PARTY-NOTICES.md 的 S6/S7/S8。
//   * 本文件的 **FMax（PBO boost limit）** 一部分（命令行 0x42/0x47/0x6E/0x70/0x12/0x86、
//     SMN 邮箱地址组、6 参数打包、事务时序）是在阅读下列 GPL-3.0 参考实现后重写的：
//         irusanov/ZenStates-Core  https://github.com/irusanov/ZenStates-Core  (GPL-3.0)
//         irusanov/SMUDebugTool    https://github.com/irusanov/SMUDebugTool    (GPL-3.0)
//     该部分按 GPL-3.0 分发；完整许可文本见仓库根 LICENSES\GPL-3.0.txt，
//     第三方清单见 THIRD-PARTY-NOTICES.md。
//   * 本文件**不含** namazso/PawnIO 的 C# 代码；命名空间 `PawnIO` 只是本项目的子系统名。
//     随程序分发的 PawnIO.Modules `RyzenSMU.bin` 是 LGPL-2.1-or-later，见 LICENSES\LGPL-2.1.txt。
// ==============================================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PawnIO;

public sealed class RyzenSmuService : IDisposable
{
	private const int MAILBOX_TIMEOUT_MS = 200;

	private readonly PawnIOWrapper _io = new PawnIOWrapper();

	// 2026-10-05 step7A (upstream 0.290.6 A-35/A-33): SMU register access is routed through a
	// delegate so the command tables can be unit-tested without the PawnIO driver.
	private readonly Func<string, ulong[]?, ulong[]?, bool> _execute;

	private bool _init;

	private bool _disposed;

	private CpuCodeName _cpu;

	private uint _smuVer;

	private readonly Mutex _smuMutex = new Mutex(initiallyOwned: false, "Global\\Access_PCI");

	public bool IsInitialized => _init;

	public CpuCodeName CpuCodeName => _cpu;

	public uint SmuVersion => _smuVer;

	public CpuFamily Family => GetFamily(_cpu);

	public RyzenSmuService()
	{
		_execute = _io.Execute;
	}

	internal RyzenSmuService(CpuCodeName cpu, Func<string, ulong[]?, ulong[]?, bool> execute)
	{
		_execute = execute;
		_cpu = cpu;
		_smuVer = 1u;
		_init = GetFamily(cpu) != CpuFamily.Unknown;
	}

	public bool Initialize(byte[] moduleData)
	{
		if (_init)
		{
			return true;
		}
		if (_io.Connect() != PawnIOWrapper.ConnectResult.OK || !_io.LoadModule(moduleData))
		{
			return false;
		}
		_init = GetCodeName(out _cpu) && Family != CpuFamily.Unknown && GetSmuVersion(out _smuVer) && _smuVer != 0;
		return _init;
	}

	public static bool IsPawnInstalled()
	{
		using PawnIOWrapper pawnIOWrapper = new PawnIOWrapper();
		return pawnIOWrapper.Connect() != PawnIOWrapper.ConnectResult.NotInstalled;
	}

	public bool Initialize(Assembly assembly)
	{
		string name = assembly.GetManifestResourceNames().FirstOrDefault((string n) => n.EndsWith("RyzenSMU.bin")) ?? throw new InvalidOperationException("RyzenSMU.bin not found.");
		using Stream stream = assembly.GetManifestResourceStream(name);
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return Initialize(memoryStream.ToArray());
	}

	public bool SetAllLimits(int stapmW, int fastW, int slowW)
	{
		return (SetStapm(stapmW) == SmuStatus.OK) & (SetFast(fastW) == SmuStatus.OK) & (SetSlow(slowW) == SmuStatus.OK);
	}

	public void SetAllLimits(int stapmW, int fastW, int slowW, out SmuStatus stapm, out SmuStatus fast, out SmuStatus slow)
	{
		stapm = SetStapm(stapmW);
		fast = SetFast(fastW);
		slow = SetSlow(slowW);
	}

	public SmuStatus SetCoAll(int value)
	{
		uint arg = EncodeCurve(value);
		(bool, uint)? tuple = CurveCommand(_cpu);
		if (tuple.HasValue)
		{
			if (!tuple.Value.Item1)
			{
				return SendMp1(tuple.Value.Item2, arg);
			}
			return SendPsmu(tuple.Value.Item2, arg);
		}
		return SmuStatus.UnknownCmd;
	}

	// ==========================================================================================
	// 2026-10-05 percore-product: **每核** CO 写（Curve Optimizer，逐核 margin）。
	//
	// 与上面的 SetCoAll（全核写）**完全并列**：本方法不改 SetCoAll / ClockCommand / Publish 的
	// 任何既有语义，只是在**已实测通过**的代号上多提供一条逐核写路径。
	//
	// 命令 / 位宽 / 编码（全部来自上游 ZenStates-Core，且台式机实测确认）：
	//   * 命令 ID  = MP1 0x35（Mp1Smu.SMU_MSG_SetDldoPsmMargin；Zen3Settings.cs:22 =
	//                "SetPsmMarginSingleCore"，上游在 Vermeer 上走的就是它）；
	//   * 通道     = MP1 邮箱（GetMp1Addrs(Matisse 家族) —— Matisse/Vermeer 共用同一组地址）；
	//   * margin   = **16 位二补码**（Utils.cs:178-184 MakePsmMarginArg：(margin<0?0x100000:0)+margin 再 &0xFFFF；
	//                -5 -> 0xFFFB）。注意：LM 自己的 EncodeCurve 编成 **低 20 位**，两者**不同**，
	//                所以本方法**不能**复用 EncodeCurve，必须按 16 位打包。
	//   * arg0     = (ccd << 28) | ((core % 8) << 20) | margin
	//                （Cpu.cs:929-940 MakeCoreMask + SetPsmMarginSingleCore.cs:20-27 的
	//                 [31-28]ccd | [27-24]ccx | [23-20]core | [15-0]margin 布局）。
	//                本仓库**没有 APOB 解析**，所以沿用上游在无映射时的假设：每 CCD 8 核，
	//                ccd = core / 8、localCore = core % 8。8 核单 CCD 的 5700X 上 ccd 恒 0，
	//                这个假设实际未被使用（arg0 会打进日志，可逐条核对）。
	//
	// 依据（台式机实测报告 D:\Downloads\SMU-CO-corewrite-20261005-230824.txt）：
	//   AMD Ryzen 7 5700X / Vermeer / SMU 0x00384C00：候选 mp1-35-m16 写"只写核 0 = -5"，
	//   结果 core0: -15 -> -5、core1..7 全部不变 -> verdict CORE-WRITE-OK。
	//   => 命令 ID、arg0 编码（core<<20）、16 位 margin 三项**均被实测确认**。
	//
	// ⚠️ 2026-10-10 percorenolog（用户原话「只要支持就开 分核负压代码就参照SMU，全核负压框删掉」）：
	//   每核写**不再**是 Vermeer 独占 —— 命令表见 CurvePerCoreCommand（Cezanne/Lucienne/Renoir
	//   MP1 0x54；VanGogh…KrackanPoint MP1 0x4B；Raphael/GraniteRidge/DragonRange PSMU 0x06；
	//   Matisse MP1 0x34；Vermeer MP1 0x35）。没有命令的代号（Zen1/Zen2 TR/Rome/Milan/Chagall/
	//   StormPeak/FireFlight/Genoa/Bergamo/Turin*/ShimadaPeak/KrackanPoint2/Mero）仍然直接返回
	//   UnknownCmd —— **一条命令都不发**。
	// ⚠️ 只有 Vermeer 那一行是台式机实测过的；其余各行只有 SMU 参考依据（g-helper / UXTU /
	//   ZenStates-Core 的每核写命令 + `core << 20` 打包）。所以真正的安全网在
	//   RuntimeCpuTuning.ApplyPerCore 的"写前基线 + 逐核回读 + 不一致则全量回写"，不在本文件。
	// ==========================================================================================
	public SmuStatus SetCoPerCore(int coreIndex, int margin)
	{
		if (coreIndex < 0)
		{
			throw new ArgumentOutOfRangeException("coreIndex");
		}
		// 与 EncodeCurve 同一区间：LM 的写入路径只接受 -40..0，不能越过自己的约束。
		if (margin < -40 || margin > 0)
		{
			throw new ArgumentOutOfRangeException("margin");
		}
		(bool, uint)? tuple = CurvePerCoreCommand(_cpu);
		if (!tuple.HasValue)
		{
			// 未获批准的代号：不猜命令、不发命令。
			return SmuStatus.UnknownCmd;
		}
		uint arg = EncodePerCoreCurveArg(coreIndex, margin);
		return tuple.Value.Item1 ? SendPsmu(tuple.Value.Item2, arg) : SendMp1(tuple.Value.Item2, arg);
	}

	// ==========================================================================================
	// 2026-10-10 co-attribution: **本文件按代号的 SMU 命令表是派生作品**（不是自创的命令 ID）。
	//
	// 逐组来源、许可、"像到什么程度"（完整逐条对照 / 上游 URL / blob 校验见
	// ATTRIBUTION-CO-ADDENDUM.md；清单见 THIRD-PARTY-NOTICES.md S6/S7/S8；许可文本见 LICENSES\）：
	//
	//  ① seerge/g-helper — `app/Pawn/RyzenSmu.cs`  (GPL-3.0)
	//     https://github.com/seerge/g-helper/blob/b2b7bae52cd39095002078af98349e6f6d57b960/app/Pawn/RyzenSmu.cs
	//     * **命令 ID / 通道 / 调用顺序的整张表**与它相同：
	//         - 全核 CO      CurveCommand / SetCoAll : MP1 0x55（Renoir/Cezanne/Lucienne）、
	//                        MP1 0x4C（Rembrandt/Vangogh/Phoenix/Phoenix2/HawkPoint/Strix*/KrackanPoint）、
	//                        PSMU 0x07（Raphael/GraniteRidge/DragonRange）== g-helper :160-175。
	//                        差异：g-helper 的 StrixHalo 行还有"MP1 0x4C 失败再试 PSMU 0x5D"的兜底，
	//                        本表**只发 MP1 0x4C**（无兜底）。
	//         - iGPU CO      SetCoGfx : MP1 0x64（Renoir 族）、PSMU 0xB7（Mobile/StrixHalo）
	//                        == g-helper :192-205。
	//                        注意：g-helper 给 StrixHalo 也发 0xB7，而 RyzenAdj 明确写"0xB7 在该架构上
	//                        被拒绝"（见 ③）；本表照 g-helper。
	//         - 功率/温度    SetStapm / SetFast / SetSlow / SetThm 的 ID、通道与**调用顺序**（含 Renoir
	//                        先发 MP1 再镜像 PSMU、SetSlow 额外补一条 ApuSlow 0x34）== g-helper :379-432
	//                        与 :207-223。
	//         - PM 表读取    GetPowerLimits / GetTctlIndex / HasApuSlowField 的 ioctl 时序、两次 sleep、
	//                        float 下标（[0]/[2]/[4]/[tctl]/[6]）== g-helper :249-331；g-helper 自己把
	//                        这张表归给 RyzenAdj（见 ③）。本仓库唯一自研改动：未知表版本 -> -1（不可读），
	//                        g-helper 是 default 16。
	//         - 邮箱事务     MailboxRaw / SendMp1 / SendPsmu / GetMp1Addrs / GetPsmuAddrs 的算法**与全部
	//                        12 组 SMN 地址常量** == g-helper :434-589。
	//     * 本仓库保留的参考副本 `diagnostics\smudbg-reference-20261005\key-files\ghelper_RyzenSmu.cs`
	//       与上述上游**逐字节相同**（22,317 B，SHA-256 07925B97DEF1A77D4A7F5D1D3B896032B0D10BF3DF635B0CCE6E603D5EEDDE35）。
	//
	//  ② irusanov/ZenStates-Core  (GPL-3.0)  https://github.com/irusanov/ZenStates-Core
	//     * 每核 CO 写（CurvePerCoreCommand + EncodePerCoreCurveArg + SetCoPerCore）：
	//         MP1 0x35 = `Mp1Smu.SMU_MSG_SetDldoPsmMargin`（Zen3Settings.cs:22）；
	//         arg0 = (ccd<<28)|((core%8)<<20)|(margin&0xFFFF) 与上游 `Cpu.cs:932-940 MakeCoreMask`
	//         + `SetPsmMarginSingleCore.cs:20-29`（`(coreMask & 0xfff00000) | Utils.MakePsmMarginArg(margin)`）
	//         **逐位相同**；margin 的 16 位二补码同 `Utils.cs:180-184`。→ **identical**。
	//         （唯一差异是 ccd/core 的取值来源：上游读 APOB 核心映射，本仓库假设每 CCD 8 核。）
	//     * CO 回读（CurveReadCommand）：0xE1（APUSettings1_Phoenix.cs:19）、0xC3（…_Cezanne.cs:8）、
	//         0x2F（…_Rembrandt.cs:8）、0xAF（…_Strix.cs:11）、0xD5（Zen4Settings.cs:39）、
	//         0xA3（Zen5Settings_ShimadaPeak.cs:35）、0x7C（Zen2Settings.cs:35，上游自注 "// Not sure"）；
	//         通道 = Rsmu（= 本文件的 PSMU 邮箱），arg0 = coreMask，同
	//         `Commands/GetPsmMarginSingleCore.cs:9-16`。→ **ID / 通道 / arg 语义 identical**。
	//         ⚠️ `DecodeCurveMargin` 的"32 位 -> 20 位 -> 16 位"解码阶梯**没有**上游对应物，
	//            是本仓库自己的判定 —— 别把它当成上游定义。
	//     * 全核 CO 的 Vermeer 行（PSMU 0x0B）：值 = `Zen2Settings.cs:34
	//         Rsmu.SMU_MSG_SetAllDldoPsmMargin = 0xB; // Not sure`（Zen3Settings 继承）；
	//         上游 Zen3 **自己**的写命令是 MP1 0x36（Zen3Settings.cs:23）。该行 g-helper / RyzenAdj 都没有。
	//     * 全核 CO 的 Raphael 行（PSMU 0x07）：= `Zen4Settings.cs:37`；但上游运行期选择器
	//         （`SetPsmMarginAllCores.cs:92-94`）会**先发 MP1 0x36** —— 我们与 g-helper/RyzenAdj 一样走
	//         PSMU 0x07（同一张上游表里的另一个条目 + 不同的通道优先级）。
	//     * 固定频率 OC 的 ID：Matisse/Vermeer MP1 0x24/0x26/0x25 = Zen2Settings.cs:60-63；
	//         Raphael 族 PSMU 0x5D/0x5F/0x5E = Zen4Settings.cs:23-25；
	//         Renoir 族 MP1 0x2F/0x31/0x30 = APUSettings1.cs:96-98；
	//         Mobile 族 PSMU 0x17/0x19/0x18 = APUSettings1.cs:34-36。→ **identical（ID + 通道）**。
	//     * 功率/温度 ID：Raven 0x1A/0x1B/0x1C/0x1F（APUSettings0.cs:65-70）、Renoir 族 0x14/0x15/0x16/0x19
	//         （APUSettings1.cs:73-78）、Raphael 族 0x4F/0x3E/0x5F/0x3F（Zen4Settings.cs:66-72）。
	//         ⚠️ 但通道**优先级**不同：上游 `Commands/SetSmuLimit.cs` / `SetTctlMax.cs` 是"先 Rsmu 后 Mp1"，
	//         本表（同 g-helper / RyzenAdj）是"先 Mp1"。
	//     * 打包**有意的差异**：上游 `Utils.MakePsmMarginArg` 把 CO margin 编低 **16** 位（-5 -> 0xFFFB），
	//         本文件 `EncodeCurve` 编低 **20** 位（-5 -> 0xFFFFB）—— 这是本仓库自己的约定，不是上游的。
	//     * FMax（PBO boost limit）部分另有独立声明，见下方 FMax 小节的"来源与许可"。
	//
	//  ③ FlyGoat/RyzenAdj  (LGPL-3.0)  https://github.com/FlyGoat/RyzenAdj  （`lib/api.c`）
	//     * iGPU CO 的两个取值**只在这里**：`set_cogfx` Renoir 族 `_do_adjust(0x64)`（api.c:1453）、
	//         Mobile 族 `_do_adjust_psmu(0xB7)`（api.c:1459）；StrixHalo 它明确不发（api.c:1461-1462）。
	//         ZenStates 表里**没有** 0x64（它的 `Rsmu.SMU_MSG_SetGpuPsmMargin` 是 APUSettings1.cs:49=0x53、
	//         _Phoenix.cs:20=0x1F、_Rembrandt.cs:9=0xB7），所以本表的 0x64 只能来自这里（经 g-helper 带出）。
	//     * 固定频率 OC 的**顺序与兜底 ID**：`set_enable_oc`（Renoir MP1 0x2F，api.c:1376）、
	//         `set_oc_clk`（MP1 0x31，失败后 `_do_adjust_psmu(0x19)`，api.c:1290-1293）、
	//         `set_disable_oc`（MP1 0x30，失败后 `_do_adjust_psmu(0x1d)`，api.c:1352-1355）——
	//         本文件 SetOcClock 的 Renoir 兜底 `SendPsmu(25)` 与 DisableOc 的 `SendPsmu(29)` 与它逐条对应；
	//         "使能 -> 设频 -> 失败即关"这个**编排**是本仓库把它的三个函数串起来（similar but rewritten）。
	//     * 功率/温度：`set_stapm_limit` / `set_fast_limit` / `set_slow_limit` / `set_tctl_temp`
	//         （api.c:444-653，含 "Retry with PSMU"）与本表逐条相同；PM 表的 `get_tctl_temp` /
	//         `get_apu_slow_limit` 偏移来源也是它（g-helper 的注释就是这么写的）。
	//     * UXTU（JamesCJ60/Universal-x86-Tuning-Utility，GPL-3.0，
	//         https://github.com/JamesCJ60/Universal-x86-Tuning-Utility）：g-helper 在 iGPU CO 那一行注明
	//         "set-cogfx 0xB7" 的来源是 UXTU 的 `Socket_FT6_FP7_FP8` 表（ghelper_RyzenSmu.cs:199）。
	//         本轮**没有**定位到 UXTU 的对应源码行，因此只作**礼节性致谢**记入清单，
	//         **不声称**已核对的 ID 匹配。
	//
	//  ④ 明确"没找到上游"的两处（**本轮不改**，只登记，见 addendum §8）：
	//     * Zen1/Zen+ 的 ClockCommand 行（MP1 0x23 / 0x39 / 0x24）在上游 master 里**找不到**：
	//       ZenSettings.cs:51-53 是 0x37 / 0x39 / 0x37，而 0x23 / 0x24 在它表里是
	//       `AcBtcStartCal` / `AcBtcStopCal`（ZenSettings.cs:57-59）。疑似老版本转录或误抄。
	//     * 温度命令的 Matisse 族用 0x3E（= 上游 Zen3Settings.cs:17）；真 Zen2 上游是 0x3D
	//       （Zen2Settings.cs:55）——本仓库的 CpuFamily.Matisse 同时涵盖 Matisse 与 Vermeer。
	//
	//  ⑤ 结论：g-helper（GPL-3.0）、ZenStates-Core（GPL-3.0）、RyzenAdj（LGPL-3.0）、UXTU（GPL-3.0）
	//     与本项目 GPL-3.0-only **全部相容**；许可文本见 LICENSES\GPL-3.0.txt（LGPL-2.1 模块另有
	//     LICENSES\LGPL-2.1.txt）；第三方清单见 THIRD-PARTY-NOTICES.md；逐条对照与上游 URL 见
	//     ATTRIBUTION-CO-ADDENDUM.md。
	// ==========================================================================================
	// 2026-10-05 step7A (upstream 0.290.6 A-33): per-codename command tables replace the old
	// CpuFamily switch. A codename missing from the table returns null, i.e. "refuse to write"
	// instead of guessing a mailbox command for an unknown part.
	internal static (bool Psmu, uint Command)? CurveCommand(CpuCodeName cpu)
	{
		switch (cpu)
		{
		case CpuCodeName.Renoir:
		case CpuCodeName.Cezanne:
		case CpuCodeName.Lucienne:
			return (false, 85u);
		case CpuCodeName.Rembrandt:
		case CpuCodeName.Vangogh:
		case CpuCodeName.Phoenix:
		case CpuCodeName.Phoenix2:
		case CpuCodeName.HawkPoint:
		case CpuCodeName.StrixPoint:
		case CpuCodeName.StrixHalo:
		case CpuCodeName.KrackanPoint:
			return (false, 76u);
		case CpuCodeName.Raphael:
		case CpuCodeName.GraniteRidge:
		case CpuCodeName.DragonRange:
			return (true, 7u);
		case CpuCodeName.Vermeer:
			return (true, 11u);
		default:
			return null;
		}
	}

	// ==========================================================================================
	// 2026-10-10 percorenolog：**每核** CO 写命令表（区别于上面的全核 CurveCommand）。
	//
	// 用户原话（⭐ 本节按它实施）：「只要支持就开 分核负压代码就参照SMU，全核负压框删掉」
	//   => ⛔ 不再有"Vermeer-only 白名单"，也⛔ 不再有"已实测 / SMU 声明·未实测"的分档；
	//      凡是 **SMU 参考里存在每核写命令** 的代号一律放开（"只要支持就开"）。
	//
	// arg0 打包与通道都不变：`EncodePerCoreCurveArg` = `(ccd << 28) | ((core % 8) << 20) | (margin & 0xFFFF)`
	// = 用户给的 `((ccd << 8 | core) << 20) | (margin & 0xFFFF)`（8 核/CCD 时逐位等价），
	// 与 g-helper `SetCoPer` / UXTU `Socket_FT6_FP7_FP8` 一致 —— 每核写**必须** `core << 20`。
	// ⛔ 绝不照抄 SMUDebugTool 那个坏实现（`EncodeCoreMarginBitmask` 对 APU 返回裸核号，
	//    随后被 `SetPsmMarginSingleCore` 的 `(coreMask & 0xfff00000)|m` 掩掉 ⇒ 核号丢失、只发 margin）。
	//
	// 逐组命令号（三个来源互相印证：g-helper `app/Pawn/RyzenSmu.cs` `SetCoPer`、UXTU 的
	// Socket_FT6_FP7_FP8 表、ZenStates-Core 各 *Settings.cs 的 SMU_MSG_SetDldoPsmMargin）：
	//
	//   Cezanne / Lucienne / Renoir        -> MP1 0x54 （g-helper :184 —— CpuFamily.Renoir 那一行）
	//   VanGogh / Rembrandt / Mendocino / Phoenix / Phoenix2 / HawkPoint / StrixPoint /
	//   StrixHalo / KrackanPoint           -> MP1 0x4B （g-helper :185-186 —— Mobile / Strix* 那两行）
	//   Raphael / GraniteRidge / DragonRange -> PSMU 0x06（g-helper :187 —— CpuFamily.Raphael 那一行；
	//                                         ZenStates-Core dev 的 Zen4Settings 另有 MP1 0x35 一说）
	//   Matisse                            -> MP1 0x34（Zen2Settings 的每核写；旧表里 Matisse 是空的）
	//   Vermeer                            -> MP1 0x35（唯一**台式机实测**过的条目：R7 5700X /
	//                                         SMU 0x00384C00，只写核 0 = -5 时只有 core0 从 -15 变 -5）
	//
	// ⚠️ **仍然故意不列**（= 返回 null ⇒ SetCoPerCore 直接 UnknownCmd、一条命令都不发）：
	//   Colfax / SummitRidge / PinnacleRidge / Threadripper / CastlePeak / RavenRidge / RavenRidge2 /
	//   Picasso / Dali / Milan / Rome / Naples / Chagall / StormPeak / FireFlight / Genoa / Bergamo /
	//   Turin / TurinD / ShimadaPeak / KrackanPoint2 / Mero / Undefined
	//   —— 它们在 SMU 参考里**没有**每核写命令（要么整族没有 CO，要么我们的 CurveCommand 里本来
	//      就没有 CO 全核写，如 KrackanPoint2 / ShimadaPeak）。这些代号上**没有 CO 输入口**。
	//
	// ⚠️ Matisse 是唯一"有每核写、但没有每核读"的代号（CurveReadCommand(Matisse) = null）⇒
	//    写后**无法回读校验**，ApplyPerCore 会如实返回 `Verified: false` 并说明原因（不假装已校验）。
	// ==========================================================================================
	internal static (bool Psmu, uint Command)? CurvePerCoreCommand(CpuCodeName cpu)
	{
		switch (cpu)
		{
		case CpuCodeName.Cezanne:
		case CpuCodeName.Lucienne:
		case CpuCodeName.Renoir:
			return (false, 84u);
		case CpuCodeName.Vangogh:
		case CpuCodeName.Rembrandt:
		case CpuCodeName.Mendocino:
		case CpuCodeName.Phoenix:
		case CpuCodeName.Phoenix2:
		case CpuCodeName.HawkPoint:
		case CpuCodeName.StrixPoint:
		case CpuCodeName.StrixHalo:
		case CpuCodeName.KrackanPoint:
			return (false, 75u);
		case CpuCodeName.Raphael:
		case CpuCodeName.GraniteRidge:
		case CpuCodeName.DragonRange:
			return (true, 6u);
		case CpuCodeName.Matisse:
			return (false, 52u);
		case CpuCodeName.Vermeer:
			return (false, 53u);
		default:
			return null;
		}
	}

	// 该代号在 SMU 参考里有没有每核写命令（= UI 能不能给每核输入框 / 重置按钮）。
	// ⚠️ 只看"命令在不在"，⛔ 不代表该命令已被实测（Vermeer 之外都只有 SMU 参考依据）。
	internal static bool SupportsPerCoreCurve(CpuCodeName cpu)
	{
		return CurvePerCoreCommand(cpu).HasValue;
	}

	// 该代号有没有**每核读**命令（= 每核写之后能不能回读校验）。这是安全网的前提：
	//   * 有读命令 -> ApplyPerCore 走"写前基线 + 写 + 逐核回读 + 不一致则按写前基线全量回写"；
	//   * 没有读命令（Matisse）-> 写仍然发（用户要求"只要支持就开"），但**绝不假装已校验**：
	//     ApplyPerCore 如实返回 `Verified: false` 并说明"该代号没有每核读命令、无法回读校验"。
	internal static bool SupportsPerCoreReadback(CpuCodeName cpu)
	{
		return CurveReadCommand(cpu).HasValue;
	}

	// 每核写的 arg0 打包：(ccd << 28) | ((core % 8) << 20) | (margin & 0xFFFF)。
	// 16 位二补码 margin 是关键：-5 -> 0x0000FFFB（**不是** EncodeCurve 的 0x000FFFFB）。
	internal static uint EncodePerCoreCurveArg(int coreIndex, int margin)
	{
		int localCore = coreIndex % 8;
		int ccd = coreIndex / 8;
		return (uint)((ccd << 28) | (localCore << 20) | (margin & 0xFFFF));
	}

	// ==========================================================================================
	// 2026-10-05 coread: **只读** CO 回读命令表（每核一条 GetDldoPsmMargin）。
	//
	// 来源（与 LM 同一套通道，已实测）：
	//   * 上游引擎 ZenStates-Core 的 *Settings.cs 里 Rsmu.SMU_MSG_GetDldoPsmMargin：
	//       Phoenix / Phoenix2 / HawkPoint          = 0xE1  (APUSettings1_Phoenix.cs)
	//       Cezanne                                 = 0xC3  (APUSettings1_Cezanne.cs)
	//       Rembrandt / Mendocino                   = 0x2F  (APUSettings1_Rembrandt.cs; Mendocino 同族)
	//       StrixPoint / StrixHalo / KrackanPoint   = 0xAF  (APUSettings1_Strix.cs)
	//       Raphael / GraniteRidge / DragonRange    = 0xD5  (Zen4Settings.cs)
	//       ShimadaPeak (Zen5)                      = 0xA3  (Zen5Settings_ShimadaPeak.cs)
	//       其余（Renoir / Lucienne / VanGogh / Picasso / RavenRidge / Zen1 / Zen2 ...）上游留 0
	//       —— **没有**读命令，本表返回 null = 不可回读，绝不猜 ID 盲试。
	//   * 通道：SMU_MSG_GetDldoPsmMargin 在上游一律是 **Rsmu（= 本文件的 PSMU 邮箱）** 属性，
	//     所以读走 SendPsmu/GetPsmuAddrs，与写命令（Phoenix MP1 0x4C 全核写）用的是同一颗 SMU。
	//   * 本机实测（Ryzen 7 8745H / Phoenix / SMU 0x4C6500，2026-10-05 只读）：
	//     PSMU 0xE1 + arg0 = 裸核号 0..7 -> 全部 status OK(0x01)，resp[0] = 0x00000000。
	//
	// ⚠️ 本表**只读**。它不参与 CurveCommand/ClockCommand 的任何写入决策，也不给 CO 开新的写路径。
	//
	// ------------------------------------------------------------------------------------------
	// 2026-10-05 vermeer-audit: 台式机 Zen2/Zen3（Matisse / Vermeer）**依据不足，故意留空**。
	// ⚠️ 这一档**已被下面的 percore-product 小节部分取代**（Vermeer 已实测通过并进表）；
	//    Matisse 仍然留空。保留原文只为记录"当时凭什么不写进表"的推导过程。
	//
	//   用户实测（R7 5700X / Vermeer / SMU 0x00384C00）：write path = PSMU 0x0B，本表返回 null
	//   -> smu-co-tool 在"没有回读命令"处安全停手，一个字节都没写（设计正确）。缺的正是这一档。
	//
	//   参考素材查证结果（ZenStates-Core dev，tmp_smudbg\zscore\ZenStates-Core-dev）：
	//     * Vermeer 走 Zen3Settings（Hardware\SMU.cs:282），而 Zen3Settings.cs **只定义写命令**
	//       （Mp1Smu.SMU_MSG_SetDldoPsmMargin = 0x35、SetAllDldoPsmMargin = 0x36），
	//       **没有任何 Get 命令** —— Zen3 表里没有读命令。
	//     * 唯一候选是 Zen3Settings 从 Zen2Settings **继承**下来的
	//       Rsmu.SMU_MSG_GetDldoPsmMargin = 0x7C，但上游自己在同一行写着：
	//           Rsmu.SMU_MSG_GetDldoPsmMargin = 0x7C; // Not sure      (Zen2Settings.cs:35)
	//       （Zen3Settings.cs:4 `class Zen3Settings : Zen2Settings` 继承它且没有清零；
	//         Zen2Settings.cs:11-13 的 RSMU 地址 0x3B10524/0x3B10570/0x3B10A40
	//         与 RyzenSMU.p 的 k_addrinfo[0]（Matisse/Vermeer 共用，RyzenSMU.p:195,214）
	//         以及本文件 GetPsmuAddrs(Matisse) 完全一致，即"RSMU == 我们的 PSMU"。）
	//     * 上游确实**会**在 Vermeer 上发这条命令（GetPsmMarginSingleCore.cs:15-16 在
	//       Rsmu.SMU_MSG_GetDldoPsmMargin > 0 时走 RSMU；arg0 = core<<20，见
	//       SettingsForm.cs:2008-2029 + Cpu.cs:932-940），但"会试"≠"验证过"：
	//       上游 UI 只按**写**命令使能（SettingsForm.cs:364-365），读失败被静默吞掉
	//       （InitPBO()，SettingsForm.cs:482-488），素材里**没有任何实测佐证**。
	//     * 结论：注释原文就是 `// Not sure` = 不确定。按本仓库"绝不猜 ID 盲试"的规矩，
	//       Vermeer / Chagall / Milan / Matisse / CastlePeak 一律**不写进表**（等实测）。
	//       候选值与可信度、以及复现实验的只读步骤见 修改日志 的"待确认"清单。
	//     * 顺带（写表差异，本轮**不动**）：我们的 Vermeer 写命令 PSMU 0x0B 正是同一块
	//       Zen2 RSMU 表里的 SetAllDldoPsmMargin = 0xB（同样 `// Not sure`），而上游 Zen3
	//       自己的写命令是 MP1 0x36（Zen3Settings.cs:23），且上游优先用 MP1
	//       （SetPsmMarginAllCores.cs:92-94）。这条差异只做报告，改写入需用户单独决定。
	// ------------------------------------------------------------------------------------------
	// 2026-10-05 percore-product: **Vermeer 放开**（上面的 vermeer-audit 已被实测取代）。
	//
	//   用户台式机实测报告 D:\Downloads\SMU-CO-corewrite-20261005-230824.txt
	//   （AMD Ryzen 7 5700X / Vermeer / family Matisse / SMU 0x00384C00 / 8 物理核）：
	//     * 只读候选探针：PSMU 0x7C 每核一条 -> 8 核全部 status=OK(0x01)，
	//       返回 0xFFFFFFF1 -> CO -15 —— 即上游标着 `// Not sure` 的 0x7C **在这台机器上确实可用**；
	//     * 逐核写校验（MP1 0x35，16 位 margin）：只写核 0 = -5 -> core0 从 -15 变 -5、
	//       其余 7 核不变 -> verdict = CORE-WRITE-OK，说明"每核读"读的正是"每核写"写的那张表；
	//     * 恢复（逐核写回 0 + SetCoAll(0) + DisableOc()）-> 恢复后 8 核全部读回 0。
	//   => 读命令 0x7C 从此进表（Vermeer），每核读 + 每核写 在这台机器上闭环成立。
	//
	//   解码位宽：读回来的是 **32 位有符号**（0xFFFFFFFB = -5，报告中的 0xFFFFFFF1 = -15），
	//   见 DecodeCurveMargin 的第 1 档（32 位有符号优先）。
	//
	//   ⚠️ 这个先例**只适用于 Vermeer**，不要顺推到同族别的代号：
	//     * Matisse（真 Zen2 桌面，如 3700X）：CPUID 代号就是 Matisse，与 Vermeer **不同代号**
	//       （只是共用 CpuFamily.Matisse 这个族标签）。同一张 Zen2 表、同样标着 `// Not sure`，
	//       但**没有任何实测** -> 本表仍然**故意不列**（依据不足就不猜，见上面的
	//       vermeer-audit 规矩）。⚠️ 2026-10-10 percorenolog **不改这一条**（读表不动）：
	//       Matisse 现在**有每核写**（MP1 0x34，见 CurvePerCoreCommand）但**没有每核读** ⇒
	//       ApplyPerCore 会如实说"无法回读校验"（`Verified: false`），绝不假装已校验。
	//     * Zen4 桌面（Raphael / GraniteRidge / DragonRange）：读命令 0xD5 在表里；
	//       ⚠️ 2026-10-10 percorenolog 起**每核写**也已放开（PSMU 0x06，见 CurvePerCoreCommand）。
	//       ⚠️ 读走"裸核号"（`BuildCurveReadArg`），写走 `core << 20` —— 读写 arg 布局**不一致**
	//       （与 g-helper / UXTU 同构），所以必须靠 ApplyPerCore 的**回读校验**兜住。
	//     * APU 各代号：读命令各自在表里（Renoir / Lucienne / VanGogh 除外）；每核写也已按
	//       SMU 参考全部放开（见 CurvePerCoreCommand）。
	// ------------------------------------------------------------------------------------------
	internal static uint? CurveReadCommand(CpuCodeName cpu)
	{
		switch (cpu)
		{
		case CpuCodeName.Phoenix:
		case CpuCodeName.Phoenix2:
		case CpuCodeName.HawkPoint:
			return 225u;
		case CpuCodeName.Cezanne:
			return 195u;
		case CpuCodeName.Rembrandt:
		case CpuCodeName.Mendocino:
			return 47u;
		case CpuCodeName.StrixPoint:
		case CpuCodeName.StrixHalo:
		case CpuCodeName.KrackanPoint:
			// KrackanPoint2 **故意不列**：上游把 KrackanPoint2 也映射到 APUSettings1_Strix
			// （Hardware\SMU.cs:323，读命令同样是 0xAF），所以"上游没有它的读命令"这句话
			// 是错的（2026-10-05 vermeer-audit 更正）。真正的原因是**我们的 CurveCommand
			// 里没有它的写命令**——CO 选项根本不会出现（RuntimeCpuTuning 只在
			// curveCommandPresent 时才看回读），列了只会让"可回读"变成空头支票。仍然不猜。
			return 175u;
		case CpuCodeName.Raphael:
		case CpuCodeName.GraniteRidge:
		case CpuCodeName.DragonRange:
			return 213u;
		case CpuCodeName.ShimadaPeak:
			return 163u;
		case CpuCodeName.Vermeer:
			// 2026-10-05 percore-product: 0x7C，台式机实测 CANDIDATE-OK（依据见上方整段注释）。
			// 上游来源：Zen3Settings 从 Zen2Settings.cs:35 继承的
			// Rsmu.SMU_MSG_GetDldoPsmMargin = 0x7C;  // Not sure（同族 RSMU 地址与本文件
			// GetPsmuAddrs(Matisse) 完全一致 = RSMU 就是我们的 PSMU），现已由实测确认。
			return 124u;
		default:
			// Matisse / Chagall / Milan / CastlePeak：**故意不列**。
			// Matisse 与 Vermeer 共用 Zen2 表的 0x7C，但 Matisse 本身**未实测**（见上方注释），
			// 依据不足就不写进表。保持 null = 不可回读。
			return null;
		}
	}

	// 该代号家族有没有 CO 回读命令（= UI 能否显示"回读: x CO"还是"不可回读（该平台没有回读命令）"）。
	internal static bool SupportsCurveRead(CpuCodeName cpu)
	{
		return CurveReadCommand(cpu).HasValue;
	}

	internal static (bool Psmu, uint Enable, uint Clock, uint Disable)? ClockCommand(CpuCodeName cpu)
	{
		switch (cpu)
		{
		case CpuCodeName.SummitRidge:
		case CpuCodeName.PinnacleRidge:
			return (false, 35u, 57u, 36u);
		case CpuCodeName.Matisse:
		case CpuCodeName.Vermeer:
			return (false, 36u, 38u, 37u);
		case CpuCodeName.Raphael:
		case CpuCodeName.GraniteRidge:
		case CpuCodeName.DragonRange:
			return (true, 93u, 95u, 94u);
		case CpuCodeName.Renoir:
		case CpuCodeName.Cezanne:
		case CpuCodeName.Lucienne:
			return (false, 47u, 49u, 48u);
		case CpuCodeName.Rembrandt:
		case CpuCodeName.Phoenix:
		case CpuCodeName.Phoenix2:
		case CpuCodeName.HawkPoint:
			return (true, 23u, 25u, 24u);
		default:
			return null;
		}
	}

	internal static bool SupportsClock(CpuCodeName cpu)
	{
		return ClockCommand(cpu).HasValue;
	}

	internal static int MaximumClock(CpuCodeName cpu)
	{
		// 2026-10-10 fmax-family-guard: 6500 这个上限只属于改前就已知的 Matisse/Raphael 档代号；
		// 白名单外的代号（Rome/Naples/Genoa/... 家族刚变已知的那些）保留改前答复 4500，
		// 这样"按代号问上限"的答案与改前逐字一致（它们本来也走不到 SetOcClock：ClockCommand 为 null）。
		if (!LegacyFamilyCommandsApproved(cpu))
		{
			return 4500;
		}
		CpuFamily family = GetFamily(cpu);
		if (family != CpuFamily.Matisse && family != CpuFamily.Raphael)
		{
			return 4500;
		}
		return 6500;
	}

	public SmuStatus SetOcClock(int mhz)
	{
		if (mhz < 1000 || mhz > MaximumClock(_cpu))
		{
			throw new ArgumentOutOfRangeException("mhz");
		}
		(bool, uint, uint, uint)? tuple = ClockCommand(_cpu);
		if (!tuple.HasValue)
		{
			return SmuStatus.UnknownCmd;
		}
		if (!_init || !AcquirePci())
		{
			return SmuStatus.Failed;
		}
		try
		{
			SmuStatus smuStatus = (tuple.Value.Item1 ? SendPsmu(tuple.Value.Item2, 0u) : SendMp1(tuple.Value.Item2, 0u));
			if (smuStatus != SmuStatus.OK)
			{
				return smuStatus;
			}
			SmuStatus smuStatus2 = (tuple.Value.Item1 ? SendPsmu(tuple.Value.Item3, (uint)mhz) : SendMp1(tuple.Value.Item3, (uint)mhz));
			if (smuStatus2 == SmuStatus.UnknownCmd && Family == CpuFamily.Renoir)
			{
				smuStatus2 = SendPsmu(25u, (uint)mhz);
			}
			if (smuStatus2 != SmuStatus.OK)
			{
				SmuStatus smuStatus3 = DisableOc();
				if (smuStatus3 != SmuStatus.OK)
				{
					throw new InvalidOperationException($"SMU clock rejected: {smuStatus2}; disable OC failed: {smuStatus3}");
				}
			}
			return smuStatus2;
		}
		finally
		{
			_smuMutex.ReleaseMutex();
		}
	}

	public SmuStatus DisableOc()
	{
		(bool, uint, uint, uint)? tuple = ClockCommand(_cpu);
		if (!tuple.HasValue)
		{
			return SmuStatus.UnknownCmd;
		}
		SmuStatus smuStatus = (tuple.Value.Item1 ? SendPsmu(tuple.Value.Item4, 0u) : SendMp1(tuple.Value.Item4, 0u));
		if (smuStatus != SmuStatus.UnknownCmd || Family != CpuFamily.Renoir)
		{
			return smuStatus;
		}
		return SendPsmu(29u, 0u);
	}

	public SmuStatus SetCoGfx(int value)
	{
		// 2026-10-10 fmax-family-guard: 族驱动写入 -> 先过白名单（名单外：不发命令、返回 Failed）。
		if (!LegacyFamilyCommandsApproved(_cpu))
		{
			return RefuseLegacyFamilyCommand(nameof(SetCoGfx));
		}
		uint arg = EncodeCurve(value);
		switch (Family)
		{
		case CpuFamily.Renoir:
			return SendMp1(100u, arg);
		case CpuFamily.Mobile:
		case CpuFamily.StrixHalo:
			return SendPsmu(183u, arg);
		default:
			return SmuStatus.Failed;
		}
	}

	public SmuStatus SetThm(int celsius)
	{
		// 2026-10-10 fmax-family-guard: 族驱动写入 -> 先过白名单（名单外：不发命令、返回 Failed）。
		if (!LegacyFamilyCommandsApproved(_cpu))
		{
			return RefuseLegacyFamilyCommand(nameof(SetThm));
		}
		switch (Family)
		{
		case CpuFamily.Raven:
			return SendMp1(31u, (uint)celsius);
		case CpuFamily.Renoir:
		case CpuFamily.Mobile:
		case CpuFamily.StrixPoint:
		case CpuFamily.StrixHalo:
			return SendMp1(25u, (uint)celsius);
		case CpuFamily.Matisse:
			return SendMp1(62u, (uint)celsius);
		case CpuFamily.Raphael:
			return SendMp1(63u, (uint)celsius);
		default:
			return SmuStatus.Failed;
		}
	}

	public bool GetCodeName(out CpuCodeName codeName)
	{
		codeName = CpuCodeName.Undefined;
		ulong[] array = new ulong[1];
		if (_io.Execute("ioctl_get_code_name", null, array))
		{
			codeName = (CpuCodeName)array[0];
			return true;
		}
		return false;
	}

	public bool GetSmuVersion(out uint version)
	{
		version = 0u;
		if (!AcquirePci())
		{
			return false;
		}
		try
		{
			ulong[] array = new ulong[1];
			if (_io.Execute("ioctl_get_smu_version", null, array))
			{
				version = (uint)array[0];
				return true;
			}
			return false;
		}
		finally
		{
			_smuMutex.ReleaseMutex();
		}
	}

	public PowerLimits? GetPowerLimits()
	{
		if (!AcquirePci())
		{
			return null;
		}
		try
		{
			ulong[] array = new ulong[2];
			if (!_io.Execute("ioctl_resolve_pm_table", null, array))
			{
				return null;
			}
			uint num = (uint)array[0];
			_io.Execute("ioctl_update_pm_table", null, null);
			Thread.Sleep(100);
			if (!_io.Execute("ioctl_update_pm_table", null, null))
			{
				return null;
			}
			Thread.Sleep(200);
			ulong[] array2 = new ulong[64];
			if (!_io.Execute("ioctl_read_pm_table", null, array2))
			{
				return null;
			}
			ReadOnlySpan<float> readOnlySpan = MemoryMarshal.Cast<ulong, float>(array2);
			StringBuilder stringBuilder = new StringBuilder();
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder2);
			handler.AppendLiteral("PMTable ver=0x");
			handler.AppendFormatted(num, "X6");
			handler.AppendLiteral(" floats:");
			stringBuilder3.Append(ref handler);
			for (int i = 0; i < readOnlySpan.Length; i++)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder4 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(4, 2, stringBuilder2);
				handler.AppendLiteral(" [");
				handler.AppendFormatted(i);
				handler.AppendLiteral("]=");
				handler.AppendFormatted(readOnlySpan[i], "G6");
				stringBuilder4.Append(ref handler);
			}
			Logger.WriteLine(stringBuilder.ToString());
			if (readOnlySpan[0] == 0f)
			{
				return null;
			}
			int tctlIndex = GetTctlIndex(num);
			if (tctlIndex < 0 || readOnlySpan.Length <= tctlIndex)
			{
				return null;
			}
			return new PowerLimits(readOnlySpan[0], readOnlySpan[2], readOnlySpan[4], readOnlySpan[tctlIndex], HasApuSlowField(num) ? new float?(readOnlySpan[6]) : ((float?)null));
		}
		finally
		{
			_smuMutex.ReleaseMutex();
		}
	}

	private static int GetTctlIndex(uint tableVersion)
	{
		switch (tableVersion >> 16)
		{
		case 30u:
		case 100u:
			return 22;
		case 55u:
		case 63u:
		case 64u:
		case 69u:
		case 76u:
		case 93u:
		case 101u:
			return 16;
		case 84u:
		case 98u:
			return 10;
		default:
			return -1;
		}
	}

	private static bool HasApuSlowField(uint tableVersion)
	{
		switch (tableVersion >> 16)
		{
		case 55u:
		case 63u:
		case 64u:
		case 69u:
		case 76u:
		case 93u:
		case 100u:
		case 101u:
			return true;
		default:
			return false;
		}
	}

	// ==========================================================================================
	// 2026-10-10 fmax-family: 本轮**只补齐** GetFamily 的映射缺口，已有代号的返回值一个都没动。
	//
	// 缺口是什么：Genoa / StormPeak / Bergamo / Turin / TurinD / Mero（以及同样是 Unknown 的 Milan /
	// Chagall）落进 default -> CpuFamily.Unknown，而 Initialize() 要求 `Family != CpuFamily.Unknown`
	// （本文件 :63；内部测试构造器同理，:50）-> 这些代号**服务根本初始化不了**。后果：ResolveFMax
	// 明明给它们写了 ReadWrite / ReadOnly（本文件 :1005-1039），却永远到不了邮箱（不是命令错，
	// 是 _init 就是 false）。补齐家族解析即可让那批能力真正可达（FMax 走的是自己那张显式地址表，
	// 与家族无关；家族只决定 "GetFamily != Unknown" 这一条门槛，以及下面列出的族驱动通路）。
	//
	// 依据（**全部取自本仓库已有的表**，没有新造语义；推理链：k_addridx 定"哪一组邮箱地址"，
	// RyzenSMU.p 的 PM 表 case 分组定"哪一套命令"，两者交叉即得家族标签）：
	//
	//   ① diagnostics\smudbg-reference-20261005\key-files\RyzenSMU.p:194-245 —— k_addrinfo[] +
	//      k_addridx[]：每个代号 -> **同一组 SMN 邮箱地址**（即本文件的 RSMU/PSMU 组）：
	//        组0 = 0x3B10524 / 0x3B10570 / 0x3B10A40 = GetPsmuAddrs(Matisse | Raphael)
	//        组1 = 0x3B1051C / 0x3B10568 / 0x3B10590 = GetPsmuAddrs(Zen1Desktop)
	//        组2 = 0x3B10A20 / 0x3B10A80 / 0x3B10A88 = GetPsmuAddrs(Raven | Renoir | Mobile | Strix*)
	//      该表里：Genoa/StormPeak/Bergamo/Turin/TurinD/Milan/Chagall = 组0；Mero = 组2。
	//      （同一个 .p 的 CPUID -> 代号表 :110-175 也把 Mero 0x1798 排在 VanGogh 0x1790/0x1791 旁边。）
	//   ② 同一份 RyzenSMU.p:436-517（get_pm_table_version / transfer_table_to_dram / get_pm_table_base）
	//      的 case 分组 = "哪一代共用同一套 SMU 命令"：
	//        Genoa/StormPeak/Bergamo/Turin/TurinD 与 Raphael/DragonRange/GraniteRidge/ShimadaPeak 同一条；
	//        Milan/Chagall 与 Matisse/Vermeer/CastlePeak/Rome 同一条；
	//        Mero 与 Renoir/Cezanne/VanGogh/Phoenix/... 同一条（APU 条）。
	//   ③ 本仓库 修改日志.txt:6676-6686（上一轮的读表审计，对照上游 ZenStates-Core）：上游把
	//      Genoa/StormPeak/Bergamo/Turin/TurinD 归到 **Zen4Settings/Zen5Settings**（= Raphael/GraniteRidge
	//      那一档，Rsmu 0xD5 由 Zen5Settings 继承），把 **Mero 与 VanGogh 放进同一个
	//      APUSettings1_VanGogh** —— 而 VanGogh 在本文件就是 CpuFamily.Mobile。
	//   ④ 本文件 FMax 能力表自己的分组（:940-941）：Genoa/StormPeak 与 Raphael/DragonRange 同一行
	//      （"Zen4 桌面"），Bergamo/Turin/TurinD 与 GraniteRidge 同一行（"Zen5 桌面"，同一组地址），
	//      Mero 在 "APU1" 那一行（与 VanGogh 同行）。
	//   => 结论：Genoa/StormPeak/Bergamo/Turin/TurinD -> CpuFamily.Raphael（Zen4/Zen5 桌面档，
	//      PSMU 组与 Raphael 完全相同）；Mero -> CpuFamily.Mobile（VanGogh 的兄弟档，PSMU 组相同）；
	//      Milan/Chagall -> CpuFamily.Matisse（Zen3 桌面档，与 Vermeer 同档同组）。
	//
	// ⚠️ 补映射后**必须**配一道闸门（2026-10-10 fmax-family-guard，用户决定 ①）：这些代号以前
	//    Family=Unknown -> 服务不初始化 -> SetStapm/SetFast/SetSlow/SetThm/SetCoGfx 全部走
	//    default = Failed（一条命令都不发）。补齐家族后若不加闸，"家族已知"会**顺手**把对应档的族驱动
	//    命令放开（Raphael 档 Stapm MP1 79 / Fast 62 / Slow 95 / Thm 63；Mobile 档 20/21/22/25；
	//    Matisse 档只有 Thm 62）—— 这些命令 ID 在这些代号上从未实测、从未获批。
	//    => 一律由下面的 **LegacyFamilyCommandsApproved 白名单**拦住（名单 = 改前就真实可用的 29 个
	//    代号），名单外返回 SmuStatus.Failed（与改前 default 逐字相同）+ 一行日志，**一个寄存器都不写**。
	//    同一把尺子也用在 ModeControl 的两条重放判据（:93 / :104）、MaximumClock 的 6500 上限与
	//    CpuInfo.HasIgpuCurveMailbox 上。
	//    FMax 自身**不走**白名单（ResolveFMax/FMaxSpec 是显式按代号的地址+命令表，见 FMaxCapability.cs），
	//    所以本轮要修的"能力表里有、却到不了邮箱"照旧修好。
	//
	// ⚠️ 残余不确定（**只报告，不猜**）：Bergamo/Turin/TurinD 的 **MP1** 地址组（0x3B10530 / 0x3B1057C /
	//    0x3B10A44）沿用 Raphael 组 —— k_addrinfo 只描述 RSMU/PSMU 组，MP1 组除本文件 GetMp1Addrs 外
	//    仓库里没有第二份表可查，只能按"同在 RyzenSMU.p 的 Zen4/Zen5 桌面 case 与同一 k_addridx 组"
	//    推定为同一组（未实测）。若将来拿到这些平台的实测，先核对这一条。
	//
	// 2026-10-10 fmax-family-guard（用户决定 ②）：**Rome / Naples 一并补上**，同一份依据 ——
	//    * Rome（Zen2 EPYC，CPUID 17h/31，pkg_type != 7）：k_addridx = 组0（桌面组）；RyzenSMU.p 的三条
	//      PM 表 case 都把它与 Matisse/Vermeer/CastlePeak/Chagall/Milan 排在同一条（:442-443, :468-469,
	//      :496-499），修改日志.txt:6684 记的上游也是 `Zen2Settings_Rome` -> CpuFamily.Matisse（Zen2 桌面档）。
	//    * Naples（Zen1 EPYC，CPUID 17h/01，pkg_type == 3）：k_addridx = 组1，正等于
	//      GetPsmuAddrs(Zen1Desktop)（0x3B1051C/0x3B10568/0x3B10590）；RyzenSMU.p 每条 PM 表 case 都把它
	//      与 SummitRidge/PinnacleRidge/Colfax/Threadripper 排在一起 -> CpuFamily.Zen1Desktop。
	//    两者都**不模糊**：地址组（k_addridx）与命令分档（RyzenSMU.p case）交叉指向同一个家族标签。
	//    ⚠️ 补映射本身**不会**让它们多发一条命令：族驱动的写命令在 LegacyFamilyCommandsApproved
	//    白名单里没有它们。
	//
	// ⚠️ **仍然故意 Unknown**：只剩 Undefined（非 AMD / CPUID 表外，没有任何依据）。
	//
	// ⚠️ 顺带记录（**本轮不动**）：CastlePeak 在 k_addridx 里是组0（桌面组），而本函数把它归到
	//    Zen1Desktop（组1）—— 这是上游就有的既有映射，属"改前就已知、一直这样跑"的代号，
	//    按"不改动已经能用的代号"原则原样保留，仅在此记录这处不一致。
	// ==========================================================================================
	public static CpuFamily GetFamily(CpuCodeName cpu)
	{
		switch (cpu)
		{
		case CpuCodeName.Colfax:
		case CpuCodeName.Threadripper:
		case CpuCodeName.CastlePeak:
		case CpuCodeName.SummitRidge:
		case CpuCodeName.PinnacleRidge:
		// 2026-10-10 fmax-family-guard: Naples（Zen1 EPYC，17h/01）在 k_addridx 是组1（= 本档的
		// 0x3B1051C/0x3B10568/0x3B10590），RyzenSMU.p 的每条 PM 表 case 都与本档同列 -> Zen1 桌面档。
		case CpuCodeName.Naples:
			return CpuFamily.Zen1Desktop;
		case CpuCodeName.Picasso:
		case CpuCodeName.RavenRidge:
		case CpuCodeName.RavenRidge2:
		case CpuCodeName.Dali:
			return CpuFamily.Raven;
		case CpuCodeName.Renoir:
		case CpuCodeName.Cezanne:
		case CpuCodeName.Lucienne:
			return CpuFamily.Renoir;
		case CpuCodeName.Matisse:
		case CpuCodeName.Vermeer:
		// 2026-10-10 fmax-family: Milan（Zen3 EPYC，CPUID 19h/00-01）/ Chagall（Zen3 Threadripper PRO，
		// 19h/08）与 Vermeer 同为 Zen3 桌面档：RyzenSMU.p 的 PM 表 case（:442-443, :468-469, :496-499）
		// 把 Milan/Chagall 与 Matisse/Vermeer/CastlePeak/Rome 排在同一条，k_addridx 也是组0；
		// 修改日志.txt:6682 也把 "Vermeer/Chagall/Milan" 当作同一档读命令候选。
		case CpuCodeName.Milan:
		case CpuCodeName.Chagall:
		// 2026-10-10 fmax-family-guard: Rome（Zen2 EPYC，17h/31）与 Matisse/Vermeer 同为 Zen2 桌面档：
		// k_addridx 组0，RyzenSMU.p 的三条 PM 表 case 都与本档同列，上游是 `Zen2Settings_Rome`
		// （修改日志.txt:6684）-> 与 Matisse 同档。
		case CpuCodeName.Rome:
			return CpuFamily.Matisse;
		case CpuCodeName.Rembrandt:
		case CpuCodeName.Vangogh:
		case CpuCodeName.Phoenix:
		case CpuCodeName.Phoenix2:
		case CpuCodeName.Mendocino:
		case CpuCodeName.HawkPoint:
		// 2026-10-10 fmax-family: Mero（CPUID 17h/98）与 VanGogh（17h/90-91）在 RyzenSMU.p 同一条
		// APU case、k_addridx 同为组2，且上游把 Mero 与 VanGogh 放进**同一个 APUSettings1_VanGogh**
		// （修改日志.txt:6686）-> 与 VanGogh 同档（Mobile），不新造族。
		case CpuCodeName.Mero:
			return CpuFamily.Mobile;
		case CpuCodeName.Raphael:
		case CpuCodeName.GraniteRidge:
		case CpuCodeName.DragonRange:
		// 2026-10-10 fmax-family: Genoa/StormPeak（Zen4 EPYC/Threadripper，19h/11、19h/18）与
		// Bergamo/Turin/TurinD（Zen4c/Zen5 服务器，19h/A0、1Ah/02、1Ah/11）都落 k_addridx 组0
		// （= 本档同一组 PSMU 地址），且在 RyzenSMU.p 的 PM 表 case 与 Raphael/DragonRange/GraniteRidge
		// 同一条；上游把 Genoa 档归入 Zen4Settings/Zen5Settings（修改日志.txt:6678-6680）。
		// 它们此前是 Unknown -> 服务不初始化 -> FMax 能力表里的 ReadWrite 永远到不了邮箱。
		case CpuCodeName.Genoa:
		case CpuCodeName.StormPeak:
		case CpuCodeName.Bergamo:
		case CpuCodeName.Turin:
		case CpuCodeName.TurinD:
			return CpuFamily.Raphael;
		case CpuCodeName.FireFlight:
			return CpuFamily.Mobile;
		case CpuCodeName.StrixPoint:
		case CpuCodeName.KrackanPoint:
		case CpuCodeName.KrackanPoint2:
			return CpuFamily.StrixPoint;
		case CpuCodeName.StrixHalo:
			return CpuFamily.StrixHalo;
		case CpuCodeName.ShimadaPeak:
			return CpuFamily.ShimadaPeak;
		default:
			// 2026-10-10 fmax-family-guard: 走到这里的只剩 Undefined（非 AMD / CPUID 表外，没有任何依据）。
			// 其余代号在 2026-10-10 已被逐条补齐（Rome/Naples 见上方 fmax-family-guard 注释）。
			return CpuFamily.Unknown;
		}
	}

	// ==========================================================================================
	// 2026-10-10 fmax-family-guard（用户决定 ①）：**族驱动命令的显式白名单（allow-list）**。
	//
	// 存在理由（一句话）：让"家族变得已知"**不可能**顺手放开写入。GetFamily 补齐之后，
	// Genoa / StormPeak / Bergamo / Turin / TurinD / Mero / Milan / Chagall / Rome / Naples 也有了
	// 非 Unknown 家族，于是所有"按 Family 选命令"的通路都会对它们生效 —— 而这些代号上的命令 ID
	// **从未实测、从未获批**（改前它们 Family=Unknown -> Initialize() 失败 -> 一条命令都不发）。
	// 本名单把两件事解耦：
	//     * 家族已知  = 服务能初始化、FMax（显式地址表）能读/写  -> 本轮要修的正是这个；
	//     * 允许发族驱动命令 = 只有本名单里的代号 -> 名单外一律拒绝，不发命令。
	//
	// 为什么做成**白名单**而不是"排除那 10 个"：将来任何新代号补进 GetFamily 时，默认就是不发命令，
	// 必须有人显式把它加到本名单才算批准 —— 这正好是仓库一贯的"依据不足就不发"。
	//
	// 名单口径 = **改前（GetFamily 还是原样时）Family != Unknown 的全部 29 个代号**，也就是改前
	// 真的能走到族驱动命令的那批；逐条与 2026-10-10 之前的 GetFamily 一一对应。
	//
	// 用在哪：SetStapm / SetFast / SetSlow / SetThm / SetCoGfx（本文件）、MaximumClock（本文件，
	// 保留改前 4500 的答复）、CpuInfo.HasIgpuCurveMailbox（PawnIO\CpuInfo.cs）、
	// MechrevoLite.Mode\ModeControl.cs 的重放判据 :93 / :104。
	// ⚠️ **FMax 不走这里**：ResolveFMax/FMaxSpec 是按代号的显式地址+命令表（FMaxCapability.cs），
	//    与家族标签无关，所以 FMax 能力在能力表覆盖的代号上照旧可达 —— 本轮的目的就是这个。
	// ⚠️ 名单外拒绝时返回 SmuStatus.Failed，与改前 Unknown 家族落 default 的**返回值逐字相同**，
	//    调用方（ModeControl 等）看到的日志/状态与改前一致，且**一个寄存器都不写**。
	// ==========================================================================================
	internal static bool LegacyFamilyCommandsApproved(CpuCodeName cpu)
	{
		switch (cpu)
		{
		// --- Zen1 桌面（= 改前 Zen1Desktop 档）---
		case CpuCodeName.Colfax:
		case CpuCodeName.Threadripper:
		case CpuCodeName.CastlePeak:
		case CpuCodeName.SummitRidge:
		case CpuCodeName.PinnacleRidge:
		// --- Raven 档 ---
		case CpuCodeName.Picasso:
		case CpuCodeName.RavenRidge:
		case CpuCodeName.RavenRidge2:
		case CpuCodeName.Dali:
		// --- Renoir 档 ---
		case CpuCodeName.Renoir:
		case CpuCodeName.Cezanne:
		case CpuCodeName.Lucienne:
		// --- Matisse 档（注意：**不含** Milan / Chagall / Rome，它们是本轮新补的）---
		case CpuCodeName.Matisse:
		case CpuCodeName.Vermeer:
		// --- Mobile 档（注意：**不含** Mero）---
		case CpuCodeName.Rembrandt:
		case CpuCodeName.Vangogh:
		case CpuCodeName.Phoenix:
		case CpuCodeName.Phoenix2:
		case CpuCodeName.Mendocino:
		case CpuCodeName.HawkPoint:
		case CpuCodeName.FireFlight:
		// --- Raphael 档（注意：**不含** Genoa / StormPeak / Bergamo / Turin / TurinD）---
		case CpuCodeName.Raphael:
		case CpuCodeName.GraniteRidge:
		case CpuCodeName.DragonRange:
		// --- Strix / ShimadaPeak 档 ---
		case CpuCodeName.StrixPoint:
		case CpuCodeName.KrackanPoint:
		case CpuCodeName.KrackanPoint2:
		case CpuCodeName.StrixHalo:
		case CpuCodeName.ShimadaPeak:
			return true;
		default:
			// Milan / Chagall / Rome / Naples / Genoa / StormPeak / Bergamo / Turin / TurinD / Mero /
			// Undefined：家族已知（或本就无依据），但族驱动命令**未获批准** -> 不发。
			return false;
		}
	}

	// 白名单外的代号：族驱动命令一律拒绝。**静默安全** = 一个寄存器都不写；返回值与改前
	// （Unknown 家族落 default）完全相同，只多一行日志把"为什么没发"写清楚，便于用户与日志核对。
	private SmuStatus RefuseLegacyFamilyCommand(string operation)
	{
		Logger.WriteLine($"[smu-legacy] {operation} refused: {_cpu} (family {GetFamily(_cpu)}) is not in the approved legacy family-command allow-list (RyzenSmuService.LegacyFamilyCommandsApproved) -> no command sent");
		return SmuStatus.Failed;
	}

	internal static uint EncodeCurve(int steps)
	{
		if ((steps < -40 || steps > 0) ? true : false)
		{
			throw new ArgumentOutOfRangeException("steps");
		}
		return (uint)(steps & 0xFFFFF);
	}

	private SmuStatus SetStapm(int watts)
	{
		// 2026-10-10 fmax-family-guard: 族驱动写入 -> 先过白名单（名单外：不发命令、返回 Failed）。
		if (!LegacyFamilyCommandsApproved(_cpu))
		{
			return RefuseLegacyFamilyCommand(nameof(SetStapm));
		}
		uint arg = (uint)(watts * 1000);
		switch (Family)
		{
		case CpuFamily.Raven:
			return SendMp1(26u, arg);
		case CpuFamily.Renoir:
		{
			SmuStatus result = SendMp1(20u, arg);
			SendPsmu(49u, arg);
			return result;
		}
		case CpuFamily.Mobile:
		case CpuFamily.StrixPoint:
		case CpuFamily.StrixHalo:
			return SendMp1(20u, arg);
		case CpuFamily.Raphael:
			return SendMp1(79u, arg);
		default:
			return SmuStatus.Failed;
		}
	}

	private SmuStatus SetFast(int watts)
	{
		// 2026-10-10 fmax-family-guard: 族驱动写入 -> 先过白名单（名单外：不发命令、返回 Failed）。
		if (!LegacyFamilyCommandsApproved(_cpu))
		{
			return RefuseLegacyFamilyCommand(nameof(SetFast));
		}
		uint arg = (uint)(watts * 1000);
		switch (Family)
		{
		case CpuFamily.Raven:
			return SendMp1(27u, arg);
		case CpuFamily.Renoir:
		{
			SmuStatus result = SendMp1(21u, arg);
			SendPsmu(50u, arg);
			return result;
		}
		case CpuFamily.Mobile:
		case CpuFamily.StrixPoint:
		case CpuFamily.StrixHalo:
			return SendMp1(21u, arg);
		case CpuFamily.Raphael:
			return SendMp1(62u, arg);
		default:
			return SmuStatus.Failed;
		}
	}

	private SmuStatus SetSlow(int watts)
	{
		// 2026-10-10 fmax-family-guard: 族驱动写入 -> 先过白名单（名单外：不发命令、返回 Failed）。
		if (!LegacyFamilyCommandsApproved(_cpu))
		{
			return RefuseLegacyFamilyCommand(nameof(SetSlow));
		}
		uint arg = (uint)(watts * 1000);
		switch (Family)
		{
		case CpuFamily.Raven:
			return SendMp1(28u, arg);
		case CpuFamily.Renoir:
		{
			SmuStatus result = SendMp1(22u, arg);
			SendPsmu(51u, arg);
			SendPsmu(52u, arg);
			return result;
		}
		case CpuFamily.Mobile:
		case CpuFamily.StrixPoint:
		case CpuFamily.StrixHalo:
			return SendMp1(22u, arg);
		case CpuFamily.Raphael:
			return SendMp1(95u, arg);
		default:
			return SmuStatus.Failed;
		}
	}

	private bool ReadReg(uint addr, out uint value)
	{
		value = 0u;
		ulong[] array = new ulong[1];
		if (_execute("ioctl_read_smu_register", new ulong[1] { addr }, array))
		{
			value = (uint)array[0];
			return true;
		}
		return false;
	}

	private bool WriteReg(uint addr, uint value)
	{
		return _execute("ioctl_write_smu_register", new ulong[2] { addr, value }, null);
	}

	private bool WaitForMailboxIdle(uint rspAddr)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		int num = 0;
		while (stopwatch.ElapsedMilliseconds < 200)
		{
			if (!ReadReg(rspAddr, out var value))
			{
				return false;
			}
			if (value != 0)
			{
				return true;
			}
			num++;
			if (num > 256)
			{
				Thread.Sleep(1);
			}
			else if (num > 32)
			{
				Thread.Yield();
			}
		}
		return false;
	}

	private SmuStatus SendMp1(uint cmd, uint arg)
	{
		GetMp1Addrs(out var cmd2, out var rsp, out var arg2);
		if (cmd2 == 0)
		{
			return SmuStatus.Failed;
		}
		uint[] response;
		return MailboxRaw(cmd2, rsp, arg2, cmd, new uint[1] { arg }, out response);
	}

	private SmuStatus SendPsmu(uint cmd, uint arg)
	{
		uint[] response;
		return SendPsmuRaw(cmd, new uint[1] { arg }, out response);
	}

	// 2026-10-05 coread: SendPsmu 的"带返回值"版本。语义与 SendPsmu 完全一致（同一个 PSMU 邮箱、
	// 同一组地址、同一条 MailboxRaw 通路），只是不再把 MailboxRaw 已经读回的 6 个 response word
	// 丢掉。**只读**改造：写入路径（SetCoAll / SetOcClock / DisableOc / SetXxx）一字未改，
	// 它们仍然走 SendPsmu/SendMp1，只是内部实现改为委托到这里。
	private SmuStatus SendPsmuRaw(uint cmd, uint[] args, out uint[] response)
	{
		GetPsmuAddrs(out var cmd2, out var rsp, out var arg2);
		if (cmd2 == 0)
		{
			response = new uint[6];
			return SmuStatus.Failed;
		}
		return MailboxRaw(cmd2, rsp, arg2, cmd, args, out response);
	}

	// ==========================================================================================
	// 2026-10-05 coread: CO（Curve Optimizer）**只读**回读。
	//
	// 语义：
	//   * 命令 = CurveReadCommand(_cpu)（每核一条；没有读命令的代号返回 UnknownCmd）；
	//   * 通道 = PSMU 邮箱（与上游 ZenStates-Core 的 Rsmu 邮箱相同，= GetPsmuAddrs(Mobile)）；
	//   * arg0 = 裸核号（与上游 Cpu.GetPsmMarginSingleCore(core) 对 APU 的用法一致；
	//     本机实测 0..7 全部 OK）；
	//   * response[0] = SMU 当前的每核 margin（原始值，未解码；解码见 SmuCoRoundtrip.DecodeCurveMargin）。
	//
	// ⚠️ 纯读：不写任何寄存器（除了 MailboxRaw 为了发命令必须走的"清 RSP / 填 ARG / 写 MSG"，
	//    这与 GetSmuVersion / GetPowerLimits 的现有行为一致——发一条读命令本身也是发一条 SMU 命令）。
	// ⚠️ 绝不放进轮询：每核一条命令，8 核 = 8 条；只在"检测能力"时读一次。
	// ==========================================================================================
	public SmuStatus ReadCurveMargin(int coreIndex, out uint[] response)
	{
		if (coreIndex < 0)
		{
			throw new ArgumentOutOfRangeException("coreIndex");
		}
		return ReadCurveMarginRaw(EncodeCurveReadArg(_cpu, coreIndex), out response);
	}

	// ==========================================================================================
	// 2026-10-05 percore-product: **每核读的 arg0 编码按代号选**（这是台式机实测暴露出来的差异）。
	//
	//   * APU 侧（本机 Phoenix / SMU 0x4C6500，2026-10-05 只读实测）：arg0 = **裸核号** 0..7，
	//     8 核全部 status OK —— 这与上游 Cpu.GetPsmMarginSingleCore 对 APU 的用法一致。
	//   * 桌面 Vermeer 侧（用户 R7 5700X / SMU 0x00384C00，2026-10-05 实测）：上游声明并按
	//     **core << 20** 读取；"全核同值"时两种编码读出来一样（无法区分），但**写后逐核回读**
	//     给出了区分证据：core0 被写成 -5 之后，用 core<<20 读 core1 仍然是 -15（没有跟着
	//     core0 变成 -5）-> SMU 确实认了 core<<20 这个字段，而不是只看低位的裸核号。
	//     因此 Vermeer 上必须用 core << 20，用裸核号读有可能读到核 0 的值（未实测，不冒险）。
	//
	// 两种编码都保留：`ReadCurveMarginRaw(arg0)` 仍然由调用方**原样**指定 arg0（给台式机
	// 验证工具做编码探针用），本方法只在 `ReadCurveMargin(core)` 这条产品通路上做代号分派。
	// 其它代号（含所有 APU）行为**一字未变** = 裸核号。
	// ==========================================================================================
	internal static uint EncodeCurveReadArg(CpuCodeName cpu, int coreIndex)
	{
		return (cpu == CpuCodeName.Vermeer) ? ((uint)coreIndex << 20) : (uint)coreIndex;
	}

	// 同一条读命令，但 arg0 由调用方**原样**给出。给台式机验证工具用：
	// 它需要分别用「裸核号」与「core<<20」两种编码读同一核，才能判定哪种编码有效。
	internal SmuStatus ReadCurveMarginRaw(uint arg0, out uint[] response)
	{
		response = new uint[6];
		uint? cmd = CurveReadCommand(_cpu);
		if (!cmd.HasValue)
		{
			return SmuStatus.UnknownCmd;
		}
		if (!_init)
		{
			return SmuStatus.Failed;
		}
		return SendPsmuRaw(cmd.Value, new uint[1] { arg0 }, out response);
	}

	// 读回原始 response[0] 的"CO 步数"解码。
	// 为什么不能只写一种：(int) 一把梭：
	//   * 上游 ZenStates-Core 用 Utils.MakePsmMarginArg 把 margin 编成 **低 16 位**（-5 -> 0xFFFB），
	//     而 LM 自己的 EncodeCurve 编成 **低 20 位**（-5 -> 0xFFFFB）；
	//   * 不同代 SMU 到底把哪一段塞进 response[0]（16 位 / 20 位 / 32 位符号扩展）未实测确认
	//     （本机读到的是 0，三种解都一样）。
	// 因此按"最像 CO 步数"的优先级挑：32 位有符号 -> 20 位有符号 -> 16 位有符号，
	// 三者都不在 CO 合理区间（-50..50）时返回 null = 无法解码（UI 侧按"读失败"处理）。
	internal static int? DecodeCurveMargin(uint raw)
	{
		int asInt32 = unchecked((int)raw);
		if (asInt32 >= -50 && asInt32 <= 50)
		{
			return asInt32;
		}
		int asInt20 = (asInt32 << 12) >> 12;
		if (asInt20 >= -50 && asInt20 <= 50)
		{
			return asInt20;
		}
		int asInt16 = (short)(raw & 0xFFFFu);
		if (asInt16 >= -50 && asInt16 <= 50)
		{
			return asInt16;
		}
		return null;
	}

	// ==========================================================================================
	// 2026-10-10 fmax: **FMax（PBO 最大频率 / boost limit）** —— RSMU 邮箱命令。
	//
	// 依据（用户给定的、已从参考实现 SMUDebugTool / ZenStates-Core 研究好的结论，本轮**不重复调研**）：
	//
	//   来源与许可：irusanov/SMUDebugTool（GPL-3.0，https://github.com/irusanov/SMUDebugTool）与
	//   irusanov/ZenStates-Core（GPL-3.0，https://github.com/irusanov/ZenStates-Core）—— 下表的能力
	//   表、Get/Set 命令行、SMN 邮箱地址组与 6 参数打包均据其重写；L-Mechrevo 本身为 GPL-3.0-only，
	//   许可相容。许可文本：LICENSES\GPL-3.0.txt；第三方清单：THIRD-PARTY-NOTICES.md。
	//
	//   代号（能力表原文）                                    能力    Get   Set   RSMU 邮箱 MSG/RSP/ARG (SMN)
	//   Phoenix/Phoenix2/HawkPoint/Rembrandt/Mendocino/Strix*   读写   0x42  0x47  0x3B10A20/0x3B10A80/0x3B10A88
	//   Raphael/Genoa/StormPeak/DragonRange (Zen4 桌面)         读写   0x6E  0x70  0x3B10524/0x3B10570/0x3B10A40
	//   GraniteRidge/Bergamo/Turin/TurinD (Zen5 桌面)           读写   0x6E  0x70  同上
	//   ShimadaPeak (Zen5)                                      只写   无    0x70  0x3B10924/0x3B10970/0x3B10A40
	//   Matisse/CastlePeak/Vermeer/Chagall/Milan (Zen2/3 桌面)  只读   0x6E  0     0x3B10524/0x3B10570/0x3B10A40
	//   Renoir/Lucienne/Cezanne/VanGogh/Mero (APU1)            只读   0x42  0     APU 组
	//   RavenRidge/RavenRidge2/FireFlight (APU0)                只读   0x12  0     APU 组；**读之前 arg0 必须先置 3**
	//                                                          （RavenRidge2 = 2026-10-10 按同硅片一致加入，用户已批准）
	//   Dali/Picasso                                            只读   0x86  0     APU 组
	//   SummitRidge/PinnacleRidge/Colfax/Rome/Carrizo/... 等    都无   0     0     —
	//
	// 用户决定（原话）：「不支持平台只读取 支持平台可以写入」
	//   => ReadOnly 的平台**照样读**并把值显示给用户，只是**写入路径被禁用**（本层直接拒绝，
	//      一条命令都不发）；None 的平台连读都不做（ResolveFMax 返回 null）。
	//
	// ⚠️ 参数语义**未确认**（用户明确要求把这一点记在代码里）：
	//   参考实现写的是 `frequency & 0xFFFFF`（**当作绝对 MHz**），但它的作者说过真实参数是
	//   -1000..+200 的**偏移**，与代码互相矛盾。本轮**照参考实现原样打包**
	//   （arg0 = value & 0xFFFFF，arg1..5 = 0，见 EncodeFMaxArg / SetBoostLimitFrequencyAllCores），
	//   并把**原始 response[0]** 一路带到 UI 与日志里，供用户判定到底是哪种语义。
	//   在语义确认之前**不要**擅自把范围/单位换成偏移那一套。
	//
	// 通道：这些地址就是本仓库已有的 PSMU/RSMU 邮箱（SendPsmuRaw 走的是同一个 MailboxRaw），
	//       所以读写都复用同一套"清 RSP / 填 ARG / 写 MSG / 等 RSP"的既有实现，不新造协议。
	// ==========================================================================================

	// 地址组与 ZenStates-Core 的 RSMU 邮箱完全同源（GPL-3.0，见本段开头的来源声明）。
	// 下面三组常量（APU 组 / 桌面组 / ShimadaPeak 组）都适用这一条。
	// APU 组（= GetPsmuAddrs 里 Raven/Renoir/Mobile/StrixPoint/StrixHalo 那一组）。
	private const uint FMaxApuMailboxCommand = 0x3B10A20u;

	private const uint FMaxApuMailboxResponse = 0x3B10A80u;

	private const uint FMaxApuMailboxArgument = 0x3B10A88u;

	// Zen2/Zen3/Zen4/Zen5 桌面组（= GetPsmuAddrs 里 Matisse/Raphael 那一组）。
	private const uint FMaxDesktopMailboxCommand = 0x3B10524u;

	private const uint FMaxDesktopMailboxResponse = 0x3B10570u;

	private const uint FMaxDesktopMailboxArgument = 0x3B10A40u;

	// ShimadaPeak (Zen5) 自己一组。
	private const uint FMaxShimadaMailboxCommand = 0x3B10924u;

	private const uint FMaxShimadaMailboxResponse = 0x3B10970u;

	private const uint FMaxShimadaMailboxArgument = 0x3B10A40u;

	// 代号 -> FMax 能力（= 上表的纯函数版本；null = 既不能读也不能写）。
	// **未列出的代号一律返回 null（= None）**，绝不"猜一个 ID 试试"。
	internal static FMaxSpec? ResolveFMax(CpuCodeName cpu)
	{
		switch (cpu)
		{
		// ---- APU2：读 + 写（本机 Ryzen 7 8745H = Phoenix 就在这一档）----
		case CpuCodeName.Phoenix:
		case CpuCodeName.Phoenix2:
		case CpuCodeName.HawkPoint:
		case CpuCodeName.Rembrandt:
		case CpuCodeName.Mendocino:
		case CpuCodeName.StrixPoint:
		case CpuCodeName.StrixHalo:
		case CpuCodeName.KrackanPoint:
		// 用户给的能力表写的是 "Strix*"；KrackanPoint2 属同一族（上游 Hardware\SMU.cs 也把它
		// 映射到 APUSettings1_Strix），因此与同族一致地给读写能力。
		case CpuCodeName.KrackanPoint2:
			return new FMaxSpec(0x42u, 0x47u, FMaxApuMailboxCommand, FMaxApuMailboxResponse, FMaxApuMailboxArgument, 0u);
		// ---- Zen4 桌面：读 + 写 ----
		case CpuCodeName.Raphael:
		case CpuCodeName.Genoa:
		case CpuCodeName.StormPeak:
		case CpuCodeName.DragonRange:
		// ---- Zen5 桌面：读 + 写（与 Zen4 桌面同一组地址）----
		case CpuCodeName.GraniteRidge:
		case CpuCodeName.Bergamo:
		case CpuCodeName.Turin:
		case CpuCodeName.TurinD:
			return new FMaxSpec(0x6Eu, 0x70u, FMaxDesktopMailboxCommand, FMaxDesktopMailboxResponse, FMaxDesktopMailboxArgument, 0u);
		// ---- ShimadaPeak (Zen5)：**只写，没有回读**（Get = 0）----
		case CpuCodeName.ShimadaPeak:
			return new FMaxSpec(0u, 0x70u, FMaxShimadaMailboxCommand, FMaxShimadaMailboxResponse, FMaxShimadaMailboxArgument, 0u);
		// ---- Zen2/Zen3 桌面：**只读**（Set = 0 -> 写入路径在本层就不存在）----
		case CpuCodeName.Matisse:
		case CpuCodeName.CastlePeak:
		case CpuCodeName.Vermeer:
		case CpuCodeName.Chagall:
		case CpuCodeName.Milan:
			return new FMaxSpec(0x6Eu, 0u, FMaxDesktopMailboxCommand, FMaxDesktopMailboxResponse, FMaxDesktopMailboxArgument, 0u);
		// ---- APU1：**只读** ----
		case CpuCodeName.Renoir:
		case CpuCodeName.Lucienne:
		case CpuCodeName.Cezanne:
		case CpuCodeName.Vangogh:
		case CpuCodeName.Mero:
			return new FMaxSpec(0x42u, 0u, FMaxApuMailboxCommand, FMaxApuMailboxResponse, FMaxApuMailboxArgument, 0u);
		// ---- APU0：**只读**，且读之前 arg0 必须先置 3（上游在 Raven 上就是这么读的）----
		// 2026-10-10 fmax-raven2: RavenRidge2 与 RavenRidge **同硅片**（同一代 APU0），本文件
		// GetFamily 里两者同为 CpuFamily.Raven，RyzenSMU.p 里两者同一条 APU case 且 k_addridx 同为
		// 组2（:202-245），即 "0x12 + arg0=3 + APU 邮箱组" 这条配方对两者是同一份。用户已批准按
		// 兄弟硅片一致处理（放宽到**只读**，Get=0x12 / Set=0 仍然没有写路径）。仓库里没有任何一处
		// 显示两者在 FMax 上应当不同（能力表只是没列它）。
		case CpuCodeName.RavenRidge:
		case CpuCodeName.RavenRidge2:
		case CpuCodeName.FireFlight:
			return new FMaxSpec(0x12u, 0u, FMaxApuMailboxCommand, FMaxApuMailboxResponse, FMaxApuMailboxArgument, 3u);
		// ---- Dali / Picasso：**只读** ----
		case CpuCodeName.Dali:
		case CpuCodeName.Picasso:
			return new FMaxSpec(0x86u, 0u, FMaxApuMailboxCommand, FMaxApuMailboxResponse, FMaxApuMailboxArgument, 0u);
		default:
			// SummitRidge / PinnacleRidge / Colfax / Threadripper / Rome / Naples / Undefined 以及
			// 一切未列出的代号：**既不能读也不能写**（"未列出 = 不支持"，绝不猜命令 ID）。
			// ⚠️ 这里曾经也列着 RavenRidge2；2026-10-10 按"同硅片 = 同配方 + 用户批准"移到了上面的
			//    APU0 分支（只读，仍然没有写路径）。
			return null;
		}
	}

	// 该代号家族有没有 FMax 能力（None = 没有）。
	internal static FMaxCapability ClassifyFMax(CpuCodeName cpu)
	{
		return ResolveFMax(cpu)?.Capability ?? FMaxCapability.None;
	}

	// 该代号能不能**写** FMax（ReadWrite / WriteNoReadback）。这是写入路径的唯一守卫。
	internal static bool SupportsFMaxWrite(CpuCodeName cpu)
	{
		FMaxCapability capability = ClassifyFMax(cpu);
		return capability == FMaxCapability.ReadWrite || capability == FMaxCapability.WriteNoReadback;
	}

	// 该代号能不能**读** FMax（ReadWrite / ReadOnly）。WriteNoReadback（ShimadaPeak）为 false。
	internal static bool SupportsFMaxRead(CpuCodeName cpu)
	{
		FMaxCapability capability = ClassifyFMax(cpu);
		return capability == FMaxCapability.ReadWrite || capability == FMaxCapability.ReadOnly;
	}

	// ==========================================================================================
	// FMax **读**（一条邮箱命令，返回 6 个 response word）。
	//   * 没有读命令的代号（ShimadaPeak / None）：直接 UnknownCmd，**一条命令都不发**；
	//   * arg0 按代号给（APU0 需要预置 3，其余 0）；
	//   * 通道 = 该代号的 FMax 邮箱地址组，走本文件既有的 MailboxRaw。
	// ⚠️ 只读：除了 MailboxRaw 为了发这条命令必须做的"清 RSP / 填 ARG / 写 MSG"之外，不写任何寄存器
	//    （与 GetSmuVersion / GetPowerLimits / ReadCurveMarginRaw 的既有行为完全同级）。
	// ⚠️ 绝不放进轮询：只在"检测能力"（Probe）时读一次。
	// ==========================================================================================
	public SmuStatus GetBoostLimitFrequency(out uint[] response)
	{
		response = new uint[6];
		FMaxSpec? spec = ResolveFMax(_cpu);
		if (!spec.HasValue || spec.Value.GetCommand == 0)
		{
			return SmuStatus.UnknownCmd;
		}
		if (!_init)
		{
			return SmuStatus.Failed;
		}
		return MailboxRaw(spec.Value.MailboxCommand, spec.Value.MailboxResponse, spec.Value.MailboxArgument, spec.Value.GetCommand, new uint[1] { spec.Value.ReadArgument }, out response);
	}

	// ==========================================================================================
	// 2026-10-10 fmax: FMax **全核写**（一条邮箱命令）。
	//
	// 打包**照参考实现原样**（这正是用户要求"实现得和参考一模一样"的那一部分）：
	//     arg0 = value & 0xFFFFF（低 20 位），arg1..5 = 0
	//   —— MailboxRaw 会把数组里没有给的 ARG 槽补 0，所以只传一个元素即可。
	//
	// ⚠️⚠️ 参数语义**未确认**（代码注释按用户要求必须留下这一条）：
	//   参考实现把 arg0 当**绝对 MHz**（这也是本机实测读回 4950 的样子 —— 4950 不可能是
	//   -1000..+200 的偏移），但它的作者说过真实参数是**偏移**，与代码矛盾。
	//   本轮**不改**打包方式，也不改单位；等语义确认后再统一改（改的话要同时改这里的掩码、
	//   UI 的范围/单位、以及回读比对的口径）。
	//
	// 守卫：分类器说该代号不能写（只读平台 / 不支持的平台）-> **一条命令都不发**，直接 UnknownCmd。
	//       未知 _cpu / 未初始化同样不发。
	// ==========================================================================================
	internal static uint EncodeFMaxArg(uint mhz)
	{
		return mhz & 0xFFFFFu;
	}

	public SmuStatus SetBoostLimitFrequencyAllCores(uint mhz)
	{
		FMaxSpec? spec = ResolveFMax(_cpu);
		if (!spec.HasValue || spec.Value.SetCommand == 0)
		{
			// 只读平台 / 不支持的平台：写入路径在这一层就不存在。
			return SmuStatus.UnknownCmd;
		}
		if (!_init)
		{
			return SmuStatus.Failed;
		}
		return MailboxRaw(spec.Value.MailboxCommand, spec.Value.MailboxResponse, spec.Value.MailboxArgument, spec.Value.SetCommand, new uint[1] { EncodeFMaxArg(mhz) }, out _);
	}

	private SmuStatus MailboxRaw(uint cmdAddr, uint rspAddr, uint argAddr, uint cmd, uint[] args, out uint[] response)
	{
		response = new uint[6];
		if (!_init || !AcquirePci())
		{
			return SmuStatus.Failed;
		}
		try
		{
			if (_disposed)
			{
				return SmuStatus.Failed;
			}
			if (!WaitForMailboxIdle(rspAddr))
			{
				return SmuStatus.CmdRejectedBusy;
			}
			if (!WriteReg(rspAddr, 0u))
			{
				return SmuStatus.Failed;
			}
			for (int i = 0; i < 6; i++)
			{
				uint value = ((args != null && i < args.Length) ? args[i] : 0u);
				if (!WriteReg(argAddr + (uint)(i * 4), value))
				{
					return SmuStatus.Failed;
				}
			}
			if (!WriteReg(cmdAddr, cmd))
			{
				return SmuStatus.Failed;
			}
			uint value2 = 0u;
			Stopwatch stopwatch = Stopwatch.StartNew();
			int num = 0;
			while (stopwatch.ElapsedMilliseconds < 200)
			{
				if (!ReadReg(rspAddr, out value2))
				{
					return SmuStatus.Failed;
				}
				if (value2 != 0)
				{
					break;
				}
				num++;
				if (num > 256)
				{
					Thread.Sleep(1);
				}
				else if (num > 32)
				{
					Thread.Yield();
				}
			}
			switch (value2)
			{
			case 0u:
				return SmuStatus.Failed;
			default:
				return (SmuStatus)value2;
			case 1u:
			{
				for (int j = 0; j < 6; j++)
				{
					if (!ReadReg(argAddr + (uint)(j * 4), out response[j]))
					{
						return SmuStatus.Failed;
					}
				}
				return SmuStatus.OK;
			}
			}
		}
		finally
		{
			_smuMutex.ReleaseMutex();
		}
	}

	private bool AcquirePci()
	{
		if (_disposed)
		{
			return false;
		}
		try
		{
			return _smuMutex.WaitOne(1500);
		}
		catch (AbandonedMutexException)
		{
			return true;
		}
	}

	private void GetMp1Addrs(out uint cmd, out uint rsp, out uint arg)
	{
		switch (Family)
		{
		case CpuFamily.Zen1Desktop:
			cmd = 61932840u;
			rsp = 61932900u;
			arg = 61932952u;
			break;
		case CpuFamily.Raven:
		case CpuFamily.Renoir:
			cmd = 61932840u;
			rsp = 61932900u;
			arg = 61933976u;
			break;
		case CpuFamily.Mobile:
			cmd = 61932840u;
			rsp = 61932920u;
			arg = 61933976u;
			break;
		case CpuFamily.StrixPoint:
		case CpuFamily.StrixHalo:
			cmd = 61933864u;
			rsp = 61933944u;
			arg = 61933976u;
			break;
		case CpuFamily.Matisse:
		case CpuFamily.Raphael:
			cmd = 61932848u;
			rsp = 61932924u;
			arg = 61934020u;
			break;
		default:
			cmd = 0u;
			rsp = 0u;
			arg = 0u;
			break;
		}
	}

	private void GetPsmuAddrs(out uint cmd, out uint rsp, out uint arg)
	{
		switch (Family)
		{
		case CpuFamily.Zen1Desktop:
			cmd = 61932828u;
			rsp = 61932904u;
			arg = 61932944u;
			break;
		case CpuFamily.Matisse:
		case CpuFamily.Raphael:
			cmd = 61932836u;
			rsp = 61932912u;
			arg = 61934144u;
			break;
		case CpuFamily.Raven:
		case CpuFamily.Renoir:
		case CpuFamily.Mobile:
		case CpuFamily.StrixPoint:
		case CpuFamily.StrixHalo:
			cmd = 61934112u;
			rsp = 61934208u;
			arg = 61934216u;
			break;
		case CpuFamily.ShimadaPeak:
			cmd = 61933860u;
			rsp = 61933936u;
			arg = 61934144u;
			break;
		default:
			cmd = 0u;
			rsp = 0u;
			arg = 0u;
			break;
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_io.Dispose();
			_smuMutex.Dispose();
			_disposed = true;
		}
	}
}
