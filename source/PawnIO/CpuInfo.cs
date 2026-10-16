// ==============================================================================================
// L-Mechrevo — CPU 代号/能力判据。本文件是 L-Mechrevo 自己的判据代码；
// 它读的 RyzenSmuService.CurveCommand / CurvePerCoreCommand / SetCoGfx 命令表是派生作品
// （来源与许可见 RyzenSmuService.cs 头部与 ATTRIBUTION-CO-ADDENDUM.md）。
// ==============================================================================================
using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using MechrevoLite;
using Microsoft.Win32;

namespace PawnIO;

public static class CpuInfo
{
	public static readonly bool IsAMD = DetectAMD();

	private const string CpuRegKey = "HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0";

	private static readonly Lazy<(string Name, string Caption)> _data = new Lazy<(string, string)>(Load, LazyThreadSafetyMode.ExecutionAndPublication);

	public static string Name => _data.Value.Name;

	public static string Caption => _data.Value.Caption;

	public static int MinCPUUV => AppConfig.Get("min_uv", -40);

	public static int MaxCPUUV => AppConfig.Get("max_uv", 0);

	public static int MinIGPUUV => AppConfig.Get("min_igpu_uv", -30);

	public static int MaxIGPUUV => AppConfig.Get("max_igpu_uv", 0);

	public static int MinTemp => AppConfig.Get("min_temp", 75);

	public static int DefaultTemp => AppConfig.Get("max_temp", 96);

	// 2026-10-05 step7A (upstream 0.290.6 A-33, 决策②): CPUID (family/model/stepping) based
	// codename detection. This is the capability source for undervolt / OC support, replacing the
	// old ProcessorNameString whitelist.
	internal static CpuCodeName DetectedAmdCodeName
	{
		get
		{
			if (!IsAMD || !X86Base.IsSupported)
			{
				return CpuCodeName.Undefined;
			}
			int item = X86Base.CpuId(1, 0).Eax;
			int num = (item >> 8) & 0xF;
			if (num == 15)
			{
				num += (item >> 20) & 0xFF;
			}
			int num2 = ((item >> 4) & 0xF) | ((item >> 12) & 0xF0);
			int num3 = (X86Base.CpuId(-2147483647, 0).Ebx >> 28) & 0xF;
			switch ((num << 8) | num2)
			{
			case 5889:
				return num3 switch
				{
					7 => CpuCodeName.Threadripper, 
					3 => CpuCodeName.Naples, 
					_ => CpuCodeName.SummitRidge, 
				};
			case 5896:
			{
				bool flag = num3 == 3 || num3 == 7;
				return (!flag) ? CpuCodeName.PinnacleRidge : CpuCodeName.Colfax;
			}
			case 5905:
				return CpuCodeName.RavenRidge;
			case 5912:
				return (num3 == 7) ? CpuCodeName.RavenRidge2 : CpuCodeName.Picasso;
			case 5920:
				return CpuCodeName.Dali;
			case 5937:
				return (num3 == 7) ? CpuCodeName.CastlePeak : CpuCodeName.Rome;
			case 5968:
				return CpuCodeName.FireFlight;
			case 6001:
				return CpuCodeName.Matisse;
			case 6040:
				return CpuCodeName.Mero;
			case 6048:
				return CpuCodeName.Mendocino;
			case 6400:
			case 6401:
				return CpuCodeName.Milan;
			case 6408:
				return CpuCodeName.Chagall;
			case 6417:
				return CpuCodeName.Genoa;
			case 6424:
				return CpuCodeName.StormPeak;
			case 6432:
			case 6433:
				return CpuCodeName.Vermeer;
			case 6560:
				return CpuCodeName.Bergamo;
			case 5984:
				return CpuCodeName.Renoir;
			case 5992:
				return CpuCodeName.Lucienne;
			case 6032:
			case 6033:
				return CpuCodeName.Vangogh;
			case 6468:
				return CpuCodeName.Rembrandt;
			case 6480:
				return CpuCodeName.Cezanne;
			case 6497:
				return (num3 == 1) ? CpuCodeName.DragonRange : CpuCodeName.Raphael;
			case 6516:
			case 6517:
				return CpuCodeName.Phoenix;
			case 6520:
				return CpuCodeName.Phoenix2;
			case 6524:
				return CpuCodeName.HawkPoint;
			case 6664:
				return CpuCodeName.ShimadaPeak;
			case 6658:
				return CpuCodeName.Turin;
			case 6672:
				return CpuCodeName.TurinD;
			case 6688:
			case 6692:
				return CpuCodeName.StrixPoint;
			case 6724:
				return CpuCodeName.GraniteRidge;
			case 6752:
				return CpuCodeName.KrackanPoint;
			case 6760:
				return CpuCodeName.KrackanPoint2;
			case 6768:
				return CpuCodeName.StrixHalo;
			default:
				return CpuCodeName.Undefined;
			}
		}
	}

	// ==========================================================================================
	// 2026-10-05 coread: **物理核心数**。
	//
	// 用途：CO 回读是"每核一条 SMU 命令"（PSMU 0xE1 等），核数必须准 —— 多读几条会白白多发命令，
	// 少读会漏核。Linux/Windows 上都以"物理核"为单位编 CO 表，所以这里不能用
	// Environment.ProcessorCount（本机 8745H 返回 16 = 含 SMT 的逻辑处理器数）。
	//
	// 取值：GetLogicalProcessorInformationEx(RelationProcessorCore) 的记录条数，逐条按 Size 步进
	// （只看条数，不解析 PROCESSOR_RELATIONSHIP 内容）。这是 Windows 上最稳的口径：
	// 尊重 BIOS 里关掉 SMT / 关核的实际情况。失败时退回 Environment.ProcessorCount（可能偏大，
	// 调用方对"读失败"要能容错）。
	//
	// ⚠️ 只读、不涉及任何 SMU 访问。
	// ==========================================================================================
	private const int RelationProcessorCore = 0;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);

	private static readonly Lazy<int> _physicalCoreCount = new Lazy<int>(CountPhysicalCores, LazyThreadSafetyMode.ExecutionAndPublication);

	public static int PhysicalCoreCount => _physicalCoreCount.Value;

	private static int CountPhysicalCores()
	{
		try
		{
			uint length = 0u;
			GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
			if (length == 0)
			{
				return Math.Max(1, Environment.ProcessorCount);
			}
			IntPtr buffer = Marshal.AllocHGlobal((int)length);
			try
			{
				uint returned = length;
				if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref returned))
				{
					return Math.Max(1, Environment.ProcessorCount);
				}
				int count = 0;
				int offset = 0;
				while (offset + 8 <= returned)
				{
					int size = Marshal.ReadInt32(buffer, offset + 4);
					if (size <= 0)
					{
						break;
					}
					count++;
					offset += size;
				}
				return (count > 0) ? count : Math.Max(1, Environment.ProcessorCount);
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}
		catch (Exception)
		{
			return Math.Max(1, Environment.ProcessorCount);
		}
	}

	private static bool DetectAMD()
	{
		if (!X86Base.IsSupported)
		{
			return false;
		}
		(int Eax, int Ebx, int Ecx, int Edx) tuple = X86Base.CpuId(0, 0);
		int item = tuple.Ebx;
		int item2 = tuple.Ecx;
		int item3 = tuple.Edx;
		return ((ReadOnlySpan<byte>)MemoryMarshal.Cast<uint, byte>(stackalloc uint[3]
		{
			(uint)item,
			(uint)item3,
			(uint)item2
		})).SequenceEqual("AuthenticAMD"u8);
	}

	// ==========================================================================================
	// 2026-10-05 step7A-fix (用户实测反馈; 决策② 的补丁): UV 能力判据 = **两层合取**
	//
	//     支持 = 第一层 CPUID/代号判据（这颗 CPU 所属代号家族有没有这条 SMU 命令）
	//            AND 第二层 SKU 层级判据（这个 SKU 厂商固件放不放行）
	//
	// 为什么必须补第二层（证据）：
	//   * 上游 0.290.6 只有第一层：CurveCommand(DetectedAmdCodeName).HasValue。
	//     它回答的是"SMU 命令表里有没有这条命令"，**没有**回答"这颗 SKU 厂商放不放"。
	//   * 【本机实测】AMD Ryzen 7 8745H（Hawk Point，CPUID 表判为 Phoenix）：
	//     --cpu-tuning-probe 报 StatusKey=CpuTuneSmuReady，Options 里 amd-co(-30..0 CO) 与
	//     amd-clock(1000..4500MHz) 都是 CanWrite=true；但用户确认**这台机器实际不能降压**。
	//     => 第一层判据过于宽松，会向用户暴露并不存在的可写能力。
	//   * 决策② 之前的字符串白名单
	//     （Name.Contains("RYZEN AI MAX") || "Ryzen AI 9" || "Ryzen 9" || "4900H" || "4800H"
	//      || "4600H"）**本来就隐含了"现代型号要 R9 级"这条 SKU 层级规则**（所以旧判据在本机
	//     是 false，是对的）。换成 CPUID 后这层 SKU 判断被丢掉了，本补丁把它接回来。
	//   * 用户提供的领域规则（原文）：「7x4xH 和 8x4xH 的 zen4 APU 只有 R9 级才支持」
	//     即 Zen4 APU（Phoenix / Hawk Point，型号形如 7x4xH / 8x4xH）里只有 R9 级支持 UV，
	//     R7 / R5 以及名称里不含 R9 字样的都不支持。
	//
	// 分家族处理（**不一刀切**）：
	//   * APU 家族（Zen4 + Zen5，完整名单见下面的 step7A-uvtier2 小节）：
	//     第一层 AND 名称是 9 级（IsRyzen9Tier()）。
	//   * **其它家族：保持上游 CPUID 行为，不额外收紧**。例：桌面 Zen4 Raphael 的 R7 是支持
	//     Curve Optimizer 的，不能被本补丁误关；Renoir/Lucienne 世代（旧白名单里的
	//     4900H / 4800H / 4600H，其中 4800H 是 R7）也属"其它家族"，CurveCommand 有值即放行
	//     —— 与旧白名单允许这些具体型号的结论一致。
	//
	// ⚠️ 本判据只决定"能不能写"，不改变任何写入/恢复行为，也不提供任何强制写路径。
	//
	// ==========================================================================================
	// 2026-10-05 step7A-fix2（用户实测反馈，用户原话：「它里面有限制频率功能可能可以用不能一棍子打死」）
	//   —— SKU 层级过滤从【入口整体】下沉为【按选项】：
	//
	//   * amd-co（全核 Curve Optimizer = 降压/UV）：**保留**两层合取
	//     （CurveCommand 有值 AND SkuTuningRejection == null）。见 IsSupportedUV()。
	//   * amd-clock（固定/限制频率）：**只看第一层** CPUID/家族判据
	//     （ClockCommand/SupportsClock 有值即提供），不加 SKU 层级过滤。
	//     理由：厂商固件对"固定全核频率"这条命令的放行策略与 CO 不是同一把锁，本机 R7
	//     实测仍可能可用；用户明确要求不要连坐。见 IsSupportedClock()。
	//   * 入口可见性（RuntimeCpuTuning.EntryVisible）改为"任一选项可用即显示"，不再被
	//     SKU 层整体否决 —— 否则 R7 机器上可能可用的固定频率入口也会消失。
	// ==========================================================================================
	// 2026-10-05 step7A-uvtier2（**依据：用户提供的 AMD 官网信息**）
	//   —— SKU 层级过滤的**族名单**从"只管 Zen4 APU"推广到"Zen4 APU + Zen5 APU"，
	//      并把"名称 -> 层级"的判定写成显式有序的 ClassifyUvSkuTier()。
	//
	// 【规则原文 ①（本次新增，用户提供的 AMD 官网信息）】
	//   「根据AMD官网锐龙AI系列 AI 9是都支持的 以下7 5不支持 AI MAX都是支持的」
	//   => 锐龙 AI 系列：AI 9 -> 全部支持；AI 7 / AI 5 -> 不支持；AI MAX -> 全部支持。
	// 【规则原文 ②（上一轮，本机实测反馈）】
	//   「7x4xH 和 8x4xH 的 zen4 APU 只有 R9 级才支持」
	//   => Zen4 APU（Phoenix / Phoenix2 / HawkPoint）里只有 R9 级放行。
	//
	// ① + ② 归纳：**按"型号层级"判 UV 是否被厂商放行**——
	//   层级 9（Ryzen 9 / Ryzen AI 9 / RYZEN AI MAX）放行；7 / 5 级不放行。
	//   这与"旧字符串白名单隐含现代型号要 R9 级"的结论一致。
	//
	// 因此需要层级过滤的族（IsSkuTierFilteredApuFamily）为：
	//   * Zen4 APU：Phoenix（7x4xH）/ Phoenix2（8x4xU 一档）/ HawkPoint（8x4xH）
	//   * Zen5 APU：StrixPoint（Ryzen AI 3xx）/ StrixHalo（RYZEN AI MAX）/ KrackanPoint /
	//     KrackanPoint2（作者表 CurveCommand 里没有 KrackanPoint2，第一层已否决；列在这里
	//     只为将来作者表补齐时行为一致）
	//   * RYZEN AI MAX（Strix Halo，如 "AMD RYZEN AI MAX+ 395"）按规则①**应当支持**：
	//     它命中 ClassifyUvSkuTier() 的第 1 条（含 "RYZEN AI MAX"）-> Tier9 -> 放行。
	//     已用自检样例验证（tools\uvshot，含 "MAX+" 变体）。
	//
	//   * **其它家族：沿用上一轮策略，不收紧**（用户规则只针对锐龙 AI / APU 系列）：
	//     桌面 Raphael / DragonRange / GraniteRidge / Vermeer / Matisse，以及 Zen3 及更早的
	//     Renoir / Cezanne / Lucienne / Rembrandt / Vangogh / FireFlight 等，一律
	//     "CurveCommand 有值即放行"，与上游 0.290.6 行为一致。
	//     ⚠️ 若日后要把桌面家族也按层级收紧，那属于**收紧**行为（会关掉桌面 R7 的 CO），
	//        需用户确认后再动，见本轮报告的"未确定项"。
	//
	// 命名匹配（大小写不敏感，作用于 CpuInfo.Name = ProcessorNameString）：
	//   允许 9 级：`Ryzen 9` / `Ryzen AI 9` / `RYZEN AI MAX`
	//   拒绝 7/5 级：`Ryzen 7` / `Ryzen AI 7` / `Ryzen 5` / `Ryzen AI 5`
	//   ⚠️ 陷阱与对策：`Ryzen AI 9` **不含**子串 `Ryzen 9`（中间隔着 "AI "），所以两条都得判；
	//      并且必须**显式按顺序**判（AI MAX -> AI 9 -> Ryzen 9 -> 7 -> 5 -> 3），判到就返回，
	//      **不做**"含 9 就放行"这类宽松匹配 —— 这样 `Ryzen AI 7` 不可能命中任何放行分支
	//      （放行分支的字面量里都带 "9" 或 "AI MAX"）。自检样例见本轮报告"命名匹配自检表"。
	//
	// 本小节只改"SkuTuningRejection 的族名单 + 层级判定写法"，
	// **仍然只否决 amd-co**；amd-clock 的"第一层通过即可用"策略（fix2）原样保留 ✅。
	// ==========================================================================================
	public static bool IsSupportedUV()
	{
		return IsSupportedUV(DetectedAmdCodeName);
	}

	// 两层判据（cpu 为调用方已探测到的代号；probe 用驱动返回的代号，其余用 CPUID 代号）。
	internal static bool IsSupportedUV(CpuCodeName cpu)
	{
		// 第一层：代号家族必须有这条 SMU curve 命令（上游 CPUID 判据，原样保留）。
		if (!RyzenSmuService.CurveCommand(cpu).HasValue)
		{
			return false;
		}
		// 第二层：SKU 层级必须放行（null = 放行）。
		return SkuTuningRejection(cpu) == null;
	}

	// ------------------------------------------------------------------------------------------
	// 固定/限制频率（amd-clock）的判据：**只有第一层**（该代号家族有没有这条 SMU OC clock 命令）。
	//
	// 刻意**不**叠加 SkuTuningRejection()：用户实测反馈"限制频率可能可以用，不能一棍子打死"。
	// 因此本机 AMD Ryzen 7 8745H（Phoenix / HawkPoint 档，SKU 层判 R7 不给 UV）依旧提供
	// amd-clock；能否真正写入由 Apply 时的 SMU 返回值决定（canWrite 只表示"命令路径存在"）。
	// ------------------------------------------------------------------------------------------
	public static bool IsSupportedClock()
	{
		return IsSupportedClock(DetectedAmdCodeName);
	}

	internal static bool IsSupportedClock(CpuCodeName cpu)
	{
		return RyzenSmuService.SupportsClock(cpu);
	}

	// ------------------------------------------------------------------------------------------
	// 第二层判据本体：SKU 层级（厂商是否放行）。
	// 返回 null = 该 SKU 允许运行时 CPU 调节；返回字符串 = 拒绝原因（供 --cpu-tuning-probe 的
	// Detail 与日志解释"为什么判不支持"）。
	//
	// 2026-10-05 step7A-uvtier2：族名单从 Zen4 APU 扩展到 Zen4 + Zen5 APU（规则原文见文件上方
	// 注释里的"用户提供的 AMD 官网信息"）。
	// ------------------------------------------------------------------------------------------
	internal static string? SkuTuningRejection(CpuCodeName cpu)
	{
		return SkuTuningRejection(cpu, Name);
	}

	// CPU 代号 + 型号名 -> 拒绝原因（null = 放行）。
	// name 显式传入是为了让自检工具能拿"合成型号名"验证命名匹配，不必依赖本机 CPU
	// （生产调用永远走上面的单参重载，即 CpuInfo.Name）。
	internal static string? SkuTuningRejection(CpuCodeName cpu, string? name)
	{
		if (!IsSkuTierFilteredApuFamily(cpu))
		{
			// 其它家族：保持上游 CPUID 行为，不收紧。
			return null;
		}
		if (ClassifyUvSkuTier(name) == UvSkuTier.Tier9)
		{
			return null;
		}
		return $"{TierLabel(name)} tier does not expose UV (tier 9 only: Ryzen 9 / Ryzen AI 9 / RYZEN AI MAX)";
	}

	// 需要走 SKU 层级过滤的 APU 家族 = Zen4 APU ∪ Zen5 APU。
	// 代号取值见上面的 DetectedAmdCodeName 表与 RyzenSmuService.CurveCommand 的命令表：
	//   Zen4 APU：Phoenix（7x4xH，如 7840H/8845H）、Phoenix2（8x4xU 一档）、HawkPoint（8x4xH）。
	//   Zen5 APU：StrixPoint（Ryzen AI 3xx，CPUID 6688/6692）、StrixHalo（RYZEN AI MAX，CPUID 6768）、
	//             KrackanPoint（CPUID 6752，Ryzen AI 3xx 低阶）、KrackanPoint2（CPUID 6760；
	//             CurveCommand 里暂无它，第一层就会否决，列在这里只为将来一致）。
	private static bool IsSkuTierFilteredApuFamily(CpuCodeName cpu)
	{
		switch (cpu)
		{
		case CpuCodeName.Phoenix:
		case CpuCodeName.Phoenix2:
		case CpuCodeName.HawkPoint:
		case CpuCodeName.StrixPoint:
		case CpuCodeName.StrixHalo:
		case CpuCodeName.KrackanPoint:
		case CpuCodeName.KrackanPoint2:
			return true;
		default:
			return false;
		}
	}

	// SKU 层级（只区分"是否 9 级放行"与拒绝原因用的档位）。
	internal enum UvSkuTier
	{
		Tier9,
		Tier7,
		Tier5,
		Tier3,
		Unknown
	}

	// 名称 -> 层级。大小写不敏感（Name 来自注册表 ProcessorNameString，例如
	// "AMD Ryzen 7 8745H w/ Radeon 780M Graphics" / "AMD RYZEN AI MAX+ 395 w/ Radeon 8060S"）。
	//
	// ⚠️ **显式有序判定**：命中即返回，绝不做宽松的"含 9 就放行"。
	//    顺序 = AI MAX -> "Ryzen AI 9" -> "Ryzen 9" -> 7 -> 5 -> 3。
	//    理由：`Ryzen AI 9` 不含子串 `Ryzen 9`（中间隔着 "AI "），两条都必须有；而把放行分支
	//    的字面量限定为带 "9" / "AI MAX" 的整串，能保证 `Ryzen AI 7` / `Ryzen AI 5` 不会
	//    从任何放行分支漏过去。
	internal static UvSkuTier ClassifyUvSkuTier(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return UvSkuTier.Unknown;
		}
		// ---- 放行档（层级 9）----
		// Strix Halo：AMD RYZEN AI MAX+ 395 / RYZEN AI MAX 385（注意 "MAX+" 变体也含本串）。
		if (name.Contains("RYZEN AI MAX", StringComparison.OrdinalIgnoreCase))
		{
			return UvSkuTier.Tier9;
		}
		// Strix Point / Krackan Point：AMD Ryzen AI 9 HX 370 / Ryzen AI 9 HX PRO 370 / Ryzen AI 9 365。
		if (name.Contains("RYZEN AI 9", StringComparison.OrdinalIgnoreCase))
		{
			return UvSkuTier.Tier9;
		}
		// 传统命名：AMD Ryzen 9 8945H / Ryzen 9 9955HX 等。
		if (name.Contains("RYZEN 9", StringComparison.OrdinalIgnoreCase))
		{
			return UvSkuTier.Tier9;
		}
		// ---- 拒绝档（层级 7 / 5 / 3）----
		if (name.Contains("RYZEN AI 7", StringComparison.OrdinalIgnoreCase) || name.Contains("RYZEN 7", StringComparison.OrdinalIgnoreCase))
		{
			return UvSkuTier.Tier7;
		}
		if (name.Contains("RYZEN AI 5", StringComparison.OrdinalIgnoreCase) || name.Contains("RYZEN 5", StringComparison.OrdinalIgnoreCase))
		{
			return UvSkuTier.Tier5;
		}
		if (name.Contains("RYZEN AI 3", StringComparison.OrdinalIgnoreCase) || name.Contains("RYZEN 3", StringComparison.OrdinalIgnoreCase))
		{
			return UvSkuTier.Tier3;
		}
		// 命中不了任何层级（例如 Ryzen Z1 Extreme 这类非 "Ryzen N" 命名）：按"不放行"处理，
		// 因为层级过滤家族里厂商只对 9 级放行。
		return UvSkuTier.Unknown;
	}

	// 用户规则里的 "R9 级" = 层级 9（Ryzen 9 / Ryzen AI 9 / RYZEN AI MAX）。
	internal static bool IsRyzen9Tier()
	{
		return ClassifyUvSkuTier(Name) == UvSkuTier.Tier9;
	}

	// 仅用于把拒绝原因写得可读（R9/R7/R5/R3/unknown）。
	private static string TierLabel(string? name)
	{
		switch (ClassifyUvSkuTier(name))
		{
		case UvSkuTier.Tier9:
			return "R9";
		case UvSkuTier.Tier7:
			return "R7";
		case UvSkuTier.Tier5:
			return "R5";
		case UvSkuTier.Tier3:
			return "R3";
		default:
			return "unknown";
		}
	}

	public static bool IsSupportedUViGPU()
	{
		return IsSupportedUViGPU(DetectedAmdCodeName);
	}

	// iGPU 降压（RyzenSmuService.SetCoGfx：Renoir 家族 → MP1(100)，Mobile / StrixHalo → PSMU(183)）。
	//
	// 取舍（硬约束第 4 条：依据不足就保留原状，并把取舍写进报告）：
	//   * 旧规则是**只** 6900H（字符串包含）。6900H 系列（Ryzen 9 6900HX/6900HS）属 Rembrandt，
	//     是 R9 级。"Rembrandt 家族 + R9" 的泛化会把 6980HX 等本机未验证的型号从"不支持"改成
	//     "支持"——那是**放宽**，而用户给的两条规则（Zen4 APU 的 R9 级、锐龙 AI 的
	//     "AI 9 支持 / AI 7 / AI 5 不支持 / AI MAX 支持"）都是**CPU UV** 的规则、
	//     **没有**给 iGPU 的 SKU 规则。
	//   * 因此这里不泛化，只加两道**只可能收紧**的闸门：
	//       ① 家族层：该代号的 SMU 家族必须真的有 iGPU curve 邮箱（否则 SetCoGfx 走 default →
	//          SmuStatus.Failed，属"命令不存在"）；
	//       ② SKU 层：保留旧白名单本体（名称含 6900H），并要求它确实是 R9 级（兜底，防止将来
	//          出现非 R9 却被这个字符串命中的部件）。
	//   * 结论：新判据是旧判据的**子集**，本机（8745H，不含 6900H）依旧判不支持。
	internal static bool IsSupportedUViGPU(CpuCodeName cpu)
	{
		if (!HasIgpuCurveMailbox(cpu))
		{
			return false;
		}
		return IsRyzen9Tier() && Name.Contains("6900H", StringComparison.OrdinalIgnoreCase);
	}

	// 白名单与 RyzenSmuService.SetCoGfx 的 switch (Family) 保持一致；
	// 那两行取值的来源见 RyzenSmuService.cs 的派生声明（CurveCommand 上方）与 ATTRIBUTION-CO-ADDENDUM.md。
	private static bool HasIgpuCurveMailbox(CpuCodeName cpu)
	{
		// 2026-10-10 fmax-family-guard: 这是又一条"按 Family 出结论"的通路（结论 = 这颗代号有没有
		// iGPU curve 邮箱）。GetFamily 补齐后，Mero 的家族变成 Mobile（改前 Unknown），若不拦这里，
		// "能不能 iGPU 降压"这个**能力**判定会跟着变。用与 SetCoGfx 同一张白名单
		// （RyzenSmuService.LegacyFamilyCommandsApproved）保持改前答案：白名单外的代号一律 false。
		// ⚠️ 下游 IsSupportedUViGPU 还有 `IsRyzen9Tier() && Name.Contains("6900H")` 两道合取，
		//    所以即使不拦，最终判定对真实部件也不会变；这里拦是为了"能力答案与改前逐字一致"。
		if (!RyzenSmuService.LegacyFamilyCommandsApproved(cpu))
		{
			return false;
		}
		switch (RyzenSmuService.GetFamily(cpu))
		{
		case CpuFamily.Renoir:
		case CpuFamily.Mobile:
		case CpuFamily.StrixHalo:
			return true;
		default:
			return false;
		}
	}

	private static (string Name, string Caption) Load()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0");
			if (registryKey != null)
			{
				string item = registryKey.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? string.Empty;
				string item2 = registryKey.GetValue("Identifier")?.ToString()?.Trim() ?? string.Empty;
				return (Name: item, Caption: item2);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("CPU registry read failed: HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0 " + ex.GetType().Name + " " + ex.Message);
		}
		return (Name: string.Empty, Caption: string.Empty);
	}
}
