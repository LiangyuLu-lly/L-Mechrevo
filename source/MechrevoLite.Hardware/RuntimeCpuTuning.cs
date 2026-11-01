using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using MechrevoLite.Helpers;
using MechrevoLite.Properties;
using PawnIO;

namespace MechrevoLite.Hardware;

// 2026-10-05 step7A: ported from upstream 0.290.6 (清单 A-29, AMD branch only).
// 2026-10-10 intel（⑦B，用户原话「intel功能合并」）：**Intel 分支合并完成** —— 这里不再是留桩。
//   * 外部模块 PawnIO\LMechrevo.IntelMSR.bin（PawnIO 模块）
//     （见 source\L-Mechrevo.csproj），由 PawnIO\IntelOcMailbox.cs 载入并读写 MSR 0x150 的 OC 邮箱；
//   * 只碰 domain 0（core）/ 2（cache）：Probe 读能力与当前值，Apply 写电压偏移(-150..0 mV)/倍频，
//     Restore 写回**探针第一次成功读到的基线**；倍频只在**非混合架构**上给（IsHybrid()==false）；
//   * A-31/A-32 的三态收口已落地：Intel 的写入必须**回读一致**才算 Verified=true
//     （WriteVerified），否则是 Accepted/失败，UI 会说清"固件可能拒绝降压"（CpuTuneIntelReady）。
//   * 被固件拒绝过的选项进 `_denied`（本会话不再开放，CanWrite:false）。
// ⚠️ 本机是 AMD 8745H：Intel 分支在本机**永远不会执行**，需要 Intel 真机才能验证（清单见修改日志）。
// CO/OC/power 通路调用的是派生自 g-helper / ZenStates-Core / RyzenAdj 的 SMU 命令表（来源见
// source\PawnIO\RyzenSmuService.cs 头部与 ATTRIBUTION-CO-ADDENDUM.md）。本文件本身不新增任何 SMU 命令 ID。
internal sealed class RuntimeCpuTuning : IDisposable
{
	internal static readonly object SyncRoot = new object();

	private readonly PawnIOWrapper _io = new PawnIOWrapper();

	private RyzenSmuService? _smu;

	// 2026-10-10 intel（⑦B）：Intel OC 邮箱（惰性构造 —— 只有真 Intel 且模块载入成功才有值）。
	private IntelOcMailbox? _intel;

	private readonly HashSet<string> _changed = new HashSet<string>();

	private readonly HashSet<string> _denied = new HashSet<string>();

	private CpuTuningProbe? _probe;

	// 2026-10-10 fmax: 最近一次探针得到的 FMax 能力 + **只读**回读（null = 这次探针没能判定）。
	// UI 的 FMax 行只读它，不自己发命令。
	private CpuFMaxProbe? _fmax;

	internal static RuntimeCpuTuning Instance { get; } = new RuntimeCpuTuning();

	internal static bool OwnsCurve { get; private set; }

	// ---------------------------------------------------------------------------------------
	// Debug force switch (user request, 2026-10-05 step7A).
	// LMECHREVO_FORCE_CPU_TUNING=1 forces the *entry* (the button in the custom-mode window and
	// the window itself) to be shown even when capability detection says the machine has no
	// writable CPU-tuning interface.
	// IMPORTANT: this switch ONLY affects visibility. It never flips CanWrite, never unlocks the
	// Apply / Restore buttons inside the window, and never writes voltage or clock by itself.
	// ---------------------------------------------------------------------------------------
	internal const string ForceEntryEnvironmentVariable = "LMECHREVO_FORCE_CPU_TUNING";

	/// <summary>
	/// 英特尔 CPU 调节总开关（临时屏蔽）：不加载 PawnIO IntelMSR、不读/写 OC 邮箱，
	/// 让 Intel XTU 独占 MSR 0x150。AMD CO/FMax 路径不受影响。
	/// </summary>
	internal const bool IntelCpuTuningDisabled = true;

	private static bool? _forceEntry;

	internal static bool ForceEntry
	{
		get
		{
			bool? forceEntry = _forceEntry;
			if (!forceEntry.HasValue)
			{
				forceEntry = ReadForceEntry();
				_forceEntry = forceEntry;
			}
			return forceEntry.Value;
		}
	}

	private static bool ReadForceEntry()
	{
		try
		{
			string? text = Environment.GetEnvironmentVariable(ForceEntryEnvironmentVariable)?.Trim();
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}
			if (text == "1")
			{
				return true;
			}
			return text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	// Entry point visibility. Capability based by default; the debug switch overrides it.
	// 2026-10-05 step7A-fix2: "any writable option exists" instead of "the whole capability
	// survived the SKU filter". On a Zen4 APU R7 (e.g. Ryzen 7 8745H) the CO option is SKU
	// blocked but the fixed/limited-clock route is still offered, so the entry must stay.
	// The SKU layer is now applied per option inside Probe(), never to the entry as a whole.
	internal static bool EntryVisible
	{
		get
		{
			if (ForceEntry)
			{
				return true;
			}
			if (!X86Base.IsSupported)
			{
				return false;
			}
			// IsSupportedUV() = CO route AND SKU tier allows it.
			// IsSupportedClock() = fixed/limited-clock route only (no SKU filter, by design).
			if (CpuInfo.IsSupportedUV() || CpuInfo.IsSupportedUViGPU() || CpuInfo.IsSupportedClock())
			{
				return true;
			}
			return IsIntel();
		}
	}

	internal bool HasChanges
	{
		get
		{
			lock (SyncRoot)
			{
				return _changed.Count > 0;
			}
		}
	}

	// 2026-10-10 fmax: 最近一次探针（Probe）得到的 FMax 结果（能力 + 只读回读）。
	// null = 本次探针没走到 SMU（缺驱动 / 非 AMD / 初始化失败）或还没探过。
	internal CpuFMaxProbe? FMaxProbe
	{
		get
		{
			lock (SyncRoot)
			{
				return _fmax;
			}
		}
	}

	internal CpuTuningProbe Probe()
	{
		lock (SyncRoot)
		{
			List<CpuTuningOption> optionList = new List<CpuTuningOption>();
			List<string> detailList = new List<string>();
			if (IntelCpuTuningDisabled && CpuVendor.IsIntel)
			{
				return Store("CpuTuneDisabled",
					"Intel CPU tuning is temporarily disabled: LM does not load IntelMSR or send any Intel MSR/OC command, so Intel XTU can own MSR 0x150.",
					optionList);
			}
			// 2026-10-10 fmax: 每次探针都从"没有 FMax 结论"重新开始 —— 上次的结论不能跨探针存活
			// （否则"驱动被卸载后重探"会继续显示上一次的回读值）。
			_fmax = null;
			try
			{
				if (!X86Base.IsSupported)
				{
					return Store("CpuTuneUnsupported", "x64 required", optionList);
				}
				PawnIOWrapper.ConnectResult connectResult = _io.Connect();
				if (connectResult != PawnIOWrapper.ConnectResult.OK)
				{
					return Store(connectResult switch
					{
						PawnIOWrapper.ConnectResult.AccessDenied => "CpuTuneAdminRequired", 
						PawnIOWrapper.ConnectResult.NotInstalled => "CpuTuneDriverMissing", 
						_ => "CpuTuneProbeFailed", 
					}, connectResult.ToString(), optionList);
				}
				if (CpuInfo.IsAMD)
				{
					RyzenSmuService? smu = _smu;
					if (smu == null || !smu.IsInitialized)
					{
						_smu?.Dispose();
						_smu = new RyzenSmuService();
					}
					if (!_smu.Initialize(Assembly.GetExecutingAssembly()))
					{
						return Store("CpuTuneProbeFailed", "RyzenSMU initialization rejected", optionList);
					}
					// 2026-10-05 step7A-fix2（用户原话：「它里面有限制频率功能可能可以用不能一棍子打死」）:
					// SKU 层级过滤**只否决 amd-co 这一个选项**，不再整体否决能力。
					//   * amd-co：CurveCommand 有值 AND SkuTuningRejection == null -> CanWrite:true；
					//     被 SKU 拒绝时仍以 CanWrite:false 列出并带上原因，方便窗口与 probe 解释
					//     "为什么这个选项不可用"，而**不是**把整台机器判成不支持。
					//   * amd-clock：不加 SKU 过滤，ClockCommand 有值即 CanWrite:true。
					// 2026-10-05 step7A-uvtier2：SkuTuningRejection 的族名单已从 Zen4 APU 扩展到
					// Zen4 + Zen5 APU（依据用户提供的 AMD 官网信息："锐龙AI系列 AI 9是都支持的
					// 以下7 5不支持 AI MAX都是支持的"）。**本文件逻辑无需改动**：这里只调用它，
					// 过滤仍然只作用在 amd-co 上，amd-clock 依旧"第一层通过即可用" ✅。
					string? skuRejection = CpuInfo.SkuTuningRejection(_smu.CpuCodeName);
					bool curveCommandPresent = RyzenSmuService.CurveCommand(_smu.CpuCodeName).HasValue;
					// Detail：第一层信息（代号 + SMU 版本）恒定；随后按选项补充说明。
					string text2 = $"{_smu.CpuCodeName}; SMU 0x{_smu.SmuVersion:X}";
					if (curveCommandPresent)
					{
						// ======================================================================
						// 2026-10-05 coread: CO 回读（**只读**，且**只在这里读一次**）。
						//
						// 为什么放在 Probe：这里本来就是"检测能力"的入口（UI 按钮 / --cpu-tuning-probe
						// / 窗口打开时各一次），不是轮询路径。每核一条 SMU 命令（8 核 = 8 条），
						// 绝不放进定时刷新。
						//
						// 三种结果分别对应 UI 三种文案（不伪造 0）：
						//   * 平台没有读命令（Renoir/VanGogh...） -> Current=null + CpuTuneNoReadCmd
						//   * 有命令但读失败/解不出 CO 步数       -> Current=null + CpuTuneReadFailed
						//   * 读到了                             -> Current=值 + CoreReadback=逐核值
						//     （逐核不一致时 Current=null，UI 显示范围，数值框回退到 0）
						// ======================================================================
						List<int>? coreMargins = null;
						string? readbackNoteKey;
						if (!RyzenSmuService.SupportsCurveRead(_smu.CpuCodeName))
						{
							readbackNoteKey = "CpuTuneNoReadCmd";
							text2 += "; CO readback: no read command for this codename";
						}
						else
						{
							int coreCount = CpuInfo.PhysicalCoreCount;
							List<int> values = new List<int>(coreCount);
							bool readFailed = false;
							for (int core = 0; core < coreCount; core++)
							{
								SmuStatus readStatus = _smu.ReadCurveMargin(core, out uint[] readResponse);
								int? margin = ((readStatus == SmuStatus.OK) ? RyzenSmuService.DecodeCurveMargin(readResponse[0]) : ((int?)null));
								if (!margin.HasValue)
								{
									readFailed = true;
									text2 += $"; CO readback failed at core {core} (SMU: {readStatus} (0x{(uint)readStatus:X2}), resp 0x{readResponse[0]:X8})";
									break;
								}
								values.Add(margin.Value);
							}
							if (readFailed || values.Count == 0)
							{
								readbackNoteKey = "CpuTuneReadFailed";
							}
							else
							{
								readbackNoteKey = null;
								coreMargins = values;
								text2 += $"; CO readback per core [{string.Join(", ", values)}] (SMU current state, this boot only)";
							}
						}
						decimal? current = null;
						if (coreMargins != null && coreMargins.All((int v) => v == coreMargins[0]))
						{
							current = coreMargins[0];
						}
						// ==========================================================================
						// 2026-10-10 percorenolog（用户原话「只要支持就开 分核负压代码就参照SMU，
						//   全核负压框删掉」）：**每核写不再有代号白名单** —— 判据只剩两条：
						//     (a) 该代号在 SMU 参考里有每核写命令（CurvePerCoreCommand 有值）；
						//     (b) SKU 层级没有被锁（与 amd-co 的 CanWrite 同一把锁）。
						//   ⛔ 不再有"已实测 / SMU 声明·未实测"的分档标注。
						//   ⚠️ 只有 Vermeer 是台式机实测过的；其余代号靠 ApplyPerCore 的
						//     "写前基线 + 逐核回读 + 不一致则全量回写"兜住（那是安全网，不是档位）。
						// ==========================================================================
						(bool, uint)? perCoreCommand = RyzenSmuService.CurvePerCoreCommand(_smu.CpuCodeName);
						bool perCoreWrite = skuRejection == null && perCoreCommand.HasValue;
						bool perCoreReadback = RyzenSmuService.SupportsPerCoreReadback(_smu.CpuCodeName);
						if (perCoreWrite)
						{
							text2 += (string.Format("; per-core CO write: {0} 0x{1:X2} per core (arg0 = core << 20) available",
								perCoreCommand!.Value.Item1 ? "PSMU" : "MP1", perCoreCommand.Value.Item2)
								+ (perCoreReadback
									? "; per-core readback available -> writes are verified per core"
									: "; no per-core read command for this codename -> writes cannot be read back")
								+ "; all-core CO input box removed, use the per-core matrix");
						}
						else if (perCoreCommand.HasValue)
						{
							text2 += "; per-core CO write: present in the SMU reference but locked on this SKU";
						}
						else
						{
							// ⚠️ 这句只在**真的没有每核命令**时出现（不是"未实测"的意思）。
							text2 += "; per-core CO write: this codename has no per-core write command in the SMU reference"
								+ (curveCommandPresent ? " (all-core CO write only)" : " (no CO write path at all)");
						}
						// 被 SKU 层级锁掉的代号（本机 R7 就是）**照样读**：能不能读与能不能写是两把锁，
						// 读回来的值也不是"可写"的暗示。
						// 2026-10-10 percorenolog：CanWrite 现在还接受**每核**写路径 —— 只写每核
						// （CurveCommand 无值）的代号也能在 UI 上给出 CO 输入口，否则"删掉全核框"之后
						// 那些代号会连一个输入口都没有。SKU 层级仍然是否决项（与改前同一把锁）。
						bool coWritable = skuRejection == null && (curveCommandPresent || perCoreCommand.HasValue);
						optionList.Add(new CpuTuningOption("amd-co", "CpuTuneCurve", -30m, 0m, "CO", current, CanWrite: coWritable, coreMargins, readbackNoteKey, PerCoreWrite: perCoreWrite));
					}
					bool clockAvailable = RyzenSmuService.SupportsClock(_smu.CpuCodeName);
					if (clockAvailable)
					{
						// 2026-10-05 coread: 固定/限制频率**确实没有 Getter**（上游也只有 Set*），
						// 这里给一个专门的说明键，免得和"CO 不可回读"混为一谈。
						optionList.Add(new CpuTuningOption("amd-clock", "CpuTuneClock", 1000m, RyzenSmuService.MaximumClock(_smu.CpuCodeName), "MHz", null, CanWrite: true, null, "CpuTuneClockNoReadback"));
					}
					// 2026-10-10 percorenolog：**只要还有 CO 写路径**（全核或每核）被 SKU 锁掉就说明原因；
					// 改前这里只认 curveCommandPresent，现在"只写每核"的代号也要能说清为什么不可写。
					bool anyCoRoute = curveCommandPresent || RyzenSmuService.SupportsPerCoreCurve(_smu.CpuCodeName);
					if (anyCoRoute && skuRejection != null)
					{
						// CO 被 SKU 层级锁掉，但其它选项（固定/限制频率）不受影响。
						text2 += $"; amd-co unavailable: {skuRejection}";
						if (clockAvailable)
						{
							text2 += "; fixed/limited clock is still available for this SKU";
						}
					}
					if (optionList.Count == 0)
					{
						text2 += "; no SMU curve/clock command for this codename";
					}
					// 2026-10-10 percorenolog（用户要求：被拒选项记入 `_denied`、失败不静默）：
					// AMD 侧也把"本会话被固件拒过的选项"写进 Detail（Intel 侧本来就有这一句，
					// 见下面 Intel 段的 detailList）—— 否则被拒之后 UI 只是悄悄置灰，原因无处可查。
					if (_denied.Count > 0)
					{
						text2 += "; firmware rejected in this session: " + string.Join(", ", _denied) + " (option disabled for this session, restart to retry)";
					}
					// ======================================================================
					// 2026-10-10 fmax: FMax（PBO 最大频率）能力 + **只读**回读。
					//
					// 放在 Probe 里，与 CO 回读同一个理由：这里本来就是"检测能力"的入口
					// （UI 按钮 / --cpu-tuning-probe / 窗口打开时各一次），不是轮询路径。
					// 一条邮箱命令，绝不放进定时刷新。
					//
					// ⚠️ 只读：本方法只调用 RyzenSmuService.GetBoostLimitFrequency；
					//    写入（SetBoostLimitFrequencyAllCores）只在用户点「应用」时才会发生，
					//    **不在探针里、不在开机流程里**（用户明确要求不要自动应用）。
					// ======================================================================
					_fmax = ProbeFMax(_smu);
					text2 += "; FMax: " + _fmax.Detail;
					// 状态键：有可写项 + 有被拒项 = 部分可用（CpuTuneSmuPartial）；全部可写 =
					// CpuTuneSmuReady；一个可写项都没有 = CpuTuneUnsupported。键名沿用上游 CpuTune* 体系。
					string statusKey;
					if (optionList.Any((CpuTuningOption o) => o.CanWrite))
					{
						statusKey = (optionList.Any((CpuTuningOption o) => !o.CanWrite) ? "CpuTuneSmuPartial" : "CpuTuneSmuReady");
					}
					else
					{
						statusKey = "CpuTuneUnsupported";
					}
					return Store(statusKey, text2, optionList);
				}
				if (!IsIntel())
				{
					return Store("CpuTuneUnsupported", "Unknown CPU vendor", optionList);
				}
				// ==================================================================================
				// 2026-10-10 intel（⑦B，用户原话「intel功能合并」）：**Intel 分支**（替换原 step-7B 留桩）。
				//
				// 逐字对齐上游（作者 0.290.6 `RuntimeCpuTuning.Probe` 的 Intel 段）：
				//   1) 惰性载入外部 PawnIO\LMechrevo.IntelMSR.bin（签名模块；载入失败 -> CpuTuneProbeFailed）；
				//   2) 对 domain 0（core）/ 2（cache）各探一次：Probe() 读能力(命令 1) + 当前值(命令 16)；
				//      * intel-core / intel-cache —— 电压偏移，范围 -150..0 mV；CanWrite 还要看
				//        VoltageSupported **且** 本会话没被固件拒过（_denied）；
				//      * intel-ratio —— 倍频，范围 8..MaximumRatio；**非混合架构**才给（IsHybrid()==false）；
				//   3) 任一选项可写 -> CpuTuneIntelReady（文案里明说"固件仍可能拒绝降压"），
				//      否则 -> CpuTuneUnsupported（Detail 逐 domain 写明 capabilities/ratio，读失败的那条也带上原因）。
				// ⚠️ 这一段在本机（AMD 8745H）**永远不会执行**；没有 Intel 真机就无法验证任何一步。
				// ==================================================================================
				if (_intel == null)
				{
					using Stream stream = PawnIOModuleLoader.Open("LMechrevo.IntelMSR.bin")
						?? throw new IOException("IntelMSR.bin missing");
					using MemoryStream memoryStream = new MemoryStream();
					stream.CopyTo(memoryStream);
					if (!_io.LoadModule(memoryStream.ToArray()))
					{
						return Store("CpuTuneProbeFailed", "Signed IntelMSR module rejected", optionList);
					}
					_intel = new IntelOcMailbox(_io.Execute);
				}
				int[] domains = new int[2] { 0, 2 };
				foreach (int domain in domains)
				{
					string optionId = ((domain == 0) ? "intel-core" : "intel-cache");
					try
					{
						IntelOcDomain intelDomain = _intel.Probe(domain);
						optionList.Add(new CpuTuningOption(optionId, (domain == 0) ? "CpuTuneCore" : "CpuTuneCache",
							-150m, 0m, "mV", intelDomain.OffsetMv,
							intelDomain.VoltageSupported && !_denied.Contains(optionId)));
						if (domain == 0 && !IsHybrid() && intelDomain.RatioSupported)
						{
							// 倍频只写 domain 0；混合架构（P/E 核）上作者不给这一项。
							optionList.Add(new CpuTuningOption("intel-ratio", "CpuTuneRatio", 8m, intelDomain.MaximumRatio, "x",
								intelDomain.Ratio, !_denied.Contains("intel-ratio")));
						}
						detailList.Add($"domain {domain}: capabilities=0x{intelDomain.Capabilities:X}, ratio={intelDomain.Ratio}/{intelDomain.MaximumRatio}"
							+ $", offset={intelDomain.OffsetMv:F3} mV, voltageSupported={intelDomain.VoltageSupported}, ratioSupported={intelDomain.RatioSupported}");
					}
					catch (Exception ex)
					{
						// 单个 domain 探失败不影响另一个（与上游一致：只把原因写进 Detail）。
						detailList.Add($"domain {domain}: {ex.Message}");
					}
				}
				detailList.Add($"hybrid architecture: {IsHybrid()} (ratio option is offered only when false)");
				if (_denied.Count > 0)
				{
					detailList.Add("Firmware rejected in this session: " + string.Join(", ", _denied));
				}
				return Store(optionList.Any((CpuTuningOption o) => o.CanWrite) ? "CpuTuneIntelReady" : "CpuTuneUnsupported",
					string.Join(Environment.NewLine, detailList), optionList);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("CPU tuning probe: " + ex);
				return Store("CpuTuneProbeFailed", ex.Message, optionList);
			}
		}
	}

	private CpuTuningProbe Store(string key, string detail, List<CpuTuningOption> options)
	{
		// 2026-10-10 fmax: 把本次探针的 FMax 结论一并带走（_fmax 在非 AMD / 前置条件不满足时恒为 null）。
		return _probe = new CpuTuningProbe(CpuInfo.Name, key, detail, options, _fmax);
	}

	// ==========================================================================================
	// 2026-10-10 fmax: FMax（PBO 最大频率）探针 —— **只读**。
	//
	// 三种情形分得开（UI 三种文案，不伪造数值）：
	//   * 代号不在能力表（None）          -> 不读、不写，说明"该代号没有 FMax 邮箱命令"；
	//   * 只有写、没有读（ShimadaPeak）    -> 不猜也不试读，说明"可写但无法回读校验"；
	//   * 有读命令                        -> 真读一条，成功给值（含原始 response[0]），失败给固件状态码。
	//
	// ⚠️ 参数语义未确认（参考实现按绝对 MHz 打包，作者却说是 -1000..+200 的偏移）。
	//    所以这里**同时**带出"掩码后的值"与"原始 response[0]"，让用户/报告能自己判定。
	// ==========================================================================================
	private CpuFMaxProbe ProbeFMax(RyzenSmuService smu)
	{
		FMaxSpec? spec = RyzenSmuService.ResolveFMax(smu.CpuCodeName);
		if (!spec.HasValue)
		{
			return new CpuFMaxProbe(FMaxCapability.None, null, 0u,
				$"not supported on {smu.CpuCodeName} (no FMax mailbox command; read or write); no command sent");
		}
		FMaxCapability capability = spec.Value.Capability;
		if (spec.Value.GetCommand == 0)
		{
			// ShimadaPeak：只有写。没有读命令就不猜 ID 盲试（沿用本仓库既有规矩）。
			return new CpuFMaxProbe(capability, null, 0u,
				$"write-only on {smu.CpuCodeName} (Set 0x{spec.Value.SetCommand:X2}); no read command -> read-back verification unavailable");
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		SmuStatus status = smu.GetBoostLimitFrequency(out uint[] response);
		stopwatch.Stop();
		uint raw = response[0];
		if (status != SmuStatus.OK)
		{
			string failed = $"read failed on {smu.CpuCodeName} (Get 0x{spec.Value.GetCommand:X2}, arg0=0x{spec.Value.ReadArgument:X8}): "
				+ $"{DescribeFMaxStatus(status)}, response[0] 0x{raw:X8}, {stopwatch.ElapsedMilliseconds} ms";
			Logger.WriteLine("[cpu-fmax] " + failed);
			return new CpuFMaxProbe(capability, null, raw, failed);
		}
		// 解码掩码与**写入**的打包掩码一致（EncodeFMaxArg = 低 20 位），这样"写后回读比对"才有意义。
		decimal decoded = raw & 0xFFFFFu;
		// 2026-10-10 fmax-persist：**第一次成功读取**就把这个值记成"原值"（cpu_fmax_baseline），
		// 供「重置」恢复。只记一次 —— 之后用户用别的工具改 FMax 也不会把原值冲掉。
		CaptureFMaxBaselineIfMissing((int)decoded, "first successful read");
		string ok = $"read OK on {smu.CpuCodeName} (Get 0x{spec.Value.GetCommand:X2}, arg0=0x{spec.Value.ReadArgument:X8}): "
			+ $"response[0] 0x{raw:X8} -> 0xFFFFF mask = {decoded}; capability {capability}, {stopwatch.ElapsedMilliseconds} ms"
			+ $"; baseline {FMaxBaselineValue?.ToString() ?? "none"} MHz (cpu_fmax_baseline, restored by Reset)";
		Logger.WriteLine("[cpu-fmax] " + ok);
		return new CpuFMaxProbe(capability, decoded, raw, ok);
	}

	// 固件状态码要**说人话**（用户要求：把 0x01 OK / 0xFC busy / 0xFD prerequisite /
	// 0xFE unknown cmd / 0xFF failed 显示给用户，而不是静默失败）。
	private static string DescribeFMaxStatus(SmuStatus status)
	{
		string meaning = status switch
		{
			SmuStatus.OK => "OK",
			SmuStatus.CmdRejectedBusy => "rejected: busy (retry later)",
			SmuStatus.CmdRejectedPrereq => "rejected: prerequisite not met",
			SmuStatus.UnknownCmd => "rejected: unknown command (no such mailbox command on this firmware)",
			SmuStatus.Failed => "failed",
			_ => "unrecognized firmware status",
		};
		return $"{status} (0x{(uint)status:X2}, {meaning})";
	}

	// FMax 输入框的允许区间（与 CpuTuningForm 里那个框的 Minimum/Maximum 一致）。
	// ⚠️ 这是"照参考实现当绝对 MHz"的口径；参数语义未确认，见 RyzenSmuService 的 FMax 小节。
	internal const decimal FMaxMinimum = 0m;

	internal const decimal FMaxMaximum = 10000m;

	// ==========================================================================================
	// 2026-10-10 fmax-persist（用户决定原话：「开机自动应用可以自动应用 我觉得可以增加一个保存和
	// 读取按钮 可以保存当前的负压值和Fmax 点读取就读回来 点重置都是恢复默认」）：
	//
	// FMax 的**持久化** —— 复用与 CO **同一份** AppConfig（同样登记进
	// ModelScopedConfig.ExactMachineScopedKeys = 按机型作用域，绝不把 A 机器的值带到 B 机器）：
	//   * cpu_fmax_baseline —— **原值**：第一次成功回读到的那个值。只在"还没有记录"时写一次
	//     （首次成功读取，或第一次写入之前的兜底回读）；「重置」只把它写回去。
	//     ⚠️ 口径必须诚实：别的工具先改过 FMax 的机器上，这个值是"上次读到的原值"，**不是**出厂值，
	//        所以 UI 一律写「上次读到的原值」，绝不许写成"出厂默认"。
	//   * cpu_fmax_saved    —— 用户点「保存」时存下的 FMax 值；「读取」与「开机自动应用」重放它。
	//
	// ⚠️ 参数语义未确认（绝对 MHz vs 偏移）这一点**没有变**：这里存的就是同一个口径的原始数字，
	//    语义确认后要改的是打包/单位，不是这一份持久化格式。
	// ==========================================================================================
	internal const string FMaxBaselineKey = "cpu_fmax_baseline";

	internal const string FMaxSavedKey = "cpu_fmax_saved";

	// 本进程里 FMax 是否真的被 SMU 接受过（「重置」据此判断"有没有东西需要恢复"）。
	private bool _fmaxApplied;

	// 记录到的 FMax 原值（null = 还没有成功读到过 -> 「重置」不猜值，UI 照实说"未知"）。
	internal static int? FMaxBaselineValue => AppConfig.Exists(FMaxBaselineKey) ? AppConfig.Get(FMaxBaselineKey) : null;

	// 用户「保存」过的 FMax 值（null = 没保存过）。
	internal static int? LoadSavedFMax()
	{
		return AppConfig.Exists(FMaxSavedKey) ? AppConfig.Get(FMaxSavedKey) : null;
	}

	// 「保存」按钮的 FMax 那一半：**只写配置文件，不碰 SMU**（与 SaveSettings 同一套做法）。
	// 返回 false = 值不在允许区间（与 ApplyFMax 同一把尺子），一个字节都没写。
	internal static bool SaveFMax(int mhz)
	{
		if ((decimal)mhz < FMaxMinimum || (decimal)mhz > FMaxMaximum)
		{
			Logger.WriteLine($"[cpu-fmax] save refused: {mhz} MHz is outside {FMaxMinimum}..{FMaxMaximum}");
			return false;
		}
		AppConfig.Set(FMaxSavedKey, mhz);
		AppConfig.Flush();
		Logger.WriteLine($"[cpu-fmax] saved: {mhz} MHz -> {FMaxSavedKey} (machine-scoped)");
		return true;
	}

	// FMax 原值只在"还没有记录"时写一次。mhz = 与写入/回读同一个掩码口径（低 20 位）的数值。
	// 明显不合理的读值（越界）不记 —— 宁可"没有原值"（「重置」会照实说未知），也不记一个垃圾值。
	private static void CaptureFMaxBaselineIfMissing(int mhz, string source)
	{
		if (AppConfig.Exists(FMaxBaselineKey))
		{
			return;
		}
		if ((decimal)mhz < FMaxMinimum || (decimal)mhz > FMaxMaximum)
		{
			Logger.WriteLine($"[cpu-fmax] baseline NOT captured ({source}): {mhz} MHz is outside {FMaxMinimum}..{FMaxMaximum}; Reset will report it as unknown");
			return;
		}
		AppConfig.Set(FMaxBaselineKey, mhz);
		AppConfig.Flush();
		Logger.WriteLine($"[cpu-fmax] baseline captured ({source}): {mhz} MHz -> {FMaxBaselineKey} (machine-scoped); this is the value Reset restores");
	}

	// 能力分类器说这个代号能不能写（ReadWrite / WriteNoReadback）。
	private static bool IsFMaxWritable(FMaxCapability capability)
	{
		return capability == FMaxCapability.ReadWrite || capability == FMaxCapability.WriteNoReadback;
	}

	// 最近一次探针的能力里含写路径（UI 的门控用它；真正的写入守卫仍在 ApplyFMax 里）。
	// ⚠️ 与下面 FMaxRestorePending 同理**故意不取 SyncRoot**：引用/bool 的单次读是原子的，
	//    而这两个属性是 UI 线程在 UpdateSelection 里频繁调用的；取锁会与"持有 SyncRoot 且
	//    在锁内做 Program.settingsForm.Invoke（Toast）"的后台开机自动应用线程互相等待 = 死锁。
	internal bool FMaxWritableCapability
	{
		get
		{
			CpuFMaxProbe? fmax = _fmax;
			return fmax != null && IsFMaxWritable(fmax.Capability);
		}
	}

	// 本进程里 FMax 被写成功过（UI 的「重置」提示按它分档）。同上：原子读，不取锁。
	internal bool FMaxAppliedThisSession => _fmaxApplied;

	// 「重置」在 FMax 上有没有事可做（UI 用它决定「重置」按钮要不要亮）：
	//   * 本机不能写 FMax（只读 / 不支持 / 还没探到）-> false（重置根本不碰它）；
	//   * 本进程写过 FMax                       -> true（哪怕没有原值，也得让用户点得到"为什么恢复不了"）；
	//   * 有原值且当前回读与之不同               -> true（别人改过 / 上次开机留下的差异）。
	// ⚠️ 不取 SyncRoot：理由是上面那一条（UI 热路径 + 锁内 Invoke 的死锁风险）；读到的 `_fmax`
	//    要么是本次探针的旧值要么是新值，两种都不会让按钮状态变得不安全（最终写命令前的守卫在
	//    ApplyFMax / ResetAll 里，那一层始终在 SyncRoot 内并重新读结论）。
	internal bool FMaxRestorePending
	{
		get
		{
			CpuFMaxProbe? fmax = _fmax;
			if (fmax == null || !IsFMaxWritable(fmax.Capability))
			{
				return false;
			}
			if (_fmaxApplied)
			{
				return true;
			}
			int? baseline = FMaxBaselineValue;
			return baseline.HasValue && fmax.Current.HasValue && (int)fmax.Current.Value != baseline.Value;
		}
	}

	// ==========================================================================================
	// 2026-10-10 fmax: FMax（PBO 最大频率）**写入路径**。
	//
	// 守卫**先行**（与本仓库既有的"代号守卫"同一写法）：能力分类器说该代号不能写
	// （ReadOnly / None / 还没探过 / SMU 没初始化）-> 直接返回失败，**一条 SMU 命令都不发**。
	//
	// 写后**一定回读**（除非该代号根本没有读命令 = ShimadaPeak），并把固件状态码
	// （0x01 OK / 0xFC busy / 0xFD prerequisite / 0xFE unknown cmd / 0xFF failed）**原样**带进
	// 结果文本里给用户看 —— 用户明确要求"绝不静默失败"。
	//
	// ⚠️ 2026-10-10 fmax-persist（用户决定「增加一个保存和读取按钮…点读取就读回来 点重置都是恢复默认」
	//    +「开机自动应用可以自动应用」）：改前"不写配置 / 不开机自动应用 / 不参与重置"的三条
	//    现在**全部有意放开**，但每一条都配了"原值"作为安全前提：
	//   * 写配置 / 开机自动应用：只重放 **cpu_fmax_saved**（用户显式点「保存」存下的那一份），
	//     且只在能力含写路径（ReadWrite / WriteNoReadback）**且**开关 cpu_co_auto_apply 为开时才做；
	//   * 「重置」：恢复的是 **cpu_fmax_baseline**（第一次成功回读到的原值，或第一次写入之前的兜底
	//     回读）。**没有记录到原值时一条命令都不发**，并照实说"没有原值可恢复"（绝不猜值写入）；
	//   * 仍然**不把 "amd-fmax" 放进 `_changed`** —— `_changed` 的语义是"Restore() 用既有的
	//     统一写法恢复（CO 逐核 0 / 固定频率 DisableOc）"，FMax 走的是**另一条**恢复路径
	//     （写回原值），塞进去会被 Restore() 误当成"退出固定频率"。所以 FMax 用独立的
	//     `_fmaxApplied` 标记（见 FMaxRestorePending），`HasChanges` 的语义一字未改；
	//   * **不碰 amd-co / amd-clock 的任何既有路径**。
	//
	// ⚠️ 参数语义未确认：写的是 `mhz & 0xFFFFF`（照参考实现），回读比对也用同一掩码，
	//    所以"匹配/不匹配"的结论在两种语义下都成立（比对的是 SMU 自己存的那 20 位）。
	// ==========================================================================================
	internal CpuTuningResult ApplyFMax(decimal mhz)
	{
		lock (SyncRoot)
		{
			if (IntelCpuTuningDisabled && CpuVendor.IsIntel)
			{
				return new CpuTuningResult(Success: false, Verified: false, "Intel CPU tuning is disabled (XTU owns MSR 0x150); no command sent.");
			}
			CpuFMaxProbe? fmax = _fmax;
			if (fmax == null)
			{
				return new CpuTuningResult(Success: false, Verified: false, "FMax: no capability result yet (run Detect first); no command sent.");
			}
			if (fmax.Capability == FMaxCapability.None)
			{
				return new CpuTuningResult(Success: false, Verified: false, "FMax: not supported on this CPU (no PBO mailbox command for this codename); no command sent.");
			}
			if (fmax.Capability == FMaxCapability.ReadOnly)
			{
				return new CpuTuningResult(Success: false, Verified: false, "FMax: read-only on this CPU (no FMax write command for this codename); no command sent.");
			}
			if (mhz < FMaxMinimum || mhz > FMaxMaximum || mhz != decimal.Truncate(mhz))
			{
				return new CpuTuningResult(Success: false, Verified: false, $"FMax value {mhz} is outside the permitted range {FMaxMinimum}..{FMaxMaximum} MHz; no command sent.");
			}
			if (_smu == null || !_smu.IsInitialized)
			{
				return new CpuTuningResult(Success: false, Verified: false, "FMax: the SMU service is not initialized; no command sent.");
			}
			FMaxSpec? spec = RyzenSmuService.ResolveFMax(_smu.CpuCodeName);
			if (!spec.HasValue || spec.Value.SetCommand == 0)
			{
				// 与能力结论矛盾（理论上到不了这里）：仍然按"不能写"处理，不发命令。
				return new CpuTuningResult(Success: false, Verified: false, $"FMax: no write command for {_smu.CpuCodeName}; no command sent.");
			}
			// 2026-10-10 fmax-persist：**第一次写入之前**必须有原值可恢复 —— 探针那次读失败
			// （所以还没有记录）时在这里补一次**只读**回读，把"写之前的样子"记下来。
			// 补读也失败 -> **不记**（宁可没有原值、「重置」照实说未知，也不记一个猜的值）。
			if (!AppConfig.Exists(FMaxBaselineKey) && spec.Value.GetCommand != 0)
			{
				SmuStatus preStatus = _smu.GetBoostLimitFrequency(out uint[] preResponse);
				if (preStatus == SmuStatus.OK)
				{
					CaptureFMaxBaselineIfMissing((int)(preResponse[0] & 0xFFFFFu), "pre-write read");
				}
				else
				{
					Logger.WriteLine($"[cpu-fmax] pre-write baseline read failed: {DescribeFMaxStatus(preStatus)};"
						+ " no baseline recorded for this machine, Reset will report it as unknown (no value guessed)");
				}
			}
			uint arg = RyzenSmuService.EncodeFMaxArg((uint)mhz);
			Stopwatch stopwatch = Stopwatch.StartNew();
			SmuStatus writeStatus = _smu.SetBoostLimitFrequencyAllCores((uint)mhz);
			Logger.WriteLine($"[cpu-fmax] write {mhz} MHz -> {_smu.CpuCodeName} Set 0x{spec.Value.SetCommand:X2} (mailbox 0x{spec.Value.MailboxCommand:X}) arg0=0x{arg:X8}: {DescribeFMaxStatus(writeStatus)}");
			if (writeStatus != SmuStatus.OK)
			{
				stopwatch.Stop();
				return new CpuTuningResult(Success: false, Verified: false,
					$"FMax write rejected by the firmware: {DescribeFMaxStatus(writeStatus)}; "
					+ $"Set 0x{spec.Value.SetCommand:X2}, arg0=0x{arg:X8} ({mhz} MHz), {stopwatch.ElapsedMilliseconds} ms");
			}
			if (spec.Value.GetCommand == 0)
			{
				// ShimadaPeak：可写但没有回读命令 —— 如实说"无法校验"，绝不假装 verified。
				stopwatch.Stop();
				_fmaxApplied = true;
				Logger.WriteLine($"[cpu-fmax] write accepted, read-back unavailable ({_smu.CpuCodeName} has no FMax read command)");
				return new CpuTuningResult(Success: true, Verified: false,
					$"FMax write accepted: {DescribeFMaxStatus(writeStatus)}, arg0=0x{arg:X8} ({mhz} MHz). "
					+ $"Read-back verification is unavailable: {_smu.CpuCodeName} has no FMax read command. "
					+ $"Semantics of this parameter is unconfirmed (absolute MHz vs offset), {stopwatch.ElapsedMilliseconds} ms");
			}
			SmuStatus readStatus = _smu.GetBoostLimitFrequency(out uint[] response);
			stopwatch.Stop();
			uint raw = response[0];
			uint readBack = RyzenSmuService.EncodeFMaxArg(raw);
			bool readOk = readStatus == SmuStatus.OK;
			bool verified = readOk && readBack == (uint)mhz;
			string detail = $"FMax: wrote {mhz} MHz (Set 0x{spec.Value.SetCommand:X2}, arg0=0x{arg:X8}) -> {DescribeFMaxStatus(writeStatus)}; "
				+ $"read back (Get 0x{spec.Value.GetCommand:X2}) -> {DescribeFMaxStatus(readStatus)}, response[0]=0x{raw:X8} (0xFFFFF mask = {readBack}) "
				+ $"-> {(verified ? "match" : (readOk ? "MISMATCH" : "readback unavailable"))}, {stopwatch.ElapsedMilliseconds} ms";
			Logger.WriteLine("[cpu-fmax] " + detail);
			// 写入被接受即 Success=true；Verified 只在"回读到了且与写入值一致"时为真。
			_fmaxApplied = true;
			return new CpuTuningResult(Success: true, Verified: verified, detail);
		}
	}

	internal CpuTuningResult Apply(string id, decimal value)
	{
		lock (SyncRoot)
		{
			if (IntelCpuTuningDisabled && CpuVendor.IsIntel)
			{
				return new CpuTuningResult(Success: false, Verified: false, "Intel CPU tuning is disabled (XTU owns MSR 0x150); no command sent.");
			}
			CpuTuningOption? cpuTuningOption = _probe?.Options.FirstOrDefault((CpuTuningOption o) => o.Id == id);
			if (cpuTuningOption == null || !cpuTuningOption.CanWrite || value < cpuTuningOption.Minimum || value > cpuTuningOption.Maximum || value != decimal.Truncate(value))
			{
				return new CpuTuningResult(Success: false, Verified: false, "Setting unavailable or outside the permitted range.");
			}
			bool flag = _changed.Contains(id);
			try
			{
				_changed.Add(id);
				if (id.StartsWith("intel-", StringComparison.Ordinal))
				{
					// ==============================================================================
					// 2026-10-10 intel（⑦B）：Intel 写入路径（上游 0.290.6 的 Intel 段，逐字对齐）。
					//   * intel-core  -> domain 0 的电压偏移；intel-cache -> domain 2 的电压偏移；
					//   * intel-ratio -> domain 0 的倍频（offsetMv=null）。
					//   * ⚠️ 写入必须**回读一致**（IntelOcMailbox.WriteVerified）才走到下一行；
					//     失败会抛异常，由下面的 catch 按 A-31/A-32 收口：
					//       Data["MailboxWriteRejected"]     -> 进 _denied（本会话该选项不再可写）
					//       Data["PreviousValueUnchanged"]   -> 若原本没记过"已改动"就撤掉（不误报已改）
					//   * Verified:true 是**有依据**的（WriteVerified 已确认回读 == 期望值），
					//     所以 UI 的「已应用，回读一致」这句在 Intel 上是真的；
					//     ⚠️ AMD 侧 Verified:false（SMU 只表示"接受命令"）—— 两边的三态语义见 CpuTuningResult。
					// ==============================================================================
					if (_intel == null)
					{
						return new CpuTuningResult(Success: false, Verified: false, "Intel OC mailbox is not open; run Detect first.");
					}
					IntelOcDomain intelDomain;
					if (id == "intel-ratio")
					{
						intelDomain = _intel.Apply(0, null, (int)value);
					}
					else
					{
						intelDomain = _intel.Apply((id == "intel-core") ? 0 : 2, value);
					}
					return new CpuTuningResult(Success: true, Verified: true, ((id == "intel-ratio") ? $"{intelDomain.Ratio}x" : $"{intelDomain.OffsetMv:F3} mV") + $" (domain {intelDomain.Id}, readback 0x{intelDomain.Value:X8})");
				}
				if (id == "amd-co")
				{
					OwnsCurve = true;
				}
				SmuStatus smuStatus = ((id == "amd-co") ? _smu.SetCoAll((int)value) : _smu.SetOcClock((int)value));
				return new CpuTuningResult(smuStatus == SmuStatus.OK, Verified: false, $"SMU: {smuStatus} (0x{(uint)smuStatus:X2})");
			}
			catch (Exception ex)
			{
				if (ex.Data.Contains("MailboxWriteRejected"))
				{
					_denied.Add(id);
				}
				if (!flag && ex.Data.Contains("PreviousValueUnchanged"))
				{
					_changed.Remove(id);
				}
				Logger.WriteLine("CPU tuning apply: " + ex);
				return new CpuTuningResult(Success: false, Verified: false, ex.Message);
			}
		}
	}

	internal CpuTuningResult Restore()
	{
		lock (SyncRoot)
		{
			if (IntelCpuTuningDisabled && CpuVendor.IsIntel)
			{
				return new CpuTuningResult(Success: false, Verified: false, "Intel CPU tuning is disabled (XTU owns MSR 0x150); no command sent.");
			}
			List<string> list = new List<string>();
			// 2026-10-10 intel（⑦B）：Intel 的两个 domain 各只恢复一次（intel-core / intel-ratio 同属
			// domain 0，重复调 Restore 只是多发一遍同样的写回，没必要）。
			HashSet<string> intelRestored = new HashSet<string>();
			bool verified = true;
			string[] array = _changed.ToArray();
			foreach (string text in array)
			{
				try
				{
					if (text.StartsWith("intel-", StringComparison.Ordinal))
					{
						// =========================================================================
						// 2026-10-10 intel（⑦B）：Intel 恢复路径（上游 0.290.6 的 Intel 段，逐字对齐）。
						//   * 写回的是 **IntelOcMailbox 里探针第一次成功读到的基线**（Probe 时 TryAdd），
						//     不是"猜测的默认值"；
						//   * domain 0（core，含倍频）与 domain 2（cache）各恢复一次；
						//   * 写回同样走 WriteVerified（回读一致才算成功），失败照实记进结论；
						//   * `verified` 保持 true —— Intel 的恢复是真回读校验过的（与 AMD 侧相反）。
						// ⚠️ Restore 用的 `_intel` 必须是同一次 Probe 的对象（基线存在它里面）；
						//    本会话没探过（_intel == null）就无法恢复，照实报错。
						// =========================================================================
						if (_intel == null)
						{
							throw new InvalidOperationException("Intel OC mailbox is not open; the captured baseline is unavailable.");
						}
						string domainName = ((text == "intel-cache") ? "cache" : "core");
						if (intelRestored.Add(domainName))
						{
							_intel.Restore((domainName == "cache") ? 2 : 0);
						}
						_changed.Remove(text);
						continue;
					}
					verified = false;
					SmuStatus smuStatus;
					if (text == "amd-co")
					{
						// 2026-10-05 percore-product: 每核路径可用的代号（目前只有 Vermeer）上，
						// 恢复也必须把**每核**值写回 0 —— 否则逐核写进去的偏移会活过 Restore
						// （SetCoAll 走 PSMU 0x0B，每核写走 MP1 0x35，不是同一张表）。
						// SetCoAll(0) 的既有语义**一字未改**，这里只是在它之前多走一遍逐核 0。
						WriteAllCoresZero("restore", out _);
						smuStatus = _smu.SetCoAll(0);
					}
					else
					{
						smuStatus = _smu.DisableOc();
					}
					if (smuStatus != SmuStatus.OK)
					{
						throw new InvalidOperationException($"{text}: {smuStatus}");
					}
					if (text == "amd-co")
					{
						OwnsCurve = false;
					}
					_changed.Remove(text);
				}
				catch (Exception ex)
				{
					list.Add(ex.Message);
					Logger.WriteLine("CPU tuning restore: " + ex);
				}
			}
			return new CpuTuningResult(list.Count == 0, verified, string.Join(Environment.NewLine, list));
		}
	}

	// ==========================================================================================
	// 2026-10-05 percore-product: 每核 CO（Curve Optimizer）—— 逐核写 / 重置(每核写0) / 保存 /
	// 开机自动应用。
	//
	// 用户需求原话：「可以写进产品了 点重置是每个核心写0 然后允许开机自动应用和保存超频设置」
	//
	// 四层安全措施（代码里逐条落地，验收时逐条核对）：
	//   1. **能力检查永远先行**：每核写要求 RyzenSmuService.CurvePerCoreCommand(代号) 有值
	//      （= 该代号在 SMU 参考里有每核写命令；2026-10-10 percorenolog 起**不再是 Vermeer 白名单**）
	//      **AND** SKU 层级放行（与 amd-co 的 CanWrite 同一把锁）。
	//      不满足 -> 直接返回失败并说明原因，**一条 SMU 命令都不发**
	//      （只有 SMU 参考里没有每核写命令的代号才落到这里，如 Zen1 / Zen2 TR / Rome / Milan /
	//       Chagall / StormPeak / FireFlight / Genoa / Bergamo / Turin* / ShimadaPeak / KrackanPoint2 / Mero）。
	//   2. **本机（R7 8745H / Phoenix，SKU 层级锁掉 UV）** 的 PerCoreWrite 恒 false：
	//      UI 的每核输入框只显示实读值、按钮置灰，开机自动应用也会被能力检查拦住。
	//   3. **写后校验 + 全量回退**（见 ApplyPerCore）：写前同锁读基线 -> 逐核写（被拒重试一次）->
	//      逐核回读 -> 任一核不一致/被拒就用**写前基线**全量回写，并把结果如实写进 Detail
	//      （⛔ 不静默）；被拒/回退过的选项进 `_denied`（本会话置灰）。
	//   4. **开机自动应用默认关**：AppConfig 里没有 cpu_co_auto_apply 就是关。打开后每次启动
	//      才会真的写 SMU，所以 UI 上的警告文案必须写清"开机后会自动写入这些值"。
	//
	// 配置键（都是**按机型**的设置，已登记进 ModelScopedConfig.ExactMachineScopedKeys）：
	//   * cpu_co_core0..cpu_co_coreN —— 每核 CO 值（N = 物理核数，8 核机就是 0..7）
	//   * cpu_co_all                —— **历史键，已无 UI 输入口**（用户要求删掉"全核负压框"）。
	//                                  旧配置里残留的值仍然读得到（LoadSavedAllCore）：
	//                                  「读取」在**没有每核写**的代号上会用它走一次既有 SetCoAll；
	//                                  开机自动应用**只重放每核表**（见 ApplySavedOnStartup），
	//                                  UI 上不再有任何控件写这个键（重置会把它清 0）。
	//   * cpu_co_auto_apply         —— 开机自动应用开关，**默认关**（同时管 CO 与 FMax）。
	// 2026-10-10 fmax-persist：FMax 的两把键**同一份持久化、同一套按机型作用域**
	// （见文件上方 FMaxBaselineKey / FMaxSavedKey 那一节）：
	//   * cpu_fmax_baseline         —— 首次成功读到的 FMax 原值（「重置」写回它）；
	//   * cpu_fmax_saved            —— 「保存」存下的 FMax 值（「读取」/开机自动应用重放它）。
	// ==========================================================================================
	internal const string PerCoreAutoApplyKey = "cpu_co_auto_apply";

	internal const string PerCoreAllCoreKey = "cpu_co_all";

	private const int MaximumSavedCores = 64;

	private const int MinimumPerCoreMargin = -40;

	private const int MaximumPerCoreMargin = 0;

	private static int _autoApplyRan;

	// 2026-10-05 uifix（用户要求④「保底机制」）：本进程这一次「开机自动应用」的判定与结果。
	//   * GuardVerdict        —— 上次是否正常关机（NotEvaluated = 本进程还没判定）；
	//   * SkippedByGuard      —— 这次是不是因为"上次非正常关机"而跳过；
	//   * LastAutoApplyDetail —— 给 --cpu-co-auto-apply / UI 看的一行结论。
	internal static CpuCoShutdownVerdict GuardVerdict => CpuCoAutoApplyGuard.LastVerdict;

	internal static bool SkippedByGuard { get; private set; }

	internal static string LastAutoApplyDetail { get; private set; } = "";

	internal static string PerCoreValueKey(int coreIndex)
	{
		return "cpu_co_core" + coreIndex;
	}

	// 每核表最多记多少核（按物理核数，8 核机 -> 8；16 核 5950X -> 16）。
	internal static int PerCoreCapacity => Math.Clamp(CpuInfo.PhysicalCoreCount, 1, MaximumSavedCores);

	internal static bool PerCoreAutoApplyEnabled => AppConfig.Is(PerCoreAutoApplyKey);

	// 读回"保存过的"每核值（没保存过的核返回 null，与"保存了 0"区分开）。
	internal static IReadOnlyList<int?> LoadSavedPerCore(int coreCount)
	{
		List<int?> list = new List<int?>(coreCount);
		for (int i = 0; i < coreCount; i++)
		{
			string key = PerCoreValueKey(i);
			list.Add(AppConfig.Exists(key) ? AppConfig.Get(key, 0) : ((int?)null));
		}
		return list;
	}

	internal static int? LoadSavedAllCore()
	{
		return AppConfig.Exists(PerCoreAllCoreKey) ? AppConfig.Get(PerCoreAllCoreKey, 0) : ((int?)null);
	}

	// 保存（**只写配置文件，不碰 SMU**）。这是"保存超频设置"的落地：显式保存按钮 / 应用成功 /
	// 重置 三条路径都走它。
	internal static void SaveSettings(IReadOnlyList<int> perCore, int allCore)
	{
		for (int i = 0; i < perCore.Count && i < MaximumSavedCores; i++)
		{
			AppConfig.Set(PerCoreValueKey(i), perCore[i]);
		}
		AppConfig.Set(PerCoreAllCoreKey, allCore);
		AppConfig.Flush();
		Logger.WriteLine($"[cpu-co] saved: per-core [{string.Join(", ", perCore)}], all-core {allCore}, auto-apply {(AppConfig.Is(PerCoreAutoApplyKey) ? "ON" : "OFF")}");
	}

	internal static void SetAutoApply(bool enabled)
	{
		AppConfig.Set(PerCoreAutoApplyKey, enabled ? 1 : 0);
		AppConfig.Flush();
		Logger.WriteLine($"[cpu-co] auto-apply switch -> {(enabled ? "ON" : "OFF")} (cpu_co_auto_apply)");
	}

	// 每核写 0（"重置"按钮 + Restore 共用）。返回是否真的写了以及哪些核失败。
	private bool WriteAllCoresZero(string reason, out List<int> failedCores)
	{
		failedCores = new List<int>();
		if (!EnsurePerCoreWrite(out _, out string why))
		{
			Logger.WriteLine($"[cpu-co] per-core zero ({reason}) skipped: {why}; no command sent");
			return false;
		}
		int coreCount = PerCoreCapacity;
		for (int core = 0; core < coreCount; core++)
		{
			SmuStatus status = _smu!.SetCoPerCore(core, 0);
			Logger.WriteLine($"[cpu-co] per-core zero ({reason}) core{core} = 0 CO -> {status} (0x{(uint)status:X2}) arg0=0x{RyzenSmuService.EncodePerCoreCurveArg(core, 0):X8}");
			if (status != SmuStatus.OK)
			{
				failedCores.Add(core);
			}
		}
		return true;
	}

	private bool EnsurePerCoreWrite(out CpuTuningOption option, out string reason)
	{
		option = null!;
		CpuTuningOption? candidate = _probe?.Options.FirstOrDefault((CpuTuningOption o) => o.Id == "amd-co");
		if (candidate == null)
		{
			reason = "the amd-co option is absent from the last probe (no CO route for this codename)";
			return false;
		}
		// 2026-10-22（蛟龙 AMD：逐核降压点应用直接崩）：这里原来只取到 candidate 却没赋给 out option，
		// 调用方随后读 option.Minimum/Maximum 就 NullReferenceException —— SMU 命令一条都没发出去。
		option = candidate;
		if (_smu == null || !_smu.IsInitialized)
		{
			reason = "the SMU service is not initialized";
			return false;
		}
		if (!candidate.CanWrite)
		{
			reason = "amd-co is not writable (SKU tier / probe rejection)";
			return false;
		}
		if (!candidate.PerCoreWrite)
		{
			// 2026-10-10 percorenolog：只可能因为**两条之一**——(a) 该代号在 SMU 参考里没有每核写命令，
			// 或 (b) SKU 层级没放行。⛔ 不再是"未实测/未批准"的意思（那个分档已按用户要求删除）。
			reason = RyzenSmuService.SupportsPerCoreCurve(_smu.CpuCodeName)
				? "amd-co is not writable on this SKU (per-core CO route locked by the SKU tier)"
				: "this codename has no per-core CO write command in the SMU reference";
			return false;
		}
		reason = "";
		return true;
	}

	private List<int?> ReadPerCore(int coreCount)
	{
		List<int?> list = new List<int?>(coreCount);
		for (int core = 0; core < coreCount; core++)
		{
			SmuStatus status = _smu!.ReadCurveMargin(core, out uint[] response);
			list.Add((status == SmuStatus.OK) ? RyzenSmuService.DecodeCurveMargin(response[0]) : ((int?)null));
		}
		return list;
	}

	private static string Describe(List<int?> values)
	{
		return "[" + string.Join(", ", values.Select((int? v) => v.HasValue ? v.Value.ToString() : "?")) + "]";
	}

	// 逐核写（"应用每核"按钮 / 开机自动应用共用）。**只发每核写命令**，不动全核 SetCoAll。
	//
	// ==========================================================================================
	// 2026-10-10 percorenolog（用户要求「安全网保留：写后逐核回读校验 · 失败重试一次 ·
	//   不一致则用【写前基线】全量回写 · 被拒选项记入 `_denied` · 失败不静默」）：
	//
	// 安全网接线（四步，全部在发命令**之前**就已经有基线）：
	//   ① 写前同锁读基线（`ReadPerCore`）—— 与写在同一把 `SyncRoot` 锁里，中间不会被别的路径改；
	//   ② 逐核写 + 对**被拒的核重试一次**（SMU 忙/超时的瞬时拒绝不至于直接判失败）；
	//   ③ 写后逐核回读校验：要求每个核都读回 `请求值`；
	//   ④ 只要"有核被拒"或"有核回读对不上"，就用**写前基线**把全部核写回去（每个核同样重试一次），
	//      并把结果如实写进返回值（`Success: false` + 明写"已按写前基线回写"及其成败）—— ⛔ 不静默。
	//
	// ⚠️ 两个例外，都**照实说**，绝不假装已校验：
	//   * 该代号没有每核读命令（Matisse）-> 基线读不到、写后也读不回：写**照发**
	//     （用户"只要支持就开"），但 `Verified: false` + 明写"无法回读校验"；
	//   * 基线读到了但在允许范围外（例如基线 -50 而 UI 只允许 -40..0）-> 回读一致仍然给
	//     `Verified: true`（**值确实写进去了**），但在 Detail 里明写"基线超出可写范围（-40..0）、
	//     回写路径不会用这个值"，避免用户误以为点一下就能恢复到一个写不回去的状态。
	// ==========================================================================================
	internal CpuTuningResult ApplyPerCore(IReadOnlyList<int> margins)
	{
		lock (SyncRoot)
		{
			if (IntelCpuTuningDisabled && CpuVendor.IsIntel)
			{
				return new CpuTuningResult(Success: false, Verified: false, "Intel CPU tuning is disabled (XTU owns MSR 0x150); no command sent.");
			}
			if (!EnsurePerCoreWrite(out CpuTuningOption option, out string reason))
			{
				return new CpuTuningResult(Success: false, Verified: false, "per-core CO write refused: " + reason);
			}
			if (margins.Count == 0 || margins.Count > MaximumSavedCores)
			{
				return new CpuTuningResult(Success: false, Verified: false, $"per-core count out of range (1..{MaximumSavedCores}).");
			}
			foreach (int margin in margins)
			{
				if (margin < option.Minimum || margin > option.Maximum || margin < MinimumPerCoreMargin || margin > MaximumPerCoreMargin)
				{
					return new CpuTuningResult(Success: false, Verified: false, $"CO value {margin} is outside the permitted range.");
				}
			}
			int coreCount = margins.Count;
			// ① 写前基线（同锁、只读、每核一条）。null 元素 = 该核读不到（没有读命令 / 读失败）。
			List<int?> baseline = ReadPerCore(coreCount);
			bool baselineComplete = baseline.All((int? v) => v.HasValue);
			bool readbackAvailable = RyzenSmuService.SupportsPerCoreReadback(_smu!.CpuCodeName);
			Stopwatch stopwatch = Stopwatch.StartNew();
			List<int> failed = new List<int>();
			int accepted = 0;
			for (int core = 0; core < coreCount; core++)
			{
				SmuStatus status = _smu.SetCoPerCore(core, margins[core]);
				Logger.WriteLine($"[cpu-co] per-core write core{core} = {margins[core]} CO -> {status} (0x{(uint)status:X2}) arg0=0x{RyzenSmuService.EncodePerCoreCurveArg(core, margins[core]):X8}");
				if (status != SmuStatus.OK)
				{
					// ② 瞬时拒绝重试一次（同值、同核、同命令；参数与打包一字未改）。
					SmuStatus retry = _smu.SetCoPerCore(core, margins[core]);
					Logger.WriteLine($"[cpu-co] per-core write core{core} retry (once) -> {retry} (0x{(uint)retry:X2})");
					status = retry;
				}
				if (status == SmuStatus.OK)
				{
					accepted++;
				}
				else
				{
					failed.Add(core);
				}
			}
			stopwatch.Stop();
			// 只要有一条命令被 SMU 接受就记进 _changed（恢复按钮要能出来），并在至少一条被接受时
			// 接管曲线的所有权 —— 与手动 Apply("amd-co") 的保护一致，防止模式控制事后覆盖。
			_changed.Add("amd-co");
			if (accepted > 0)
			{
				OwnsCurve = true;
			}
			// ③ 写后回读校验（只读，每核一条）。⚠️ 读不回来（不能读/读失败）时 verified 必须为 false。
			List<int?> readback = ReadPerCore(coreCount);
			bool verified = false;
			string note;
			if (readbackAvailable && baselineComplete)
			{
				verified = readback.All((int? v) => v.HasValue);
				if (verified)
				{
					for (int i = 0; i < coreCount; i++)
					{
						if (readback[i]!.Value != margins[i])
						{
							verified = false;
							break;
						}
					}
				}
				note = verified
					? "readback matches the requested values on every core"
					: "readback does not match the requested values";
			}
			else
			{
				note = "read-back verification unavailable on " + _smu.CpuCodeName
					+ " (no per-core read command), so the write cannot be confirmed";
			}
			// ④ 不一致 / 有核被拒 -> 用**写前基线**全量回写（每个核同样重试一次）。
			bool rolledBack = false;
			bool rollbackOk = false;
			bool rollbackIncomplete = false;
			if (note.StartsWith("readback does not match", StringComparison.Ordinal)
				|| note.StartsWith("read-back verification unavailable", StringComparison.Ordinal)
				|| failed.Count > 0)
			{
				if (!baselineComplete)
				{
					rollbackIncomplete = true;
					Logger.WriteLine("[cpu-co] rollback skipped: the pre-write baseline could not be read on every core"
						+ $" (baseline {Describe(baseline)}); no value guessed");
				}
				else
				{
					rolledBack = true;
					rollbackOk = true;
					for (int core = 0; core < coreCount; core++)
					{
						int target = baseline[core]!.Value;
						SmuStatus status = _smu.SetCoPerCore(core, target);
						if (status != SmuStatus.OK)
						{
							SmuStatus retry = _smu.SetCoPerCore(core, target);
							Logger.WriteLine($"[cpu-co] rollback core{core} -> {target} CO retry (once) -> {retry} (0x{(uint)retry:X2})");
							status = retry;
						}
						Logger.WriteLine($"[cpu-co] rollback core{core} -> {target} CO (pre-write baseline): {status} (0x{(uint)status:X2})");
						if (status != SmuStatus.OK)
						{
							rollbackOk = false;
						}
					}
					// 回写之后再读一次：能读的代号要求读回值与基线逐核一致；读不回来只能如实记"无法确认"。
					if (readbackAvailable)
					{
						List<int?> afterRollback = ReadPerCore(coreCount);
						for (int i = 0; i < coreCount; i++)
						{
							if (!afterRollback[i].HasValue || afterRollback[i]!.Value != baseline[i]!.Value)
							{
								rollbackOk = false;
								break;
							}
						}
						Logger.WriteLine($"[cpu-co] rollback readback {Describe(afterRollback)} (baseline {Describe(baseline)})");
					}
				}
			}
			string detail = $"per-core CO: requested {Describe(margins.Select((int v) => (int?)v).ToList())}, "
				+ $"pre-write baseline {Describe(baseline)}, readback {Describe(readback)}, "
				+ $"SMU accepted {accepted}/{coreCount}"
				+ ((failed.Count == 0) ? "" : $", rejected cores [{string.Join(", ", failed)}]")
				+ "; " + note
				+ (rollbackIncomplete
					? "; NOT rolled back: the pre-write baseline is incomplete (no value guessed) - the CPU may keep a partial change"
					: (rolledBack
						? $"; rolled back every core to the pre-write baseline: {(rollbackOk ? "confirmed" : "NOT confirmed (a write or a readback failed)")}"
						: ""))
				+ $", {stopwatch.ElapsedMilliseconds} ms";
			Logger.WriteLine("[cpu-co] " + detail);
			if (failed.Count > 0 || rolledBack)
			{
				// 有核被拒 / 已回退 = 本次写入没有落地：把 amd-co 记进 `_denied`
				// （用户要求：被拒选项进 `_denied`、本会话置灰；恢复仍走「重置」而非本选项的 Apply）。
				_denied.Add("amd-co");
			}
			// Success 只在"没有被拒、且（能校验时）回读逐核一致"时为真 —— 回退过的写入不算成功。
			return new CpuTuningResult(failed.Count == 0 && verified, verified, detail);
		}
	}

	// "重置"按钮 = **先把每个核心写 0，再走一次全核 SetCoAll(0) 兜底**（用户明确要求）。
	// 另外把保存的每核值一并清零 —— 否则开着"开机自动应用"的机器下次启动会把刚清掉的偏移再写回来。
	internal CpuTuningResult ResetAllCores()
	{
		lock (SyncRoot)
		{
			if (!EnsurePerCoreWrite(out _, out string reason))
			{
				return new CpuTuningResult(Success: false, Verified: false, "reset refused: " + reason);
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			// 2026-10-10 percorenolog（安全网）：写前先记下每核基线 —— 这一次动作本身"就是要写 0"，
			// 所以**不为写 0 回退**（写 0 是用户明确要求的终点）；基线只用于两件事：
			//   ① 读不回来时（Matisse）如实把"基线未知"写进 Detail，不假装 verified；
			//   ② 失败不清保存值（见下），避免"硬件没清掉、配置里的每核值也被抹掉"这种不可逆的组合。
			List<int?> baseline = ReadPerCore(PerCoreCapacity);
			WriteAllCoresZero("reset", out List<int> failed);
			// 全核兜底：既有 SetCoAll(0) 路径（语义未改），只在这一步调用。
			SmuStatus allCoreStatus = _smu!.SetCoAll(0);
			Logger.WriteLine($"[cpu-co] reset all-core fallback SetCoAll(0) -> {allCoreStatus} (0x{(uint)allCoreStatus:X2})");
			int coreCount = PerCoreCapacity;
			bool readbackAvailable = RyzenSmuService.SupportsPerCoreReadback(_smu.CpuCodeName);
			List<int?> readback = ReadPerCore(coreCount);
			bool verified = readbackAvailable && readback.TrueForAll((int? v) => v.HasValue && v.Value == 0);
			bool ok = failed.Count == 0 && allCoreStatus == SmuStatus.OK;
			if (ok)
			{
				OwnsCurve = false;
				_changed.Remove("amd-co");
			}
			// 保存的每核值同步清零（清的是"保存的超频设置"，不碰开机自动应用开关本身）。
			// 2026-10-10 percorenolog：只在本次重置**成功**时清 —— 失败还清掉保存值会让用户连重试
			// 的输入都没有（失败不静默，见下面的 Detail）。
			if (ok)
			{
				for (int core = 0; core < coreCount; core++)
				{
					AppConfig.Set(PerCoreValueKey(core), 0);
				}
				AppConfig.Set(PerCoreAllCoreKey, 0);
				AppConfig.Flush();
			}
			stopwatch.Stop();
			string rejected = (failed.Count == 0) ? "" : $", rejected cores [{string.Join(", ", failed)}]";
			string detail = $"reset: per-core 0 written ({coreCount - failed.Count}/{coreCount} accepted{rejected}), "
				+ $"all-core SetCoAll(0) -> {allCoreStatus}, pre-write baseline {Describe(baseline)}, readback {Describe(readback)}"
				+ (readbackAvailable ? "" : " (no per-core read command -> cannot verify)")
				+ (ok ? ", saved per-core values cleared" : ", saved per-core values KEPT (reset did not complete)")
				+ $", {stopwatch.ElapsedMilliseconds} ms";
			Logger.WriteLine("[cpu-co] " + detail);
			return new CpuTuningResult(ok, verified, detail);
		}
	}

	// ==========================================================================================
	// 2026-10-05 uifix（用户要求①）：底部那一排只留「检测能力 / 应用 / 重置」，
	// 「恢复」改名「重置」，行为 = 旧「重置（每核写 0）」+ 旧「恢复」的合并：
	//   * CO    ：能走每核写就复用旧的 ResetAllCores（逐核写 0 + 全核 SetCoAll(0) 兜底 + 清空保存值，
	//             实现与日志格式**一字未改**）；该代号没有每核写路径时，退回旧 Restore 的 amd-co 分支
	//             （只发一次 SetCoAll(0)）；
	//   * 固定频率：只有本轮真的应用过 amd-clock 才 DisableOc（旧 Restore 的 amd-clock 分支）。
	//   * FMax（2026-10-10 fmax-persist，用户决定③）：能写 FMax 时把 **cpu_fmax_baseline**（原值）
	//     写回去；没有记录到原值就一条命令都不发并明说"没有原值可恢复"。
	// ⚠️ SetCoAll / DisableOc / WriteAllCoresZero / DisableOc 的语义未改，这里只是把它们编排到一起。
	// ⚠️ 这个合并**不引入**任何新的写命令，也不放宽任何能力检查。
	// ==========================================================================================
	internal CpuTuningResult ResetAll()
	{
		lock (SyncRoot)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			List<string> notes = new List<string>();
			bool success = true;
			bool verified = true;
			// 每核写路径是否可用（该代号有每核写命令 + SKU 放行）。不可用时退回旧 Restore 的全核分支。
			bool perCoreReady = EnsurePerCoreWrite(out _, out string perCoreWhy);
			if (perCoreReady)
			{
				// 复用旧「重置」：逐核写 0 + 全核兜底 + 清空保存的每核值。
				CpuTuningResult co = ResetAllCores();
				success = co.Success;
				verified = co.Verified;
				notes.Add(co.Detail);
			}
			else if (_changed.Contains("amd-co") && _smu != null)
			{
				// 没有每核写路径：只发一次既有 SetCoAll(0)（旧 Restore 的 amd-co 分支，语义未改）。
				SmuStatus allCoreStatus = _smu.SetCoAll(0);
				Logger.WriteLine($"[cpu-co] reset all-core SetCoAll(0) -> {allCoreStatus} (0x{(uint)allCoreStatus:X2})"
					+ $" (per-core write path unavailable: {perCoreWhy})");
				verified = false;
				if (allCoreStatus == SmuStatus.OK)
				{
					OwnsCurve = false;
					_changed.Remove("amd-co");
				}
				else
				{
					success = false;
				}
				notes.Add($"all-core SetCoAll(0) -> {allCoreStatus} (per-core write unavailable: {perCoreWhy})");
			}
			else
			{
				// 本轮窗口里没碰过 CO：一条 CO 命令都不发（"重置"不该动它没动过的东西）。
				verified = false;
				notes.Add("CO untouched in this window: no CO command sent"
					+ (perCoreReady ? "" : $" (per-core write unavailable: {perCoreWhy})"));
			}
			// 固定频率：只有本轮应用过才退出（旧 Restore 的 amd-clock 分支，语义未改）。
			if (_changed.Contains("amd-clock") && _smu != null)
			{
				SmuStatus clockStatus = _smu.DisableOc();
				Logger.WriteLine($"[cpu-co] reset DisableOc() -> {clockStatus} (0x{(uint)clockStatus:X2})");
				if (clockStatus == SmuStatus.OK)
				{
					_changed.Remove("amd-clock");
				}
				else
				{
					success = false;
				}
				notes.Add($"fixed/limited clock exited (DisableOc -> {clockStatus})");
			}
			// ==================================================================================
			// 2026-10-10 intel（⑦B）：Intel 的恢复（「重置」按钮在 Intel 上**唯一**的落点）。
			//
			// 上面 CO / 固定频率两段都带 `_changed.Contains("amd-…")` 前提，Intel 上恒为假 ⇒ 一条
			// SMU 命令都不会发；下面 FMax 那段也被 `_fmax == null`（非 AMD 恒 null）挡住。
			// 所以 Intel 的恢复必须**自己一段**（与 Restore() 的 Intel 分支同一套语义、同一份基线）：
			//   * 取 `_changed` 里 `intel-*` 的条目，映射到 domain（core=0 / cache=2，倍频也在 domain 0），
			//     每个 domain 只写回一次（`intel-*` 与 `intel-cache` 同理）；
			//   * 写回的是 **IntelOcMailbox 探针第一次成功读到的基线**（Probe 时 TryAdd，不是猜的默认值），
			//     并且同样走 WriteVerified（回读一致才算成功）—— 所以这里 `verified` 保持为真；
			//   * 成功一个移除一个；失败把原因写进 notes 并把整次「重置」判为失败（绝不伪报成功）；
			//   * 本会话根本没动过 Intel 选项（`_changed` 里没有 intel-*）-> 一条命令都不发（"重置"不该
			//     动它没动过的东西），此时不对 `verified` 下任何新结论。
			// ==================================================================================
			string[] intelIds = _changed.Where((string id) => id.StartsWith("intel-", StringComparison.Ordinal)).ToArray();
			if (intelIds.Length > 0)
			{
				if (_intel == null)
				{
					success = false;
					verified = false;
					notes.Add("Intel tuning reset failed: the OC mailbox is not open, so the captured baseline is unavailable (no command sent)");
				}
				else
				{
					HashSet<string> restoredDomains = new HashSet<string>();
					foreach (string intelId in intelIds)
					{
						string domainName = ((intelId == "intel-cache") ? "cache" : "core");
						try
						{
							if (restoredDomains.Add(domainName))
							{
								_intel.Restore((domainName == "cache") ? 2 : 0);
								Logger.WriteLine($"[cpu-intel] reset: domain {(domainName == "cache" ? 2 : 0)} ({domainName}) restored to the captured baseline (read-back verified)");
							}
							_changed.Remove(intelId);
							notes.Add($"Intel {intelId} -> restored to the captured baseline (domain {domainName}, read-back verified)");
						}
						catch (Exception ex)
						{
							success = false;
							verified = false;
							notes.Add($"Intel {intelId} restore failed: {ex.Message}");
							Logger.WriteLine($"[cpu-intel] reset failed for {intelId}: {ex}");
						}
					}
				}
			}
			// ==================================================================================
			// 2026-10-10 fmax-persist（用户决定③「点重置都是恢复默认」）：FMax 也恢复为
			// **记录到的原值**（cpu_fmax_baseline）。四档，每一档都如实写进结论：
			//   * 本机不能写 FMax（ReadOnly / None / 还没探到）-> 一条命令都不发；
			//   * 有原值且当前回读与之不同                     -> 走既有 ApplyFMax（写 + 回读校验 +
			//     固件状态码），成功后清掉"本进程写过 FMax"的标记；
			//   * 有原值且已经等于原值                         -> 一条命令都不发（没什么要恢复的）；
			//   * **没有原值**                                 -> 一条命令都不发，照实说"没有原值可恢复"
			//     （绝不猜一个值写进去 —— 这正是用户要求"没有基线就明说"的那一条）。
			// ⚠️ 恢复走的是 ApplyFMax（与手动「应用」**同一条**写入路径），不新增任何 SMU 命令。
			// ==================================================================================
			CpuFMaxProbe? fmaxProbe = _fmax;
			if (fmaxProbe != null && IsFMaxWritable(fmaxProbe.Capability))
			{
				int? fmaxBaseline = FMaxBaselineValue;
				if (!fmaxBaseline.HasValue)
				{
					verified = false;
					notes.Add($"FMax left unchanged: no baseline recorded ({FMaxBaselineKey} is absent);"
						+ " no value guessed and no command sent (the FMax row shows the baseline as unknown)");
				}
				else if (fmaxProbe.Current.HasValue && (int)fmaxProbe.Current.Value == fmaxBaseline.Value)
				{
					_fmaxApplied = false;
					notes.Add($"FMax already at the recorded baseline {fmaxBaseline.Value} MHz; no command sent");
				}
				else
				{
					CpuTuningResult fmaxResult = ApplyFMax(fmaxBaseline.Value);
					success = success && fmaxResult.Success;
					verified = verified && fmaxResult.Verified;
					if (fmaxResult.Success)
					{
						_fmaxApplied = false;
					}
					notes.Add($"FMax restored to the recorded baseline {fmaxBaseline.Value} MHz -> "
						+ $"success={fmaxResult.Success} verified={fmaxResult.Verified}: {fmaxResult.Detail}");
				}
			}
			else
			{
				notes.Add("FMax untouched: this codename has no FMax write path (read-only or unsupported)");
			}
			stopwatch.Stop();
			string detail = "reset: " + string.Join("; ", notes) + $", {stopwatch.ElapsedMilliseconds} ms";
			Logger.WriteLine("[cpu-co] " + detail);
			return new CpuTuningResult(success, verified, detail);
		}
	}

	// ------------------------------------------------------------------------------------------
	// 开机自动应用（默认关）。要求：
	//   * 只在开关为真时动 SMU；写前必须过能力检查（CanWrite + 代号白名单 PerCoreWrite）；
	//   * 不满足就**跳过并记日志**，绝不弹窗（开机流程里不能炸用户）；
	//   * 记录应用了什么值、成功/失败、耗时；
	//   * 每个进程只跑一次（_autoApplyRan），重启一次才重放一次。
	// **只重放每核表**：cpu_co_all 只做持久化/回显。理由：每核表是逐核精确值，若再叠一次全核写
	// （不同 SMU 表），全核写会把逐核值盖掉，用户按"逐核"保存的意图就被静默否定。
	//
	// 2026-10-10 fmax-persist（用户决定①「开机自动应用可以自动应用」）：保存过 FMax
	// （cpu_fmax_saved，来自「保存」按钮）时，**同一个开关、同一次流程**里也把 FMax 写回去。
	// 没保存过 FMax 的机器上本函数的文本/返回值与改前逐字一致。
	//
	// simulate = true（`--cpu-co-auto-apply` 不带 --yes）：开关、能力、要写的值全都照常检查并打印，
	// 但**在第一条写命令之前停下**（日志里写明 "SIMULATE ... nothing written"）。
	//
	// 2026-10-05 uifix（用户要求④）：真正动 SMU 之前先过 `CpuCoAutoApplyGuard`（上次是否正常关机）。
	// 判定为"上次非正常关机"时直接跳过（一条命令都不发）并提示；首次运行/正常关机照常往下走。
	// ------------------------------------------------------------------------------------------
	internal static CpuTuningResult ApplySavedOnStartup(bool simulate = false)
	{
		if (Interlocked.CompareExchange(ref _autoApplyRan, 1, 0) != 0)
		{
			return new CpuTuningResult(Success: false, Verified: false, "auto-apply already ran in this process.");
		}
		return Instance.AutoApplySaved(simulate);
	}

	private CpuTuningResult AutoApplySaved(bool simulate)
	{
		lock (SyncRoot)
		{
			// ----------------------------------------------------------------------------------
			// 2026-10-05 uifix（用户要求④）：**执行前**先判断上次是不是正常关机。
			//   * 这一步只读/写配置，**完全不碰 SMU**（连 PawnIO 都不打开）；
			//   * 每次开机只判定一次（CpuCoAutoApplyGuard 内部按开机身份缓存）；
			//   * 判定为"上次非正常关机"时**跳过自动应用**（一条写命令都不发），
			//     并写日志 + 一次轻量提示；正常/首次运行则照常走下面的能力检查。
			// ----------------------------------------------------------------------------------
			CpuCoShutdownVerdict guard = CpuCoAutoApplyGuard.EvaluateForThisBoot();
			bool switchOn = AppConfig.Is(PerCoreAutoApplyKey);
			Logger.WriteLine($"[cpu-co] startup auto-apply: shutdown guard = {CpuCoAutoApplyGuard.Name(guard)} ({CpuCoAutoApplyGuard.Describe(guard)})");
			if (guard == CpuCoShutdownVerdict.Abnormal)
			{
				if (!switchOn)
				{
					// 开关本来就是关的：没有任何东西会被写，不打扰用户，只留一行日志。
					LastAutoApplyDetail = "auto-apply is off (unclean last shutdown noted, nothing would have been written anyway)";
					Logger.WriteLine("[cpu-co] startup auto-apply: OFF (cpu_co_auto_apply); nothing written (unclean shutdown noted, no action needed)");
					return new CpuTuningResult(Success: false, Verified: false, "auto-apply is off.");
				}
				SkippedByGuard = true;
				// 上次非正常关机 ⇒ 本次跳过，并自动关掉开关，避免“不稳定值→重启→再自动应用”的循环。
				AppConfig.Set(PerCoreAutoApplyKey, 0);
				AppConfig.Flush();
				LastAutoApplyDetail = "auto-apply skipped by the shutdown guard and the switch was disabled: the clean-exit marker was missing (last shutdown was not clean)";
				Logger.WriteError("[cpu-co] startup auto-apply SKIPPED: previous shutdown was not clean (crash / power loss / forced kill);"
					+ " the saved per-core CO values and the saved FMax were NOT written this boot, so a possibly unstable setting is not re-applied;"
					+ " cpu_co_auto_apply was turned OFF to prevent a reboot loop.");
				NotifyGuardSkip();
				return new CpuTuningResult(Success: false, Verified: false,
					"auto-apply skipped: previous shutdown was not clean (no write command sent).");
			}
			if (!switchOn)
			{
				LastAutoApplyDetail = "auto-apply is off (cpu_co_auto_apply)";
				Logger.WriteLine("[cpu-co] startup auto-apply: OFF (cpu_co_auto_apply); nothing written");
				return new CpuTuningResult(Success: false, Verified: false, "auto-apply is off.");
			}
			// Intel 的开机自动应用只有 AMD 的每核 CO / FMax 两类，Intel 没有对应项。
			// 这里提前返回，避免开机就加载 PawnIO IntelMSR 模块、占用 MSR 0x150 OC 邮箱，
			// 否则 Intel XTU 启动时会被 LM 抢先占用而打不开。
			if (CpuVendor.IsIntel)
			{
				LastAutoApplyDetail = "Intel CPU: per-core CO/FMax auto-apply is AMD-only; skipped before opening PawnIO/IntelMSR.";
				Logger.WriteLine("[cpu-co] startup auto-apply: Intel CPU ⇒ 跳过（不加载 PawnIO IntelMSR，避免与 Intel XTU 冲突）");
				return new CpuTuningResult(Success: false, Verified: false, "Intel CPU: auto-apply not applicable; PawnIO/IntelMSR not opened.");
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			Logger.WriteLine($"[cpu-co] startup auto-apply: ON{(simulate ? " (SIMULATE, --yes not given)" : "")}; checking capability before touching the SMU");
			Logger.WriteLine("[cpu-co] startup auto-apply: clean startup, proceeding with the normal capability check");
			int coreCount = PerCoreCapacity;
			// 2026-10-10 fmax-persist：保存过的 FMax 值（**只读配置，不碰 SMU**）。
			// ⚠️ 没有保存过 FMax 的机器 -> 下面的路径与改前**逐字一致**（LastAutoApplyDetail / 日志 /
			//    返回值都由 CO 那一份决定），所以既有验收（--cpu-co-auto-apply）看到的文本不变。
			int? savedFMax = LoadSavedFMax();
			try
			{
				// 能力检查：先探一次（这一步同时完成 PawnIO + RyzenSMU 初始化）。
				Probe();
				CpuTuningResult coResult = AutoApplyPerCoreCo(simulate, coreCount, stopwatch, out string coSummary, out bool coAttempted);
				if (!savedFMax.HasValue)
				{
					LastAutoApplyDetail = coSummary;
					Logger.WriteLine($"[cpu-co] startup auto-apply finished: success={coResult.Success} verified={coResult.Verified} elapsed={stopwatch.ElapsedMilliseconds} ms detail={coResult.Detail}");
					return coResult;
				}
				// FMax 那一步（只有"保存过 FMax"的机器才走）。
				CpuTuningResult fmaxResult = AutoApplyFMax(savedFMax.Value, simulate, stopwatch, out string fmaxSummary, out bool fmaxAttempted);
				stopwatch.Stop();
				bool attempted = coAttempted || fmaxAttempted;
				bool success = attempted
					&& (!coAttempted || coResult.Success)
					&& (!fmaxAttempted || fmaxResult.Success);
				bool verified = attempted
					&& (!coAttempted || coResult.Verified)
					&& (!fmaxAttempted || fmaxResult.Verified);
				LastAutoApplyDetail = coSummary + "; " + fmaxSummary;
				string detail = coResult.Detail + " || " + fmaxResult.Detail;
				Logger.WriteLine($"[cpu-co] startup auto-apply finished: success={success} verified={verified} elapsed={stopwatch.ElapsedMilliseconds} ms detail={detail}");
				return new CpuTuningResult(success, verified, detail);
			}
			catch (Exception ex)
			{
				// 开机流程里绝不让异常冒出去（也绝不弹窗）。
				stopwatch.Stop();
				LastAutoApplyDetail = "auto-apply failed: " + ex.Message;
				Logger.WriteLine($"[cpu-co] startup auto-apply failed after {stopwatch.ElapsedMilliseconds} ms: {ex}");
				return new CpuTuningResult(Success: false, Verified: false, ex.Message);
			}
		}
	}

	// ------------------------------------------------------------------------------------------
	// 2026-10-10 fmax-persist：把改前 AutoApplySaved 里那一段**每核 CO** 原样抽出来
	// （语义、文案、日志格式**一字未改**，只是不再用 return 提前结束整个开机流程，
	//  好让 FMax 那一步能跟在它后面）。
	//   * coAttempted —— 真的打算写（能力通过 + 有保存值 + 非 simulate）；
	//   * summary     —— 与改前 LastAutoApplyDetail 完全相同的字符串。
	// ------------------------------------------------------------------------------------------
	private CpuTuningResult AutoApplyPerCoreCo(bool simulate, int coreCount, Stopwatch stopwatch, out string summary, out bool coAttempted)
	{
		coAttempted = false;
		if (!EnsurePerCoreWrite(out _, out string reason))
		{
			// 能力不满足：跳过，**一条命令都不发**，只记日志（不弹窗）。
			summary = "auto-apply skipped: " + reason;
			Logger.WriteLine($"[cpu-co] startup auto-apply skipped: {reason}; no write command sent ({stopwatch.ElapsedMilliseconds} ms)");
			return new CpuTuningResult(Success: false, Verified: false, "auto-apply skipped: " + reason);
		}
		IReadOnlyList<int?> saved = LoadSavedPerCore(coreCount);
		List<int> values = new List<int>(coreCount);
		foreach (int? value in saved)
		{
			values.Add(value ?? 0);
		}
		if (saved.All((int? v) => !v.HasValue))
		{
			summary = "auto-apply skipped: no saved per-core values";
			Logger.WriteLine("[cpu-co] startup auto-apply skipped: no saved per-core values (cpu_co_core0.." + (coreCount - 1) + " are absent); nothing written");
			return new CpuTuningResult(Success: false, Verified: false, "auto-apply skipped: no saved per-core values.");
		}
		if (simulate)
		{
			// 检查全部通过，但**不写**：这正是"不带 --yes 只打印计划"的语义。
			summary = $"simulate: capability OK; would write per-core CO [{string.Join(", ", values)}]; nothing written";
			Logger.WriteLine($"[cpu-co] auto-apply SIMULATE: capability OK; would write per-core CO [{string.Join(", ", values)}]; nothing written ({stopwatch.ElapsedMilliseconds} ms)");
			return new CpuTuningResult(Success: false, Verified: false, summary);
		}
		Logger.WriteLine($"[cpu-co] startup auto-apply writing per-core CO (missing entries treated as 0): [{string.Join(", ", values)}]");
		coAttempted = true;
		CpuTuningResult result = ApplyPerCore(values);
		summary = $"applied per-core CO [{string.Join(", ", values)}]: success={result.Success} verified={result.Verified}";
		Logger.WriteLine($"[cpu-co] startup auto-apply: per-core CO -> success={result.Success} verified={result.Verified} detail={result.Detail}");
		return result;
	}

	// ------------------------------------------------------------------------------------------
	// 2026-10-10 fmax-persist（用户决定①「开机自动应用可以自动应用」+ ②「保存/读取」）：
	// 开机时重放**保存过的** FMax。守卫与 CO 那一步**完全同级**，一条都不放宽：
	//   * 开关 cpu_co_auto_apply（与 CO 共用同一个开关）必须为开 —— 在 AutoApplySaved 开头已经查过；
	//   * 关机守卫（上次非正常关机）在 AutoApplySaved 开头已经拦下整段流程，FMax 一起被跳过；
	//   * `_fmax` 必须存在且能力含写路径（ReadWrite / WriteNoReadback）：
	//     None / ReadOnly 的机器**一条命令都不发**，只记日志（UI 上写入按钮也是灰的）；
	//   * simulate（`--cpu-co-auto-apply` 不带 --yes）只打印计划，绝不写；
	//   * 回读值与保存值**已经相同**时也不写（没有需要改变的 SMU 状态）。
	//   * fmaxAttempted —— 真的打算写（能力通过 + 非 simulate + 回读值不同）。
	// ------------------------------------------------------------------------------------------
	private CpuTuningResult AutoApplyFMax(int savedFMax, bool simulate, Stopwatch stopwatch, out string summary, out bool fmaxAttempted)
	{
		fmaxAttempted = false;
		CpuFMaxProbe? fmax = _fmax;
		if (fmax == null)
		{
			summary = "FMax skipped: no FMax capability result (probe did not reach the SMU)";
			Logger.WriteLine("[cpu-fmax] startup auto-apply skipped: no FMax capability result; no command sent");
			return new CpuTuningResult(Success: false, Verified: false, "FMax auto-apply skipped: no capability result.");
		}
		if (!IsFMaxWritable(fmax.Capability))
		{
			summary = $"FMax skipped: capability {fmax.Capability} has no write path (read-only or unsupported)";
			Logger.WriteLine($"[cpu-fmax] startup auto-apply skipped: capability {fmax.Capability} on this codename; no write command sent");
			return new CpuTuningResult(Success: false, Verified: false, $"FMax auto-apply skipped: capability {fmax.Capability}.");
		}
		if (fmax.Current.HasValue && (int)fmax.Current.Value == savedFMax)
		{
			summary = $"FMax already at the saved value {savedFMax} MHz; no command sent";
			Logger.WriteLine($"[cpu-fmax] startup auto-apply: saved FMax {savedFMax} MHz already matches the read-back; no command sent");
			return new CpuTuningResult(Success: false, Verified: false, summary);
		}
		if (simulate)
		{
			summary = $"simulate: FMax capability {fmax.Capability} OK; would write FMax {savedFMax} MHz; nothing written";
			Logger.WriteLine($"[cpu-fmax] auto-apply SIMULATE: capability {fmax.Capability} OK; would write FMax {savedFMax} MHz; nothing written ({stopwatch.ElapsedMilliseconds} ms)");
			return new CpuTuningResult(Success: false, Verified: false, summary);
		}
		Logger.WriteLine($"[cpu-fmax] startup auto-apply writing FMax {savedFMax} MHz (saved value, cpu_fmax_saved)");
		fmaxAttempted = true;
		CpuTuningResult result = ApplyFMax(savedFMax);
		summary = $"applied FMax {savedFMax} MHz: success={result.Success} verified={result.Verified}";
		Logger.WriteLine($"[cpu-fmax] startup auto-apply: FMax -> success={result.Success} verified={result.Verified} detail={result.Detail}");
		return result;
	}

	// 2026-10-05 uifix（用户要求④）：判定为"上次非正常关机"时的**轻量提示**。
	// 沿用本仓库既有的 Toast（与 GpuRestartVerifier 的开机提示同一套），只在
	// **开关为开、而且真的跳过了**这一次才提示 —— 一次开机最多一条，绝不弹窗轰炸。
	// 日志是始终写的那一份（见 AutoApplySaved）；CLI 里 Program.toast 为 null，这里自然静默。
	private static void NotifyGuardSkip()
	{
		try
		{
			ToastForm.ShowNotice(Strings.CpuTuneGuardSkipToast);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[cpu-co] shutdown-guard notice failed: " + ex.Message);
		}
	}

	internal static bool IsIntel()
	{
		if (!X86Base.IsSupported)
		{
			return false;
		}
		(int Eax, int Ebx, int Ecx, int Edx) tuple = X86Base.CpuId(0, 0);
		int item = tuple.Ebx;
		int item2 = tuple.Ecx;
		int item3 = tuple.Edx;
		return ((ReadOnlySpan<byte>)MemoryMarshal.AsBytes(stackalloc int[3] { item, item3, item2 })).SequenceEqual("GenuineIntel"u8);
	}

	// 2026-10-10 intel（⑦B）：混合架构（P 核 + E 核）判定 —— CPUID.07H:EDX bit15 = Hybrid。
	// 上游拿它决定"要不要给 intel-ratio（倍频）这一项"：混合架构上倍频语义不明确，故不给。
	// ⚠️ 这个位在**老 Intel**（非混合）上是 0 -> 照常给倍频；在 AMD/未知厂商上也返回 false，
	//    但那时 Intel 分支根本走不到（调用点只在 IsIntel() 之后）。
	internal static bool IsHybrid()
	{
		if (X86Base.IsSupported)
		{
			return (X86Base.CpuId(7, 0).Edx & 0x8000) != 0;
		}
		return false;
	}

	public void Dispose()
	{
		lock (SyncRoot)
		{
			_intel?.Dispose();
			_smu?.Dispose();
			_io.Dispose();
		}
	}
}
