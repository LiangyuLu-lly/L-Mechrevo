using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MechrevoLite.Gpu;
using MechrevoLite.Hardware;
using Microsoft.Win32;

namespace MechrevoLite.Mode;

internal sealed class PerfModeService
{
	private readonly MechrevoHw _hw;

	private readonly IPerfModeBackend _backend;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private readonly object _requestLock = new object();

	private CancellationTokenSource? _activationCts;

	private CancellationTokenSource? _editCts;

	private readonly object _redirectLock = new object();

	private readonly Queue<long> _recentRedirects = new Queue<long>();

	private IReadOnlyList<PerfModeDefinition>? _modes;

	private int _requestSeq;
	/// <summary>2026-10-21：最近一次用户/面板下发时刻（TickCount64；0=还没发过）。启动重放据此避让。</summary>
	private static long _lastApplyTick;

	private int _guardSeq;

	private int _activating;

	private int _reconcileSeq;

	private string? _pendingModeId;

	// (b)（2026-10-09，用户原话「OK 按你说的」）：懒验证 —— 把"已下发·待确认(Sent)"的步骤连同期望值留着，
	// 在 300ms/2s/4s 多拍重读 + 每次 GCU 状态刷新上再读，读到一致原地升级为已确认（重抛 Applied 刷新界面）。
	private readonly PerfLazyVerifier _lazyVerifier;

	// (c)（2026-10-09）：reconcile 重应用循环收敛用的状态 ——
	// ① 记下"本进程刚下发的字段/值"，比对时排除掉（抑制"自己改的配置被自己判为外部变化"）；
	// ② _applyInFlight：有下发在飞行中时不得并发重应用；③ _lastReconcileTicks：同档位重应用冷却。
	private readonly object _selfIssueLock = new object();

	private readonly Dictionary<string, SelfIssuedWrite> _selfIssuedWrites = new Dictionary<string, SelfIssuedWrite>(StringComparer.Ordinal);

	private int _applyInFlight;

	private long _lastReconcileTicks;

	private int _reconcileRetryScheduled;

	private const string SelfKeyPowerOverlay = "app:WindowsPowerMode";

	private const string SelfKeyCpuBoost = "app:CpuBoost";

	private const string SelfKeyPowerPlan = "app:PowerPlanGuid";

	private const string SelfKeyFanBoost = "app:FanBoost";

	private const string SelfKeyRefreshHz = "app:RefreshHz";

	/// <summary>(c) 宽限期：刚由本进程下发的字段在这之内不判"外部改变/漂移"（驱动与固件回读实测滞后 2~4s）。</summary>
	internal static int SelfIssueGraceMs { get; set; } = 6000;

	/// <summary>(c) 冷却：同一档位这么多毫秒内最多重应用一次（去抖，避免一串状态刷新各写一遍硬件）。</summary>
	internal static int ReconcileCooldownMs { get; set; } = 15000;

	private sealed record SelfIssuedWrite(string Value, long Ticks);

	public static PerfModeService? Instance { get; private set; }

	public IReadOnlyList<PerfModeDefinition> Modes => _modes ?? (_modes = PerfModeStore.Load());

	/// <summary>丢弃缓存、从存储重新读取全部档位（用于切档后强制刷新界面）。</summary>
	internal void ReloadModesFromStore()
	{
		_modes = PerfModeStore.Load();
	}

	public string ActiveModeId => PerfModeStore.ActiveModeId;

	public string? PendingModeId => Volatile.Read(in _pendingModeId);

	public string DisplayedModeId => PendingModeId ?? ActiveModeId;

	public void NotifyExternalSwitch(string modeId)
	{
		if (string.Equals(ActiveModeId, modeId, StringComparison.Ordinal))
		{
			return;
		}
		PerfModeStore.ActiveModeId = modeId;
		RaiseActiveChanged();
	}

	public PerfModeDefinition? Active => Find(ActiveModeId);

	public PerfModeDefinition LastCustom
	{
		get
		{
			PerfModeDefinition perfModeDefinition = Find(PerfModeStore.LastCustomId);
			if ((object)perfModeDefinition == null || !perfModeDefinition.IsCustom)
			{
				return Modes.First((PerfModeDefinition m) => m.IsCustom);
			}
			return perfModeDefinition;
		}
	}

	public static int SlotCount => FirmwareSlotPlanner.SlotCountFor(ReadCustomizeTarget());

	internal static int[] GuardCheckDelaysMs { get; set; } = new int[4] { 1500, 3000, 6000, 10000 };

	public event Action? ModesChanged;

	public event Action? ActiveChanged;

	public event Action<PerfApplyOutcome>? Applied;

	private PerfModeService(MechrevoHw hw, IPerfModeBackend backend)
	{
		_hw = hw;
		_backend = backend;
		// (b)：懒验证 —— 探测函数走"驱动/NVAPI 回读（GPU 超频）→ GCU 页面状态回读"两级；
		// 升级后重抛 Applied（界面刷新）；GPU 超频确认后回调 MechrevoHw 丢掉滞后的提升助手快照。
		_lazyVerifier = new PerfLazyVerifier(ProbeStepAsync, RaiseApplied, OnGpuOverclockReadbackConfirmed);
		// (b)："下一次 GCU 状态刷新（或下一次读回）"的入口：GCU 每次刷页面状态（含 HWOC 的 Support/Enable/偏移）都会触发。
		// ⚠️ 这里只把待办标成"到期立刻读"，读硬件在懒验证的后台循环里做（事件处理器里不做慢活）。
		hw.CustomModeChanged += OnHwCustomModeChanged;
	}

	private void OnHwCustomModeChanged()
	{
		_lazyVerifier.ProbeNow();
	}

	private Task<bool?> ProbeStepAsync(PerfApplyStep step)
	{
		MechrevoHw hw = _hw;
		// ① GPU 超频相关步骤优先走驱动/NVAPI 回读 —— 就是 (a) 里那串多拍重读。
		//    用三值判据（读得到=true/false，读不到=null），这样"读得到却一直对不上"到期限才如实标「可能没生效」。
		if (hw != null && PerfLazyVerifier.IsGpuOverclockStep(step) && hw.DirectGpuOverclockBackendPresent)
		{
			bool? verdict = (step.Kind == PerfApplyStepKind.ApplyGpuOverclock)
				? hw.DriverGpuOverclockOffsetsVerdict(step.GpuOffsets?.Core ?? 0, step.GpuOffsets?.Memory ?? 0)
				: hw.DriverGpuOverclockFieldVerdict(step.WireKey, step.Number);
			if (verdict.HasValue)
			{
				return Task.FromResult(verdict);
			}
		}
		// ② 其余（以及驱动后端缺失时）走 GCU 页面状态回读：与 reconcile 同一份快照，判据完全一致。
		if (hw == null || !hw.IsConnected)
		{
			return Task.FromResult<bool?>(null);
		}
		return Task.FromResult(PerfLazyVerifier.CompareLive(SnapshotLive(includeFanCurves: false), step));
	}

	private void OnGpuOverclockReadbackConfirmed()
	{
		MechrevoHw hw = _hw;
		if (hw != null)
		{
			hw.NotifyGpuOverclockReadbackConfirmed();
		}
	}

	/// <summary>
	/// (b)(c)（2026-10-09，用户原话「OK 按你说的」）：**所有下发都从这里走** —— 除了照旧调 PerfModeRunner，额外做两件事：
	/// ① 记下"本进程刚下发的字段/值"（= 抑制 reconcile/guard 把自己的写入误判成"外部改变"，见 WasSelfIssuedRecently）；
	/// ② 把结果里"已下发·待确认(Sent)"的步骤登记给懒验证（多拍重读 + 每次 GCU 状态刷新，读到一致自动升级为已确认）。
	/// ⚠️ _applyInFlight 计数：有下发在飞行中时，reconcile/guard 一律不得并发重应用。
	/// ⚠️ 下发链本身（PerfModeApplyPlanner → PerfModeRunner → MechrevoService.SetCustomDetail）一字未改。
	/// </summary>
	private async Task<PerfApplyOutcome> RunStepsAsync(string modeId, IReadOnlyList<PerfApplyStep> steps, CancellationToken ct = default(CancellationToken))
	{
		Interlocked.Increment(ref _applyInFlight);
		PerfApplyOutcome perfApplyOutcome;
		try
		{
			perfApplyOutcome = await PerfModeRunner.RunAsync(modeId, steps, _backend, ct).ConfigureAwait(continueOnCapturedContext: false);
			// 但 PL1/PL2/温度墙会走 WMI 23 真下发 ⇒ 标记 LocalApply，让 ModeSwitched 按"字段全成功"判定
			if (_hw != null && (_hw.UsesBaolongda || _hw.SmuCustomApu))
			{
				perfApplyOutcome = perfApplyOutcome with { LocalApply = true };
			}
		}
		finally
		{
			Interlocked.Decrement(ref _applyInFlight);
		}
		RecordSelfIssued(perfApplyOutcome);
		MechrevoHw hw = _hw;
		_lazyVerifier.Register(perfApplyOutcome, hw != null && hw.DirectGpuOverclockBackendPresent);
		return perfApplyOutcome;
	}

	/// <summary>(c)：把这次下发**真正写进去**（没被判失败）的字段/值记下来。</summary>
	private void RecordSelfIssued(PerfApplyOutcome outcome)
	{
		if (outcome == null)
		{
			return;
		}
		long now = Environment.TickCount64;
		lock (_selfIssueLock)
		{
			PruneSelfIssued(now);
			foreach (PerfApplyStepOutcome item in outcome.Steps)
			{
				if (item.Result != PerfApplyResult.Failed)
				{
					RecordSelfIssuedLocked(item.Step, now);
				}
			}
		}
	}

	/// <summary>(c)：reconcile 自己补写的单个字段同样算"我方下发"。</summary>
	private void RecordSelfIssued(string key, string value)
	{
		long now = Environment.TickCount64;
		lock (_selfIssueLock)
		{
			PruneSelfIssued(now);
			_selfIssuedWrites[key] = new SelfIssuedWrite(value ?? "", now);
		}
	}

	private void RecordSelfIssuedLocked(PerfApplyStep step, long now)
	{
		switch (step.Kind)
		{
		case PerfApplyStepKind.WriteFirmwareField:
			if (step.WireKey != null)
			{
				_selfIssuedWrites[step.WireKey] = new SelfIssuedWrite(step.Value ?? "", now);
			}
			break;
		case PerfApplyStepKind.ApplyGpuOverclock:
			if (step.GpuOffsets.HasValue)
			{
				_selfIssuedWrites["GpuCoreClockOffsetOC"] = new SelfIssuedWrite(step.GpuOffsets.Value.Core.ToString(CultureInfo.InvariantCulture), now);
				_selfIssuedWrites["GpuMemoryClockOffsetOC"] = new SelfIssuedWrite(step.GpuOffsets.Value.Memory.ToString(CultureInfo.InvariantCulture), now);
			}
			_selfIssuedWrites["OverClockingSwitch"] = new SelfIssuedWrite((step.Number == 1) ? "1" : "0", now);
			break;
		case PerfApplyStepKind.ApplyPowerOverlay:
			_selfIssuedWrites[SelfKeyPowerOverlay] = new SelfIssuedWrite(step.Number.ToString(CultureInfo.InvariantCulture), now);
			break;
		case PerfApplyStepKind.ApplyCpuBoost:
			_selfIssuedWrites[SelfKeyCpuBoost] = new SelfIssuedWrite(step.Number.ToString(CultureInfo.InvariantCulture), now);
			break;
		case PerfApplyStepKind.ApplyPowerPlan:
			_selfIssuedWrites[SelfKeyPowerPlan] = new SelfIssuedWrite(step.Value ?? "", now);
			break;
		case PerfApplyStepKind.ApplyFanBoost:
			_selfIssuedWrites[SelfKeyFanBoost] = new SelfIssuedWrite((step.Number == 1) ? "1" : "0", now);
			break;
		case PerfApplyStepKind.ApplyRefreshRate:
			_selfIssuedWrites[SelfKeyRefreshHz] = new SelfIssuedWrite(step.Number.ToString(CultureInfo.InvariantCulture), now);
			break;
		}
	}

	private void PruneSelfIssued(long now)
	{
		if (_selfIssuedWrites.Count < 32)
		{
			return;
		}
		long num = SelfIssueGraceMs * 4;
		foreach (string item in _selfIssuedWrites.Where((KeyValuePair<string, SelfIssuedWrite> kv) => now - kv.Value.Ticks > num).Select((KeyValuePair<string, SelfIssuedWrite> kv) => kv.Key).ToList())
		{
			_selfIssuedWrites.Remove(item);
		}
	}

	/// <summary>(c)：这个字段是不是"本进程刚下发过、且目标值一模一样" —— 是的话本次差异属于自触发，宽限期内不重应用。</summary>
	private bool WasSelfIssuedRecently(string key, string value)
	{
		lock (_selfIssueLock)
		{
			if (!_selfIssuedWrites.TryGetValue(key, out SelfIssuedWrite selfIssuedWrite))
			{
				return false;
			}
			if (Environment.TickCount64 - selfIssuedWrite.Ticks > SelfIssueGraceMs)
			{
				return false;
			}
			return string.Equals(selfIssuedWrite.Value, value ?? "", StringComparison.Ordinal);
		}
	}

	private bool ApplyInFlight => Volatile.Read(in _applyInFlight) > 0;

	public static PerfModeService Create(MechrevoService service, MechrevoHw hw)
	{
		ArgumentNullException.ThrowIfNull(service, "service");
		ArgumentNullException.ThrowIfNull(hw, "hw");
		PerfModeStore.MigrateLegacy();
		PerfModeService perfModeService = new PerfModeService(hw, new PerfModeBackend(service, hw));
		Instance = perfModeService;
		// 2026-10-09（修 B1「旧键 split-brain」）：托盘切自定义档（SettingsForm）与电源变化重应用
		// （ModeControl.ReapplyCustomProfileAsync）都走 WinPowerPlan.ApplyProfile(固件档位号)，而旧实现
		// 读的是已废弃的旧键 customN_plan / customN_boost（从不清除，还会被「当前活动方案」自动播种），
		// 会用过期方案覆盖新引擎刚设好的电源计划。这里把那条路径接到新引擎：按**固件槽位归属**反查
		// 自定义档（与 NotifyTrayCustomProfile 同一口径），只下发该档**已保存**的方案 / 睿频，只读不写。
		WinPowerPlan.ApplyProfileOverride = ApplySlotPowerProfile;
		return perfModeService;
	}

	/// <summary>2026-10-09（修 B1）：按固件槽位归属把「该自定义档已保存的 Windows 电源设置」同步到系统。
	/// 只读配置，不写任何键、不做任何自动播种；槽位没有归属档位时什么都不做并视为成功。</summary>
	private static bool ApplySlotPowerProfile(int slotIndex)
	{
		PerfModeService perfModeService = Instance;
		if (perfModeService == null)
		{
			return false;
		}
		PerfModeDefinition mode = null;
		foreach (PerfModeDefinition candidate in perfModeService.Modes)
		{
			if (perfModeService.SlotOf(candidate.Id) == slotIndex)
			{
				mode = candidate;
				break;
			}
		}
		if ((object)mode == null)
		{
			Logger.WriteLine($"WinPowerPlan ApplyProfile({slotIndex}): 该固件槽位没有归属自定义档，未改动 Windows 电源设置");
			return true;
		}
		PerfModeSettings settings = mode.Settings;
		bool planConfirmed = string.IsNullOrWhiteSpace(settings.PowerPlanGuid) || WinPowerPlan.SetActivePlan(settings.PowerPlanGuid);
		bool boostConfirmed = planConfirmed && (!settings.CpuBoost.HasValue || WinPowerPlan.SetBoost(settings.CpuBoost.Value));
		Logger.WriteLine($"WinPowerPlan ApplyProfile({slotIndex}) -> mode {mode.Id} plan={(string.IsNullOrWhiteSpace(settings.PowerPlanGuid) ? "(unset)" : settings.PowerPlanGuid)} boost={(settings.CpuBoost.HasValue ? settings.CpuBoost.Value.ToString() : "(unset)")} confirmed={boostConfirmed}");
		return boostConfirmed;
	}

	public PerfModeDefinition? Find(string? id)
	{
		if (id != null)
		{
			return Modes.FirstOrDefault((PerfModeDefinition m) => string.Equals(m.Id, id, StringComparison.Ordinal));
		}
		return null;
	}

	private static int ReadCustomizeTarget()
	{
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
			using RegistryKey registryKey2 = registryKey.OpenSubKey("SOFTWARE\\OEM\\GamingCenter2");
			return (!(registryKey2?.GetValue("CustomizeTarget") is int num)) ? 1 : num;
		}
		catch
		{
			return 1;
		}
	}

	public static bool TurboAutoOcEnabled()
	{
		PerfModeSettings perfModeSettings = PerfModeStore.LoadSettings(PerfModeDefinition.BuiltInId(PerfModeKind.Turbo));
		if (!perfModeSettings.GpuOverclockOn.HasValue && !perfModeSettings.GpuCoreOffset.HasValue)
		{
			return !perfModeSettings.GpuMemoryOffset.HasValue;
		}
		return false;
	}

	private void Invalidate()
	{
		_modes = null;
		try
		{
			ModesChanged?.Invoke();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModes ModesChanged handler failed: " + ex.Message);
		}
	}

	private void RaiseActiveChanged()
	{
		try
		{
			ActiveChanged?.Invoke();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModes ActiveChanged handler failed: " + ex.Message);
		}
	}

	private void RaiseApplied(PerfApplyOutcome outcome)
	{
		try
		{
			Applied?.Invoke(outcome);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModes Applied handler failed: " + ex.Message);
		}
	}

	private PerfModeCapabilities Capabilities()
	{
		return PerfModeBackend.CapabilitiesFrom(_hw);
	}

	private static int PowerSourceKey()
	{
		return (int)SystemInformation.PowerStatus.PowerLineStatus;
	}

	public async Task<PerfApplyOutcome?> ActivateAsync(string modeId, string reason, bool forceParameters = false)
	{
		// 2026-10-21：只有“用户/面板发起的下发”才记时刻；启动重放本身不记（否则会把自己挡掉）。
		if (string.IsNullOrEmpty(reason) || reason.IndexOf("replay", StringComparison.OrdinalIgnoreCase) < 0)
		{
			Interlocked.Exchange(ref _lastApplyTick, Environment.TickCount64);
		}
		// ⭐⭐ 2026-10-17（「高性能电源」**彻底解耦 GCU**）：⭐ 切档时由 **LM 自己**同步 Windows「电源模式」
		//   （设置 → 系统 → 电源 → 电源模式：最佳性能 / 平衡 / 最佳能效）✅
		//   ⚠️ 纯 Windows API（powrprof `PowerSetActiveOverlayScheme`）⇒ ⛔ 不依赖 GCU、不按能力位、不写 EC ✅
		//   ⭐ 用户口径：「GCU 也解耦（不按能力位来了），走 Windows 替换我的客户端」
		//      ⇒ ⭐ **所有机型**都由这里设（GCU 机型不再等厂商链路）✅
		ApplyWindowsPowerOverlay(modeId);
		return await ActivateIfCurrentAsync(modeId, reason, forceParameters, null).ConfigureAwait(continueOnCapturedContext: false);
	}
	// <para>默认映射：安静 ⇒ 2、平衡 ⇒ 0、狂暴/静音狂暴 ⇒ 1；自定义档按其 <see cref="PerfModeSettings.WindowsPowerMode" />（设了才动）。</para>
	public bool ApuAntiDegradeEnabled
	{
		get
		{
			// AC / DC 两套都是 5（高效、积极且有保障）才算开
			PowerNative.GetCPUBoostBoth(out int ac, out int dc);
			return ac == 5 && dc == 5;
		}
		set
		{
			if (value)
			{
				PowerNative.GetCPUBoostBoth(out int prevAc, out int prevDc);
				if (prevAc != 5 || prevDc != 5)
				{
					AppConfig.Set("apuboost_prev", prevAc);
					AppConfig.Set("apuboost_prev_dc", prevDc);
					PowerNative.SetCPUBoost(5);
					Logger.WriteLine($"[power] APU 防缩缸：开 ⇒ 处理器性能提升模式 5（高效、积极且有保障）· 原值 AC={prevAc}/DC={prevDc} 已记");
				}
				AppConfig.Set("apuboost", 1);
				return;
			}
			int restoreAc = AppConfig.Get("apuboost_prev", 1);
			int restoreDc = AppConfig.Get("apuboost_prev_dc", restoreAc);
			PowerNative.SetCPUBoost(restoreAc);
			if (restoreDc != restoreAc)
			{
				PowerNative.SetCPUBoostDc(restoreDc);
			}
			AppConfig.Set("apuboost", 0);
			Logger.WriteLine($"[power] APU 防缩缸：关 ⇒ 还原处理器性能提升模式 AC={restoreAc}/DC={restoreDc}");
		}
	}

	/// <summary>启动时按用户保存的意图恢复「APU 防缩缸」；不强制关闭（避免覆盖其它来源的设置）。</summary>
	internal void ApplyApuAntiDegradeOnStartup()
	{
		if (AppConfig.Get("apuboost", 0) == 0)
		{
			return;
		}
		PowerNative.GetCPUBoostBoth(out int ac, out int dc);
		if (ac == 5 && dc == 5)
		{
			return;
		}
		try
		{
			AppConfig.Set("apuboost_prev", ac);
			AppConfig.Set("apuboost_prev_dc", dc);
			PowerNative.SetCPUBoost(5);
			Logger.WriteError($"[power] 启动恢复 APU 防缩缸：处理器性能提升模式 AC={ac}/DC={dc} → 5");
		}
		catch (Exception ex)
		{
			Logger.WriteError("[power] 启动恢复 APU 防缩缸失败：" + ex.Message);
		}
	}

	public bool WindowsPowerOverlayEnabled
	{
		get
		{
			return AppConfig.Get("bd_power_overlay", 1) != 0;
		}
		set
		{
			AppConfig.Set("bd_power_overlay", value ? 1 : 0);
			Logger.WriteLine($"[power] 高性能电源联动 ⇒ {(value ? "开" : "关")}");
			if (value)
			{
				// ⭐ 后台执行（回读+重试会 sleep，不能卡 UI 线程）
				_ = Task.Run(() => ApplyWindowsPowerOverlay(Active?.Id ?? string.Empty));
			}
			else
			{
				PowerNative.SetPowerMode(0);
				Logger.WriteLine("[power] 已关闭联动 ⇒ Windows 电源模式回到「平衡」");
			}
		}
	}
	// <para>映射：2=静音 ⇒ 最佳能效 · 0=平衡 ⇒ 平衡 · 1=狂暴/静音狂暴 ⇒ 最佳性能</para>
	/// <summary>按“当前已激活档位”的完整配置应用 Windows 电源模式；自定义档的 WindowsPowerMode 优先。</summary>
	public void ApplyWindowsPowerOverlayForActiveMode()
	{
		if (Active == null)
		{
			return;
		}
		ApplyWindowsPowerOverlay(Active.Id);
	}

	public void ApplyWindowsPowerOverlayForUiMode(int mode, bool silentTurbo)
	{
		if (!WindowsPowerOverlayEnabled)
		{
			return;
		}
		int overlay = mode switch
		{
			2 => 2,
			0 => 0,
			1 => 1,
			_ => -1
		};
		if (overlay < 0)
		{
			return;
		}
		// ⭐ 后台执行：里面有"回读 + 重试（最多 ~1.4 s）"，⛔ 不能卡 UI 线程 ✅
		_ = Task.Run(() => ApplyWindowsPowerOverlayValue(overlay));
	}

	/// <summary>⭐ 把 Windows 电源模式设成给定 overlay（0=平衡 / 1=最佳性能 / 2=最佳能效），失败只记日志。</summary>
	private void ApplyWindowsPowerOverlayValue(int overlay)
	{
		// （机主口径：「代码顺序 —— 直接跟着 GCU 发命令同时，不要等回读」）
		string name = (overlay == 1) ? "最佳性能" : ((overlay == 2) ? "最佳能效" : "平衡");
		try
		{
			PowerNative.SetPowerMode(overlay);
			Logger.WriteLine($"[power] Windows 电源模式 → {name}（overlay={overlay} · 与切档同时下发 · 不等回读 · 纯 Windows API）");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[power] Windows 电源模式设置失败（已吞掉）：" + ex.Message);
		}
	}

	private void ApplyWindowsPowerOverlay(string modeId)
	{
		PerfModeDefinition mode = Find(modeId);
		if ((object)mode == null)
		{
			return;
		}
		// 0.293.5.1 基线：“高性能电源”总开关关闭时，任何档位都不下发 Windows 电源模式。
		if (AppConfig.Get("bd_power_overlay", 1) == 0)
		{
			return;
		}
		bool explicitWindowsMode = mode.Settings.WindowsPowerMode.HasValue;
		int overlay;
		if (explicitWindowsMode)
		{
			overlay = mode.Settings.WindowsPowerMode.Value;
		}
		else
		{
			overlay = mode.Kind switch
			{
				PerfModeKind.Silent => 2,
				PerfModeKind.Balanced => 0,
				PerfModeKind.Turbo => 1,
				PerfModeKind.SilentTurbo => 1,
				_ => -1
			};
		}
		if (overlay < 0 || overlay > 2)
		{
			return;
		}
		try
		{
			string name = (overlay == 1) ? "最佳性能" : ((overlay == 2) ? "最佳能效" : "平衡");
			PowerNative.SetPowerMode(overlay);
			Logger.WriteLine($"[power] Windows 电源模式 → {name}（overlay={overlay} · 纯 Windows API，不依赖 GCU/能力位）");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[power] Windows 电源模式设置失败（已吞掉）：" + ex.Message);
		}
	}

	private async Task<PerfApplyOutcome?> ActivateIfCurrentAsync(string modeId, string reason, bool forceParameters, int? expectedRequest)
	{
		int request;
		CancellationTokenSource requestCts;
		CancellationTokenSource activationCts;
		CancellationTokenSource editCts;
		lock (_requestLock)
		{
			if (expectedRequest.HasValue)
			{
				int valueOrDefault = expectedRequest.GetValueOrDefault();
				if (valueOrDefault != _requestSeq)
				{
					return null;
				}
			}
			request = Interlocked.Increment(ref _requestSeq);
			requestCts = new CancellationTokenSource();
			activationCts = _activationCts;
			editCts = _editCts;
			_activationCts = requestCts;
			Volatile.Write(ref _pendingModeId, modeId);
		}
		try
		{
			activationCts?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		try
		{
			editCts?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		RaiseActiveChanged();
		bool locked = false;
		Stopwatch elapsed = Stopwatch.StartNew();
		try
		{
			await _gate.WaitAsync(requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			locked = true;
			Interlocked.Increment(ref _activating);
			Interlocked.Increment(ref _guardSeq);
			if (request != Volatile.Read(in _requestSeq))
			{
				return null;
			}
			Logger.WriteLine($"PerfModes: {modeId} gate acquired after {elapsed.ElapsedMilliseconds}ms");
			return await ActivateLockedAsync(modeId, reason, forceParameters, requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
		{
			Logger.WriteLine($"PerfModes: {modeId} superseded after {elapsed.ElapsedMilliseconds}ms");
			return null;
		}
		finally
		{
			if (locked)
			{
				Interlocked.Decrement(ref _activating);
				_gate.Release();
			}
			lock (_requestLock)
			{
				if (_activationCts == requestCts)
				{
					_activationCts = null;
					Volatile.Write(ref _pendingModeId, null);
				}
			}
			requestCts.Dispose();
			RaiseActiveChanged();
		}
	}

	private async Task<PerfApplyOutcome?> ActivateLockedAsync(string modeId, string reason, bool forceParameters, CancellationToken ct)
	{
		PerfModeDefinition mode = Find(modeId);
		if ((object)mode == null)
		{
			Logger.WriteLine("PerfModes: activate(" + modeId + ") ignored, mode does not exist");
			return null;
		}
		MechrevoHw? intelXingyaoGuard = _hw;
		if (mode.IsCustom && intelXingyaoGuard != null
			&& MechrevoLite.Hardware.CpuVendor.IsIntel
			&& MechrevoLite.Hardware.XingyaoPmc2.IsXingyaoPlatform())
		{
			Logger.WriteError("[perfmode] Intel 星耀：自定义模式已暂时屏蔽（不应用、不下发）");
			PerfApplyOutcome blocked = new PerfApplyOutcome(modeId, new PerfApplyStepOutcome[1]
			{
				new PerfApplyStepOutcome(SwitchStepFor(mode, 0), PerfApplyResult.Failed,
					"Intel 星耀暂不支持自定义模式")
			});
			RaiseApplied(blocked);
			return blocked;
		}
		PerfModeSettings expectedSettings = mode.Settings;
		MechrevoHw hw = _hw;
		// （无界 APU）：无 GCU 也可走本机 SMU 应用/启动恢复 ⇒ 连接判据放宽（其它机型不变）
		if (hw == null || (!hw.IsConnected && !hw.SmuCustomApu))
		{
			Logger.WriteLine("PerfModes: activate(" + modeId + ") refused, GCU not connected");
			// ⭐⭐ 2026-10-16（宝龙达 · 诚实性）：⭐ 失败原因【按后端如实写】✅ ——
			//   ⭐ 原来恒写「GCU 未连接」⚠️，⭐ 但宝龙达机型**本来就没有 GCU**（⭐ 而 `IsConnected` 的判据是
			//      `UsesBuiltInModes || IsGcuConnected` ⇒ ⭐ 语义是"后端在不在"、⭐ 不是"有没有跟 GCU 说过话" ✅）
			//      ⇒ ⭐ 在宝龙达上这句是【误导】❌。
			//   ⚠️ 非宝龙达（⭐ 含 `hw == null` 这种判不出来的情形 ✅）⇒ ⭐ 字符串逐字不变 ✅。
			string refusalDetail = ((hw != null && hw.UsesBaolongda) ? "本机为非 GCU 后端（宝龙达），该步骤不适用" : "GCU 未连接");
			PerfApplyOutcome perfApplyOutcome = new PerfApplyOutcome(modeId, new PerfApplyStepOutcome[1]
			{
				new PerfApplyStepOutcome(SwitchStepFor(mode, 0), PerfApplyResult.Failed, refusalDetail)
			});
			RaiseApplied(perfApplyOutcome);
			return perfApplyOutcome;
		}
		// （机主定稿）：无界 + 当前自定义档 ⇒ 明确记录"跳过性能档位下发"（来源=应用/对账）
		if (hw.SmuCustomApu && mode.IsCustom)
		{
			string suppressLog = "[perfmode] 无界 APU：当前为自定义档 ⇒ 跳过性能档位下发（来源=应用/对账）";
			if (Logger.TryMarkChanged("wujie-skip-mode-dispatch-activate", suppressLog))
			{
				Logger.WriteError(suppressLog);
			}
		}		PerfModeCapabilities caps = Capabilities();
		IReadOnlyList<FirmwareSlotState> slots = PerfModeStore.LoadSlots(SlotCount);
		FirmwareSlotPlan plan = null;
		if (mode.Route == PerfModeRoute.FirmwareSlot)
		{
			string activeModeId = ActiveModeId;
			plan = FirmwareSlotPlanner.Plan(mode.Id, mode.Settings.FirmwareSignature(), slots, activeModeId);
			if (forceParameters && !plan.NeedsParameterWrite)
			{
				plan = plan with
				{
					NeedsParameterWrite = true,
					Reason = "forced rewrite"
				};
			}
		}
		// 纯本机 SMU 路径：规划器不生成任何 GCU 模式/固件档步骤；这里落一行去重日志（默认可见）
		if (caps.WujieSmuLocalOnly)
		{
			string wujieLog = "[perfmode] 无界 APU：应用自定义 ⇒ 跳过 GCU 性能模式下发（仅本机 SMU：PL1="
				+ mode.Settings.Pl1.GetValueOrDefault() + "W/PL2=" + mode.Settings.Pl2.GetValueOrDefault()
				+ "W/tctl=" + mode.Settings.TccTarget.GetValueOrDefault() + "℃）";
			if (Logger.TryMarkChanged("wujie-smu-local-apply", wujieLog))
			{
				Logger.WriteError(wujieLog);
			}
		}
		IReadOnlyList<PerfApplyStep> readOnlyList = PerfModeApplyPlanner.Build(mode, plan, caps);
		// （机主：「显卡超频设置了但频率没变化」· 可观测性①）
		LogGpuOverclockPlan(mode, plan, caps, readOnlyList);
		Logger.WriteLine($"PerfModes: activate {mode.Id} ({reason}) route={mode.Route} slot={plan?.SlotIndex.ToString() ?? "-"} write={plan?.NeedsParameterWrite.ToString() ?? "-"} steps={readOnlyList.Count}");
		// 2026-10-21（机主）：无界目标功耗 ≥ 60W 时，先切狂暴模式再下发 SMU（已在狂暴不重复发）。
		if (caps.WujieSmuLocalOnly && mode.IsCustom)
		{
			int wujieTargetMax = Math.Max(mode.Settings.Pl1.GetValueOrDefault(), mode.Settings.Pl2.GetValueOrDefault());
			if (wujieTargetMax >= 60)
			{
				try
				{
					await _backend.EnsureWujieHighPowerModeAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception exTurbo)
				{
					Logger.WriteError("[perfmode] 无界：切狂暴模式异常（继续下发 SMU）：" + exTurbo.Message);
				}
			}
		}
		Stopwatch elapsed = Stopwatch.StartNew();
		PerfApplyOutcome outcome;
		try
		{
			outcome = await RunStepsAsync(mode.Id, readOnlyList, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			if ((object)plan != null && plan.NeedsParameterWrite)
			{
				PerfModeStore.SaveSlots(slots.Select((FirmwareSlotState s) => (s.Index != plan.SlotIndex) ? s : s with
				{
					Signature = null
				}).ToArray());
			}
			throw;
		}
		Logger.WriteLine("PerfModes: " + mode.Id + " -> " + outcome.Describe());
		Logger.WriteLine($"PerfModes: {mode.Id} apply elapsed={elapsed.ElapsedMilliseconds}ms");
		if (!outcome.ModeSwitched)
		{
			// 2026-10-09（「切自定义档被弹回静音/平衡」取证）：这次激活**没能切档**（档位步被判失败 / 回读对不上 /
			// 或本来就在该档）⇒ ActiveModeId 与固件槽位都不更新。档位**意图**已由
			// MechrevoService.SwitchCustomProfile 在请求时写进 performance_<电源来源>，所以此后的自动重应用
			// 会**重试自定义档**，不会再拿老键里的内置档（静音/平衡）把它顶掉。
			Logger.WriteLine($"PerfModes: {mode.Id} 未切换成功（reason={reason} route={mode.Route}）→ ActiveModeId 仍为 {ActiveModeId}；档位意图保持用户选择，等下一次重试。失败/未确认项：{outcome.Describe()}");
		}
		// （机主：Wujie 规划器误生成固件字段步/ActiveModeId 回退）：本机 SMU 应用成功的判据日志
		if (caps.WujieSmuLocalOnly && mode.IsCustom)
		{
			string wujieApplyLog = "[perfmode] 无界 APU 本机应用：步骤=SMU(stapm/slow/fast/tctl) · 无固件字段步 · ModeSwitched=" + outcome.ModeSwitched;
			if (Logger.TryMarkChanged("wujie-local-apply-result", wujieApplyLog))
			{
				Logger.WriteError(wujieApplyLog);
			}
		}		if (outcome.ModeSwitched)
		{
			if ((object)plan != null)
			{
				slots = FirmwareSlotPlanner.Assign(slots, plan.SlotIndex, mode.Id, outcome.FirmwareParametersIssued ? mode.Settings.FirmwareSignature() : null, PerfModeStore.NowStamp());
				PerfModeStore.SaveSlots(slots);
			}
			PerfModeStore.ActiveModeId = mode.Id;
			PerfModeStore.SetActiveFor(PowerSourceKey(), mode.Id);
			if (mode.IsCustom)
			{
				PerfModeStore.LastCustomId = mode.Id;
			}
			if ((object)plan != null)
			{
				await CaptureSlotAsync(mode, plan.SlotIndex, ct).ConfigureAwait(continueOnCapturedContext: false);
				await PushSlotNameAsync(mode.Id, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			StartGuard(mode.Id);
			if (mode.IsCustom)
			{
				_ = ReconcileCustomAsync(mode, expectedSettings);
			}
		}
		RaiseApplied(outcome);
		return outcome;
	}

	/// <summary>
	/// (c)（2026-10-09，用户原话「OK 按你说的」）reconcile 重应用循环收敛。触发源：切到自定义档**成功之后** 1 秒
	/// （<see cref="ActivateLockedAsync"/> 末尾）比一次 GCU 实时快照 vs 该档已保存值，有差异就补写。
	/// 收敛后的判据（四条措施）：
	/// ① <b>diff-based</b>：只补写**真正有差异**的字段（原来就是逐字段 diff，保持）；
	/// ② <b>抑制自触发</b>：刚由本进程下发、且目标值一模一样的字段在宽限期（<see cref="SelfIssueGraceMs"/>）内
	///    不判"外部改变"，本次不下发 —— 这就是"自己改的配置被自己判为外部变化"的根治；
	/// ③ <b>冷却/去抖 + 不并发</b>：同一档位 <see cref="ReconcileCooldownMs"/> 内最多重应用一次；有下发在飞行中（ApplyInFlight）时不重应用；
	/// ④ <b>保留真实重应用</b>：被跳过的差异会在宽限期后**复核一次**（ScheduleReconcileRecheck），
	///    那时若回读仍对不上（真被固件钳制/丢弃）照样补写；真正的外部改变（值与我们下发的不一样、或早于宽限期）不受影响。
	/// 日志口径：每次都能看出"这次没有重应用"或"只重应用了 X 个字段"。
	/// </summary>
	private async Task ReconcileCustomAsync(PerfModeDefinition mode, PerfModeSettings expected, bool recheck = false)
	{
		try
		{
			if (!recheck)
			{
				await Task.Delay(1000).ConfigureAwait(continueOnCapturedContext: false);
			}
			MechrevoHw hw = _hw;
			if (hw == null || !hw.IsConnected || hw.OperatingMode != 3 || !string.Equals(ActiveModeId, mode.Id, StringComparison.Ordinal))
			{
				return;
			}
			// (c)③ 冷却 + 不并发：同档位 15s 内最多重应用一次；有下发在飞行中直接放弃本次。
			long tickCount = Environment.TickCount64;
			if (ApplyInFlight)
			{
				Logger.WriteLine("PerfModes: reconcile 有下发在飞行中，本次不重应用（recheck=" + recheck + "）");
				return;
			}
			if (!recheck && tickCount - Volatile.Read(in _lastReconcileTicks) < ReconcileCooldownMs)
			{
				Logger.WriteLine($"PerfModes: reconcile 冷却中（距上次重应用 <{ReconcileCooldownMs}ms），本次不重应用");
				return;
			}
			PerfLiveSnapshot live = SnapshotLive(includeFanCurves: false);
			Dictionary<string, string> diffs = new Dictionary<string, string>();
			void Check(string wireKey, int? expectedValue, int? actual)
			{
				if (expectedValue.HasValue && actual.HasValue && actual.GetValueOrDefault() != expectedValue.GetValueOrDefault())
				{
					diffs[wireKey] = expectedValue.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
				}
			}
			void CheckSwitch(string wireKey, bool? expectedValue, bool? actual)
			{
				if (expectedValue.HasValue && actual.HasValue && actual.GetValueOrDefault() != expectedValue.GetValueOrDefault())
				{
					diffs[wireKey] = (expectedValue.GetValueOrDefault() ? 1 : 0).ToString(CultureInfo.InvariantCulture);
				}
			}
			CheckSwitch("CpuTccOffsetSwitch", expected.TccOn, live.TccOn);
			CheckSwitch("GpuDynamicBoostSwitch", expected.GpuDynamicBoostOn, live.GpuDynamicBoostOn);
			CheckSwitch("OverClockingSwitch", expected.GpuOverclockOn, live.GpuOverclockOn);
			CheckSwitch("FanSwitchSpeedEnabled", expected.FanSwitchSpeedOn, live.FanSwitchSpeedOn);
			Check("PL1", expected.Pl1, live.Pl1);
			Check("PL2", expected.Pl2, live.Pl2);
			Check("PL4", expected.Pl4, live.Pl4);
			Check("CpuTccOffset", expected.TccTarget, live.TccTarget);
			Check("GpuConfigurableTGPTarget", expected.GpuTgp, live.GpuTgp);
			Check("GpuDynamicBoost", expected.GpuDynamicBoost, live.GpuDynamicBoost);
			Check("GpuCoreClockOffsetOC", expected.GpuCoreOffset, live.GpuCoreOffset);
			Check("GpuMemoryClockOffsetOC", expected.GpuMemoryOffset, live.GpuMemoryOffset);
			Check("FanSwitchSpeed", expected.FanSwitchSpeedMs, live.FanSwitchSpeedMs);
			if (diffs.Count == 0)
			{
				Logger.WriteLine("PerfModes: reconcile 检查：无差异，本次不重应用（0 个字段需要下发）");
				return;
			}
			// (c)① 只重应用真正有差异的字段；(c)② 其中"刚由本进程下发过、值还一样"的算自触发，跳过。
			Dictionary<string, string> toApply = new Dictionary<string, string>();
			List<string> list = new List<string>();
			foreach (KeyValuePair<string, string> diff in diffs)
			{
				if (WasSelfIssuedRecently(diff.Key, diff.Value))
				{
					list.Add(diff.Key);
				}
				else
				{
					toApply[diff.Key] = diff.Value;
				}
			}
			Logger.WriteLine($"PerfModes: reconcile 检查：差异={diffs.Count}，自触发跳过={list.Count}，待重应用={toApply.Count}" + ((list.Count == 0) ? "" : ("（跳过: " + string.Join(", ", list) + "，等驱动/固件回读跟上）")));
			if (toApply.Count == 0)
			{
				Logger.WriteLine("PerfModes: reconcile 本次不重应用（差异全部是本进程刚下发的字段，等回读）");
				ScheduleReconcileRecheck(mode, expected);
				return;
			}
			Interlocked.Exchange(ref _lastReconcileTicks, Environment.TickCount64);
			Logger.WriteLine("PerfModes: reconcile 只重应用 " + toApply.Count + " 个字段: " + string.Join(", ", toApply.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)));
			foreach (KeyValuePair<string, string> kv in toApply)
			{
				if (!string.Equals(ActiveModeId, mode.Id, StringComparison.Ordinal) || _hw == null || !_hw.IsConnected || _hw.OperatingMode != 3)
				{
					return;
				}
				try
				{
					if (await _backend.WriteDetailFieldAsync(kv.Key, kv.Value, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false))
					{
						// (c)②：这次补写同样登记为"我方下发"，免得下一次 reconcile/guard 又把它自己判成外部改变。
						RecordSelfIssued(kv.Key, kv.Value);
					}
					await Task.Delay(PerfModeApplyPlanner.FieldDelayMs).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex)
				{
					Logger.WriteLine("PerfModes: reconcile field " + kv.Key + " failed: " + ex.Message);
				}
			}
			PerfModeStore.SaveSettings(mode.Id, expected);
			Invalidate();
			Logger.WriteLine("PerfModes: reconcile 完成, 已同步本地配置");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModes: reconcile failed: " + ex.Message);
		}
	}

	/// <summary>
	/// (c)④ 保留真实重应用：当一次 reconcile 的差异**全部**是本进程刚下发的字段（自触发）时，不在宽限期内写硬件，
	/// 而是在宽限期后再**复核一次** —— 若那时回读仍未跟上（真的被固件钳制/丢弃），照样补写。
	/// 只安排一次，避免形成新的循环。
	/// </summary>
	private void ScheduleReconcileRecheck(PerfModeDefinition mode, PerfModeSettings expected)
	{
		if (Interlocked.CompareExchange(ref _reconcileRetryScheduled, 1, 0) != 0)
		{
			return;
		}
		Task.Run(async () =>
		{
			try
			{
				await Task.Delay(SelfIssueGraceMs + 500).ConfigureAwait(continueOnCapturedContext: false);
				await ReconcileCustomAsync(mode, expected, recheck: true).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("PerfModes: reconcile recheck failed: " + ex.Message);
			}
			finally
			{
				Interlocked.Exchange(ref _reconcileRetryScheduled, 0);
			}
		});
	}

	public async Task<bool> PushSlotNameAsync(string modeId, CancellationToken ct = default(CancellationToken))
	{
		PerfModeDefinition perfModeDefinition = Find(modeId);
		if ((object)perfModeDefinition == null || perfModeDefinition.Route != PerfModeRoute.FirmwareSlot)
		{
			return false;
		}
		if (!string.Equals(ActiveModeId, perfModeDefinition.Id, StringComparison.Ordinal))
		{
			return false;
		}
		MechrevoHw hw = _hw;
		if (hw == null || !hw.IsConnected || _hw.OperatingMode != 3)
		{
			return false;
		}
		string text = PerfModeText.Name(perfModeDefinition);
		if (text.Length > 12)
		{
			text = text.Substring(0, 12);
		}
		if (string.Equals(_hw.ProfileName, text, StringComparison.Ordinal))
		{
			return true;
		}
		try
		{
			return await _backend.SetSlotNameAsync(text, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("PerfModes: slot name push failed: " + ex2.Message);
			return false;
		}
	}
	// **规划结果一行日志**（默认级别可见、去重）：[gpu-oc] 规划：supports=… · enabled=… · core=… mem=… ⇒ 生成步骤=是/否（原因=…）
	private static void LogGpuOverclockPlan(PerfModeDefinition mode, FirmwareSlotPlan plan, PerfModeCapabilities caps, IReadOnlyList<PerfApplyStep> steps)
	{
		PerfModeSettings settings = mode.Settings;
		if (!caps.SupportsDirectGpuOverclock && !settings.GpuOverclockOn.HasValue && !settings.GpuCoreOffset.HasValue && !settings.GpuMemoryOffset.HasValue)
		{
			return;
		}
		bool generated = steps.Any((PerfApplyStep s) => s.Kind == PerfApplyStepKind.ApplyGpuOverclock);
		string reason;
		if (generated)
		{
			reason = "生成";
		}
		else if (!caps.SupportsDirectGpuOverclock)
		{
			reason = "后端不支持本机直写（旧 GCU 固件字段路径）";
		}
		else if (mode.Route != PerfModeRoute.FirmwareSlot)
		{
			reason = "路由不是 FirmwareSlot（route=" + mode.Route + "）";
		}
		else if (plan == null)
		{
			reason = "无槽位计划（slotPlan=null）";
		}
		else if (!plan.NeedsParameterWrite)
		{
			reason = "本次不需要写参数（NeedsParameterWrite=false）";
		}
		else
		{
			reason = "该档没有 GPU 超频字段（开关/偏移都未设置）";
		}
		string enabledText = settings.GpuOverclockOn.HasValue ? (settings.GpuOverclockOn.Value ? "true" : "false") : "-";
		string coreText = settings.GpuCoreOffset.HasValue ? settings.GpuCoreOffset.Value.ToString(CultureInfo.InvariantCulture) : "-";
		string memText = settings.GpuMemoryOffset.HasValue ? settings.GpuMemoryOffset.Value.ToString(CultureInfo.InvariantCulture) : "-";
		string writeText = (plan == null) ? "-" : plan.NeedsParameterWrite.ToString();
		string message = "[gpu-oc] 规划：supports=" + caps.SupportsDirectGpuOverclock + " · enabled=" + enabledText + " · core=" + coreText + " · mem=" + memText
			+ " ⇒ 生成步骤=" + (generated ? "是" : "否") + "（原因=" + reason + " · mode=" + mode.Id + " · route=" + mode.Route + " · write=" + writeText + "）";
		if (Logger.TryMarkChanged("gpu-oc-plan", message))
		{
			Logger.WriteError(message);
		}
	}

	private static PerfApplyStep SwitchStepFor(PerfModeDefinition mode, int slot)
	{
		if (mode.Route != PerfModeRoute.FirmwareSlot)
		{
			return new PerfApplyStep(PerfApplyStepKind.SwitchBuiltIn, null, null, (int)mode.Kind, IsCpuCurve: false, null, PerfApplyVerify.ModeReadback);
		}
		return new PerfApplyStep(PerfApplyStepKind.SwitchFirmwareSlot, null, null, slot, IsCpuCurve: false, null, PerfApplyVerify.ModeReadback);
	}

	private async Task CaptureSlotAsync(PerfModeDefinition mode, int slot, CancellationToken ct = default(CancellationToken))
	{
		string expectedTable;
		try
		{
			if (!PerfModeResolver.IsFirmwareOn(mode, PerfModeStore.LoadSlots(SlotCount), _hw.OperatingMode, _hw.CustomProfileIndex, silentTurbo: false, turboSubModeSupported: false))
			{
				return;
			}
			expectedTable = "M4T" + (slot + 1).ToString(CultureInfo.InvariantCulture);
			PerfModeSettings current = PerfModeStore.LoadSettings(mode.Id);
			if (!TableIsThisSlot() && _hw.FanCurveSeen && (current.CpuFanDuty == null || current.GpuFanDuty == null))
			{
				await _hw.WaitForStateAsync(TableIsThisSlot, TimeSpan.FromMilliseconds(1500L), ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			ct.ThrowIfCancellationRequested();
			bool includeFanCurves = TableIsThisSlot();
			PerfModeSettings perfModeSettings = SnapshotLive(includeFanCurves).FillNulls(current);
			if (!(perfModeSettings.FirmwareSignature() == current.FirmwareSignature()))
			{
				PerfModeStore.SaveSettings(mode.Id, perfModeSettings);
				IReadOnlyList<FirmwareSlotState> slotStates = PerfModeStore.LoadSlots(SlotCount);
				PerfModeStore.SaveSlots(FirmwareSlotPlanner.Assign(slotStates, slot, mode.Id, slotStates.Any((FirmwareSlotState s) => s.Index == slot && s.OwnerModeId == mode.Id && s.Signature == current.FirmwareSignature()) ? perfModeSettings.FirmwareSignature() : null, PerfModeStore.NowStamp()));
				Logger.WriteLine($"PerfModes: captured slot {slot} parameters into {mode.Id}");
				Invalidate();
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("PerfModes: slot capture failed: " + ex2.Message);
		}
		bool TableIsThisSlot()
		{
			return string.Equals(_hw.CurveName, expectedTable, StringComparison.OrdinalIgnoreCase);
		}
	}

	internal PerfLiveSnapshot SnapshotLive(bool includeFanCurves)
	{
		return SnapshotLive(_hw, includeFanCurves);
	}

	internal static PerfLiveSnapshot SnapshotLive(MechrevoHw hw, bool includeFanCurves)
	{
		return new PerfLiveSnapshot((hw.Pl1Adjustable && hw.Pl1 > 0) ? new int?(hw.Pl1) : ((int?)null), (hw.Pl2Adjustable && hw.Pl2 > 0) ? new int?(hw.Pl2) : ((int?)null), (hw.Pl4Adjustable && hw.Pl4 > 0) ? new int?(hw.Pl4) : ((int?)null), hw.TccAdjustable ? new bool?(hw.TccSwitch) : ((bool?)null), (hw.TccAdjustable && hw.TccTarget > 0) ? new int?(hw.TccTarget) : ((int?)null), (hw.GpuTgpAdjustable && hw.GpuTgp > 0) ? new int?(hw.GpuTgp) : ((int?)null), hw.GpuDynamicBoostAdjustable ? new bool?(hw.GpuDbSwitch) : ((bool?)null), (hw.GpuDynamicBoostAdjustable && hw.GpuDb >= 0) ? new int?(hw.GpuDb) : ((int?)null), hw.SupportsFanSwitchSpeed ? new bool?(hw.FanSwitchSpeedEnabled) : ((bool?)null), (hw.SupportsFanSwitchSpeed && hw.FanSwitchSpeed > 0) ? new int?(hw.FanSwitchSpeed) : ((int?)null), (includeFanCurves && hw.FanCurveSeen) ? Duty(hw.CpuCurveDuty) : null, (includeFanCurves && hw.FanCurveSeen) ? Duty(hw.GpuCurveDuty) : null, hw.SupportsGpuOverclock ? new bool?(hw.GpuOverclockEnabled) : ((bool?)null), hw.SupportsGpuOverclock ? new int?(hw.EffectiveGpuCoreClockOffset) : ((int?)null), hw.SupportsGpuOverclock ? new int?(hw.EffectiveGpuMemoryClockOffset) : ((int?)null));
		static int[]? Duty(byte[] raw)
		{
			if (raw == null || raw.Length != 16 || !raw.Any((byte b) => b > 0))
			{
				return null;
			}
			return raw.Select((byte b) => Math.Clamp((int)b, 0, 100)).ToArray();
		}
	}

	public async Task<PerfApplyOutcome?> UpdateAsync(string modeId, Func<PerfModeSettings, PerfModeSettings> edit, string reason, bool applyHardware = true)
	{
		ArgumentNullException.ThrowIfNull(edit, "edit");
		int request = Volatile.Read(in _requestSeq);
		(PerfApplyOutcome, bool) tuple = await UpdateCoreAsync(modeId, edit, reason, applyHardware, request).ConfigureAwait(continueOnCapturedContext: false);
		var (perfApplyOutcome, _) = tuple;
		return (!tuple.Item2) ? perfApplyOutcome : (await ActivateIfCurrentAsync(modeId, "route changed by edit", forceParameters: true, request).ConfigureAwait(continueOnCapturedContext: false));
	}
	// （机主：无界 APU 点自定义后模式仍显示「平衡」）
	internal void ActivateLocalProfile(string modeId)
	{
		PerfModeDefinition mode = Find(modeId);
		if ((object)mode == null || !mode.IsCustom)
		{
			return;
		}
		string previousActive = PerfModeStore.ActiveModeId;
		PerfModeStore.ActiveModeId = mode.Id;
		PerfModeStore.SetActiveFor(PowerSourceKey(), mode.Id);
		PerfModeStore.LastCustomId = mode.Id;
		string backendName = ((_hw != null && _hw.SmuCustomApu) ? (_hw.WujieSmuApu ? "无界 APU" : "星耀 AMD") : "宝龙达");
		string activateLog = "[perfmode] " + backendName + "：选自定义档 ⇒ 本机配置激活（ActiveModeId=" + previousActive + " → " + mode.Id + "（已落盘 perf_active）· 不发固件档命令）";
		if (Logger.TryMarkChanged("wujie-local-profile-active", activateLog))
		{
			Logger.WriteError(activateLog);
		}
		RaiseActiveChanged();
	}
	// <summary> （启动预热顺序）：④ 模式对账（串行 await；Wujie 已有“不被固件覆盖”门控）</summary>
	internal Task ReconcileForStartupAsync()
	{
		return ReconcileAsync("startup warmup");
	}
	// <para> 幂等：先读当前值，与配置一致 ⇒ 跳过；失败不重试、只记日志 </para>
	internal async Task ReplayLocalProfileOnStartupAsync()
	{
		MechrevoHw hw = _hw;
		if (hw == null)
		{
			return;
		}
		// 同方 GCU（30/40 系）按 LM 本地保存的自定义档恢复（更新/重装 GCU 后不丢配置）
		bool gen40Gcu = hw.IsGcuConnected && (hw.DgpuGeneration == DgpuGenerationKind.Gen40
			|| hw.DgpuGeneration == DgpuGenerationKind.Gen30);
		if (!hw.SmuCustomApu && !hw.UsesBaolongda && !gen40Gcu)
		{
			return;
		}
		bool isWujie = hw.SmuCustomApu;
		bool isBaolongda = !isWujie && hw.UsesBaolongda;
		bool isGcu = !isWujie && !isBaolongda;
		if (isBaolongda)
		{
			string stateLog = "[perfmode] 宝龙达启动：PerfModeStore.ActiveModeId=" + PerfModeStore.ActiveModeId
				+ "（HasStoredActive=" + PerfModeStore.HasStoredActive + "）· 固件回读=" + hw.OperatingMode;
			if (Logger.TryMarkChanged("baolongda-startup-state", stateLog))
			{
				Logger.WriteError(stateLog);
			}
		}
		if (!PerfModeStore.HasStoredActive)
		{
			Logger.WriteError("[perfmode] 启动恢复：没有保存的活动档（HasStoredActive=False）⇒ 不重放；界面可能按固件回读显示默认档");
			return;
		}
		PerfModeDefinition mode = Find(ActiveModeId);
		// 30 系内置档：按「模式 × 配置号」用 LM 存档重放一次
		if ((object)mode != null && !mode.IsCustom && hw.IsGcuConnected && hw.DgpuGeneration == DgpuGenerationKind.Gen30)
		{
			int visualMode30 = mode.Kind switch
			{
				PerfModeKind.Turbo => 1,
				PerfModeKind.Silent => 2,
				_ => 0
			};
			int profileIndex30 = ModeProfileConfig.Get(visualMode30);
			PerfModeSettings storedProfile30 = PerfModeStore.LoadSettings("fw" + visualMode30.ToString() + "p" + profileIndex30.ToString());
			if (!storedProfile30.IsUntouched)
			{
				PerfModeStore.SaveSettings(mode.Id, storedProfile30);
				ActivateLocalProfile(mode.Id);
				try
				{
					await ActivateAsync(mode.Id, "30 series profile replay", forceParameters: true).ConfigureAwait(continueOnCapturedContext: false);
					Logger.WriteError($"[perfmode] 30 系启动恢复：模式={visualMode30} 配置={profileIndex30 + 1} ⇒ 按 LM 存档重放一次");
				}
				catch (Exception ex)
				{
					Logger.WriteError("[perfmode] 30 系配置重放失败（已吞掉）：" + ex.Message);
				}
			}
			return;
		}
		// ⇒ 其它档位只恢复「保存的当前档位本身就是自定义」的情况
		if ((object)mode == null || !mode.IsCustom)
		{
			string skipLog = "[perfmode] 启动恢复：保存的当前档不是自定义（ActiveModeId=" + ActiveModeId
				+ "）⇒ 不切模式、不重放自定义参数";
			if (Logger.TryMarkChanged("local-startup-skip-notcustom", skipLog))
			{
				Logger.WriteError(skipLog);
			}
			return;
		}
		// ⭐ 把当前模式设为该档（持久化 + RaiseActiveChanged）✅
		ActivateLocalProfile(mode.Id);
		string backendLabel = isWujie ? (hw.WujieSmuApu ? "无界 APU" : "星耀 AMD") : (isBaolongda ? "宝龙达" : "同方 GCU");
		string modeLog = isWujie
			? "[perfmode] 无界 APU 启动恢复（晚于 GCU 各项）：ActiveModeId=" + mode.Id + " ⇒ 重放 SMU"
			: "[perfmode] 宝龙达启动恢复：ActiveModeId=" + mode.Id + " ⇒ 模式=自定义";
		if (Logger.TryMarkChanged("local-startup-mode-" + backendLabel, modeLog))
		{
			Logger.WriteError(modeLog);
		}

		PerfModeSettings settings = mode.Settings;
		// 宝龙达重启后固件参数可能已被复位，但 WMI 回读可能仍是**缓存/默认值**恰好与配置相同 ⇒ 用「一致就跳过」会漏应用 ❌
		bool match;
		if (isWujie)
		{
		// 2026-10-21：无界开机“默认自定义但没应用”：SMU 回读可能还是固件默认/缓存值，
		// 恰好与配置相同 ⇒ 误判“已应用”而跳过重放；启动恢复一律强制重放一次（幂等）。
		if (TryReadWujieSmuCurrent(out int curPl1, out int curPl2, out int curTctl))
		{
			Logger.WriteError("[perfmode] 无界启动恢复：SMU 当前 PL1=" + curPl1 + "/PL2=" + curPl2 + "/tctl=" + curTctl
				+ " · 配置 PL1=" + settings.Pl1.GetValueOrDefault() + "/PL2=" + settings.Pl2.GetValueOrDefault() + "/tctl=" + settings.TccTarget.GetValueOrDefault());
		}
		match = false;
		}
		else if (isBaolongda)
		{
			match = BaolongdaLocalValuesMatch(settings, hw);
			if (match)
			{
				Logger.WriteLine("[perfmode] 宝龙达启动恢复：回读与配置一致，但按机主要求仍**强制应用一次**（30/40 系通用）");
			}
			match = false;
		}
		else
		{
			// 同方 GCU：以 LM 本地配置为准，启动强制应用一次
			match = false;
		}
		if (match)
		{
			string skip = "[perfmode] " + backendLabel + " 启动恢复：当前值与配置一致 ⇒ 跳过（ActiveModeId=" + mode.Id + "）";
			if (Logger.TryMarkChanged("local-startup-skip-" + backendLabel, skip))
			{
				Logger.WriteError(skip);
			}
			return;
		}
		string replay = isWujie
			? "[perfmode] 无界 APU 启动恢复：ActiveModeId=" + mode.Id + " ⇒ 重放本机 SMU（PL1="
				+ settings.Pl1.GetValueOrDefault() + "/PL2=" + settings.Pl2.GetValueOrDefault() + "/tctl=" + settings.TccTarget.GetValueOrDefault() + "）"
			: "[perfmode] " + backendLabel + " 启动恢复：ActiveModeId=" + mode.Id + " ⇒ 重放本机参数（PL1="
				+ settings.Pl1.GetValueOrDefault() + "/PL2=" + settings.Pl2.GetValueOrDefault() + "/tctl=" + settings.TccTarget.GetValueOrDefault() + "）";
		// 2026-10-21（真机：启动空闲预热的重放与自定义页应用并发，两边互写导致值来回跳）
		// 最近 10 秒内已经有过下发 ⇒ 用户/面板在管事，启动重放让位，避免并发覆盖。
		long lastApply = Interlocked.Read(ref _lastApplyTick);
		if (lastApply != 0 && Environment.TickCount64 - lastApply < 10000)
		{
			Logger.WriteError("[perfmode] " + backendLabel + " 启动恢复：最近 10 秒内已有下发（用户/面板在操作）⇒ 跳过本次启动重放，避免并发覆盖");
			return;
		}
		if (Logger.TryMarkChanged("local-startup-replay-" + backendLabel, replay))
		{
			Logger.WriteError(replay);
		}
		try
		{
			await ActivateAsync(mode.Id, isWujie ? "wujie startup replay" : (isBaolongda ? "baolongda startup replay" : "gcu startup replay"), forceParameters: true).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Logger.WriteError("[perfmode] " + backendLabel + " 启动恢复失败（已吞掉、不重试）：" + ex.Message);
			Logger.WriteError("[perfmode] " + backendLabel + "：重放失败 ⇒ 高亮仍按自定义（参数未恢复）· ActiveModeId=" + ActiveModeId);
		}
	}

	/// <summary>⭐ 兼容旧调用：无界专用入口（转发到泛化方法）✅</summary>
	internal Task ReplayWujieLocalProfileOnStartupAsync()
	{
		return ReplayLocalProfileOnStartupAsync();
	}

	/// <summary>⭐ 幂等判据（宝龙达）：配置里设过的项与当前 WMI 本机值一致（±2W / ±1℃）⇒ 无需重写 ✅</summary>
	private static bool BaolongdaLocalValuesMatch(PerfModeSettings settings, MechrevoHw hw)
	{
		int curPl1 = hw.Pl1;
		int curPl2 = hw.Pl2;
		int curTctl = hw.TccTarget;
		if (settings.Pl1.HasValue && (curPl1 <= 0 || Math.Abs(settings.Pl1.Value - curPl1) > 2))
		{
			return false;
		}
		if (settings.Pl2.HasValue && (curPl2 <= 0 || Math.Abs(settings.Pl2.Value - curPl2) > 2))
		{
			return false;
		}
		if (settings.TccTarget.HasValue && (curTctl <= 0 || Math.Abs(settings.TccTarget.Value - curTctl) > 1))
		{
			return false;
		}
		return true;
	}
	/// <summary>⭐ 只读：SMU 当前 PL1(slow)/PL2(fast)/tctl（读不到返回 false；⛔ 不写任何东西）✅</summary>
	private static bool TryReadWujieSmuCurrent(out int pl1, out int pl2, out int tctl)
	{
		pl1 = (pl2 = (tctl = -1));
		try
		{
			PawnIO.RyzenSmuService smu = ModeControl.SharedSmu;
			if (smu == null)
			{
				return false;
			}
			PawnIO.PowerLimits limits = smu.GetPowerLimits();
			if (limits == null)
			{
				return false;
			}
			pl1 = (int)Math.Round((double)limits.Slow);
			pl2 = (int)Math.Round((double)limits.Fast);
			tctl = (int)Math.Round((double)limits.TctlTemp);
			return pl1 > 0 && pl2 > 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>⭐ 幂等判据：配置里设过的项与当前 SMU 值一致（±2W / ±1℃）⇒ 无需重写 ✅</summary>
	private static bool WujieSmuValuesMatch(PerfModeSettings settings, int curPl1, int curPl2, int curTctl)
	{
		if (settings.Pl1.HasValue && Math.Abs(settings.Pl1.Value - curPl1) > 2)
		{
			return false;
		}
		if (settings.Pl2.HasValue && Math.Abs(settings.Pl2.Value - curPl2) > 2)
		{
			return false;
		}
		if (settings.TccTarget.HasValue && Math.Abs(settings.TccTarget.Value - curTctl) > 1)
		{
			return false;
		}
		return true;
	}
	internal void CancelPendingApply()
	{
		CancellationTokenSource activationCts;
		CancellationTokenSource editCts;
		lock (_requestLock)
		{
			Interlocked.Increment(ref _requestSeq);
			Interlocked.Increment(ref _guardSeq);
			activationCts = _activationCts;
			editCts = _editCts;
		}
		try
		{
			activationCts?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		try
		{
			editCts?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private async Task<(PerfApplyOutcome? Outcome, bool NeedsActivation)> UpdateCoreAsync(string modeId, Func<PerfModeSettings, PerfModeSettings> edit, string reason, bool applyHardware, int request)
	{
		using CancellationTokenSource editCts = new CancellationTokenSource();
		await _gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		Interlocked.Increment(ref _activating);
		Interlocked.Increment(ref _guardSeq);
		try
		{
			PerfModeDefinition mode = Find(modeId);
			if ((object)mode == null)
			{
				return (Outcome: null, NeedsActivation: false);
			}
			PerfModeSettings settings = mode.Settings;
			PerfModeSettings after = edit(settings);
			if (after == settings)
			{
				return (Outcome: null, NeedsActivation: false);
			}
			if (!mode.IsCustom && !settings.NeedsFirmwareSlot && after.NeedsFirmwareSlot)
			{
				PerfModeSettings vendorSeed = VendorModeDefaults.Seed(mode.Kind, _hw);
				PerfLiveSnapshot live = (string.Equals(ActiveModeId, mode.Id, StringComparison.Ordinal) ? SnapshotLive(includeFanCurves: false) : null);
				after = PerfModeResolver.Materialize(after, vendorSeed, live);
				Logger.WriteLine("PerfModes: " + mode.Id + " now runs on a firmware slot (seeded from vendor defaults)");
			}
			PerfModeStore.SaveSettings(mode.Id, after);
			Invalidate();
			PerfModeDefinition perfModeDefinition = mode.With(after);
			lock (_requestLock)
			{
				if (!applyHardware || request != _requestSeq || PendingModeId != null)
				{
					return (Outcome: null, NeedsActivation: false);
				}
				_editCts = editCts;
			}
			// 而这里原来只在 ActiveModeId == mode.Id 时才走硬件下发 ⇒
			bool isActiveMode = string.Equals(ActiveModeId, mode.Id, StringComparison.Ordinal);
			bool localCustomApply = PerfApplyGate.ShouldApplyLocalCustom(_hw != null && _hw.UsesBaolongda, mode.IsCustom, isActiveMode, _hw != null && _hw.SmuCustomApu);
			if (localCustomApply)
			{
				MechrevoHw hw = _hw;
				if (hw != null && hw.IsConnected)
				{
					IReadOnlyList<FirmwareSlotState> slots = PerfModeStore.LoadSlots(SlotCount);
					bool flag = PerfModeResolver.IsFirmwareOn(mode, slots, _hw.OperatingMode, _hw.CustomProfileIndex, MechrevoService.IsSilentTurboActive, _hw.Capabilities.SilentTurboAvailability == FeatureAvailability.Supported);
					if (perfModeDefinition.Route != mode.Route || !flag)
					{
						return (Outcome: null, NeedsActivation: true);
					}
					PerfModeSettings settings2 = PerfModeResolver.Delta(settings, after);
					FirmwareSlotPlan plan = null;
					if (perfModeDefinition.Route == PerfModeRoute.FirmwareSlot)
					{
						FirmwareSlotState firmwareSlotState = slots.FirstOrDefault((FirmwareSlotState s) => string.Equals(s.OwnerModeId, mode.Id, StringComparison.Ordinal));
						if ((object)firmwareSlotState == null)
						{
							return (Outcome: null, NeedsActivation: true);
						}
						if (firmwareSlotState.Signature != settings.FirmwareSignature())
						{
							settings2 = after;
						}
						plan = new FirmwareSlotPlan(firmwareSlotState.Index, NeedsParameterWrite: true, "edit");
					}
					IReadOnlyList<PerfApplyStep> steps = PerfModeApplyPlanner.Build(perfModeDefinition with
					{
						Settings = settings2
					}, plan, Capabilities(), includeSwitch: false, perfModeDefinition.Route);
					// （机主：「显卡超频设置了但频率没变化」· 可观测性①）：编辑路径同样落规划结果
					LogGpuOverclockPlan(perfModeDefinition with
					{
						Settings = settings2
					}, plan, Capabilities(), steps);
					PerfApplyOutcome perfApplyOutcome;
					try
					{
						perfApplyOutcome = await RunStepsAsync(mode.Id, steps, editCts.Token).ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (OperationCanceledException) when (editCts.IsCancellationRequested)
					{
						if ((object)plan != null)
						{
							PerfModeStore.SaveSlots(slots.Select((FirmwareSlotState s) => (s.Index != plan.SlotIndex) ? s : s with
							{
								Signature = null
							}).ToArray());
						}
						return (Outcome: null, NeedsActivation: false);
					}
					Logger.WriteLine($"PerfModes: edit {mode.Id} ({reason}) -> {perfApplyOutcome.Describe()}");
					if ((object)plan != null)
					{
						PerfModeStore.SaveSlots(FirmwareSlotPlanner.Assign(slots, plan.SlotIndex, mode.Id, perfApplyOutcome.FirmwareParametersIssued ? after.FirmwareSignature() : null, PerfModeStore.NowStamp()));
					}
					StartGuard(mode.Id);
					RaiseApplied(perfApplyOutcome);
					return (Outcome: perfApplyOutcome, NeedsActivation: false);
				}
			}
			return (Outcome: null, NeedsActivation: false);
		}
		finally
		{
			lock (_requestLock)
			{
				if (_editCts == editCts)
				{
					_editCts = null;
				}
			}
			Interlocked.Decrement(ref _activating);
			_gate.Release();
		}
	}

	public async Task<PerfApplyOutcome?> EnsureFirmwareSlotAsync(string modeId)
	{
		PerfModeDefinition perfModeDefinition = Find(modeId);
		if ((object)perfModeDefinition == null || perfModeDefinition.Route == PerfModeRoute.FirmwareSlot)
		{
			return null;
		}
		PerfModeSettings seed = VendorModeDefaults.Seed(perfModeDefinition.Kind, _hw);
		PerfLiveSnapshot live = (string.Equals(ActiveModeId, perfModeDefinition.Id, StringComparison.Ordinal) ? SnapshotLive(includeFanCurves: true) : null);
		return await UpdateAsync(modeId, (PerfModeSettings s) => PerfModeResolver.Materialize(s, seed, live), "fan curve editing").ConfigureAwait(continueOnCapturedContext: false);
	}

	public void RecordFanCurves(int[]? cpuDuty, int[]? gpuDuty)
	{
		RecordFanCurves(null, cpuDuty, gpuDuty);
	}

	/// <summary>
	/// 2026-10-10（用户反馈 ②「自定义模式风扇表不能保存」）：
	/// 原实现把风扇曲线记进 <see cref="Active" />（**当时恰好活动的那个档位**），且在
	/// 「找不到档位 / 该档不是固件槽档」时**无声 return**（一个字都不写、也不留日志）。
	/// 风扇曲线窗口（<c>FanCurveForm.FlushPendingAsync</c>）那边是「写 EC + 700ms 回读比对一致」
	/// 就报「已保存」的，配置侧却可能什么都没有 ⇒ 之后任何一次切档/应用（forceParameters=true）
	/// 都会用旧曲线**全量重写** EC，用户看到的就是"风扇表保存不了"。
	/// 现在：① 支持显式目标档位（从自定义模式打开时传编辑中的那个档，不再赌"活动档=编辑档"）；
	/// ② 找不到档位或不是固件槽档时，**留一行日志说明**，不再静默。
	/// ⚠️ 落盘键名/写入点一个字没改：仍是 <c>perf_&lt;档位id&gt;_fancpu</c> / <c>_fangpu</c>（PerfModeStore.SaveSettings）。
	/// </summary>
	/// <summary>风扇曲线下发开关（perf_&lt;档&gt;_fanon）：关闭后只保存曲线、不随档位下发。</summary>
	public void RecordFanCurveEnabled(string targetModeId, bool enabled)
	{
		PerfModeDefinition mode = (string.IsNullOrWhiteSpace(targetModeId) ? Active : Find(targetModeId));
		if (mode == null)
		{
			return;
		}
		PerfModeSettings settings = PerfModeStore.LoadSettings(mode.Id) with { FanCurveOn = enabled };
		PerfModeStore.SaveSettings(mode.Id, settings);
		Invalidate();
		Logger.WriteLine("[fan] 曲线下发开关：" + (enabled ? "开" : "关") + "（档位=" + mode.Id + "）");
	}

	public void RecordFanCurves(string targetModeId, int[] cpuDuty, int[] gpuDuty)
	{
		PerfModeDefinition mode = (string.IsNullOrWhiteSpace(targetModeId) ? Active : Find(targetModeId));
		if ((object)mode == null)
		{
			Logger.WriteLine($"[fan] RecordFanCurves 跳过：目标档位={targetModeId ?? "(未指定，用当前活动档)"} 找不到 ⇒ 这条曲线只写了 EC、没有写进配置");
			return;
		}
		if (mode.Route != PerfModeRoute.FirmwareSlot)
		{
			Logger.WriteLine($"[fan] RecordFanCurves 跳过：档位 {mode.Id} 的 route={mode.Route}（不是固件槽档）⇒ 这条曲线只写了 EC、没有写进配置；要保存曲线请先让该档固定到固件槽");
			return;
		}
		bool flag = cpuDuty != null && cpuDuty.Length == 16;
		bool flag2 = gpuDuty != null && gpuDuty.Length == 16;
		PerfModeSettings perfModeSettings = mode.Settings with
		{
			CpuFanDuty = (flag ? cpuDuty.ToArray() : mode.Settings.CpuFanDuty),
			GpuFanDuty = (flag2 ? gpuDuty.ToArray() : mode.Settings.GpuFanDuty)
		};
		if (!(perfModeSettings.FirmwareSignature() == mode.Settings.FirmwareSignature()))
		{
			try
			{
				// 2026-10-09（75:00）：曲线窗口保存成功（写 EC + 700ms 回读比对通过）后会调到这里。
				// 只记「这一侧真正被记下的值」，null 一侧写 (保留原值) —— 免得把「没发的那一侧」误读成刚写的。
				string text = flag ? string.Join(",", cpuDuty) : "(保留原值)";
				string text2 = flag2 ? string.Join(",", gpuDuty) : "(保留原值)";
				Logger.WriteLine($"[fan] RecordFanCurves cpu={text} gpu={text2} → perf_{mode.Id}_fancpu/perf_{mode.Id}_fangpu 已写");
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[fan] RecordFanCurves 日志写入失败: " + ex.Message);
			}
			PerfModeStore.SaveSettings(mode.Id, perfModeSettings);
			IReadOnlyList<FirmwareSlotState> readOnlyList = PerfModeStore.LoadSlots(SlotCount);
			FirmwareSlotState firmwareSlotState = readOnlyList.FirstOrDefault((FirmwareSlotState x) => string.Equals(x.OwnerModeId, mode.Id, StringComparison.Ordinal));
			if ((object)firmwareSlotState != null)
			{
				PerfModeStore.SaveSlots(FirmwareSlotPlanner.Assign(readOnlyList, firmwareSlotState.Index, mode.Id, perfModeSettings.FirmwareSignature(), PerfModeStore.NowStamp()));
			}
			Invalidate();
		}
	}

	public int? SlotOf(string modeId)
	{
		return PerfModeStore.LoadSlots(SlotCount).FirstOrDefault((FirmwareSlotState s) => string.Equals(s.OwnerModeId, modeId, StringComparison.Ordinal))?.Index;
	}

	public async Task<bool> ResetAsync(string modeId)
	{
		PerfModeDefinition mode = Find(modeId);
		if ((object)mode == null)
		{
			return false;
		}
		bool active = string.Equals(ActiveModeId, mode.Id, StringComparison.Ordinal);
		await _gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			PerfModeStore.ResetSettings(mode.Id);
			if (!mode.IsCustom)
			{
				PerfModeStore.SaveSlots(FirmwareSlotPlanner.Release(PerfModeStore.LoadSlots(SlotCount), mode.Id));
			}
			Invalidate();
		}
		finally
		{
			_gate.Release();
		}
		if (active)
		{
			MechrevoHw hw = _hw;
			if (hw != null && hw.IsConnected)
			{
				// ① 界面把「恢复出厂」显示成**失败**；② 硬件侧**一个字节都没动**（PL/温度墙/GPU 超频/NVML 功耗上限）
				if (mode.IsCustom && hw.UsesBaolongda)
				{
					return ResetBaolongdaCustomLocally(hw);
				}
				if (mode.IsCustom && hw.SmuCustomApu)
				{
					return ResetWujieCustomLocally(hw);
				}
				if (!mode.IsCustom)
				{
					PerfApplyOutcome perfApplyOutcome = await ActivateAsync(mode.Id, "reset").ConfigureAwait(continueOnCapturedContext: false);
					if ((object)perfApplyOutcome == null || !perfApplyOutcome.ModeSwitched)
					{
						return false;
					}
					return await RestoreVendorProfileAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				bool restored = await RestoreVendorProfileAsync().ConfigureAwait(continueOnCapturedContext: false);
				FirmwareSlotState firmwareSlotState = PerfModeStore.LoadSlots(SlotCount).FirstOrDefault((FirmwareSlotState s) => string.Equals(s.OwnerModeId, mode.Id, StringComparison.Ordinal));
				if (restored && (object)firmwareSlotState != null)
				{
					await CaptureSlotAsync(mode.With(PerfModeSettings.Default), firmwareSlotState.Index).ConfigureAwait(continueOnCapturedContext: false);
				}
				return restored;
			}
		}
		return true;
	}
	// （机主：「恢复出厂设置」不生效 · 30/40 系宝龙达）
	private bool ResetBaolongdaCustomLocally(MechrevoHw hw)
	{
		bool is30 = hw.IsBaolongda30Series;
		bool powerLimitReleased = false;
		bool powerLimitConfirmed = false;
		if (!is30)
		{
			// 40 系：功能号 23 单字节 0 = 关闭自定义 ⇒ 固件恢复默认功耗墙（协议未改）
			powerLimitReleased = hw.TryApplyCpuPowerLimitViaBackend(enable: false, 0, 0, 0, out powerLimitConfirmed);
		}
		// ⭐ 40 系：**回读确认**关闭成功才算功耗墙恢复（写不进去/固件不认 ⇒ 如实 false）；
		//   30 系：本机配置清空即算恢复（SMU 侧无恢复命令，日志如实说明）✅
		bool powerLimitResetOk = is30 || (powerLimitReleased && powerLimitConfirmed);
		// ⭐ 只有权限恢复成功才清本地记忆；失败则**保留**（界面继续显示硬件真实值，⛔ 不假装"未设置"）✅
		if (powerLimitResetOk)
		{
			hw.ClearBaolongdaPowerLimitMemory();
		}
		// ④ GPU 超频：P0 0/0 + 清直写记录 / gpu_oc
		bool gpuOcExpected = hw.SupportsGpuOverclock;
		bool gpuOcReset = NvapiPStates.TrySetP0Offsets(0, 0, out int appliedCore, out int appliedMem);
		// 直写记录（direct_gpu_oc_*）也要清：否则下次切到该自定义档时
		// `RestoreDirectGpuOverclockProfile` 会把刚才的偏移又推回去（⛔ 重置等于白做）
		hw.SelectGcuGpuOverclockBackend();
		AppConfig.Set("gpu_oc", 0);
		AppConfig.Flush();
		MechrevoService.GpuOverclockEnabled = false;
		if (gpuOcExpected && (!gpuOcReset || appliedCore != 0 || appliedMem != 0))
		{
			Logger.WriteError($"[reset] ⚠️ GPU 偏移恢复默认**未确认**：写成功={gpuOcReset} · 驱动回读 core={appliedCore} mem={appliedMem} MHz"
				+ "（如实记录，⛔ 不假装已恢复）");
		}
		// ⑤ 显卡功耗上限：写回驱动默认值（读不到默认值 ⇒ 不动，如实记日志）
		string powerDefaultText = "默认值读不到（未改）";
		if (NvmlHelper.TryGetDefaultPowerLimitWatts(out int defaultW) && NvmlHelper.TrySetPowerLimitWatts(defaultW, out int appliedW))
		{
			powerDefaultText = appliedW + "W";
		}
		// ⭐ 后端恢复明细（⭐ 机主要求的那一行 `[reset] 自定义档已恢复默认 ⇒ …` 由 CustomModeForm 统一落盘）
		Logger.WriteError($"[reset] 后端恢复明细：功耗墙={(powerLimitResetOk
				? (is30 ? "本机配置已清（SMU 运行时值需重启才回固件默认）" : "WMI 23 已关闭自定义（回读确认）")
				: "⚠️ 未确认（保留硬件当前值，界面不冒充已重置）")}"
			+ $" · GPU 超频=关（NVAPI P0 0/0 写成功={gpuOcReset}）"
			+ $" · 显卡功耗上限={powerDefaultText}"
			+ (is30
				? " · ⚠️ 30 系 SMU 无「恢复默认」命令 ⇒ 本次开机内 SMU 功耗墙保持当前值（本机配置已清；重启后回固件默认）"
				: $" · WMI 23 关闭自定义 写成功={powerLimitReleased} · 回读确认={powerLimitConfirmed}"));
		return powerLimitResetOk;
	}
	/// <summary>无界 APU 自定义档恢复：清本地档位配置 + GPU 超频归零；SMU 功耗墙无恢复命令，重启后回固件默认。</summary>
	private bool ResetWujieCustomLocally(MechrevoHw hw)
	{
		bool gpuOcReset = NvapiPStates.TrySetP0Offsets(0, 0, out int appliedCore, out int appliedMem);
		hw.SelectGcuGpuOverclockBackend();
		AppConfig.Set("gpu_oc", 0);
		AppConfig.Flush();
		MechrevoService.GpuOverclockEnabled = false;
		Logger.WriteError($"[reset] 无界 APU 自定义档：本地配置已清 · GPU 超频=关（写成功={gpuOcReset} · 回读 core={appliedCore} mem={appliedMem} MHz）"
			+ " · SMU 功耗墙为运行时状态、无「恢复默认」命令 ⇒ 重启后回固件默认");
		return true;
	}

	// （机主：「恢复出厂设置」风扇转速/曲线表没恢复 · 强冷也要复位）
	internal void ResetBaolongdaFanToFirmwareDefault()
	{
		MechrevoHw hw = _hw;
		if (hw == null || !hw.UsesBaolongda)
		{
			return;
		}
		// ── ① 强冷复位：开着 ⇒ 先走关闭流程（恢复快照 / FanBoostActive=false / 清临时状态）──
		bool boostWasActive = MechrevoHw.FanBoostActive;
		bool boostConfirmed = false;
		if (boostWasActive)
		{
			hw.TrySetFanBoostViaBackend(false, out boostConfirmed);
		}
		bool boostClosed = !MechrevoHw.FanBoostActive;
		// ── ② 曲线表回默认（按本机代际满转；默认 Enabled=false ⇒ 闭环停用）──
		//   ⭐ 满转基准：⭐ 40 系 **6000** / 30 系 **4900**（⭐ `BaolongdaFanCurveSettings.FullRpm40Series/FullRpm30Series`
		//      —— 与曲线默认值同一套现有常量；⛔ 不临时改基准、⛔ 不拿 `FanRpmMaxForPercent`（30 系那只是÷35 的显示分母）✅
		int fullRpm = (hw.DgpuGeneration == DgpuGenerationKind.Gen30)
			? BaolongdaFanCurveSettings.FullRpm30Series
			: BaolongdaFanCurveSettings.FullRpm40Series;
		BaolongdaFanCurveSettings defaults = BaolongdaFanCurveSettings.CreateDefault(fullRpm);
		bool curveSaved = hw.SaveBaolongdaFanCurve(defaults);
		BaolongdaFanCurveSettings curveNow = hw.LoadBaolongdaFanCurve();
		bool curveDisabled = curveNow != null && !curveNow.Enabled;
		// ── ③ EC 交回固件（目标 0 + 清 0x02|0x08，写后回读）──
		//   ⚠️ 闸门未放开也照做：机主明确要求「清掉可能残留的接管位」✅
		bool released = FanWriter.TryReleaseToFirmware(out string releaseDetail);
		// ── ④ 再读一次 EC（0xC83C/0xC83D/0xB20）：证明真的回默认 ──
		int fan1 = -1;
		int fan2 = -1;
		int mask = -1;
		string readVia;
		bool acpi1 = EcPortIo.TryReadByteStable(EcPortIo.AcpiFan1TargetAddress, out fan1, out _);
		bool acpi2 = EcPortIo.TryReadByteStable(EcPortIo.AcpiFan2TargetAddress, out fan2, out _);
		bool acpi3 = EcPortIo.TryReadByteStable(EcPortIo.AcpiFanManualMaskAddress, out mask, out _);
		if (acpi1 && acpi2 && acpi3)
		{
			readVia = "ACPI-EC";
		}
		else
		{
			// ⭐ ACPI 有任一读不到 ⇒ 用 LPC 通道补读（同一批地址；两通道都只读）✅
			if (!acpi1 && EcLpcIo.TryReadByteStable(EcLpcIo.Fan1TargetAddress, out int l1, out _))
			{
				fan1 = l1;
			}
			if (!acpi2 && EcLpcIo.TryReadByteStable(EcLpcIo.Fan2TargetAddress, out int l2, out _))
			{
				fan2 = l2;
			}
			if (!acpi3 && EcLpcIo.TryReadByteStable(EcLpcIo.FanManualMaskAddress, out int l3, out _))
			{
				mask = l3;
			}
			readVia = "ACPI-EC/LPC";
		}
		bool targetsZero = fan1 == 0 && fan2 == 0;
		bool maskClean = mask >= 0 && (mask & 0x0A) == 0;
		// ── ⑤ 日志（默认级别可见）──
		Logger.WriteError($"[reset] 风扇：已交回固件（目标=0 · 掩码清位 ok={released && maskClean} · 通道={FanWriter.ChannelName(FanWriter.PreferredChannel)}）"
			+ $" · 曲线表已重置为默认（满转={defaults.MaxRpm} RPM · 16 点 · 保存 ok={curveSaved}）· 闭环已停用（enabled={(curveNow != null ? curveNow.Enabled : false)}，应 false）");
		Logger.WriteError($"[reset] 强冷：已关闭并交回固件（FanBoostActive={(MechrevoHw.FanBoostActive ? "true" : "false")} · 掩码={(mask < 0 ? "读不到" : "0x" + mask.ToString("X2"))} 回读 ok={maskClean}）"
			+ (boostWasActive ? $" · 原状态=开 ⇒ 已走关闭流程（快照恢复={boostConfirmed}）" : " · 原状态=关（无需关闭流程）"));
		Logger.WriteError($"[reset] 风扇 EC 回读（{readVia}）：0xC83C={(fan1 < 0 ? "读不到" : fan1.ToString())} · 0xC83D={(fan2 < 0 ? "读不到" : fan2.ToString())}"
			+ $" · 0xB20={(mask < 0 ? "读不到" : "0x" + mask.ToString("X2"))} ⇒ 目标归零={targetsZero} · 接管位(0x02|0x08)已清={maskClean}"
			+ $" · 诊断标记 FanCurveVerified={hw.FanCurveVerified}（仅诊断；UI/写入已直接开放）"
			+ (string.IsNullOrEmpty(releaseDetail) ? "" : " · " + releaseDetail));
		if (!boostClosed || !curveDisabled || !released || !targetsZero || !maskClean)
		{
			Logger.WriteError("[reset] ⚠️ 风扇恢复默认**未全部确认**（如实记录，⛔ 不假装成功）："
				+ $"强冷已关={boostClosed} · 闭环已停={curveDisabled} · EC交回={released} · 目标归零={targetsZero} · 接管位已清={maskClean}");
		}
	}

	private async Task<bool> RestoreVendorProfileAsync()
	{
		try
		{
			long statusBefore = _hw.FanStatusVersion;
			long tableBefore = _hw.FanTableVersion;
			await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "RESTORE_OPERATING_MODE_DETAIL" }).ConfigureAwait(continueOnCapturedContext: false);
			await _hw.Publish("Fan/Control", new Dictionary<string, object>
			{
				["Action"] = "RESTORE_FAN_SPEED_CURVE_SETTING",
				["Name"] = _hw.TableName
			}).ConfigureAwait(continueOnCapturedContext: false);
			await Task.Delay(300).ConfigureAwait(continueOnCapturedContext: false);
			await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
			await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" }).ConfigureAwait(continueOnCapturedContext: false);
			return await _hw.WaitForStateAsync(() => _hw.FanStatusVersion > statusBefore && _hw.FanTableVersion > tableBefore, TimeSpan.FromMilliseconds(3000L)).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModes: vendor restore failed: " + ex.Message);
			return false;
		}
	}

	public PerfModeDefinition? AddCustom(string? seedFromId)
	{
		PerfModeSettings settings = EffectiveSettings(Find(seedFromId));
		PerfModeDefinition perfModeDefinition = PerfModeStore.AddCustom();
		if ((object)perfModeDefinition == null)
		{
			return null;
		}
		PerfModeStore.SaveSettings(perfModeDefinition.Id, settings);
		Invalidate();
		return Find(perfModeDefinition.Id);
	}

	private PerfModeSettings EffectiveSettings(PerfModeDefinition? mode)
	{
		if ((object)mode == null)
		{
			return PerfModeSettings.Default;
		}
		bool flag = string.Equals(ActiveModeId, mode.Id, StringComparison.Ordinal) && _hw.IsConnected;
		PerfModeSettings perfModeSettings = mode.Settings;
		if (!mode.IsCustom && mode.Route == PerfModeRoute.BuiltIn)
		{
			perfModeSettings = PerfModeResolver.Materialize(perfModeSettings, VendorModeDefaults.Seed(mode.Kind, _hw), flag ? SnapshotLive(includeFanCurves: false) : null);
		}
		else if (flag)
		{
			perfModeSettings = SnapshotLive(includeFanCurves: true).FillNulls(perfModeSettings);
		}
		return perfModeSettings;
	}

	public bool RemoveCustom(string id)
	{
		bool flag = string.Equals(ActiveModeId, id, StringComparison.Ordinal);
		if (!PerfModeStore.RemoveCustom(id))
		{
			return false;
		}
		if (string.Equals(PerfModeStore.LastCustomId, id, StringComparison.Ordinal))
		{
			PerfModeStore.LastCustomId = null;
		}
		Invalidate();
		if (flag)
		{
			ActivateAsync(PerfModeDefinition.BuiltInId(PerfModeKind.Balanced), "active custom mode removed");
		}
		else
		{
			RaiseActiveChanged();
		}
		return true;
	}

	public void Rename(string id, string? name)
	{
		if ((object)Find(id) != null)
		{
			PerfModeStore.SaveName(id, name);
			Invalidate();
		}
	}

	public void OnFirmwareModeChanged()
	{
		if (Volatile.Read(in _activating) > 0)
		{
			return;
		}
		int seq = Interlocked.Increment(ref _reconcileSeq);
		Task.Run(async () =>
		{
			_ = 1;
			try
			{
				await Task.Delay(700).ConfigureAwait(continueOnCapturedContext: false);
				if (seq == Volatile.Read(in _reconcileSeq) && Volatile.Read(in _activating) <= 0)
				{
					await ReconcileAsync("firmware mode changed").ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("PerfModes: reconcile failed: " + ex.Message);
			}
		});
	}

	private async Task ReconcileAsync(string reason)
	{
		PerfModeResolution redirect = null;
		int expectedRequest = Volatile.Read(in _requestSeq);
		await _gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (PendingModeId != null || expectedRequest != Volatile.Read(in _requestSeq))
			{
				return;
			}
			MechrevoHw hw = _hw;
			if (hw == null || (!hw.IsConnected && !hw.SmuCustomApu))
			{
				return;
			}
			// 无界 APU 的本机自定义档**优先于固件回读**（无固件档语义）——
			if (hw.SmuCustomApu || hw.UsesBaolongda)
			{
				PerfModeDefinition persisted = Find(ActiveModeId);
				if ((object)persisted != null && persisted.IsCustom)
				{
					string keepMsg = "[perfmode] " + (hw.SmuCustomApu ? (hw.WujieSmuApu ? "无界 APU" : "星耀 AMD") : "宝龙达") + "：固件模式回读 ⇒ 保持本机自定义档（ActiveModeId=" + persisted.Id + "，⛔ 不覆盖）";
					if (Logger.TryMarkChanged("wujie-external-mode-change", keepMsg))
					{
						Logger.WriteError(keepMsg);
					}
					return;
				}
			}
			IReadOnlyList<FirmwareSlotState> slots = PerfModeStore.LoadSlots(SlotCount);
			PerfModeResolution perfModeResolution = PerfModeResolver.Resolve(Modes, slots, _hw.OperatingMode, _hw.CustomProfileIndex, MechrevoService.IsSilentTurboActive, PerfModeStore.LastCustomId);
			if ((object)perfModeResolution == null)
			{
				return;
			}
			if (perfModeResolution.Redirect && AllowRedirect())
			{
				Logger.WriteLine($"PerfModes: {reason}: redirect to {perfModeResolution.ModeId} ({perfModeResolution.Reason})");
				redirect = perfModeResolution;
			}
			else
			{
				if (string.Equals(perfModeResolution.ModeId, ActiveModeId, StringComparison.Ordinal))
				{
					return;
				}
				PerfModeDefinition perfModeDefinition = Find(perfModeResolution.ModeId);
				if ((object)perfModeDefinition == null)
				{
					return;
				}
				Logger.WriteLine($"PerfModes: {reason}: follow {perfModeDefinition.Id} ({perfModeResolution.Reason})");
				PerfModeStore.ActiveModeId = perfModeDefinition.Id;
				PerfModeStore.SetActiveFor(PowerSourceKey(), perfModeDefinition.Id);
				if (perfModeDefinition.IsCustom)
				{
					PerfModeStore.LastCustomId = perfModeDefinition.Id;
				}
				RaiseActiveChanged();
				await ApplyAppSideLockedAsync(perfModeDefinition, "follow").ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		finally
		{
			_gate.Release();
		}
		if ((object)redirect != null && PendingModeId == null)
		{
			await ActivateIfCurrentAsync(redirect.ModeId, "redirect: " + redirect.Reason, forceParameters: false, expectedRequest).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private bool AllowRedirect()
	{
		long tickCount = Environment.TickCount64;
		lock (_redirectLock)
		{
			while (_recentRedirects.Count > 0 && tickCount - _recentRedirects.Peek() > 10000)
			{
				_recentRedirects.Dequeue();
			}
			if (_recentRedirects.Count >= 2)
			{
				Logger.WriteLine("PerfModes: redirect budget exhausted, following the external change instead");
				return false;
			}
			_recentRedirects.Enqueue(tickCount);
			return true;
		}
	}

	private async Task<PerfApplyOutcome> ApplyAppSideAsync(PerfModeDefinition mode, string reason)
	{
		await _gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (PendingModeId == null && !(ActiveModeId != mode.Id))
			{
				PerfModeDefinition perfModeDefinition = Find(mode.Id);
				if ((object)perfModeDefinition != null)
				{
					return await ApplyAppSideLockedAsync(perfModeDefinition, reason).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			return new PerfApplyOutcome(mode.Id, Array.Empty<PerfApplyStepOutcome>());
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<PerfApplyOutcome> ApplyAppSideLockedAsync(PerfModeDefinition mode, string reason)
	{
		PerfModeSettings settings = mode.Settings;
		PerfModeSettings settings2 = new PerfModeSettings
		{
			FanBoost = settings.FanBoost,
			WindowsPowerMode = settings.WindowsPowerMode,
			CpuBoost = settings.CpuBoost,
			PowerPlanGuid = settings.PowerPlanGuid,
			RefreshHz = settings.RefreshHz
		};
		IReadOnlyList<PerfApplyStep> steps = PerfModeApplyPlanner.Build(mode with
		{
			Settings = settings2
		}, new FirmwareSlotPlan(0, NeedsParameterWrite: false, reason), Capabilities(), includeSwitch: false, PerfModeRoute.FirmwareSlot);
		PerfApplyOutcome perfApplyOutcome = await RunStepsAsync(mode.Id, steps).ConfigureAwait(continueOnCapturedContext: false);
		if (steps.Count > 0)
		{
			Logger.WriteLine($"PerfModes: app-side {mode.Id} ({reason}) -> {perfApplyOutcome.Describe()}");
		}
		StartGuard(mode.Id);
		RaiseApplied(perfApplyOutcome);
		return perfApplyOutcome;
	}

	public async Task ReplayAsync(string reason, bool powerSourceChanged, bool appSideOnly)
	{
		MechrevoHw hw = _hw;
		if (hw == null || !hw.IsConnected)
		{
			return;
		}
		PerfModeDefinition target = null;
		int powerKey = PowerSourceKey();
		if (powerSourceChanged)
		{
			target = Find(PerfModeStore.ActiveFor(powerKey));
			// 无界 APU：插拔电源不自动跳到「按电源来源记住的自定义档」，保持当前档
			if (_hw != null && _hw.SmuCustomApu && (object)target != null && target.IsCustom
				&& !string.Equals(target.Id, ActiveModeId, StringComparison.Ordinal))
			{
				Logger.WriteLine("[perfmode] 无界 APU：电源变化 ⇒ 保持当前档，不自动跳自定义（" + target.Id + "）");
				target = null;
			}
		}
		if ((object)target == null)
		{
			target = (PerfModeStore.HasStoredActive ? Active : null);
		}
		bool onBattery = powerKey == 0;
		if (onBattery)
		{
			PerfModeKind? kind = target?.Kind;
			bool turboLike = false;
			if (kind.HasValue)
			{
				PerfModeKind valueOrDefault = kind.GetValueOrDefault();
				if ((uint)(valueOrDefault - 2) <= 1u)
				{
					turboLike = true;
				}
			}
			onBattery = turboLike || (powerSourceChanged && (object)target == null);
		}
		if (onBattery)
		{
			target = Find(PerfModeDefinition.BuiltInId(PerfModeKind.Silent));
		}
		if ((object)target == null)
		{
			await ReconcileAsync(reason + " (no stored mode)").ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		IReadOnlyList<FirmwareSlotState> slots = PerfModeStore.LoadSlots(SlotCount);
		bool slotReady = target.Route != PerfModeRoute.FirmwareSlot || slots.Any((FirmwareSlotState s) => s.OwnerModeId == target.Id && s.Signature == target.Settings.FirmwareSignature());
		if ((slotReady & appSideOnly) && string.Equals(target.Id, ActiveModeId, StringComparison.Ordinal))
		{
			await ApplyAppSideAsync(target, reason).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		bool firmwareOn = PerfModeResolver.IsFirmwareOn(target, slots, _hw.OperatingMode, _hw.CustomProfileIndex, MechrevoService.IsSilentTurboActive, _hw.Capabilities.SilentTurboAvailability == FeatureAvailability.Supported);
		if (!(slotReady & firmwareOn) || !string.Equals(target.Id, ActiveModeId, StringComparison.Ordinal))
		{
			await ActivateAsync(target.Id, reason).ConfigureAwait(continueOnCapturedContext: false);
		}
		else
		{
			await ApplyAppSideAsync(target, reason).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private void StartGuard(string modeId)
	{
		int request = Interlocked.Increment(ref _guardSeq);
		if (Program.UiAuditMode)
		{
			return;
		}
		PerfModeDefinition perfModeDefinition = Find(modeId);
		if ((object)perfModeDefinition == null || (!perfModeDefinition.Settings.HasAppSideOverride && !perfModeDefinition.Settings.FanBoost.HasValue))
		{
			return;
		}
		Task.Run(async () =>
		{
			_ = 1;
			try
			{
				int elapsed = 0;
				int[] guardCheckDelaysMs = GuardCheckDelaysMs;
				foreach (int at in guardCheckDelaysMs)
				{
					await Task.Delay(Math.Max(0, at - elapsed)).ConfigureAwait(continueOnCapturedContext: false);
					elapsed = at;
					await _gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
					try
					{
						if (request != Volatile.Read(in _guardSeq) || Volatile.Read(in _activating) > 0 || PendingModeId != null || !string.Equals(ActiveModeId, modeId, StringComparison.Ordinal))
						{
							break;
						}
						// (c)③：有下发在飞行中时不得并发重应用。
						if (ApplyInFlight)
						{
							Logger.WriteLine("PerfModes: guard 有下发在飞行中，本次不并发重应用");
							break;
						}
						PerfModeDefinition perfModeDefinition2 = Find(modeId);
						if ((object)perfModeDefinition2 == null)
						{
							break;
						}
						PerfModeSettings perfModeSettings = ProbeDrift(perfModeDefinition2.Settings, out List<string> driftedKeys);
						// (c)②（2026-10-09，用户原话「OK 按你说的」）：抑制自触发 —— 刚由本进程下发、且目标值一模一样的
						// app-side 设置，在宽限期（SelfIssueGraceMs）内不判"外部改变"：否则切档/应用后 1.5s 的那一拍
						// 几乎必然把"自己刚写下去的值"当成外部改变再写一遍（这正是日志里那句 re-applying 的来源之一）。
						List<string> selfIssued = new List<string>();
						foreach (string key in driftedKeys)
						{
							if (WasSelfIssuedRecently(key, DriftValue(key, perfModeSettings)))
							{
								selfIssued.Add(key);
							}
						}
						if (selfIssued.Count != 0)
						{
							perfModeSettings = ClearDrifted(perfModeSettings, selfIssued);
							Logger.WriteLine($"PerfModes: guard 漂移检查：漂移={driftedKeys.Count}，自触发跳过={selfIssued.Count}（{string.Join(", ", selfIssued)}，等回读），待重应用={driftedKeys.Count - selfIssued.Count}");
						}
						if (perfModeSettings.HasAppSideOverride || perfModeSettings.FanBoost.HasValue)
						{
							// (c)①：只重应用真正有差异（且不是自触发）的字段，日志里能直接看出是哪几个。
							string text = string.Join(", ", driftedKeys.Where((string k) => !selfIssued.Contains(k)));
							Logger.WriteLine("PerfModes: " + modeId + " app-side settings changed externally, re-applying（只重应用 " + (driftedKeys.Count - selfIssued.Count) + " 个字段: " + text + "）");
							IReadOnlyList<PerfApplyStep> steps = PerfModeApplyPlanner.Build(perfModeDefinition2 with
							{
								Settings = perfModeSettings
							}, new FirmwareSlotPlan(0, NeedsParameterWrite: false, "guard"), Capabilities(), includeSwitch: false, PerfModeRoute.FirmwareSlot);
							// (b)(c)：走统一入口 —— 记自触发值 + 登记懒验证（guard 只发 app-side，故不会清掉 GPU 超频的待办）。
							if ((await RunStepsAsync(modeId, steps).ConfigureAwait(continueOnCapturedContext: false)).AnyFailed)
							{
								break;
							}
						}
						else if (selfIssued.Count != 0)
						{
							Logger.WriteLine("PerfModes: guard 本次不重应用（漂移项全部是本进程刚下发的，等回读）");
						}
					}
					finally
					{
						_gate.Release();
					}
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("PerfModes: guard failed: " + ex.Message);
			}
		});
	}

	/// <summary>
	/// (c)（2026-10-09）：app-side 设置的漂移检查（逐项独立判定，未漂移的项一律 null ⇒ 规划器不会为它生成步骤，
	/// 所以"只重应用有差异的字段"这条本来就在这里成立）。<paramref name="drifted" /> 把漂移的**项名**带出去，
	/// 供调用方做"自触发"排除与明确日志。
	/// </summary>
	private PerfModeSettings ProbeDrift(PerfModeSettings s, out List<string> drifted)
	{
		drifted = new List<string>();
		int? windowsPowerMode = s.WindowsPowerMode;
		int num;
		if (windowsPowerMode.HasValue)
		{
			int valueOrDefault = windowsPowerMode.GetValueOrDefault();
			num = ((PowerNative.IsOverlayActive(valueOrDefault) == false) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool flag = (byte)num != 0;
		if (flag)
		{
			drifted.Add(SelfKeyPowerOverlay);
		}
		windowsPowerMode = s.CpuBoost;
		int num2;
		if (windowsPowerMode.HasValue)
		{
			int valueOrDefault2 = windowsPowerMode.GetValueOrDefault();
			if (WinPowerPlan.TryGetBoost(out var ac, out var dc))
			{
				num2 = ((!WinPowerPlan.IsBoostConfirmed(valueOrDefault2, ac, dc)) ? 1 : 0);
				goto IL_006f;
			}
		}
		num2 = 0;
		goto IL_006f;
		IL_006f:
		bool flag2 = (byte)num2 != 0;
		if (flag2)
		{
			drifted.Add(SelfKeyCpuBoost);
		}
		int num3;
		if (!string.IsNullOrEmpty(s.PowerPlanGuid))
		{
			string activePlan = WinPowerPlan.GetActivePlan();
			if (activePlan != null && activePlan.Length > 0)
			{
				num3 = ((!string.Equals(activePlan, s.PowerPlanGuid, StringComparison.OrdinalIgnoreCase)) ? 1 : 0);
				goto IL_00a6;
			}
		}
		num3 = 0;
		goto IL_00a6;
		IL_0166:
		int num4;
		bool flag3 = (byte)num4 != 0;
		bool flag4;
		bool flag5;
		return new PerfModeSettings
		{
			WindowsPowerMode = (flag ? s.WindowsPowerMode : ((int?)null)),
			CpuBoost = (flag2 ? s.CpuBoost : ((int?)null)),
			PowerPlanGuid = (flag4 ? s.PowerPlanGuid : null),
			FanBoost = (flag5 ? s.FanBoost : ((bool?)null)),
			RefreshHz = (flag3 ? s.RefreshHz : ((int?)null))
		};
		IL_00a6:
		flag4 = (byte)num3 != 0;
		if (flag4)
		{
			drifted.Add(SelfKeyPowerPlan);
		}
		bool? fanBoost = s.FanBoost;
		int num5;
		if (fanBoost.HasValue)
		{
			bool valueOrDefault3 = fanBoost == true;
			MechrevoHw hw = _hw;
			if (hw != null && hw.IsConnected && hw.SupportsFanBoost)
			{
				num5 = ((_hw.FanBoost != valueOrDefault3) ? 1 : 0);
				goto IL_00f5;
			}
		}
		num5 = 0;
		goto IL_00f5;
		IL_00f5:
		flag5 = (byte)num5 != 0;
		if (flag5)
		{
			drifted.Add(SelfKeyFanBoost);
		}
		windowsPowerMode = s.RefreshHz;
		if (windowsPowerMode.HasValue)
		{
			int valueOrDefault4 = windowsPowerMode.GetValueOrDefault();
			if (valueOrDefault4 > 0)
			{
				MechrevoHw hw = _hw;
				if (hw != null && hw.IsConnected && !hw.DcHz && _hw.CurrentHz > 0 && _hw.CurrentHz != valueOrDefault4)
				{
					num4 = (_hw.HzList.Contains(valueOrDefault4) ? 1 : 0);
					goto IL_0166;
				}
			}
		}
		num4 = 0;
		goto IL_0166;
	}

	/// <summary>(c)：把漂移项的"目标值"取出来（与该档已保存值同源，用来判断这次漂移是不是我们自己刚写下去的那个值）。</summary>
	private static string DriftValue(string key, PerfModeSettings drifted)
	{
		return key switch
		{
			SelfKeyPowerOverlay => drifted.WindowsPowerMode?.ToString(CultureInfo.InvariantCulture) ?? "", 
			SelfKeyCpuBoost => drifted.CpuBoost?.ToString(CultureInfo.InvariantCulture) ?? "", 
			SelfKeyPowerPlan => drifted.PowerPlanGuid ?? "", 
			SelfKeyFanBoost => (drifted.FanBoost == true) ? "1" : "0", 
			SelfKeyRefreshHz => drifted.RefreshHz?.ToString(CultureInfo.InvariantCulture) ?? "", 
			_ => "", 
		};
	}

	/// <summary>(c)：把"自触发"的那几项从漂移集合里摘掉，剩下的才允许重应用。</summary>
	private static PerfModeSettings ClearDrifted(PerfModeSettings drifted, List<string> suppressed)
	{
		PerfModeSettings perfModeSettings = drifted;
		foreach (string item in suppressed)
		{
			perfModeSettings = item switch
			{
				SelfKeyPowerOverlay => perfModeSettings with
				{
					WindowsPowerMode = null
				}, 
				SelfKeyCpuBoost => perfModeSettings with
				{
					CpuBoost = null
				}, 
				SelfKeyPowerPlan => perfModeSettings with
				{
					PowerPlanGuid = null
				}, 
				SelfKeyFanBoost => perfModeSettings with
				{
					FanBoost = null
				}, 
				SelfKeyRefreshHz => perfModeSettings with
				{
					RefreshHz = null
				}, 
				_ => perfModeSettings, 
			};
		}
		return perfModeSettings;
	}
}
