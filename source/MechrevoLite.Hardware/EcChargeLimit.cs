using System;
using System.Runtime.InteropServices;
using MechrevoLite.Properties;

namespace MechrevoLite.Hardware;

internal static class EcChargeLimit
{
	public const int UpperRegister = 1977;

	// 2026-10-10（对齐上游 · 复充下限只读）：上游 Leo-stro/mechrevo-charge-limit **只写 0x07B9**，
	// 0x07D0 由固件 store_limit() 自己推导。我们仍**读**它（诊断/观察），但 ⛔ 不再写、不再参与成败判定。
	public const int LowerRegister = 2000;

	public const int RechargeHysteresis = 5;

	public const int MinimumPercent = 40;

	public const int MaximumPercent = 100;

	// 2026-10-10（限充门控持久化 · 移植 Leo-stro/mechrevo-charge-limit）：Uniwill EC 固件的充电控制循环
	// 开头有产品线门控（0x07C3 平台值或 0x0770 ROMID 首字节为 4/5 才执行限充），且持久化靠固件把实时上限
	// 抄进存储区 0x087F（主机不可写的 H2RAM 盲区）。门控默认不成立的机器上，直接写 0x7B9 往往无效或重启即丢。
	// 方案：写入前若门控未开，临时把 0x07C3 写成 4，保持数秒让固件循环执行 store_limit() 把上限抄进存储区
	// （跨重启/断电持久），随后还原原值（用完即还、零残留、不碰 ROMID 身份字节）。
	public const int GateRegister = 1987;

	public const int GateOpenValue = 4;

	public const int GatePersistDelayMs = 2500;

	// 2026-10-10（限充真效果判据 · 按上游 Leo-stro/mechrevo-charge-limit 口径）：
	// 上游实测（无界 15X，非 50 系）—— 写 0x07B9 + 保持 0x07C3=4 约 2.5s 让固件 store_limit() 把上限抄进
	// 存储区 0x087F 之后，还必须再等约 7s，读 0x0742 的 bit2（=1）才算**真的生效**；
	// 只看 0x07B9 回读只能证明"写进去了"，不能证明"生效了"（上层判据见 ChargeLimitEffectEvidence）。
	// 0x0770 = ROMID 首字节（上游用它做 4/5 产品线门控判断）；
	// 0x087F = 固件存储区（跨重启持久）；上游明确指出它是 H2RAM 盲区，主机通常**读不到**。
	public const int EffectRegister = 1858;

	public const int EffectBitMask = 4;

	public const int EffectCheckDelayMs = 7000;

	public const int RomIdRegister = 1904;

	public const int StoredLimitRegister = 2175;

	private const uint IoctlEcRead = 2621482120u;

	private const uint IoctlEcWrite = 2621482124u;

	private const string DevicePath = "\\\\.\\ACPIDriver";

	private static readonly object Gate = new object();

	// 2026-10-10（限充真效果判据 · 门控还原时序）：最近一次写入安排的门控还原任务。
	// 真效果判据必须**等门控还原完成**之后再开始 7s 计时（上游顺序：还原 → 回读确认 → 等 7s → 读 0x0742）。
	// 没有安排还原时保持 Task.CompletedTask ⇒ 判据立即进入 7s 等待。
	private static System.Threading.Tasks.Task _gateRestoreCompleted = System.Threading.Tasks.Task.CompletedTask;

	// 2026-10-10（门控残留兜底）：门控被临时置 4 且尚未还原时记下原值（int.MinValue = 无未决还原）。
	// 进程在 2.5s 保持窗口内退出时后台还原任务不会执行 ⇒ 门控会留在 4（会让电池**完全不充电**），
	// 故在首次开门时注册一次进程退出兜底，退出时同步尽力还原并回读。
	private static int _gateOutstandingOriginal = int.MinValue;

	private static int _gateExitHookRegistered;

	internal static Func<int, (bool Success, int AppliedPercent)>? TrySetOverride { get; set; }

	internal static Func<int>? ReadPercentOverride { get; set; }

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(nint handle, uint code, nint inBuffer, int inSize, nint outBuffer, int outSize, out int returned, nint overlapped);

	internal static int ValueFor(int percent)
	{
		if (percent < 100)
		{
			return percent;
		}
		return 0;
	}

	/// <summary>上游口径下的期望复充下限（上限 − 5）。⚠️ 仅用于**只读比对/日志**：⛔ 不再写进 EC（0x07D0 交由固件推导）。</summary>
	internal static int LowerValueFor(int percent)
	{
		if (percent < 100)
		{
			return Math.Max(0, percent - 5);
		}
		return 0;
	}

	// 2026-10-10（限充回读掩码 · ⭐ 用户实测）：0x07B9 的 bit7 是**固件自己置的"限充已启用"标志**，
	// 低 7 位才是百分数 —— 同一台机器实测三组读数（写多少读多少）：
	//   上限 80% ⇒ 0x07B9=0xD0（= 0x80 | 0x50=80）、复充下限 0x07D0=0x4B（75 = 80−5）；
	//   上限 60% ⇒ 0x07B9=0xBC（= 0x80 | 0x3C=60）、0x07D0=0x37（55 = 60−5）；
	//   不限充（0x07B9 写 0）⇒ 0x07B9=0x00（全日志 7 次读数均为 0x00，bit7 从不置位）。
	// ⇒ 凡"语义上是百分数"的读取都必须**先掩 bit7 再 Clamp**，否则 0xD0(208)/0xBC(188) 一律被读成 100%
	//   （症状：UI 显示"不限充"、每 10s 一条"回读不一致"噪音、每次 GCU 上报都白重写一次限充）。
	// ⚠️ 只读探测/诊断照实打印**原始字节**（含 bit7），⛔ 不在这里把标志位藏掉。
	public const int EnabledFlagMask = 128;

	public const int PercentMask = 127;

	internal static int PercentFor(int registerValue)
	{
		if (registerValue <= 0)
		{
			return 100;
		}
		int num = registerValue & PercentMask;
		if (num > 0)
		{
			return Math.Clamp(num, 40, 100);
		}
		return 100;
	}

	internal const string VerdictKey = "ec_charge_limit_verdict";

	public static ChargeLimitVerdict Verdict
	{
		get
		{
			string text = AppConfig.GetString("ec_charge_limit_verdict");
			if (!(text == "verified"))
			{
				if (text == "ineffective")
				{
					return ChargeLimitVerdict.Ineffective;
				}
				return ChargeLimitVerdict.Pending;
			}
			return ChargeLimitVerdict.Verified;
		}
	}

	public static string PendingNotice => Strings.ChargeLimitPending;

	public static string VerifiedNotice => Strings.ChargeLimitVerified;

	public static string IneffectiveNotice => Strings.ChargeLimitIneffective;

	public static void RecordVerdict(ChargeLimitVerdict verdict)
	{
		AppConfig.Set("ec_charge_limit_verdict", verdict switch
		{
			ChargeLimitVerdict.Verified => "verified", 
			ChargeLimitVerdict.Ineffective => "ineffective", 
			_ => "", 
		});
		AppConfig.Flush();
	}

	public static bool IsSupportedLimit(int percent)
	{
		if (percent >= 40)
		{
			return percent <= 100;
		}
		return false;
	}

	public static bool IsSupportedMachine(SupportDecision support, FeatureMatrix matrix)
	{
		ArgumentNullException.ThrowIfNull(support, "support");
		ArgumentNullException.ThrowIfNull(matrix, "matrix");
		string text = AppConfig.GetString("ec_charge_limit");
		if (text == "1")
		{
			return true;
		}
		if (text == "0")
		{
			return false;
		}
		return support.IsSupported;
	}

	// 2026-10-15（限充可用性 · 能力探测 · 用户批准的修法②）：改前只查机型白名单（ModelSupport 平台码集合 /
	// GCU 已服务）—— 白名单外的机型（用户报的「很多机型」）连设备都不试开就判不可用 ⇒ 开关灰掉 / 弹窗；
	// 而白名单内但固件门控不成立的机型又「盲写 0x07B9、静默无效」。现在改为**实际尝试打开一次设备**
	// (\\.\ACPIDriver)：打得开 ⇒ 支持；打不开 ⇒ 不支持。
	// ⚠️ 全程**只读**：只 CreateFile + CloseHandle，⛔ 不写任何寄存器。
	// ⚠️ 带**缓存**（别每次调都开设备）：正/负结果都缓存 <see cref="ChannelProbeTtl"/>，过期自动重探 ——
	//    这样「驱动尚未加载 ⇒ 判不支持」不会永久粘住（装好驱动后最多一个 TTL 就自愈）。
	// ⚠️ 保留 ec_charge_limit=1/0 强制覆盖：覆盖值**不缓存**、每次现读（与改前 IsSupportedMachine 同款语义）。
	// ⚠️ ModelSupport 白名单**保留**（仍作为「已知支持机型」的信息源、界面侧仍用它决定整卡可见性），
	//    但**不再作为唯一门**。⛔ 未删任何既有判据（IsSupportedMachine 原样保留、仍可被其它调用方使用）。
	// ⚠️ 打开权限用与 TrySet 相同的 GENERIC_READ|GENERIC_WRITE：只有「能按写入方式打开」才真正预示
	//    TrySet 会成功（只读打开成功而写打开失败会造成「判支持却写不进」的假阳性）。
	private static readonly object AvailabilityGate = new object();

	/// <summary>能力探测缓存有效期：正/负结果都只信这么久，过期重探（避免「驱动晚加载」被永久判死）。</summary>
	private static readonly TimeSpan ChannelProbeTtl = TimeSpan.FromSeconds(60L);

	// -1 = 未探测/已过期；0 = 打不开（不支持）；1 = 打得开（支持）。
	private static int _channelProbeCache = -1;

	private static DateTime _channelProbeAtUtc = DateTime.MinValue;

	/// <summary>只读能力探测（带 TTL 缓存）：\\.\ACPIDriver 能否以读写方式打开。⛔ 不写任何寄存器、不改 config。</summary>
	internal static bool ProbeChannelAvailable()
	{
		lock (AvailabilityGate)
		{
			bool cacheFresh = _channelProbeCache >= 0 && DateTime.UtcNow - _channelProbeAtUtc < ChannelProbeTtl;
			if (cacheFresh)
			{
				return _channelProbeCache == 1;
			}
			bool opened = false;
			nint handle = CreateFile("\\\\.\\ACPIDriver", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (handle != new IntPtr(-1))
			{
				opened = true;
				CloseHandle(handle);
			}
			_channelProbeCache = (opened ? 1 : 0);
			_channelProbeAtUtc = DateTime.UtcNow;
			Logger.WriteLine($"限充能力探测（只读）：\\\\.\\ACPIDriver {(opened ? "可打开 ⇒ 判定支持" : "打不开 ⇒ 判定不支持")}（结果缓存 {ChannelProbeTtl.TotalSeconds:0}s；⛔ 未写任何寄存器）");
			return opened;
		}
	}

	/// <summary>丢弃能力探测缓存（驱动装/卸之后可由调用方显式失效）。</summary>
	internal static void InvalidateChannelProbe()
	{
		lock (AvailabilityGate)
		{
			_channelProbeCache = -1;
			_channelProbeAtUtc = DateTime.MinValue;
		}
	}

	public static bool IsAvailableOnThisMachine()
	{
		// 强制覆盖优先（且不缓存）：ec_charge_limit=1 放行 / =0 关闭。
		string text = AppConfig.GetString("ec_charge_limit");
		if (text == "1")
		{
			return true;
		}
		if (text == "0")
		{
			return false;
		}
		// 2026-10-21（机主要求）：无界 Intel（LNL）与星耀（WJ 载荷）暂未验证 EC 限充位置 ⇒ 暂时隐藏。
		// ⚠️ 只影响这两类机型的 EC 限充 UI 判定；其它同方/GCU 机型逐位不变；ec_charge_limit=1/0 强覆盖仍在上面优先生效。
		if (IsTemporarilyHiddenModel(out string hiddenReason))
		{
			Logger.WriteLineIfChanged("charge-limit-hidden-model",
				"限充可用性：暂时隐藏（" + hiddenReason + "）⇒ 判定不可用（未写任何 EC）");
			return false;
		}
		// ⚠️ 2026-10-15（隐藏规则 B 的**分层** · 关键设计）：10/16/20/30 系的代际门控**故意不放在这里** ——
		// 本方法回答的是「EC 通道能不能用」，而界面侧还用它决定**整张电池卡片**的可见性
		// （ApplyChargeLimitSliderGating 的 panelBattery.Visible）与三档按钮的启用态。若把代际门控塞进来，
		// 老机型会连电池卡片带**三档按钮（GCU 路）**一起消失，与用户「只保留GCU路」的要求相反。
		// ⇒ 代际门控只作用在「EC 直写」这一件事上，见 IsEcDirectWriteAllowed()（并作为 TrySet 的兜底）。
		// UI 审计模式：与改前逐字一致（RuntimeModelSupport.Current() 在审计模式返回 Supported("audit") ⇒ 恒真），
		// 否则审计/回归会在没有驱动的机器上少掉整块电池布局。
		if (Program.UiAuditMode)
		{
			return true;
		}
		// ⭐⭐ 2026-10-16（0.291.9 · 修"宝龙达机上限充没隐藏"）：
		//   ⚠️ 根因：⭐ 下面那条 ⭐ **机型白名单**（⭐ `IsSupportedMachine(...)` ✅）⭐ 的语义是"或" ✅，
		//      本来是为"⭐ 有 GCU 但没装 ACPIDriver"⭐ 的同方机型放宽用的 ✅。
		//   ⚠️ 但 ⭐ 我们给宝龙达加了 ⭐ `RuntimeModelSupport` 的 Supported 分支 ⚠️
		//      ⇒ ⭐ 白名单**命中** ⇒ ⭐ 本方法**直接 return true** ❌
		//      ⇒ ⭐ ⭐ **限充 UI 照样显示** ❌ —— ⭐ 而蛟龙上 `\\.\ACPIDriver` **打不开**
		//      （⭐ 真机诊断包实锤：⭐ `Win32 error 2 系统找不到指定的文件` ❌）
		//      ⇒ ⭐ ⭐ **"能点但没用" = ⭐ 违反铁律** ⚠️
		//   ⭐ 修法：⭐ **宝龙达直接判不可用** ✅ —— ⭐ 该模具【本来就没有】`UWACPIDriver`
		//      （⭐ 真机已证实 ✅）⇒ ⭐ 限充物理上做不到 ⇒ ⭐ 整块限充 UI 隐藏 ✅
		//   ⚠️ 安全性：⭐ 这一支**只对宝龙达生效** ⇒ ⭐ 同方/GCU 机型（⭐ 含"有 GCU 但没装
		//      ACPIDriver"⭐ 那些 ✅）**逐位不变** ✅
		//   ⚠️ 位置：⭐ 在 ⭐ `ec_charge_limit` 配置覆盖**之后** ✅ ⇒ ⭐ 需要强行打开时
		//      仍可用 `ec_charge_limit=1` 逃生 ✅（⭐ 与既有设计一致 ✅）
		if (Program.hw?.UsesBaolongda == true)
		{
			// 2026-10-21：宝龙达没有 UWACPIDriver 不等于没有 EC 通道 —— PawnIO LpcACPIEC（0x62/0x66）可用。
			// 参考脚本同样走端口 I/O；这里以“能否只读 EC 身份”为准（读不到不算支持）。
			bool pawnioEcOk = EcPortIo.TryReadEcIdentityAcpi(out _, out _, out string baolongdaEcNote);
			Logger.WriteLineIfChanged("charge-limit-baolongda",
				"限充可用性：本机是宝龙达模具 ⇒ 经 PawnIO ACPI-EC 通道探测 ⇒ "
				+ (pawnioEcOk ? "可用（不再依赖 UWACPIDriver）" : ("不可用：" + baolongdaEcNote)));
			return pawnioEcOk;
		}
		// ⚠️ 语义是**或**、不是替换（用户批准的修法②：「ModelSupport 白名单**保留**…但**不再作为唯一门**」）：
		//   ① 既有判据（机型白名单 / GCU 已服务）判支持 ⇒ 支持 —— 与改前逐字一致；
		//   ② 否则再看**能力探测**（设备真能打开）⇒ 白名单外的可用机型也能进（正是用户报的「很多机型」）。
		// ⛔ 用「与」会把「有 GCU、但没装 ACPIDriver」的机型判成不支持 ⇒ 连电池卡片带 GCU 三档一起消失，
		//    那既是回归、也违反用户「只保留 GCU 路」。故这里取或：只**放宽**、不收紧。
		if (IsSupportedMachine(RuntimeModelSupport.Current(), FeatureMatrix.Current()))
		{
			return true;
		}
		return ProbeChannelAvailable();
	}

	/// <summary>2026-10-21：无界 Intel 里只有 Lunar Lake 平台暂时隐藏（其它 Intel 无界不动）。
	/// 判据：CPU 名匹配 Core Ultra xxxV，或 CPUID family 6 / model 0xBD（Lunar Lake）。只读，不写任何东西。</summary>
	private static bool IsLunarLakeCpu()
	{
		try
		{
			string cpuName = PawnIO.CpuInfo.Name ?? string.Empty;
			if (cpuName.IndexOf("Ultra", StringComparison.OrdinalIgnoreCase) >= 0
				&& System.Text.RegularExpressions.Regex.IsMatch(cpuName, @"\b\d{3}V\b"))
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			if (System.Runtime.Intrinsics.X86.X86Base.IsSupported)
			{
				int eax = System.Runtime.Intrinsics.X86.X86Base.CpuId(1, 0).Eax;
				int family = (int)((eax >> 8) & 0xF);
				int model = (int)((eax >> 4) & 0xF);
				int extModel = (int)((eax >> 16) & 0xF);
				if (family == 6 && extModel != 0)
				{
					model |= extModel << 4;
				}
				if (family == 6 && model == 0xBD)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	/// <summary>2026-10-21：暂时隐藏 EC 限充 UI 的机型判定（只读，不写 EC）。</summary>
	private static bool IsTemporarilyHiddenModel(out string reason)
	{
		reason = string.Empty;
		try
		{
			// 星耀：GCU 载荷为 WJ（安装器登记 / 包特征识别）
			if (string.Equals(GcuIdentity.Variant, "WJ", StringComparison.OrdinalIgnoreCase))
			{
				reason = "星耀机型（GCU 载荷 WJ）暂未验证 EC 限充地址";
				return true;
			}
			try
			{
				BoardWhitelist.BoardProductEvidence evidence = BoardWhitelist.GetBoardProductEvidence();
				string model = ((evidence.RegSystemProduct ?? string.Empty) + " " + (evidence.WmiModel ?? string.Empty)).Trim();
				bool wujieName = model.IndexOf("无界", StringComparison.OrdinalIgnoreCase) >= 0
					|| model.IndexOf("WUJIE", StringComparison.OrdinalIgnoreCase) >= 0;
				bool xingyaoName = model.IndexOf("星耀", StringComparison.OrdinalIgnoreCase) >= 0
					|| model.IndexOf("XINGYAO", StringComparison.OrdinalIgnoreCase) >= 0;
				if (xingyaoName)
				{
					reason = "星耀机型暂未验证 EC 限充地址（model=" + model + "）";
					return true;
				}
				if (wujieName && IsLunarLakeCpu())
				{
					reason = "无界 Intel（LNL）机型暂未验证 EC 限充地址（model=" + model + "）";
					return true;
				}
			}
			catch
			{
			}
		}
		catch
		{
		}
		return false;
	}

	// 2026-10-15（限充代系门控 · 用户原话「10系-30系用这个可能损坏硬件必须强制隐藏EC三档和滑块 只保留GCU路」）：
	// 判据选**安装器登记的 GCU 变体** HKLM\SOFTWARE\L-Mechrevo\GcuVariant —— 它直接编码代际
	// （Select-GcuPayload.ps1：'1020' = legacy 10/16/20-series、'30' = 30-series），随程序集持久化，
	// 硬件没连上也能判，且与 GCU 载荷选择同源。
	// ⛔ 不选 DgpuGenerationProbe：它只认 RTX 30/40/50（GTX 10/16 与 RTX 20 落 Gen30 或 Unknown），
	//    既分不出 10/16/20 与 30，又会在「识别不出独显」的机器上误判。
	// ⚠️ 读不到 / 变体不认识 / 注册表不可读 ⇒ 判**否**（不阻断）—— 天然关闭、零回归。
	// ⚠️ 本判据**只读注册表**（⛔ 不写注册表），结果缓存一次。
	private static int _legacyGenerationCache = -1;

	private static readonly object LegacyGenerationGate = new object();

	/// <summary>
	/// 2026-10-15（隐藏规则 B 的落地判据）：**EC 直写方式**在本机是否被允许 —— 等价于
	/// 「<see cref="IsAvailableOnThisMachine"/>（通道可用）**且** 不是 10/16/20/30 系（可能损坏硬件）」。
	/// <para>⛔ 所有**写 0x07B9** 的入口都过这里（SetBatteryChargeLimit / EnsureChargeLimitHeld / 开关与
	/// 三档的 EC 分支）；而 <see cref="IsAvailableOnThisMachine"/> 继续回答「通道能不能用」，界面侧据此
	/// 决定整张卡片与三档按钮的可见/启用 ⇒ 老机型仍然保留 GCU 路。</para>
	/// <para>⚠️ 注：ec_charge_limit=1 会在 IsAvailableOnThisMachine 里放行（通道层面），但**不**绕过
	/// 代际门控 —— 用户原话「必须强制隐藏」，故代际门控优先于该强制位。</para>
	/// </summary>
	internal static bool IsEcDirectWriteAllowed()
	{
		return IsAvailableOnThisMachine() && !IsLegacyGenerationBlocked();
	}

	/// <summary>本机是否属 10/16/20/30 系（GcuVariant 为 "1020" 或 "30"）—— 规则 B 的唯一判据。</summary>
	internal static bool IsLegacyGenerationBlocked()
	{
		lock (LegacyGenerationGate)
		{
			if (_legacyGenerationCache >= 0)
			{
				return _legacyGenerationCache == 1;
			}
			bool blocked = false;
			string variant = null;
			try
			{
				using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\L-Mechrevo");
				variant = key?.GetValue("GcuVariant") as string;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("限充代系门控：读 GcuVariant 失败（按「不阻断」处理，零回归）：" + ex.Message);
			}
			string family = GcuVariantFamily.Resolve(variant);
			blocked = family == "1020" || family == "30";
			_legacyGenerationCache = (blocked ? 1 : 0);
			if (blocked)
			{
				Logger.WriteLine($"限充代系门控：GcuVariant={family} 属 10/16/20/30 系 ⇒ EC 直写方式判为不可用（规则 B：可能损坏硬件，只保留 GCU 路；⛔ 未写任何寄存器/注册表）");
			}
			return blocked;
		}
	}

	public static bool TrySet(int percent, out int appliedPercent)
	{
		return TrySet(percent, out appliedPercent, out _);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-16（0.291.8.1 · 修「耀世16ultra 2025 表示 EC 限制充电打不开」）：
	/// **写入前**的"本机 EC 固件到底支不支持限充"预检（⭐ 纯只读，⛔ 不写任何寄存器/配置）。
	/// <para>背景（真机诊断包逐字证据）：那台机每次点开关都被 `TrySet` 里的证据门拒绝
	/// （`门控 0x7C3=0x07（非 4/5）、0x770=0xFF（非 4/5）、存储区 0x87F=0xFF（无内容）`），
	/// 但拒绝只写在**日志**里 + 一条**异步** toast ⇒ 用户看到开关"打开了"却又没效果，
	/// 反复点了 60+ 次 ⇒ 观感就是"打不开"，还刷了一屏错误日志。</para>
	/// <para>判据与 `TrySet` 里那道门**逐字同源**（⭐ 不另造一套）：
	/// 门控 0x7C3 读得到且 ∉{4,5} ＋ ROMID 0x770 ∉{4,5} ＋ 存储区 0x87F 无内容
	/// ⇒ 判"本机固件不支持"，界面侧据此**提前**给出可见反馈并还原开关（⛔ 不进入写入路径）。
	/// ⚠️ 逃生口：`ec_charge_limit=1` 时一律返回 true（与 `TrySet` 的强制覆盖一致）。</para>
	/// <para>⛔ 只读：不发帧、不写 EC/config/注册表、不碰控件。</para>
	/// </summary>
	internal static bool IsFirmwareLimitSupported()
	{
		// 2026-10-16（0.291.9.3）：本方法**不再是「固件支持与否」的判据** —— 它的旧判据与 TrySet 那道门逐字同源，
		// 而那道门已被证明三条都不是正面证据（详见 TrySet 内 2026-10-16 那段注释：门控非 4/5 是正常初值、
		// 0x0770 的 0xFF 疑似未映射填充、0x087F 只在一次成功写入后才被固件抄写 ⇒ 循环论证）。
		// ⇒ 写之前**没有**可靠证据可以否定，故这里一律返回 true（「可尝试」），把判定交给写后证据
		//    （0x07B9 写后回读确认 ＋ 0x0742 bit2 效果证据）。⚠️ 唯一例外：ec_charge_limit=0 表示用户显式关闭。
		// ⛔ 只读：不发帧、不写 EC/config/注册表、不碰控件。
		return AppConfig.GetString("ec_charge_limit") != "0";
	}

	/// <summary>
	/// 2026-10-15（写入前门控预读 · 用户批准的修法③）：新增 <paramref name="refusedByFirmware"/> —— 为真表示
	/// 本次**不是**「写失败」，而是「按证据判定本机 EC 固件不支持充电上限」故**主动拒绝盲写**（调用方据此给
	/// 用户可见文案，见 BatteryControl.SetBatteryChargeLimit）。
	/// ⚠️ 两参重载语义与改前逐字一致（委托到本重载并丢弃该标志）。
	/// </summary>
	public static bool TrySet(int percent, out int appliedPercent, out bool refusedByFirmware)
	{
		appliedPercent = -1;
		refusedByFirmware = false;
		if (!IsSupportedLimit(percent))
		{
			return false;
		}
		Func<int, (bool, int)> trySetOverride = TrySetOverride;
		if (trySetOverride != null)
		{
			(bool, int) tuple = trySetOverride(percent);
			bool item = tuple.Item1;
			int item2 = tuple.Item2;
			appliedPercent = (item ? item2 : (-1));
			return item;
		}
		// 2026-10-15（隐藏规则 B · 最后一道兜底）：10/16/20/30 系上**一个字节都不写 EC** —— 放在这里是因为
		// 本方法（TrySet）是**全部** 0x07B9 写入的唯一汇聚点（BatteryControl / ChargeLimitMonitor / Program
		// 三处调用方），比在各自调用点分别加闸更稳、且不可能漏。⛔ 未打开设备、未写任何寄存器。
		// 用户原话：「10系-30系用这个可能损坏硬件必须强制隐藏EC三档和滑块 只保留GCU路」。
		if (IsLegacyGenerationBlocked())
		{
			Logger.WriteLine($"限充写入被拒绝：本机 GcuVariant 属 10/16/20/30 系 ⇒ EC 直写方式整体禁用（可能损坏硬件，只保留 GCU 路）；请求 {percent}% 未下发，⛔ 未写任何寄存器");
			return false;
		}
		lock (Gate)
		{
			nint num = CreateFile("\\\\.\\ACPIDriver", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (num == new IntPtr(-1))
			{
				return false;
			}
			try
			{
				// 2026-10-10（限充门控持久化 · 移植 Leo-stro/mechrevo-charge-limit）：
				// 门控本就没开（0x07C3 原值非 4/5）时，先临时写成 4，写入并回读确认后保持 2.5s
				// 让固件 store_limit() 把上限抄进存储区 0x087F（跨重启/断电持久），最后还原原值。
				// 全程尽力而为：读不到或写门控失败都跳过，行为与改前逐字一致，不影响原有写入/回读。
				int gateOriginal = Read(num, GateRegister);
				bool gateWasOpen = gateOriginal == 4 || gateOriginal == 5;
				// 2026-10-16（0.291.9.3 · ⭐ 撤销「写入前固件预检拒绝」——真机实例：翼龙15Pro 无极限充滑块被误挡）：
				// 原判据「门控 0x07C3 ∉{4,5} **且** ROMID 0x0770 ∉{4,5} **且** 存储区 0x087F 无内容 ⇒ 判本机固件不支持、拒绝盲写」
				// 经复核：**三条都不是正面证据**，故整条拒绝**删除**（只读预读与默认级别可见日志保留）：
				//   ① 门控 0x07C3 ∉{4,5} 是**正常初值**、不是「不支持」的证据 —— 本方法紧接着（见下面 gateTouched 分支）
				//      正是因为它非 4/5 才**自己把它写成 4** 来开闸；把开闸前的状态当否定证据，语义自相矛盾。
				//   ② ROMID 0x0770 读成 0xFF：同一 H2RAM 窗口里的 0x07C3 / 0x07B9 都读得到真实值 ⇒ 0x0770 的 0xFF
				//      更可能是「该地址未映射时的填充值」（上面旧注释自己也这么写），不是「固件说没有」。
				//   ③ 存储区 0x087F 无内容：该地址是**固件在一次成功写入之后**（门控保持 2.5s 期间）才抄写的
				//      ⇒ 拿它当「写入前的准入条件」是**循环论证**：永不写入 ⇒ 0x087F 永为 0xFF ⇒ 永远被拒。
				// ⭐ 结论：**写之前没有任何可靠证据**能判定「本机不支持」；**写之后**才有真证据 ——
				//    0x07B9 写后回读确认（掩 bit7，见下面）＋ ChargeLimitEffectEvidence（门控还原后等 >=7s 读 0x0742 bit2）。
				//    ⇒ 改为「按上游口径先尝试写入，再用写后证据如实告知」：既不再放任「写进去也不生效」的静默无效
				//    （那由写后证据与可见文案承担），也不再拿未经证实的判据把可用机型挡在门外（无极限充滑块 ⇒ 恢复可用）。
				// ⚠️ 仍然保留的两道**真闸**：IsLegacyGenerationBlocked()（10/16/20/30 系，可能损坏硬件，见上面）
				//    与 ec_charge_limit=0（用户显式关闭，见 IsAvailableOnThisMachine）。ec_charge_limit=1 依旧放行。
				// ⚠️ refusedByFirmware 现恒为 false（保留出参以不动调用方签名与 BatteryControl 侧那条可见文案分支）。
				{
					int romId = Read(num, RomIdRegister);
					int stored = Read(num, StoredLimitRegister);
					string gateText = gateWasOpen ? "已开" : "未开（由本方法按上游口径临时开闸）";
					string romText = ((romId == 4 || romId == 5) ? "为 4/5（正面证据）" : "非 4/5（含 0xFF，按未映射处理、不作否定证据）");
					string storedText = ((stored >= 0 && stored != 255) ? "有内容" : "无内容/不可读（0x87F 由固件在一次成功写入后才抄写 ⇒ ⛔ 不作为写入前条件）");
					string precheckFingerprint = $"{gateOriginal}|{romId}|{stored}";
					if (Logger.TryMarkChanged("charge-limit-precheck", precheckFingerprint))
					{
						Logger.WriteError($"限充写入前预读（⭐ 只读 · ⭐ 仅作诊断 · ⛔ 不再据此拒绝）：0x{GateRegister:X3}={ChargeLimitEcProbe.FormatRaw(gateOriginal)}、0x{RomIdRegister:X3}={ChargeLimitEcProbe.FormatRaw(romId)}、0x{StoredLimitRegister:X3}={ChargeLimitEcProbe.FormatRaw(stored)}；门控 {gateText}、0x{RomIdRegister:X3} {romText}、存储区 {storedText}；⭐ 按上游口径继续尝试写入 0x{UpperRegister:X3}");
					}
				}
				bool gateTouched = false;
				if (gateOriginal >= 0 && !gateWasOpen)
				{
					gateTouched = Write(num, GateRegister, (byte)GateOpenValue);
					if (gateTouched)
					{
						// 记下未决的原值 + 注册进程退出兜底（幂等），防"退出时门控残留"。
						System.Threading.Volatile.Write(ref _gateOutstandingOriginal, gateOriginal);
						EnsureGateExitHook();
						Logger.WriteLine($"限充门控持久化：0x{GateRegister:X3} 原值 0x{gateOriginal:X2} → 临时置 {GateOpenValue}（等待固件抄上限进存储区）");
					}
				}
				bool upperWritten = false;
				try
				{
					int num2 = ValueFor(percent);
					if (!Write(num, 1977, (byte)num2))
					{
						return false;
					}
					// 2026-10-10（回读容忍 bit7 标志）：固件可能在"写 → 回读"窗口内自己把 bit7 置上
					// （实测 0x07B9 写 80 后读回 0xD0 = 0x80 | 0x50）。比较**掩掉 bit7 的低 7 位**：
					// bit7 只是"已启用"标志、不携带百分数信息 ⇒ 容忍它不会放过真失败（写丢了或
					// 固件改回时低 7 位就不是目标值，例如目标是 60 而回读仍是 0x50=80）；
					// 相反要求逐位相等会把"写成功 + 固件置标志"误报成"写入未确认"并弹失败提示。
					int upperReadback = Read(num, 1977);
					if (upperReadback < 0 || (upperReadback & PercentMask) != (num2 & PercentMask))
					{
						return false;
					}
					upperWritten = true;
					// 参考 OpenRevo 的 EC 限充：上限成功后 best-effort 补齐复充下限 0x07D0。
					// ⚠️ 不参与成败判定（部分固件会自己推导，写失败/被覆盖都不影响上限）。
					if (percent < 100)
					{
						int wantLower = LowerValueFor(percent);
						int nowLower = Read(num, LowerRegister);
						if (nowLower != wantLower)
						{
							bool lowerWrite = Write(num, LowerRegister, (byte)wantLower);
							int lowerRead = Read(num, LowerRegister);
							Logger.WriteLine("[charge-limit] 复充下限 0x" + LowerRegister.ToString("X3")
								+ " 原=" + (nowLower < 0 ? "不可读" : nowLower.ToString())
								+ " → " + wantLower + " 写=" + lowerWrite
								+ " 回读=" + (lowerRead < 0 ? "不可读" : lowerRead.ToString())
								+ "（best-effort，不影响上限成败）");
						}
					}
					// 2026-10-10（对齐上游 Leo-stro/mechrevo-charge-limit · 停写复充下限 0x7D0）：
					// 上游**只写 0x07B9**，复充下限 0x07D0 由固件 store_limit() 在门控保持窗口内自己推导。
					// 我们以前把 0x07D0 的写入成败与 0x07B9 绑成"全或无"（0x7D0 写失败或回读不符就回滚
					// 0x7D0 + 0x7B9 并整次返回 false）—— 多出一个本不该存在的失败模式，且 0x7D0 的语义
					// 在不同机型上未必一致。现在：**0x7D0 只读、不写、不回滚**，上限成败只由 0x07B9
					// （掩掉 bit7 后）决定；只读观察见 ScheduleLowerBoundObservation（2.5s 后等固件推完再看）。
					bool applied = appliedPercent == PercentFor(num2);
					if (applied)
					{
						// best-effort：把同一上限写进存储区 0x087F（不参与成败判定）
						if (WmiStoredLimitProbe.TryWriteStoredLimit(percent, out string bPersistDetail))
						{
							Logger.WriteError("[charge-limit] 已将上限写入存储区 0x087F（持久化）：" + bPersistDetail);
						}
					}
					return applied;
				}
				finally
				{
					if (gateTouched)
					{
						// 保持门控开着等固件完成存储，再还原原值（用完即还、零残留）。
						// 18:50（卡顿修复 · 用户反馈「开启 EC 限充卡一下」）：原先在这里同步 Sleep 2.5s，
						// 而 TrySet 全程持 Gate 锁 —— UI 侧同锁的 EC 回读（SyncChargeLimitSwitch →
						// ResolveDisplayLimitPercent → ReadPercent）会被堵住最多 2.5s，界面表现为卡一下。
						// 改为后台延迟任务：TrySet 立即返回（不持锁等待），2.5s 后重新拿锁还原门控原值，
						// 持久化语义不变（门控在存储窗口内保持打开），还原失败有日志兜底。
						int original = gateOriginal;
						System.Threading.Tasks.Task restore = System.Threading.Tasks.Task.Run(async () =>
						{
							await System.Threading.Tasks.Task.Delay(GatePersistDelayMs);
							lock (Gate)
							{
								nint handle = CreateFile("\\\\.\\ACPIDriver", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
								if (handle == new IntPtr(-1))
								{
									Logger.WriteError($"限充门控持久化：还原时打不开 ACPIDriver，0x{GateRegister:X3} 保持临时值 {GateOpenValue}（可能不充电；重启后固件回默认值自愈）");
									return;
								}
								try
								{
									// 2026-10-10（门控还原确认 + 失败重试一次 · 按上游口径）：
									// 上游写完必回读确认，不一致就重试一次；我们以前只 Write 不看结果，
									// 写丢了也不知道 —— 而门控留在 4 会让电池**完全不充电**（上游明确警告过）。
									bool restored = Write(handle, GateRegister, (byte)original);
									int readback = Read(handle, GateRegister);
									if (readback != original)
									{
										bool retryWrite = Write(handle, GateRegister, (byte)original);
										int retryReadback = Read(handle, GateRegister);
										Logger.WriteLine($"限充门控持久化：还原后回读 0x{GateRegister:X3}={(readback < 0 ? "不可读" : "0x" + readback.ToString("X2"))} ≠ 原值 0x{original:X2}，重试一次 → 写{(retryWrite ? "已下发" : "下发失败")}、回读 0x{(retryReadback < 0 ? "不可读" : retryReadback.ToString("X2"))}");
										restored = retryWrite;
										readback = retryReadback;
									}
									if (readback == original)
									{
										System.Threading.Volatile.Write(ref _gateOutstandingOriginal, int.MinValue);
										Logger.WriteLine($"限充门控持久化：等待 {GatePersistDelayMs}ms 后还原并回读确认 0x{GateRegister:X3}=0x{original:X2}（成功）");
									}
									else
									{
										Logger.WriteError($"⚠️⚠️ 充电上限门控未还原，可能导致不充电：0x{GateRegister:X3} 期望 0x{original:X2}，回读 0x{(readback < 0 ? "不可读" : readback.ToString("X2"))}（写返回 {restored}）。请关闭充电上限开关再打开，或重启一次让固件恢复默认值。");
									}
								}
								finally
								{
									CloseHandle(handle);
								}
							}
						});
						System.Threading.Volatile.Write(ref _gateRestoreCompleted, restore);
					}
					// 2026-10-10（对齐上游 · 只读观察复充下限）：仅在 0x07B9 确实写成功（回读确认过）时才观察 ——
					// 不论门控是否被我们临时打开，都在存储窗口（2.5s，与门控保持窗口同时长）之后**只读**看一眼 0x07D0，
					// 验证固件是否自己推成"上限 − 5"。⛔ 纯只读：不写 0x07D0、不回滚、不影响本方法的返回值。
					if (upperWritten)
					{
						ScheduleLowerBoundObservation(percent);
					}
				}
			}
			finally
			{
				CloseHandle(num);
			}
		}
	}

	// 2026-10-10（只读观察 · 复充下限）：写完上限、等固件的存储窗口（2.5s）过去之后，**只读**一次 0x07D0。
	// 目的：验证"上游只写 0x07B9、下限由固件 store_limit() 自己推"这条口径在本机是否成立 ——
	//   · 读到 == 上限 − 5（= LowerValueFor）⇒ 固件自主推导成立 ⇒ 我们以前那笔显式写入本来就是冗余的；
	//   · 读到别的值 ⇒ ⚠️ 本机固件可能不推导下限，0x07D0 停留在上一次的旧值（只影响回充时机，
	//     **不影响充电上限**：上限只由 0x07B9 决定）。⛔ 不写、不回滚、不改变 TrySet 结果。
	private static void ScheduleLowerBoundObservation(int percent)
	{
		int expectedLower = LowerValueFor(percent);
		_ = System.Threading.Tasks.Task.Run(async delegate
		{
			try
			{
				await System.Threading.Tasks.Task.Delay(GatePersistDelayMs);
				lock (Gate)
				{
					nint handle = CreateFile("\\\\.\\ACPIDriver", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
					if (handle == new IntPtr(-1))
					{
						Logger.WriteLine($"复充下限 0x{LowerRegister:X3}（上游不写、交由固件推导）只读观察：打不开 ACPIDriver，未取到值");
						return;
					}
					try
					{
						ObserveLowerBound(handle, expectedLower);
					}
					finally
					{
						CloseHandle(handle);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("复充下限只读观察失败（不影响限充本身）：" + ex.Message);
			}
		});
	}

	private static void ObserveLowerBound(nint handle, int expectedLower)
	{
		int num = Read(handle, LowerRegister);
		if (num < 0)
		{
			Logger.WriteLine($"复充下限 0x{LowerRegister:X3}（上游不写、交由固件推导）只读观察：不可读");
			return;
		}
		if (num == expectedLower)
		{
			Logger.WriteLine($"复充下限 0x{LowerRegister:X3}=0x{num:X2}（{num}）== 上限 − {RechargeHysteresis} = {expectedLower} ⇒ 固件自己推的（已按上游停写 0x{LowerRegister:X3}，只读观察）");
		}
		else
		{
			Logger.WriteLine($"⚠️ 复充下限 0x{LowerRegister:X3}=0x{num:X2}（{num}）≠ 上限 − {RechargeHysteresis} = {expectedLower}（期望值，仅比对）⇒ 本机固件可能不推导下限、0x{LowerRegister:X3} 仍是旧值：只影响回充时机，**不影响充电上限**（上限只由 0x{UpperRegister:X3} 决定）；已按上游口径停写，若回充表现异常请反馈");
		}
	}

	public static int ReadPercent()
	{
		Func<int> readPercentOverride = ReadPercentOverride;
		if (readPercentOverride != null)
		{
			return readPercentOverride();
		}
		lock (Gate)
		{
			nint num = CreateFile("\\\\.\\ACPIDriver", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (num == new IntPtr(-1))
			{
				return -1;
			}
			try
			{
				int num2 = Read(num, 1977);
				return (num2 < 0) ? (-1) : PercentFor(num2);
			}
			finally
			{
				CloseHandle(num);
			}
		}
	}

	// 2026-10-10（限充真效果判据 · 门控还原时序）：真效果判据要等这次写入的**门控还原完成后**再计时。
	internal static System.Threading.Tasks.Task GateRestoreCompleted => System.Threading.Volatile.Read(ref _gateRestoreCompleted);

	// 2026-10-10（只读探测 · 按上游口径存原始值）：一次性把限充相关寄存器读成包 ——
	// 0x07B9（上限）/ 0x07D0（复充下限：上游不写、我们**自 2026-10-10 起也停写**，只读用于验证固件是否自己推成 上限−5）/ 0x07C3（门控）/ 0x0742（生效位）/
	// 0x0770（ROMID 首字节）/ 0x087F（固件存储区，上游指明是盲区，通常读不到）。
	// ⚠️ 全程只读、复用本类既有的 \\.\ACPIDriver IOCTL 封装（⛔ 不新造 IOCTL、⛔ 不改任何寄存器）。
	// ⛔ 任何调用方都不得据此改变写入行为（只存档，供以后按机型判断）。
	internal static ChargeLimitEcProbe ProbeRegisters()
	{
		lock (Gate)
		{
			nint handle = CreateFile("\\\\.\\ACPIDriver", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (handle == new IntPtr(-1))
			{
				return new ChargeLimitEcProbe(-1, -1, -1, -1, -1, -1, false, "不可读（\\\\.\\ACPIDriver 打不开：UWACPIDriver.sys 未装/未加载，或权限不足）");
			}
			try
			{
				return new ChargeLimitEcProbe(Read(handle, UpperRegister), Read(handle, LowerRegister), Read(handle, GateRegister), Read(handle, EffectRegister), Read(handle, StoredLimitRegister), Read(handle, RomIdRegister), true, "\\\\.\\ACPIDriver（UWACPIDriver.sys · H2RAM 0x700–0x7FF）");
			}
			finally
			{
				CloseHandle(handle);
			}
		}
	}

	// ── XRAM：EC 版本号（⭐ 只读试读 · 2026-10-16）──
	// 蛟龙限充项目实测：EC 版本在 **XRAM 0x8410~0x8413**（4 字节，形如 00.28.00.00）。
	// ⚠️⚠️ 那是**另一套地址空间** —— 我们既有的 \\.\ACPIDriver 通道按文档只覆盖
	//   **H2RAM 0x700–0x7FF**（见 ProbeRegisters 的 channelNote），0x8410 远超该窗口。
	//   ⇒ ⭐ 这个地址**能不能读到 {由驱动决定}，必须实测**，⛔ 不能想当然（用户批准的诊断项）。
	// ⚠️ 只读：只发 IoctlEcRead，⛔ 绝不发 IoctlEcWrite、⛔ 不改任何寄存器。
	// ⛔ 任何调用方都不得据此改变限充行为（限充当前【暂缓】）。
	internal const int XramEcVersionRegister = 33808;

	/// <summary>
	/// 只读试读任意 EC 地址（⭐ 诊断用）。⛔ 不写任何寄存器。
	/// <para>⚠️ 复用本类既有的 <c>\\.\ACPIDriver</c> IOCTL 封装（⛔ 不新造 IOCTL ✅）。</para>
	/// <para>⚠️ 本方法**不保证**地址可达：现有通道按文档只覆盖 H2RAM 0x700–0x7FF，
	/// 超出该窗口的地址（例如 XRAM 0x8410）能否读到由驱动决定 ⇒ 返回 false 只代表「这次没读到」✅。</para>
	/// </summary>
	/// <param name="address">EC 地址（H2RAM 或 XRAM 的完整 16 位地址）。</param>
	/// <param name="value">读到的原始字节；失败时为 -1。</param>
	internal static bool TryReadByteAt(int address, out int value)
	{
		value = -1;
		if (address < 0)
		{
			return false;
		}
		lock (Gate)
		{
			nint handle = CreateFile("\\\\.\\ACPIDriver", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (handle == new IntPtr(-1))
			{
				return false;
			}
			try
			{
				value = Read(handle, address);
				return value >= 0;
			}
			finally
			{
				CloseHandle(handle);
			}
		}
	}
	private static void EnsureGateExitHook()
	{
		if (System.Threading.Interlocked.Exchange(ref _gateExitHookRegistered, 1) != 0)
		{
			return;
		}
		try
		{
			AppDomain.CurrentDomain.ProcessExit += delegate
			{
				RestoreGateOnExit();
			};
		}
		catch (Exception ex)
		{
			Logger.WriteLine("限充门控持久化：注册进程退出兜底失败（门控残留只能靠重启自愈）：" + ex.Message);
		}
	}

	private static void RestoreGateOnExit()
	{
		int original = System.Threading.Volatile.Read(ref _gateOutstandingOriginal);
		if (original == int.MinValue)
		{
			return;
		}
		System.Threading.Volatile.Write(ref _gateOutstandingOriginal, int.MinValue);
		try
		{
			nint handle = CreateFile("\\\\.\\ACPIDriver", 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (handle == new IntPtr(-1))
			{
				Logger.WriteError($"⚠️⚠️ 充电上限门控未还原，可能导致不充电：进程退出时打不开 ACPIDriver，0x{GateRegister:X3} 可能仍是临时值 {GateOpenValue}（重启后固件回默认值自愈）");
				return;
			}
			try
			{
				bool written = Write(handle, GateRegister, (byte)original);
				int readback = Read(handle, GateRegister);
				if (readback == original)
				{
					Logger.WriteLine($"限充门控持久化：进程退出兜底已完成还原并回读确认 0x{GateRegister:X3}=0x{original:X2}");
				}
				else
				{
					Logger.WriteError($"⚠️⚠️ 充电上限门控未还原，可能导致不充电：进程退出兜底写回 0x{GateRegister:X3}=0x{original:X2}（下发{(written ? "成功" : "失败")}）后回读 0x{(readback < 0 ? "不可读" : readback.ToString("X2"))}，重启一次让固件恢复默认值。");
				}
			}
			finally
			{
				CloseHandle(handle);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteError("⚠️⚠️ 充电上限门控未还原，可能导致不充电：进程退出兜底异常 " + ex.Message);
		}
	}

	private static int Read(nint handle, int address)
	{
		nint num = Marshal.AllocHGlobal(4);
		nint num2 = Marshal.AllocHGlobal(16);
		try
		{
			Marshal.WriteInt32(num, 0, address);
			int returned;
			return (DeviceIoControl(handle, 2621482120u, num, 4, num2, 16, out returned, IntPtr.Zero) && returned > 0) ? Marshal.ReadByte(num2) : (-1);
		}
		finally
		{
			Marshal.FreeHGlobal(num);
			Marshal.FreeHGlobal(num2);
		}
	}

	private static bool Write(nint handle, int address, byte value)
	{
		nint num = Marshal.AllocHGlobal(8);
		nint num2 = Marshal.AllocHGlobal(16);
		try
		{
			Marshal.WriteInt32(num, 0, address);
			Marshal.WriteByte(num, 4, value);
			int returned;
			return DeviceIoControl(handle, 2621482124u, num, 5, num2, 16, out returned, IntPtr.Zero);
		}
		finally
		{
			Marshal.FreeHGlobal(num);
			Marshal.FreeHGlobal(num2);
		}
	}
}
