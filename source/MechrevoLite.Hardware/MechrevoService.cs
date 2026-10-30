using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Display;
using MechrevoLite.Gpu;
using MechrevoLite.Helpers;
using MechrevoLite.Mode;
using Microsoft.Win32;
using Probe;

namespace MechrevoLite.Hardware;

public class MechrevoService
{
	public enum AdvancedColorState
	{
		Off,
		Hdr,
		Acm
	}

	public enum ModeSwitchResult
	{
		Ok,
		Failed,
		Superseded
	}

	private readonly MechrevoHw _hw;

	private readonly Func<bool> _readColorCalibrationOn;

	private readonly Func<int> _readColorCalibrationMode;

	private readonly Func<bool> _readHdrEnabled;

	private readonly Func<bool, int, bool> _syncHighPerformancePowerMode;

	public const int ModeGaming = 0;

	public const int ModeTurbo = 1;

	public const int ModeOffice = 2;

	public const int ModeCustom = 3;

	public const int GpuIGpu = 0;

	public const int GpuStandard = 1;

	public const int GpuDgpu = 2;


	private readonly SemaphoreSlim _switchLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _settingLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _colorCalibrationLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _liquidCoolingLock = new SemaphoreSlim(1, 1);

	private CancellationTokenSource? _modeSwitchCts;

	/// <summary>
	/// 2026-10-09（30 系「模式 × 配置」失败可见化）：最近一次「带 ProfileIndex 的切模式」里，
	/// 设备是否**回读确认**了那个配置号。⚪ NotEvaluated 是初值（也用于自定义模式 / 非「模式 × 配置」机型
	/// / 切模式本身失败 —— 这些情况本判定不适用）。
	/// <para>⚠️ 只读判定：它**不参与**任何下发决策、不改变 <c>SetMode</c>/<c>SwitchMode</c> 的返回值、
	/// 不改档位/风扇/校色/电源计划逻辑；失败时只写日志 + 由 UI 弹一次提示（不静默）。</para>
	/// </summary>
	public enum ProfileIndexReadback
	{
		/// <summary>本判定不适用（自定义模式、非「模式 × 配置」机型、切模式失败或未走到判定）。</summary>
		NotEvaluated,
		/// <summary>设备回读的 ProfileName 与期望的配置号一致 ⇒ 30 系「模式 × 配置」真的生效了。</summary>
		Confirmed,
		/// <summary>设备回读的 ProfileName 与期望不一致 ⇒ 该固件/GCU 没接受这个配置号。</summary>
		Mismatch
	}

	private ProfileIndexReadback _lastProfileIndexReadback;

	/// <summary>
	/// 2026-10-09：最近一次切模式后「配置号是否被设备确认」的结论（只读；见
	/// <c>ProfileIndexReadback</c>）。
	/// UI 用它决定要不要弹「模式已切换，但配置号未被设备确认」。
	/// </summary>
	public ProfileIndexReadback LastProfileIndexReadback => _lastProfileIndexReadback;

	/// <summary>
	/// 2026-10-09（30 系「模式 × 配置」失败可见化）：本次 ProfileIndex 回读判定要不要把结论交给 UI 弹提示。
	/// 只有**用户主动切模式**（主界面模式按钮 / 托盘右键）才置为 true，由
	/// <c>SettingsForm.SwitchPerformanceModeAsync</c> / <c>SetContextMenu</c> 在调用前标记；
	/// <c>SwitchMode</c> 入口处消费一次（读后即清）。
	/// <para>⚠️ 它<b>不参与</b>任何下发决策，只决定「这一次判定结果是否可见」——
	/// 避免 AppConfig 启动重放、开机应用、热键定时器这类非用户发起的模式切换弹提示打扰用户。</para>
	/// </summary>
	private int _userModeSwitchRequested;

	/// <summary>标记「紧接着的这次模式切换是用户主动发起的」（见 <c>_userModeSwitchRequested</c>）。</summary>
	public void MarkUserModeSwitch()
	{
		Interlocked.Exchange(ref _userModeSwitchRequested, 1);
	}

	private CancellationTokenSource? _customProfileSwitchCts;

	private CancellationTokenSource? _customDetailCts;

	private CancellationTokenSource? _gpuSwitchCts;

	private CancellationTokenSource? _longRunningSwitchCts;

	internal static readonly TimeSpan SwitchLockHandoverTimeout = TimeSpan.FromSeconds(3L);

	internal static readonly TimeSpan SwitchLockAcquireTimeout = TimeSpan.FromSeconds(10L);

	internal static readonly (string Key, string Value)[] TurboGpuOverclockDefaultFields = new (string, string)[3]
	{
		("OverClockingSwitch", "1"),
		("GpuCoreClockOffsetOC", 105.ToString()),
		("GpuMemoryClockOffsetOC", 500.ToString())
	};

	internal static readonly (string Key, string Value)[] RestoreGpuOverclockDefaultFields = new (string, string)[3]
	{
		("OverClockingSwitch", "0"),
		("GpuCoreClockOffsetOC", "0"),
		("GpuMemoryClockOffsetOC", "0")
	};

	internal static readonly TimeSpan ColorCalibrationCacheTtl = TimeSpan.FromMilliseconds(500L);

	private static readonly object ColorCalibrationCacheLock = new object();

	private static bool? colorCalibrationCache;

	private static long colorCalibrationCacheUntil;

	internal static readonly TimeSpan LiquidCoolingWriteLockTimeout = TimeSpan.FromSeconds(15L);

	internal static readonly TimeSpan LiquidCoolingQueryLockTimeout = TimeSpan.FromSeconds(2L);

	internal const string KeyboardEffectFunction = "SetEffectALL";

	internal const string KeyboardEffectLightField = "light";

	// 2026-10-09（崩溃针对性改动，超时/重发 C）：
	// 字段下发这一步的超时量级沿用本方法既有的 WaitForStateAsync（400ms 首查 / 2200ms 补查）量级，不新造一套计时体系；
	// 重发退避 120ms = 既有字段间 Task.Delay(120)。
	internal const int FieldPublishTimeoutMs = 400;

	internal const int FieldRetryBackoffMs = 120;

	// 2026-10-09（批量下发超时口径 + 重发前收敛）：
	// 切档强制全量下发时一批就有 ~15 个字段 + 2 条风扇曲线，字段之间还有既有 Task.Delay(120)，
	// 整批固定按 400ms 计时必然假报超时，且 WaitAsync 不取消原批 ⇒ 重发会与飞行中的首批重复下发。
	// 口径(a)：超时按字段数放大 = FieldPublishTimeoutMs + FieldPublishPerEntryMarginMs × (N-1)；
	// 余量 320ms = 既有字段间 Task.Delay(120) + MQTT 往返余量 200ms。
	// 单字段/两字段批次仍落在 400/720ms 的快速失败量级（口径不变）。
	internal const int FieldPublishPerEntryMarginMs = 320;

	/// <summary>整批下发的超时口径：短批保持 400ms 快速失败，长批按字段数线性放大。</summary>
	internal static int PublishBatchTimeoutMs(int entryCount)
	{
		int count = ((entryCount > 0) ? entryCount : 1);
		return FieldPublishTimeoutMs + FieldPublishPerEntryMarginMs * (count - 1);
	}


	public int CurrentMode => OpToMode(_hw.OperatingMode);

	/// <summary>切换失败后主动获取 GCU 当前模式，用于纠正显示。
	/// GCU 对紧连切换指令处理慢（数秒）且完成后推送不及时，因此周期性 GETSTATUS 直至拿到变化后的稳定值。</summary>
	public async Task<int> RefreshCurrentModeAsync(int fallbackMode)
	{
		try
		{
			int last = CurrentMode;
			// 最长约 5 秒：模式发生变化且连续两次一致视为稳定
			for (int i = 0; i < 10; i++)
			{
				await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
				await Task.Delay(500).ConfigureAwait(continueOnCapturedContext: false);
				int now = CurrentMode;
				if (now >= 0 && now != last)
				{
					await Task.Delay(400).ConfigureAwait(continueOnCapturedContext: false);
					int stable = CurrentMode;
					if (stable == now)
					{
						Logger.WriteLine($"RefreshCurrentMode settled: {last}->{now}");
						return now;
					}
					last = stable;
				}
				else
				{
					last = now;
				}
			}
			int m = CurrentMode;
			Logger.WriteLine($"RefreshCurrentMode timed out, current={m}");
			return (m >= 0) ? m : fallbackMode;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RefreshCurrentMode failed: " + ex.Message);
			return fallbackMode;
		}
	}


	internal static Func<IEcReadTransport?> EcReadTransportFactory { get; set; } = DefaultEcReadTransport;

	public int CurrentGpuMode => _hw.GpuMode;

	internal static Func<int, bool, IReadOnlyList<Dictionary<string, object>>>? GpuRestartRouteOverride { get; set; }

	private bool CanControlLiquidCooling
	{
		get
		{
			if (_hw.SupportsLiquidCooling)
			{
				if (_hw.LcStatusSeen)
				{
					if (!_hw.LcActionSupportReported || _hw.LcActionSupported)
					{
						if (_hw.LcConnectionStateReported)
						{
							return _hw.LcGcuControllable;
						}
						return true;
					}
					return false;
				}
				return true;
			}
			return false;
		}
	}

	public static bool IsSilentTurboActive => ReadSilentTurboState() == true;

	internal static bool? ReadSilentTurboState()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\OEM\\GamingCenter2\\ItemSupport");
			string text = registryKey?.GetValue("SilentPerformanceModeSwitch")?.ToString();
			bool? result;
			if (text == "0")
			{
				result = true;
			}
			else
			{
				result = ((!(text == "1")) ? ((bool?)null) : new bool?(false));
			}
			return result;
		}
		catch (Exception ex)
		{
			Logger.WriteLineIfChanged("turbo-state-read", "Turbo sub-mode read failed: " + ex.Message);
			return null;
		}
	}

	public event Action<int>? ModeChanged;

	public event Action<int>? GpuModeChanged;

	public event Action? DataChanged;

	private async Task<bool> AcquireSwitchLockAsync(string operation, CancellationToken cancellationToken)
	{
		if (await _switchLock.WaitAsync(SwitchLockHandoverTimeout, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
		{
			return true;
		}
		CancellationTokenSource cancellationTokenSource = Volatile.Read(in _longRunningSwitchCts);
		if (cancellationTokenSource != null)
		{
			Logger.WriteLine(operation + ": a long-running switch is still confirming; requesting hand-over");
			try
			{
				cancellationTokenSource.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}
		if (await _switchLock.WaitAsync(SwitchLockAcquireTimeout, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
		{
			return true;
		}
		Logger.WriteLine(operation + " skipped: the switch lock is still held after the hand-over request");
		return false;
	}

	public MechrevoService(MechrevoHw hw)
		: this(hw, IsColorCalibrationOn, GetColorCalibrationMode, IsAdvancedColorEnabled)
	{
	}

	internal MechrevoService(MechrevoHw hw, Func<bool> readColorCalibrationOn, Func<int> readColorCalibrationMode, Func<bool> readHdrEnabled, Func<bool, int, bool>? syncHighPerformancePowerMode = null)
	{
		_hw = hw;
		_readColorCalibrationOn = readColorCalibrationOn;
		_readColorCalibrationMode = readColorCalibrationMode;
		_readHdrEnabled = readHdrEnabled;
		_syncHighPerformancePowerMode = syncHighPerformancePowerMode ?? new Func<bool, int, bool>(SyncHighPerformancePowerMode);
		_hw.ModeChanged += (int opMode) =>
		{
			ModeChanged?.Invoke(OpToMode(opMode));
		};
		_hw.GpuModeChanged += () =>
		{
			GpuModeChanged?.Invoke(CurrentGpuMode);
		};
		_hw.DataChanged += () =>
		{
			DataChanged?.Invoke();
		};
	}

	private static int OpToMode(int op)
	{
		if (op < 0 || op > 3)
		{
			return 0;
		}
		return PowerModeMapping.ToVisualMode((ConsoleOperatingMode)op);
	}

	public static int ToVisualMode(int mode)
	{
		return mode switch
		{
			2 => 2, 
			0 => 0, 
			1 => 1, 
			3 => 4, 
			_ => 0, 
		};
	}

	private static IEcReadTransport? DefaultEcReadTransport()
	{
		if (!AcpiDriverReadTransport.TryOpen(out AcpiDriverReadTransport transport, out string _))
		{
			return null;
		}
		return transport;
	}

	public static PlDefaultsResult ReadPlDefaults(int visualMode)
	{
		PlDefaultMode? plDefaultMode = PlDefaults.ModeForVisualMode(visualMode);
		if (plDefaultMode.HasValue)
		{
			PlDefaultMode valueOrDefault = plDefaultMode.GetValueOrDefault();
			IEcReadTransport ecReadTransport = EcReadTransportFactory();
			if (ecReadTransport == null)
			{
				return PlDefaultsResult.Unavailable(valueOrDefault, "EC read transport unavailable");
			}
			try
			{
				return PlDefaults.Read(valueOrDefault, ecReadTransport);
			}
			finally
			{
				(ecReadTransport as IDisposable)?.Dispose();
			}
		}
		return PlDefaultsResult.Unavailable(null, "customize has no EC default set");
	}

	public async Task<ModeSwitchResult> SwitchMode(int mode)
	{
		CancellationTokenSource requestCts = new CancellationTokenSource();
		Interlocked.Exchange(ref _modeSwitchCts, requestCts)?.Cancel();
		bool lockTaken = false;
		Stopwatch elapsed = Stopwatch.StartNew();
		try
		{
			lockTaken = await AcquireSwitchLockAsync($"SwitchMode({mode})", requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (!lockTaken)
			{
				return ModeSwitchResult.Failed;
			}
			requestCts.Token.ThrowIfCancellationRequested();
			MechrevoHw hw = _hw;
			// ⭐⭐ 2026-10-15（C-1 第 2 步，对照 A-22）：老机型后端**不走 GCU** —— 这里单独一支：
			// 视觉档 0/1/2 映射到老接口的内部档，档 3（自定义）老接口根本没有 ⇒ 直接判失败（⛔ 不假装可写）。
			// ⚠️ GCU 分支（下面那一整段）**一字未动**，本机走的就是它。
			// ⭐⭐ 2026-10-16（宝龙达适配）：⭐ 判据由 `UsesLegacyMifs` 放宽为 **`UsesBuiltInModes`**
			//   ⇒ ⭐ 宝龙达（蛟龙16Pro 等）**也走这一支** ✅ —— ⭐ 因为两者**协议同族、档位映射逐字相同**
			//     （⭐ 真机已验证：⭐ 静音→MIFS 2 · ⭐ 平衡→MIFS 0 · ⭐ 狂暴→MIFS 1 ✅）
			//   ⇒ ⭐ 且 `SwitchLegacyModeAsync` 内部已经是"谁在管事就调谁"（⭐ 见 `SwitchBuiltInModeAsync` ✅）
			//   ⚠️ 老同方机型上 `UsesBaolongda` 恒为 false ⇒ ⭐ 行为与改动前逐位一致 ✅。
			if (hw != null && hw.UsesBuiltInModes)
			{
				if (mode == 3)
				{
					Logger.WriteLine($"MechrevoService.SwitchMode({mode}) 内置档位后端（老接口/宝龙达）不支持自定义档");
					return ModeSwitchResult.Failed;
				}
				bool legacyOk = await hw.SwitchLegacyModeAsync((int)PowerModeMapping.FromVisualMode(mode), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				Logger.WriteLine($"MechrevoService.SwitchMode({mode}) 老接口后端 confirmed={legacyOk} elapsed={elapsed.ElapsedMilliseconds}ms");
				if (legacyOk)
				{
					ModeControl.SyncExternalModeStatic(mode);
					ModeChanged?.Invoke(mode);
					return ModeSwitchResult.Ok;
				}
				return ModeSwitchResult.Failed;
			}
			if (hw == null || !hw.IsGcuConnected)
			{
				return ModeSwitchResult.Failed;
			}
			if (CurrentMode == mode)
			{
				// 2026-10-09（实验开关验收）：本分支不切模式（模式没变）⇒ 原先**根本不跑** ProfileIndex
				// 回读判定，`LastProfileIndexReadback` 会留着上一次的旧值。带实验开关时，
				// 「配置号下拉改配置」走的正是这条路（SettingsForm 里先 SetMode 再
				// SwitchPerformanceModeAsync(同一模式)）⇒ 这里补一次**只读**回读判定，
				// 否则「40A 认不认 ProfileIndex」在改配置号这条路上永远看不到结论。
				// ⚠️ 严格限定：仅在开关为真时多跑这一步，且**只读**（不重新下发那一次 SetMode 之外的东西、
				//    不改返回值、不改档位/风扇/校色/电源计划逻辑）；开关关闭时此分支与改动前逐字一致。
				if (Force30Perf.Enabled)
				{
					bool sameModeUserInitiated = Interlocked.Exchange(ref _userModeSwitchRequested, 0) != 0;
					ProfileIndexReadback sameModeReadback = await VerifyProfileIndexAsync(mode, requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
					_lastProfileIndexReadback = ((sameModeReadback == ProfileIndexReadback.Mismatch && !sameModeUserInitiated) ? ProfileIndexReadback.NotEvaluated : sameModeReadback);
					Logger.WriteLine($"[perf-profile] 模式未变（{mode}）⇒ 按实验开关补跑一次配置号回读判定{Force30Perf.LogSourceSuffix}: {_lastProfileIndexReadback}");
				}
				ModeControl.SyncExternalModeStatic(mode);
				ModeChanged?.Invoke(mode);
				if (mode == 1)
				{
					await _hw.RepairPathologicalTurboFanCurveAsync().ConfigureAwait(continueOnCapturedContext: false);
				}
				return ModeSwitchResult.Ok;
				if (ShouldApplyTurboGpuOverclockDefaultsOnModeSwitch(mode, _hw.Capabilities.SilentTurboAvailability == FeatureAvailability.Supported))
				{
					await ApplyTurboGpuOverclockDefaults().ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			string text = mode switch
			{
				2 => "OPERATING_OFFICE_MODE", 
				0 => "OPERATING_GAMING_MODE", 
				1 => "OPERATING_TURBO_MODE", 
				3 => "OPERATING_CUSTOM_MODE", 
				_ => "OPERATING_GAMING_MODE", 
			};
			Logger.WriteLine($"MechrevoService.SwitchMode({mode}) -> {text}");
			int num = (int)((mode < 0 || mode > 3) ? ConsoleOperatingMode.Gaming : PowerModeMapping.FromVisualMode(mode));
			_hw.MarkModeSwitchPending(num);
			Dictionary<string, object> payload;
			Dictionary<string, object> overclockPayload;
			// 2026-10-04：非自定义模式也下发该模式自己的配置号（30 系「模式 × 配置」；未设置时为 0 = 旧行为）
			int resolvedProfileIndex = MechrevoHw.ResolveModeProfileIndex(_hw, mode, num, mode == 3);
			(payload, overclockPayload) = MechrevoHw.BuildModeSwitchPayloads(text, num, resolvedProfileIndex, mode == 3);
			// 2026-10-09（实验开关验收）：把**实际进 Fan/Control 的那个 ProfileIndex** 记一行 ——
			// 用于从日志直接确认「带/不带 LMECHREVO_FORCE_30PERF 时下发到底有没有这个字段」。
			// ⚠️ 只写日志：取值/下发/档位/风扇逻辑一行未动（resolvedProfileIndex 与改动前同源同值）。
			Logger.WriteLine($"[perf-profile] SwitchMode 下发 Action={text} ProfileIndex={resolvedProfileIndex}{Force30Perf.LogSourceSuffix}");
			try
			{
				await _hw.Publish("Fan/Control", payload).ConfigureAwait(continueOnCapturedContext: false);
				await _hw.Publish("LCHWOC/Control", overclockPayload).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
				_hw.ClearModeSwitchPending();
				throw;
			}
			bool flag = await _hw.WaitForStateAsync(() => CurrentMode == mode, TimeSpan.FromMilliseconds(180L), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (!flag)
			{
				await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
				flag = await _hw.WaitForStateAsync(() => CurrentMode == mode, TimeSpan.FromMilliseconds(700L), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (!flag)
			{
				await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
				flag = await _hw.WaitForStateAsync(() => CurrentMode == mode, TimeSpan.FromMilliseconds(1000L), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			}
			Logger.WriteLine($"SwitchMode confirmed={flag} expect={mode} actual={CurrentMode} elapsed={elapsed.ElapsedMilliseconds}ms");
			if (!flag)
			{
				if (requestCts.IsCancellationRequested)
				{
					// 被新请求取代（快速连点）而非真实失败：静默返回，由新请求接管，不弹失败提示
					_hw.ClearModeSwitchPending();
					return ModeSwitchResult.Superseded;
				}
				_hw.ClearModeSwitchPending();
				return ModeSwitchResult.Failed;
			}
			_hw.ClearModeSwitchPending();
			if (mode == 3)
			{
				_hw.RestoreDirectGpuOverclockProfile(_hw.CustomProfileIndex);
			}
			else
			{
				_hw.SuspendDirectGpuOverclock();
			}
			ModeControl.SyncExternalModeStatic(mode);
			ModeChanged?.Invoke(mode);
			await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" }).ConfigureAwait(continueOnCapturedContext: false);
			// 2026-10-09（30 系「模式 × 配置」失败可见化）：模式本身已确认切换成功，但上面下发的
			// ProfileIndex 是否被设备接受，原先**无从得知**（40B 的 GCU 可能直接忽略它）⇒ 这里补一次
			// **只读**回读判定。⚠️ 只在 SupportsModeProfileConfig 为真的机型上跑（非此机型恒 NotEvaluated，
			// 逐项不变）；它不重新下发、不改返回值、不改任何档位逻辑，只决定日志 + UI 提示。
			// 判定结论只在「用户主动切模式」时才对外可见（否则会被非用户发起的切换弹提示打扰）——
			// 判定本身照跑照记日志，失败永远可从日志查到。
			bool userInitiated = Interlocked.Exchange(ref _userModeSwitchRequested, 0) != 0;
			ProfileIndexReadback readback = await VerifyProfileIndexAsync(mode, requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			_lastProfileIndexReadback = ((readback == ProfileIndexReadback.Mismatch && !userInitiated) ? ProfileIndexReadback.NotEvaluated : readback);
			if (mode == 1)
			{
				await _hw.RepairPathologicalTurboFanCurveAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			return ModeSwitchResult.Ok;
			if (ShouldApplyTurboGpuOverclockDefaultsOnModeSwitch(mode, _hw.Capabilities.SilentTurboAvailability == FeatureAvailability.Supported))
			{
				await ApplyTurboGpuOverclockDefaults().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
			Logger.WriteLine($"SwitchMode({mode}) superseded after {elapsed.ElapsedMilliseconds}ms");
			_hw.ClearModeSwitchPending();
			return ModeSwitchResult.Superseded;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("SwitchMode fail: " + ex2.Message);
			return ModeSwitchResult.Failed;
		}
		finally
		{
			if (lockTaken)
			{
				_switchLock.Release();
			}
			Interlocked.CompareExchange(ref _modeSwitchCts, null, requestCts);
			requestCts.Dispose();
		}
	}

	/// <summary>
	/// 2026-10-09（30 系「模式 × 配置」失败可见化）：把「该视觉模式绑定的配置号」换算成设备侧
	/// **ProfileName 的 Mode{N} 编号**（1=游戏 / 2=办公 / 3=狂飙，见 ModeProfileConfig 类注释；
	/// 与 CustomModeForm.SelectFirmwareTargetAsync 的既有换算逐字一致，勿改用视觉模式号）。
	/// </summary>
	internal static int ProfileNameModeNumber(int visualMode)
	{
		return (visualMode == 1) ? 3 : ((visualMode == 2) ? 2 : 1);
	}

	/// <summary>
	/// 2026-10-09（30 系「模式 × 配置」失败可见化）：切模式时下发的 <c>ProfileIndex</c> 是否**真的被设备接受**。
	///
	/// <para>判据（沿用 2026-10-05 编辑器已经在用的那条设备回读）：<c>Fan/Status</c> 的
	/// <see cref="P:MechrevoLite.Hardware.MechrevoHw.ProfileName" /> 会反映固件当前加载的配置
	/// （形如 <c>Mode2_Profile3</c>）。官方 30 系控制台切模式时把配置号随 Action 一起下发，
	/// 固件随后加载该配置 ⇒ ProfileName 变成对应值。若设备（例如 40B 的 GCU）根本不认
	/// ProfileIndex，ProfileName 就<b>不会</b>变成期望值 —— 这正是可判定的失败。</para>
	///
	/// <para>⚠️ 本方法**只读**：重新发一次同样的 Action+ProfileIndex 是必要的（否则设备不会重新加载
	/// 配置、回读永远对不上 —— 见 SettingsForm 配置号下拉那条 2026-10-05 注释），
	/// 但它<b>不</b>改 <c>SwitchMode</c> 的返回值、<b>不</b>改任何档位/风扇/校色/电源计划状态，
	/// 唯一副作用是日志与 <see cref="P:MechrevoLite.Hardware.MechrevoService.LastProfileIndexReadback" />。
	/// 非「模式 × 配置」机型（<c>SupportsModeProfileConfig=false</c>）与自定义模式一律
	/// <c>NotEvaluated</c> ⇒ 行为与改动前逐项一致。</para>
	/// </summary>
	private async Task<ProfileIndexReadback> VerifyProfileIndexAsync(int visualMode, CancellationToken cancellationToken)
	{
		// 2026-10-09（实验开关验收）：本批新增的来源标记 ——
		// Force30Perf（LMECHREVO_FORCE_30PERF）打开时为 " source=force30perf"，关闭时为 ""（默认）。
		// 于是**默认状态下这些 [perf-profile] 日志与加这个开关之前逐字一致**，
		// 而带开关跑出来的日志一眼能认出「这次是实验开关逼出来的 30 系行为，不是真 30 系机型」。
		string source = Force30Perf.LogSourceSuffix;
		if (!_hw.SupportsModeProfileConfig || !MechrevoLite.Mode.ModeProfileConfig.IsBindable(visualMode))
		{
			return ProfileIndexReadback.NotEvaluated;
		}
		int index = Math.Clamp(MechrevoLite.Mode.ModeProfileConfig.Get(visualMode), 0, 3);
		int consoleMode = (int)PowerModeMapping.FromVisualMode(visualMode);
		string action = visualMode switch
		{
			2 => "OPERATING_OFFICE_MODE", 
			1 => "OPERATING_TURBO_MODE", 
			_ => "OPERATING_GAMING_MODE", 
		};
		string expected = "Mode" + ProfileNameModeNumber(visualMode) + "_Profile" + (index + 1);
		try
		{
			// 让固件真正去「加载」这个配置号：与切模式同样的 Fan/Control 动作 + ProfileIndex。
			Dictionary<string, object> payload;
			Dictionary<string, object> overclockPayload;
			int resolved = MechrevoHw.ResolveModeProfileIndex(_hw, visualMode, consoleMode, custom: false);
			(payload, overclockPayload) = MechrevoHw.BuildModeSwitchPayloads(action, consoleMode, resolved, custom: false);
			await _hw.Publish("Fan/Control", payload).ConfigureAwait(continueOnCapturedContext: false);
			await _hw.Publish("LCHWOC/Control", overclockPayload).ConfigureAwait(continueOnCapturedContext: false);
			// 2026-10-09（实验开关验收）：把**真正进 Fan/Control 的那个 ProfileIndex** 也记一行 ——
			// 「开关有没有让字段真的下发」这件事必须能从日志直接看出来，不用猜。
			Logger.WriteLine($"[perf-profile] 已下发 Action={action} ProfileIndex={resolved}{source}");
			// 固件加载配置的耗时不确定（编辑器那条路径最多等 2000ms），这里同样轮询而不是定长等待。
			for (int attempt = 0; attempt < 15; attempt++)
			{
				if (_hw.ProfileName == expected)
				{
					break;
				}
				await Task.Delay(100, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (cancellationToken.IsCancellationRequested)
			{
				Logger.WriteLine($"[perf-profile] ProfileIndex 回读被新请求取代（未判定，不报失败）{source}: visualMode={visualMode} profileIndex={index} expected={expected} actual=\"{_hw.ProfileName}\"");
				return ProfileIndexReadback.NotEvaluated;
			}
			if (_hw.ProfileName == expected)
			{
				Logger.WriteLine($"[perf-profile] ProfileIndex 已被设备确认{source}: visualMode={visualMode} profileIndex={index} expected={expected} actual=\"{_hw.ProfileName}\"");
				return ProfileIndexReadback.Confirmed;
			}
			// ⚠️ 明确失败路径：绝不静默 —— 这行日志 + UI 的一次可见提示（Strings.PerfProfileIndexUnconfirmed）。
			Logger.WriteLine($"[perf-profile] ProfileIndex 未被设备确认（模式已切换，但配置号没生效）{source}: visualMode={visualMode} profileIndex={index} expected={expected} actual=\"{_hw.ProfileName}\" ⇒ 该 GCU/固件可能不接受 ProfileIndex（GcuVariant=30 但载荷不是 30 系）");
			return ProfileIndexReadback.Mismatch;
		}
		catch (OperationCanceledException)
		{
			Logger.WriteLine($"[perf-profile] ProfileIndex 回读被取消（未判定，不报失败）{source}: visualMode={visualMode} profileIndex={index}");
			return ProfileIndexReadback.NotEvaluated;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"[perf-profile] ProfileIndex 回读失败（未判定，不报失败）{source}: visualMode={visualMode} profileIndex={index} ex={ex.Message}");
			return ProfileIndexReadback.NotEvaluated;
		}
	}

	public Task<bool> SwitchCustomProfileWithResend(int index, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SwitchCustomProfileCoreAsync(index, 2, cancellationToken);
	}

	public Task<bool> SwitchCustomProfile(int index)
	{
		return SwitchCustomProfileCoreAsync(index, 1, default(CancellationToken));
	}

	internal async Task<bool> ApplyGcuGpuOverclock(bool enabled, int core, int memory)
	{
		MechrevoHw hw = _hw;
		// 2026-10-15（C-1 第 2 步分类 ①）：显卡超频走 GCU ⇒ 真正需要 GCU。
		if (hw == null || !hw.IsGcuConnected)
		{
			return false;
		}
		if (!_hw.SupportsGpuOverclock && !_hw.Capabilities.OverclockSettings)
		{
			return false;
		}
		if (!enabled)
		{
			core = 0;
			memory = 0;
		}
		try
		{
			(string Key, string Value)[] fields = ((!enabled) ? new (string, string)[3]
			{
				("GpuCoreClockOffsetOC", "0"),
				("GpuMemoryClockOffsetOC", "0"),
				("OverClockingSwitch", "0")
			} : new (string, string)[3]
			{
				("OverClockingSwitch", "1"),
				("GpuCoreClockOffsetOC", core.ToString(CultureInfo.InvariantCulture)),
				("GpuMemoryClockOffsetOC", memory.ToString(CultureInfo.InvariantCulture))
			});
			for (int i = 0; i < fields.Length; i++)
			{
				await _hw.Publish("Fan/Control", new Dictionary<string, object>
				{
					["Action"] = "SET_OPERATING_MODE_DETAIL",
					[fields[i].Key] = fields[i].Value
				}).ConfigureAwait(continueOnCapturedContext: false);
				if (i + 1 < fields.Length)
				{
					await Task.Delay(120).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			bool Matches()
			{
				if (!_hw.DirectGpuOverclockBackendPresent)
				{
					if (_hw.GpuCoreClockOffset == core)
					{
						return _hw.GpuMemClockOffset == memory;
					}
					return false;
				}
				if (_hw.DriverGpuOverclockFieldMatches("GpuCoreClockOffsetOC", core))
				{
					return _hw.DriverGpuOverclockFieldMatches("GpuMemoryClockOffsetOC", memory);
				}
				return false;
			}
			if (Matches())
			{
				return true;
			}
			await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
			bool flag = await _hw.WaitForStateAsync(Matches, TimeSpan.FromMilliseconds(6000L)).ConfigureAwait(continueOnCapturedContext: false);
			Logger.WriteLine($"ApplyGcuGpuOverclock({enabled}) core={core} memory={memory} confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ApplyGcuGpuOverclock fail: " + ex.Message);
			return false;
		}
	}

	private async Task<bool> SwitchCustomProfileCoreAsync(int index, int attempts, CancellationToken cancellationToken)
	{
		CancellationTokenSource requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(ref _customProfileSwitchCts, requestCts);
		try
		{
			cancellationTokenSource?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		bool lockTaken = false;
		Stopwatch elapsed = Stopwatch.StartNew();
		long before;
		try
		{
			lockTaken = await AcquireSwitchLockAsync($"SwitchCustomProfile({index})", requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (!lockTaken)
			{
				return false;
			}
			requestCts.Token.ThrowIfCancellationRequested();
			if (index < 0 || index >= 5)
			{
				return false;
			}
			MechrevoHw hw = _hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：日志自己写着「GCU 未连接」⇒ 真正需要 GCU。
			if (hw == null || !hw.IsGcuConnected)
			{
				Logger.WriteLine("SwitchCustomProfile: GCU 未连接");
				return false;
			}
			if (_hw.OperatingMode == 3 && _hw.CustomProfileIndex == index && !_hw.HasConflictingModeSwitchPending(3, index))
			{
				// 2026-10-09（档位路径同一处修法）：硬件本来就在这个自定义档，也要把档位**意图**补齐
				// （老键 performance_<电源来源>=3），否则一次"已经是在该档"的重放会把老键留在静音/平衡上。
				ModeControl.SyncExternalModeStatic(3);
				return true;
			}
			_hw.SuspendDirectGpuOverclock();
			_hw.MarkModeSwitchPending(3, index);
			// 2026-10-09（修「切自定义档被弹回静音/平衡」，档位路径）：档位**意图**在请求时就落盘，不再等回读确认。
			// 原来 `ModeControl.SyncExternalModeStatic(3)` 只在 `confirmed` 里面 ⇒ 一次没被回读确认的自定义切换
			// （真机日志签名：`SwitchCustomProfile(n) confirmed=False`）**两个档位存储都不更新**：老键
			// performance_0/1 还停在静音/平衡，新键 perf_active 也还是旧档 ⇒ 之后任何一次自动重应用
			// （开机 / 唤醒 / 拔插电 / GCU 连接恢复 / 显卡模式切换 → ModeControl.AutoPerformance）都会用那个
			// 陈旧的**内置档**再下发一次，用户看到的就是"刚开自定义就被弹回静音或平衡"。
			// 与老引擎自己的约定一致：ModeControl.SetPerformanceMode 也是**先** Modes.SetCurrent(意图) 再切硬件。
			ModeControl.SyncExternalModeStatic(3);
			Logger.WriteLine($"SwitchCustomProfile({index}) 已记录自定义档意图（performance_{Program.PerformanceKey()}=3，确认前即落盘，避免自动重应用把内置档再下发），等待回读确认");
			before = _hw.CustomProfileStatusVersion;
			bool confirmed = false;
			for (int attempt = 0; attempt < attempts; attempt++)
			{
				if (confirmed)
				{
					break;
				}
				requestCts.Token.ThrowIfCancellationRequested();
				// 2026-10-15（C-1 第 2 步分类 ①）：自定义档重试下发（MQTT）⇒ 真正需要 GCU。
				if (!_hw.IsGcuConnected)
				{
					return false;
				}
				if (attempt > 0)
				{
					Logger.WriteLine($"SwitchCustomProfile({index}) resend after {elapsed.ElapsedMilliseconds}ms");
				}
				Task<bool> modeWait = _hw.WaitForStateAsync(TargetReported, TimeSpan.FromMilliseconds(180L), requestCts.Token);
				try
				{
					await _hw.Publish("Fan/Control", new Dictionary<string, object>
					{
						["Action"] = "OPERATING_CUSTOM_MODE",
						["ProfileIndex"] = index
					}).ConfigureAwait(continueOnCapturedContext: false);
					await _hw.Publish("LCHWOC/Control", new Dictionary<string, object> { ["IsCustomRun"] = true }).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch
				{
					_hw.ClearModeSwitchPending();
					throw;
				}
				confirmed = await modeWait.ConfigureAwait(continueOnCapturedContext: false);
				int[] array = new int[2] { 700, 900 };
				foreach (int waitMs in array)
				{
					requestCts.Token.ThrowIfCancellationRequested();
					if (confirmed)
					{
						break;
					}
					await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
					confirmed = await _hw.WaitForStateAsync(TargetReported, TimeSpan.FromMilliseconds(waitMs), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			requestCts.Token.ThrowIfCancellationRequested();
			Logger.WriteLine($"SwitchCustomProfile({index}) confirmed={confirmed} actualMode={_hw.OperatingMode} actualProfile={_hw.CustomProfileIndex} elapsed={elapsed.ElapsedMilliseconds}ms");
			if (confirmed)
			{
				await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" }).ConfigureAwait(continueOnCapturedContext: false);
				await _hw.Publish("LCHWOC/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
				requestCts.Token.ThrowIfCancellationRequested();
				_hw.NotifyCustomModeChanged();
				_hw.RestoreDirectGpuOverclockProfile(index);
				AppConfig.Set("custom_last_profile", index);
				AppConfig.Flush();
				// 2026-10-09：档位意图已在请求时写过（见上面的 SyncExternalModeStatic(3)），这里只补"硬件真的换过来了"的通知
				ModeChanged?.Invoke(3);
			}
			return confirmed;
		}
		catch (OperationCanceledException)
		{
			Logger.WriteLine($"SwitchCustomProfile({index}) superseded after {elapsed.ElapsedMilliseconds}ms");
			return false;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("SwitchCustomProfile fail: " + ex2.Message);
			return false;
		}
		finally
		{
			if (lockTaken)
			{
				_switchLock.Release();
			}
			Interlocked.CompareExchange(ref _customProfileSwitchCts, null, requestCts);
			requestCts.Dispose();
		}
		bool TargetReported()
		{
			if (_hw.OperatingMode == 3 && _hw.CustomProfileIndex == index)
			{
				return _hw.CustomProfileStatusVersion > before;
			}
			return false;
		}
	}

	public async Task<bool> SetCustomDetail(Dictionary<string, string> fields)
	{
		// **校验层兜底** —— 下发前先把这一批里的 PL1/PL2/PL4 规范成 **PL1 ≤ PL2 ≤ PL4**
		if (!NormalizePowerLimitOrder(fields))
		{
			Logger.WriteError("SetCustomDetail rejected: PL1 ≤ PL2 ≤ PL4 无法满足（硬件范围互相冲突）⇒ 本批不下发");
			return false;
		}
		// （机主：PL1/PL2 不可调 · 日志 WriteFirmwareField:PL1=120 -> Failed）
		bool intelMsrPower = _hw != null && MechrevoLite.Hardware.CpuVendor.IsIntel
			&& !_hw.IsIntelNucMachine && !_hw.IsUmiMachine;
		if (_hw != null && (_hw.UsesBaolongda || _hw.SmuCustomApu || intelMsrPower) && fields != null && fields.Count > 0)
		{
			List<string> consumedKeys = new List<string>();
			foreach (KeyValuePair<string, string> kv in fields)
			{
				if (TryApplyPowerLimitViaBackend(kv.Key, kv.Value))
				{
					consumedKeys.Add(kv.Key);
				}
			}
			foreach (string k in consumedKeys)
			{
				fields.Remove(k);
			}
			if (consumedKeys.Count > 0)
			{
				Logger.WriteLine($"SetCustomDetail：宝龙达 ⇒ 走本机 WMI 的字段 = [{string.Join(",", consumedKeys)}] · 剩余发 MQTT = {fields.Count} 个");
			}
			if (fields.Count == 0)
			{
				return true;
			}
		}
		CancellationTokenSource requestCts = new CancellationTokenSource();
		Interlocked.Exchange(ref _customDetailCts, requestCts)?.Cancel();
		// 2026-10-09（批量下发超时口径 + 重发前收敛）：批次取消令牌挂在方法最外层，
		// 便于 finally 只要进入过本方法就一定能安全释放；它链接 requestCts，被新请求取代时同样会被取消。
		CancellationTokenSource batchCts = CancellationTokenSource.CreateLinkedTokenSource(requestCts.Token);
		bool lockTaken = false;
		Stopwatch elapsed = Stopwatch.StartNew();
		try
		{
			lockTaken = await AcquireSwitchLockAsync("SetCustomDetail", requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (!lockTaken)
			{
				return false;
			}
			requestCts.Token.ThrowIfCancellationRequested();
			if (fields.Count == 0)
			{
				return false;
			}
			if (fields.Keys.Any(IsGpuOverclockField))
			{
				_hw.EnsureDirectGpuOverclock();
			}
			if (fields.Any((KeyValuePair<string, string> field) => !ValidateCustomField(field.Key, field.Value)))
			{
				Logger.WriteLine("SetCustomDetail rejected outside device capability: " + string.Join(", ", fields.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)));
				return false;
			}
			KeyValuePair<string, string>[] source = fields.OrderBy((KeyValuePair<string, string> field) => (!IsGateField(field.Key)) ? 1 : 0).ToArray();
			KeyValuePair<string, string>[] gpuEntries = source.Where((KeyValuePair<string, string> field) => IsGpuOverclockField(field.Key)).ToArray();
			bool flag = _hw.DirectGpuOverclockBackendPresent && gpuEntries.Any((KeyValuePair<string, string> field) =>
			{
				bool flag6 = int.TryParse(field.Value, out var result4);
				if (flag6)
				{
					string key2 = field.Key;
					bool flag7 = ((key2 == "GpuCoreClockOffsetOC" || key2 == "GpuMemoryClockOffsetOC") ? true : false);
					flag6 = flag7;
				}
				return flag6 && !_hw.CanSetGpuOverclockThroughDriver(field.Key, result4);
			});
			bool flag2 = gpuEntries.Length != 0 && gpuEntries.All((KeyValuePair<string, string> field) => int.TryParse(field.Value, out var result4) && _hw.CanSetGpuOverclockThroughGcu(field.Key, result4));
			if (flag && gpuEntries.Any((KeyValuePair<string, string> field) =>
			{
				bool flag6 = int.TryParse(field.Value, out var result4);
				if (flag6)
				{
					string key2 = field.Key;
					bool flag7 = ((key2 == "GpuCoreClockOffsetOC" || key2 == "GpuMemoryClockOffsetOC") ? true : false);
					flag6 = flag7;
				}
				return flag6 && !_hw.CanSetGpuOverclockThroughGcu(field.Key, result4);
			}))
			{
				Logger.WriteLine("SetCustomDetail rejected GPU offset outside both driver and GCU ranges: " + string.Join(", ", gpuEntries.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)));
				return false;
			}
			bool useGcuForGpuFields = flag2 & flag;
			bool directGpuOverclockUsed = false;
			Dictionary<string, string> gcuFields = new Dictionary<string, string>();
			if (flag2)
			{
				KeyValuePair<string, string>[] array = gpuEntries;
				for (int num = 0; num < array.Length; num++)
				{
					KeyValuePair<string, string> keyValuePair = array[num];
					gcuFields[keyValuePair.Key] = keyValuePair.Value;
				}
			}
			foreach (KeyValuePair<string, string> item in source.Where((KeyValuePair<string, string> field) => !IsGpuOverclockField(field.Key)))
			{
				gcuFields[item.Key] = item.Value;
			}
			if (gcuFields.Count > 0)
			{
				MechrevoHw hw = _hw;
				// 2026-10-15（C-1 第 2 步分类 ①）：变量名就叫 gcuFields（GCU 上报的键）⇒ 真正需要 GCU。
				if (hw == null || !hw.IsGcuConnected)
				{
					if (gcuFields.Keys.Any((string key2) => !IsGpuOverclockField(key2)))
					{
						return false;
					}
					gcuFields.Clear();
				}
			}
			// ⭐⭐ 2026-10-16（宝龙达"全部接入"第 3 步 · 功耗墙 · ⭐ 前后端分离模板）：
			//   ⭐ 功耗墙那几项（⭐ PL1 / PL2 / 温度墙）⭐ **在非 GCU 后端下走 WMI**（⭐ 它没有 GCU ✅）
			//   ⇒ ⭐ 所以**先在这里把它们摘出去并就地应用** ✅ ⇒ ⭐ 剩下的才进 GCU 的待发列表 ✅
			//   ⚠️ 作用范围：⭐ `TryApplyPowerLimitViaBackend` ⭐ **非宝龙达时恒返回 false** ✅
			//      ⇒ ⭐ GCU / 老同方 机型 ⭐ **列表与原来逐项相同** ⇒ ⭐ **行为逐位不变** ✅
			List<KeyValuePair<string, string>> sourceForGcu = new List<KeyValuePair<string, string>>(source.Length);
			foreach (KeyValuePair<string, string> field2 in source)
			{
				if (TryApplyPowerLimitViaBackend(field2.Key, field2.Value))
				{
					continue; // ⭐ 已由非 GCU 后端处理 ⇒ ⛔ 这一条不发 MQTT ✅
				}
				sourceForGcu.Add(field2);
			}
			KeyValuePair<string, string>[] gcuEntries = sourceForGcu.Where((KeyValuePair<string, string> field) => gcuFields.ContainsKey(field.Key)).ToArray();
			KeyValuePair<string, string>[] gcuConfirmationEntries = gcuEntries.Where((KeyValuePair<string, string> field) => !IsGpuOverclockField(field.Key)).ToArray();
			// 2026-10-09（批量下发超时口径 + 重发前收敛）：
			// 下发拆成两件事：① CancelBatch（保证首批不再继续发出新字段、供重发前收敛用）；
			// ② PublishBatchAsync(token)：每个字段发送前、每段 Delay 前先看 token ⇒ 首批被取消后最多只有"已经排队的那一条"落地，绝不继续发后续字段。
			void CancelBatch()
			{
				try
				{
					batchCts.Cancel();
				}
				catch (ObjectDisposedException)
				{
				}
			}
			async Task PublishGcuEntriesAsync(CancellationToken token)
			{
				for (int i = 0; i < gcuEntries.Length; i++)
				{
					token.ThrowIfCancellationRequested();
					KeyValuePair<string, string> keyValuePair2 = gcuEntries[i];
					string text = keyValuePair2.Key;
					object value = keyValuePair2.Value;
					int result2;
					int result3;
					if (keyValuePair2.Key == "CpuTccOffset" && int.TryParse(keyValuePair2.Value, out var result))
					{
						if (_hw.UsesAmdPowerFields)
						{
							text = "CpuAmdTccTarget";
						}
						else
						{
							value = _hw.TccTargetToRaw(result);
						}
					}
					else if (keyValuePair2.Key == "PL1" && _hw.UsesAmdPowerFields)
					{
						text = "CpuAmdSPL";
					}
					else if (keyValuePair2.Key == "PL2" && _hw.UsesAmdPowerFields)
					{
						text = "CpuAmdSPPT";
					}
					else if (keyValuePair2.Key == "PL4" && _hw.UsesAmdPowerFields)
					{
						text = _hw.Pl4WireKey;
					}
					else if (keyValuePair2.Key == "PL4" && int.TryParse(keyValuePair2.Value, out result2))
					{
						value = _hw.Pl4ToWire(result2);
					}
					else if (keyValuePair2.Key == "FanSwitchSpeed" && int.TryParse(keyValuePair2.Value, out result3))
					{
						value = _hw.QuantiseFanSwitchSpeed(result3).ToString();
					}
					await _hw.Publish("Fan/Control", new Dictionary<string, object>
					{
						["Action"] = "SET_OPERATING_MODE_DETAIL",
						[text] = value
					});
					if (i + 1 < gcuEntries.Length)
					{
						await Task.Delay(120, token);
					}
				}
			}
			// 2026-10-09（批量下发超时口径 + 重发前收敛）：下发这一步按字段数计时——
			// 短批仍是 400ms 快速失败；长批 = 400 + 320×(N-1)ms（15 字段 = 2680ms，实际耗时约 14×120 + 往返 ≈ 2s，不再假超时）。
			// 超时/取消时先 CancelBatch() 再 await 首批任务收敛（含 TimeoutException 之后的残余），
			// 因此 retry#1 一定在首批停止发字段之后才开始，不会与飞行中的首批并发重复下发。
			// ⚠️ 不改目标值、不改字段顺序、重发仍最多一次。
			bool publishFaulted = false;
			int publishTimeoutMs = PublishBatchTimeoutMs(gcuEntries.Length);
			Stopwatch publishWatch = Stopwatch.StartNew();
			try
			{
				await PublishGcuEntriesAsync(batchCts.Token).WaitAsync(TimeSpan.FromMilliseconds(publishTimeoutMs), requestCts.Token);
			}
			catch (TimeoutException)
			{
				publishFaulted = true;
				publishWatch.Stop();
				CancelBatch();
				Logger.WriteLine($"SetCustomDetail attempt#0 publish timed out after {publishTimeoutMs}ms (entries={gcuEntries.Length}, batch timer {publishTimeoutMs}ms, elapsed {publishWatch.ElapsedMilliseconds}ms, fields: " + string.Join(", ", gcuEntries.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)) + ")");
			}
			finally
			{
				publishWatch.Stop();
				Logger.WriteLine($"SetCustomDetail attempt#0 publish window entries={gcuEntries.Length} timeoutMs={publishTimeoutMs} elapsed={publishWatch.ElapsedMilliseconds}ms faulted={publishFaulted}");
			}
			if (publishFaulted)
			{
				// 先把首批收敛（被取消的批次在此停住，不再发出后续字段），再进入确认/重发流程。
				CancelBatch();
				await PublishGcuEntriesAsync(batchCts.Token);
				Logger.WriteLine($"SetCustomDetail attempt#0 batch converged before confirmation/retry#1 (entries={gcuEntries.Length})");
			}
			bool flag3 = gcuConfirmationEntries.Length == 0 || gcuConfirmationEntries.All((KeyValuePair<string, string> field) => GcuCustomFieldMatches(field.Key, field.Value));
			if (!flag3)
			{
				flag3 = await _hw.WaitForStateAsync(() => gcuConfirmationEntries.All((KeyValuePair<string, string> field) => GcuCustomFieldMatches(field.Key, field.Value)), TimeSpan.FromMilliseconds(400L), requestCts.Token);
			}
			if (!flag3)
			{
				await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
				flag3 = await _hw.WaitForStateAsync(() => gcuConfirmationEntries.All((KeyValuePair<string, string> field) => GcuCustomFieldMatches(field.Key, field.Value)), TimeSpan.FromMilliseconds(2200L), requestCts.Token);
			}
			// 2026-10-09（崩溃针对性改动，超时/重发 C）：到这一步仍未确认（未回读/超时）⇒ 自动重发一次、退避 120ms 起。
			// ⚠️ 重发只重复已下发过的同一批字段、同一个目标值、同一顺序；重试最多一次（不放大）、不改目标值、不改字段顺序。
			// ⚠️ 两次尝试日志可区分：首次路径 attempt#0，重发路径 retry#1。
			// ⚠️ 影响面：仅"字段下发未被确认时的重发与提示"，返回值语义未变（仍返回 false）。
			if ((!flag3 || publishFaulted) && gcuConfirmationEntries.Length != 0)
			{
				Logger.WriteLine($"SetCustomDetail attempt#0 unconfirmed after {elapsed.ElapsedMilliseconds}ms, retry#1 (backoff {FieldRetryBackoffMs}ms, publishFaulted={publishFaulted}): " + string.Join(", ", gcuConfirmationEntries.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)));
				await Task.Delay(FieldRetryBackoffMs);
				requestCts.Token.ThrowIfCancellationRequested();
				// 2026-10-09（重发前收敛）：进入 retry#1 前首批必已收敛——
				// 未超时 ⇒ 上面的 await 已经等首批跑完；超时 ⇒ 已 CancelBatch() 并 await 首批任务（取消落在"下一个字段发送前/Delay 前"的检查点）。
				// 因此重发不会与飞行中的首批并发重复下发；最多只有 0~1 条"已排队"的同值报文与重发同窗口落地。
				CancellationTokenSource retryCts = CancellationTokenSource.CreateLinkedTokenSource(requestCts.Token);
				int retryTimeoutMs = PublishBatchTimeoutMs(gcuEntries.Length);
				Stopwatch retryWatch = Stopwatch.StartNew();
				try
				{
					await PublishGcuEntriesAsync(retryCts.Token).WaitAsync(TimeSpan.FromMilliseconds(retryTimeoutMs), requestCts.Token);
				}
				catch (TimeoutException)
				{
					retryWatch.Stop();
					retryCts.Cancel();
					Logger.WriteLine($"SetCustomDetail retry#1 publish timed out after {retryTimeoutMs}ms (entries={gcuEntries.Length}, batch timer {retryTimeoutMs}ms, elapsed {retryWatch.ElapsedMilliseconds}ms, fields: " + string.Join(", ", gcuEntries.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)) + ")");
				}
				finally
				{
					retryWatch.Stop();
					Logger.WriteLine($"SetCustomDetail retry#1 publish window entries={gcuEntries.Length} timeoutMs={retryTimeoutMs} elapsed={retryWatch.ElapsedMilliseconds}ms");
					retryCts.Dispose();
				}
				flag3 = gcuConfirmationEntries.All((KeyValuePair<string, string> field) => GcuCustomFieldMatches(field.Key, field.Value));
				if (!flag3)
				{
					flag3 = await _hw.WaitForStateAsync(() => gcuConfirmationEntries.All((KeyValuePair<string, string> field) => GcuCustomFieldMatches(field.Key, field.Value)), TimeSpan.FromMilliseconds(400L), requestCts.Token);
				}
				if (!flag3)
				{
					await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
					flag3 = await _hw.WaitForStateAsync(() => gcuConfirmationEntries.All((KeyValuePair<string, string> field) => GcuCustomFieldMatches(field.Key, field.Value)), TimeSpan.FromMilliseconds(2200L), requestCts.Token);
				}
				Logger.WriteLine("SetCustomDetail retry#1 confirmed=" + flag3 + " after " + elapsed.ElapsedMilliseconds + "ms");
			}
			bool flag4 = gcuConfirmationEntries.Where((KeyValuePair<string, string> field) => !IsGpuOverclockField(field.Key)).All((KeyValuePair<string, string> field) => GcuCustomFieldMatches(field.Key, field.Value));
			if ((gpuEntries.Length != 0) & flag4)
			{
				if (useGcuForGpuFields)
				{
					bool flag5 = DriverConfirm();
					if (!flag5)
					{
						await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
						flag5 = await _hw.WaitForStateAsync(DriverConfirm, TimeSpan.FromMilliseconds(6000L), requestCts.Token);
					}
					flag3 = flag5;
					Logger.WriteLine($"SetCustomDetail extended GPU offsets confirmed via driver readback: {flag3}");
					if (flag3)
					{
						_hw.MarkGcuGpuOverclockActive();
					}
				}
				else
				{
					directGpuOverclockUsed = await _hw.ApplyGpuOverclockAsync(gpuEntries).ConfigureAwait(continueOnCapturedContext: false);
					flag3 = directGpuOverclockUsed;
				}
			}
			else if (gpuEntries.Length != 0)
			{
				flag3 = false;
			}
			string value2;
			if (gpuEntries.Length != 0)
			{
				value2 = (directGpuOverclockUsed ? "driver" : "driver-readback-failed");
			}
			else
			{
				value2 = "GCU";
			}
			Logger.WriteLine($"SetCustomDetail source={value2} confirmed={flag3}: " + string.Join(", ", fields.Select((KeyValuePair<string, string> kv) => kv.Key + "=" + kv.Value)));
			return flag3;
			bool DriverConfirm()
			{
				return gpuEntries.All((KeyValuePair<string, string> field) => int.TryParse(field.Value, out var result4) && _hw.DriverGpuOverclockFieldMatches(field.Key, result4));
			}
		}
		catch (OperationCanceledException)
		{
			Logger.WriteLine($"SetCustomDetail superseded after {elapsed.ElapsedMilliseconds}ms");
			return false;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("SetCustomDetail fail: " + ex2.Message);
			return false;
		}
		finally
		{
			if (lockTaken)
			{
				_switchLock.Release();
			}
			// 2026-10-09（重发前收敛）：批次令牌是本方法的局部资源，随方法一起释放（不取消，避免影响上面的收敛等待顺序）。
			batchCts.Dispose();
			Interlocked.CompareExchange(ref _customDetailCts, null, requestCts);
			requestCts.Dispose();
		}
	}
	// 本方法只负责：① 把"待下发值 + 硬件当前值"凑成三元组；② 把联动结果写回字段表（ 被联动的
	private bool NormalizePowerLimitOrder(Dictionary<string, string> fields)
	{
		MechrevoHw hw = _hw;
		if (hw == null || fields == null || fields.Count == 0)
		{
			return true;
		}
		bool hasPl1 = fields.ContainsKey("PL1");
		bool hasPl2 = fields.ContainsKey("PL2");
		bool hasPl4 = fields.ContainsKey("PL4");
		if (!hasPl1 && !hasPl2 && !hasPl4)
		{
			return true;
		}
		int? pl1 = CurrentOrPendingPowerLimit(fields, "PL1", hw.Pl1Adjustable, hw.Pl1);
		int? pl2 = CurrentOrPendingPowerLimit(fields, "PL2", hw.Pl2Adjustable, hw.Pl2);
		// ⭐ 无 PL4 的机型（宝龙达 40 系）⇒ `Pl4Adjustable == false` ⇒ 传 null ⇒ 约束自动忽略 PL4 ✅
		int? pl4 = hw.Pl4Adjustable ? CurrentOrPendingPowerLimit(fields, "PL4", adjustable: true, hw.Pl4) : ((int?)null);
		int changedCount = (hasPl1 ? 1 : 0) + (hasPl2 ? 1 : 0) + (hasPl4 ? 1 : 0);
		PowerLimitOrder.Field? changed = changedCount switch
		{
			1 when hasPl1 => PowerLimitOrder.Field.Pl1,
			1 when hasPl2 => PowerLimitOrder.Field.Pl2,
			1 when hasPl4 => PowerLimitOrder.Field.Pl4,
			_ => (PowerLimitOrder.Field?)null
		};
		PowerLimitOrder.Range range1 = ToPowerLimitRange(hw.Pl1Minimum, hw.Pl1Maximum, hw.Pl1Adjustable);
		PowerLimitOrder.Range range2 = ToPowerLimitRange(hw.Pl2Minimum, hw.Pl2Maximum, hw.Pl2Adjustable);
		PowerLimitOrder.Range range4 = ToPowerLimitRange(hw.Pl4Minimum, hw.Pl4Maximum, hw.Pl4Adjustable);
		if (!PowerLimitOrder.TryLink(changed, pl1, pl2, pl4, range1, range2, range4,
			out int? linkedPl1, out int? linkedPl2, out int? linkedPl4,
			out IReadOnlyList<PowerLimitOrder.Change> changes, out string reason))
		{
			return false;
		}
		if (changes == null || changes.Count == 0)
		{
			return true;
		}
		WriteLinkedPowerLimit(fields, "PL1", pl1, linkedPl1);
		WriteLinkedPowerLimit(fields, "PL2", pl2, linkedPl2);
		WriteLinkedPowerLimit(fields, "PL4", pl4, linkedPl4);
		// ⭐ 机主要求的那一行日志（⭐ 例：`[pl] 联动：PL1 120→100（因 PL2=100）`）✅
		//   ⚠️ 用 WriteError ⇒ 默认「仅错误」级别下也**一定落盘**（⭐ 这是真机取证的主要证据）✅
		Logger.WriteError(PowerLimitOrder.Describe(changes, reason));
		return true;
	}

	/// <summary>⭐ 读"这一批里的值"；本批没带 ⇒ 用硬件上报的当前值（⭐ <c>&gt; 0</c> 才算已知）✅。</summary>
	private static int? CurrentOrPendingPowerLimit(IReadOnlyDictionary<string, string> fields, string key, bool adjustable, int current)
	{
		if (fields.TryGetValue(key, out string text))
		{
			// ⚠️ 非法数字不在这里兜底（交给后面的 `ValidateCustomField` 如实拒绝）
			return int.TryParse(text, out int value) ? value : ((int?)null);
		}
		if (!adjustable || current <= 0)
		{
			return null;
		}
		return current;
	}

	/// <summary>⭐ 硬件范围 → <see cref="PowerLimitOrder.Range" />（⭐ 读不到 ⇒ 不夹）✅。</summary>
	private static PowerLimitOrder.Range ToPowerLimitRange(int minimum, int maximum, bool adjustable)
	{
		return (adjustable && maximum > minimum)
			? new PowerLimitOrder.Range(minimum, maximum)
			: PowerLimitOrder.Range.Unbounded;
	}

	/// <summary>⭐ 把联动结果写回字段表（⭐ 只在"值真的变了"时才覆盖/补字段）✅。</summary>
	private static void WriteLinkedPowerLimit(Dictionary<string, string> fields, string key, int? original, int? linked)
	{
		if (!linked.HasValue || !original.HasValue || linked.Value == original.Value)
		{
			return;
		}
		fields[key] = linked.Value.ToString(CultureInfo.InvariantCulture);
	}
	// （宝龙达 · GPU 偏移校验范围）：与 <c>CustomModeForm.GpuOffsetUiRange</c> **同源**
	private bool TryGetBaolongdaGpuOffsetUiRange(bool core, out int minimum, out int maximum)
	{
		minimum = 0;
		maximum = 0;
		if (_hw == null || !_hw.UsesBaolongda)
		{
			return false;
		}
		if (!NvapiPStates.TryGetP0OffsetRange(out int coreMin, out int coreMax, out int memMin, out int memMax))
		{
			return false;
		}
		minimum = Math.Max(core ? coreMin : memMin, NvapiPStates.UiOffsetMinimumMhz);
		maximum = Math.Min(core ? coreMax : memMax, NvapiPStates.UiOffsetMaximumMhz);
		return maximum > minimum;
	}

	private bool ValidateCustomField(string key, string text)
	{
		if (!int.TryParse(text, out var result))
		{
			return false;
		}
		switch (key)
		{
		case "PL1":
			return _hw.Pl1Adjustable && InRange(result, _hw.Pl1Minimum, _hw.Pl1Maximum);
		case "PL2":
			return _hw.Pl2Adjustable && InRange(result, _hw.Pl2Minimum, _hw.Pl2Maximum);
		case "PL4":
			return _hw.Pl4Adjustable && InRange(result, _hw.Pl4Minimum, _hw.Pl4Maximum);
		case "FanSwitchSpeedEnabled":
		{
			bool flag = _hw.SupportsFanSwitchSpeed;
			if (flag)
			{
				bool flag2 = (uint)result <= 1u;
				flag = flag2;
			}
			return flag;
		}
		case "FanSwitchSpeed":
			return _hw.SupportsFanSwitchSpeed && InRange(result, _hw.FanSwitchSpeedMinimum, _hw.FanSwitchSpeedMaximum);
		case "CpuTccOffset":
			return _hw.TccAdjustable && InRange(result, _hw.TccMinimum, _hw.TccMaximum);
		// （机主：「是持续功耗时间」）：范围与 UI 同源（BaolongdaProfile 常量）
		case "SmuStapmTimeSeconds":
			return PerfModeCapabilities.UsesSmuForCustomPower(_hw.UsesBaolongda, _hw.IsBaolongda30Series, SmuAvailability.IsReady)
				&& InRange(result, BaolongdaProfile.StapmTimeMinSeconds, BaolongdaProfile.StapmTimeMaxSeconds);
		case "GpuConfigurableTGPTarget":
			return _hw.GpuTgpAdjustable && InRange(result, _hw.GpuTgpMinimum, _hw.GpuTgpMaximum);
		case "GpuDynamicBoost":
			return _hw.GpuDynamicBoostAdjustable && InRange(result, _hw.GpuDbMinimum, _hw.GpuDbMaximum);
		case "GpuCoreClockOffsetOC":
		{
			// （宝龙达 · GPU 偏移）：宝龙达的可用范围来自 NVAPI P0（与 CustomModeForm 的
			if (TryGetBaolongdaGpuOffsetUiRange(core: true, out int coreMin, out int coreMax))
			{
				return InOffsetRange(result, coreMin, coreMax);
			}
			return _hw.SupportsGpuOverclock && _hw.GpuCoreOffsetAdjustable && InOffsetRange(result, _hw.GpuCoreOffsetUserMinimum, _hw.GpuCoreOffsetUserMaximum);
		}
		case "GpuMemoryClockOffsetOC":
		{
			if (TryGetBaolongdaGpuOffsetUiRange(core: false, out int memMin, out int memMax))
			{
				return InOffsetRange(result, memMin, memMax);
			}
			return _hw.SupportsGpuOverclock && _hw.GpuMemoryOffsetAdjustable && InOffsetRange(result, _hw.GpuMemoryOffsetUserMinimum, _hw.GpuMemoryOffsetUserMaximum);
		}
		case "CpuTccOffsetSwitch":
		{
			bool flag = _hw.TccAdjustable;
			if (flag)
			{
				bool flag2 = (uint)result <= 1u;
				flag = flag2;
			}
			return flag;
		}
		case "GpuDynamicBoostSwitch":
		{
			bool flag = _hw.GpuDynamicBoostAdjustable;
			if (flag)
			{
				bool flag2 = (uint)result <= 1u;
				flag = flag2;
			}
			return flag;
		}
		case "OverClockingSwitch":
		{
			bool flag = _hw.SupportsGpuOverclock;
			if (flag)
			{
				bool flag2 = (uint)result <= 1u;
				flag = flag2;
			}
			return flag;
		}
		default:
			return false;
		}
		static bool InOffsetRange(int value, int min, int max)
		{
			if (max > min && value >= min)
			{
				return value <= max;
			}
			return false;
		}
		static bool InRange(int value, int min, int max)
		{
			if (min >= 0 && max >= min && value >= min)
			{
				return value <= max;
			}
			return false;
		}
	}

	private bool CustomFieldMatches(string key, string text)
	{
		if (!int.TryParse(text, out var result))
		{
			return false;
		}
		return key switch
		{
			"PL1" => _hw.Pl1 == result, 
			"PL2" => _hw.Pl2 == result, 
			"PL4" => _hw.Pl4 == _hw.Pl4Effective(result), 
			"FanSwitchSpeedEnabled" => _hw.FanSwitchSpeedEnabled == (result == 1), 
			"FanSwitchSpeed" => _hw.FanSwitchSpeed == _hw.QuantiseFanSwitchSpeed(result), 
			"CpuTccOffset" => _hw.TccTarget == result, 
			// （30 系 · SMU 时间窗）：本地记忆（写成功后更新）与下发值一致
			"SmuStapmTimeSeconds" => _hw.BaolongdaStapmTimeSeconds == result, 
			"GpuConfigurableTGPTarget" => _hw.GpuTgp == result, 
			"GpuDynamicBoost" => _hw.GpuDb == result, 
			"GpuCoreClockOffsetOC" => _hw.EffectiveGpuCoreClockOffset == result, 
			"GpuMemoryClockOffsetOC" => _hw.EffectiveGpuMemoryClockOffset == result, 
			"CpuTccOffsetSwitch" => _hw.TccSwitch == (result == 1), 
			"GpuDynamicBoostSwitch" => _hw.GpuDbSwitch == (result == 1), 
			"OverClockingSwitch" => _hw.GpuOverclockEnabled == (result == 1), 
			_ => false, 
		};
	}

	private bool GcuCustomFieldMatches(string key, string text)
	{
		if (!int.TryParse(text, out var result))
		{
			return false;
		}
		return key switch
		{
			"GpuCoreClockOffsetOC" => _hw.GpuCoreClockOffset == result, 
			"GpuMemoryClockOffsetOC" => _hw.GpuMemClockOffset == result, 
			"OverClockingSwitch" => _hw.OcSwitch == (result == 1), 
			_ => CustomFieldMatches(key, text), 
		};
	}

	private static bool IsGpuOverclockField(string key)
	{
		switch (key)
		{
		case "GpuCoreClockOffsetOC":
		case "GpuMemoryClockOffsetOC":
		case "OverClockingSwitch":
			return true;
		default:
			return false;
		}
	}

	internal static bool GpuOverclockEnabled { get; set; } = false;

	internal static bool ShouldApplyTurboGpuOverclockDefaultsOnModeSwitch(int mode, bool turboSubModeSupported)
	{
		return mode == 1 && turboSubModeSupported && GpuOverclockEnabled;
	}

	internal static bool ShouldApplyTurboGpuOverclockDefaultsOnSubMode(bool silent)
	{
		return GpuOverclockEnabled;
	}

	internal async Task<bool> ApplyTurboGpuOverclockDefaults()
	{
		MechrevoHw hw = _hw;
		// 2026-10-15（C-1 第 2 步分类 ①）：显卡超频走 GCU ⇒ 真正需要 GCU。
		if (hw == null || !hw.IsGcuConnected)
		{
			return false;
		}
		if (!_hw.SupportsGpuOverclock && !_hw.Capabilities.OverclockSettings)
		{
			return false;
		}
		try
		{
			for (int i = 0; i < TurboGpuOverclockDefaultFields.Length; i++)
			{
				var (text, value) = TurboGpuOverclockDefaultFields[i];
				await _hw.Publish("Fan/Control", new Dictionary<string, object>
				{
					["Action"] = "SET_OPERATING_MODE_DETAIL",
					[text] = value
				}).ConfigureAwait(continueOnCapturedContext: false);
				if (i + 1 < TurboGpuOverclockDefaultFields.Length)
				{
					await Task.Delay(120).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			Logger.WriteLine("ApplyTurboGpuOverclockDefaults sent core=105 memory=500");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ApplyTurboGpuOverclockDefaults fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> ApplyGpuOverclock(bool enabled)
	{
		if (enabled)
		{
			return await ApplyTurboGpuOverclockDefaults().ConfigureAwait(continueOnCapturedContext: false);
		}
		return await RestoreGpuOverclockDefaults().ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<bool> RestoreGpuOverclockDefaults()
	{
		MechrevoHw hw = _hw;
		// 2026-10-15（C-1 第 2 步分类 ①）：MQTT 下发入口 ⇒ 真正需要 GCU。
		if (hw == null || !hw.IsGcuConnected)
		{
			return false;
		}
		try
		{
			for (int i = 0; i < RestoreGpuOverclockDefaultFields.Length; i++)
			{
				var (text, value) = RestoreGpuOverclockDefaultFields[i];
				await hw.Publish("Fan/Control", new Dictionary<string, object>
				{
					["Action"] = "SET_OPERATING_MODE_DETAIL",
					[text] = value
				}).ConfigureAwait(continueOnCapturedContext: false);
				if (i + 1 < RestoreGpuOverclockDefaultFields.Length)
				{
					await Task.Delay(120).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			Logger.WriteLine("ApplyGpuOverclock off: GPU overclocking disabled (core=0 memory=0)");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ApplyGpuOverclock off fail: " + ex.Message);
			return false;
		}
	}
	private static bool IsGateField(string key)
	{
		if (!key.EndsWith("Switch", StringComparison.Ordinal))
		{
			return key.EndsWith("Enabled", StringComparison.Ordinal);
		}
		return true;
	}

	public bool RestoreCurrentDirectGpuOverclock()
	{
		if (_hw.OperatingMode == 3)
		{
			return _hw.RestoreDirectGpuOverclockProfile(_hw.CustomProfileIndex);
		}
		return false;
	}

	public async Task<bool> SwitchFanRespective(bool on)
	{
		try
		{
			MechrevoHw hw = _hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：分路风扇曲线是 GCU 专属通路 ⇒ 真正需要 GCU。
		if (hw == null || !hw.IsGcuConnected || !_hw.SupportsFanRespective)
			{
				return false;
			}
			await _hw.Publish("Fan/Control", new Dictionary<string, object>
			{
				["Action"] = "SET_FAN_CONTROL_RESPECTIVE",
				["Name"] = (string.IsNullOrEmpty(_hw.TableName) ? _hw.CurveName : _hw.TableName),
				["FanControlRespective"] = on
			});
			Logger.WriteLine($"SwitchFanRespective({on}) 已发送");
			bool flag = await _hw.WaitForStateAsync(() => _hw.FanRespective == on, TimeSpan.FromMilliseconds(180L));
			int attempt = 0;
			while (!flag && attempt < 3)
			{
				await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GET_FAN_SPEED_CURVE_SETTING" });
				flag = await _hw.WaitForStateAsync(() => _hw.FanRespective == on, TimeSpan.FromMilliseconds((attempt == 0) ? 600 : 800));
				attempt++;
			}
			Logger.WriteLine($"SwitchFanRespective confirmed={flag} expected={on} actual={_hw.FanRespective}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchFanRespective fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> SetColorCalibration(int mode)
	{
		if ((mode < 0 || mode > 4) ? true : false)
		{
			return false;
		}
		await _colorCalibrationLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			MechrevoHw hw = _hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：校色档位由 GCU 上报、下发也走 GCU ⇒ 真正需要 GCU。
		if (hw == null || !hw.IsGcuConnected || !_hw.SupportsColorCalibration)
			{
				return false;
			}
			bool flag = mode != 0;
			int num = ReadColorCalibrationMode();
			bool? knownOn = MechrevoService.TryReadColorCalibrationOn();
			// 2026-10-10（第 ② 条同源修复 · 保留 null 三态）：原实现 `bool currentOn = knownOn == true;` 把
			// 「读失败/未知(null)」折叠成「关(false)」。未知被当成「实际是关」有两重害处：① 关方向（mode=0）
			// 命中 StateMatches(false,false,...) 判 AlreadyApplied 而静默短路（「关不掉」那一半镜像 bug）；
			// ② 开方向会被误判为「需要下发」而看不出真实原因。现保留三态：**null = 未知 ⇒ 不去重、照常下发**。
			// 注意下面对 Decide 的调用必须带 knownOn.HasValue 前提：ColorCalibrationSwitchPolicy.Decide 的
			// currentOn 是 bool（策略层无三态位），null 进去会被 C# 提升规则折成 false，故在调用点就拦住。
			bool? currentOn = knownOn;
			bool binaryCalibration = IsBinaryColorCalibration();
			// 2026-10-10（「屏幕校色开关无反应」修复）：AlreadyApplied 去重只在「GCU 真能回读校色状态」的设备上才可信。
			// 本机（yilong15 Pro GM5HG0A，Gen40 + sRGB 屏，SupportColorCalibration=1、档位数 1）无回读（SupportsReadback=false），
			// 而注册表侧长期只靠厂商粘滞值 HKLM\SOFTWARE\OEM\GamingCenter2\UninstallCalibration=0
			// 读成「已校准=true」且永不复位 ⇒ 开关由「关」拨到「开」时判 AlreadyApplied 直接 return true：一条
			// COLOR_CALIBRATION_ON 都不发、硬件不动、屏幕不变，但 UI/config 记成成功（诊断包 2026-10-10 13:13:25 仅一行
			// "SetColorCalibration(mode 1) already applied"，config.json.bak calib_enabled=0 → config.json calib_enabled=1）。
			// 故无回读能力时不去重、一律下发（GCU 侧重复下发幂等）；有回读能力时只做「三态保留 + 判定源降级」两处同源修正。
			bool calibReadbackTrusted = ColorCalibrationSwitchPolicy.SupportsReadback(_hw.DgpuGeneration, _hw.ColorCalibrationModeSeen);
			bool calibWouldBeAlreadyApplied = knownOn.HasValue && currentOn.HasValue && ColorCalibrationSwitchPolicy.Decide(connectedAndSupported: true, flag, flag && _readHdrEnabled(), currentOn.Value, num, mode, binaryDevice: binaryCalibration) == ColorCalibrationDecision.AlreadyApplied;
			if (calibWouldBeAlreadyApplied && !calibReadbackTrusted)
			{
				Logger.WriteLine($"SetColorCalibration(mode {mode}) 无回读能力，跳过去重照常下发（knownOn={knownOn} mode={num} 代际={_hw.DgpuGeneration}）");
			}
			if (calibWouldBeAlreadyApplied && calibReadbackTrusted)
			{
				Logger.WriteLine($"SetColorCalibration(mode {mode}) already applied");
				return true;
			}
			{
				long initialCalibrationStatusVersion = _hw.ColorCalibrationStatusVersion;
				if (!knownOn.HasValue)
				{
					// 第 ② 条取证：未知（注册表开关键与兜底项都不可用）时按「照常下发」处理，这里留一行说明不是去重命中。
					Logger.WriteLine($"SetColorCalibration(mode {mode}) 开关状态未知(读失败)，不去重照常下发（mode={num} 代际={_hw.DgpuGeneration}）");
				}
				if (flag)
				{
					string action = ColorCalibrationAction(mode, binaryCalibration);
					await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = action });
					Logger.WriteLine($"SetColorCalibration(mode {mode}) -> {action}");
				}
				else
				{
					// 二分设备（40 系 + sRGB 屏）只认裸 COLOR_CALIBRATION_OFF，不带 FileName；
					// 多档设备（50 系）才把当前档位名放进 FileName，供 GCU 还原该档的 ICC。
					if (binaryCalibration)
					{
						await _hw.Publish("Setting/Control", new Dictionary<string, object>
						{
							["Action"] = "COLOR_CALIBRATION_OFF"
						});
						Logger.WriteLine("SetColorCalibration(off) binary-panel pure OFF");
					}
					else
					{
						string action = ColorCalibrationFileName(num);
						await _hw.Publish("Setting/Control", new Dictionary<string, object>
						{
							["Action"] = "COLOR_CALIBRATION_OFF",
							["FileName"] = action
						});
						Logger.WriteLine("SetColorCalibration(off) FileName=" + action);
					}
					await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "DISPLAY_FEATURE_STATUS_ON" });
				}
				InvalidateColorCalibrationCache();
				bool flag2 = await ConfirmColorCalibrationAsync(mode, initialCalibrationStatusVersion).ConfigureAwait(continueOnCapturedContext: false);
				Logger.WriteLine($"SetColorCalibration confirmed={flag2} expectedMode={mode} actualMode={ReadColorCalibrationMode()} switch={ResolveColorCalibrationOn(out string confirmedSwitchSource)} switch来源={confirmedSwitchSource} result={_hw.ColorCalibrationResult}");
				return flag2;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SetColorCalibration fail: " + ex.Message);
			return false;
		}
		finally
		{
			_colorCalibrationLock.Release();
		}
	}

	public async Task<bool> SetDisplayEffectSwitch(bool on)
	{
		try
		{
			await _hw.Publish("Display/Control", new Dictionary<string, object>
			{
				["Action"] = "EffectSwitch",
				["Switch"] = on
			});
			Logger.WriteLine($"SetDisplayEffectSwitch({on})");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SetDisplayEffectSwitch fail: " + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// 核显省电（<c>IGPU_ONLY_CONNECT_RB_*</c>）下发 + **真实确认**。
	/// <para>2026-10-13（用户批准「连同强制一起做」）改动三处，其余一字未动：</para>
	/// <list type="number">
	/// <item>⭐ 仿 <see cref="SwitchGpuMode" />：主动 GETSTATUS + <c>WaitForStateAsync(250ms)</c>
	/// + 两次 300ms 补查，第 1/3/5 次（0 基）补查时重发命令；</item>
	/// <item>⭐⭐ 判据换成**新谓词** <see cref="IgpuOnlySaveModeDecision.IsTargetReached" /> ——
	/// 看的是 <c>_hw.IgpuOnlySaveMode</c>（核显省电的独立回读字段），
	/// ⛔ 不再是 <c>TargetReached</c>（那条只看显示路由 <c>CurrentGpuMode</c>/<c>GpuModeStatusVersion</c>，
	/// **根本确认不了这个动作**）；</item>
	/// <item>⛔ 去掉无条件 <c>return true</c> ⇒ 返回真实结果（未确认就是 <c>false</c>）。</item>
	/// </list>
	/// <para>⚠️ 只读一次"下发前取值"用于日志与回滚判定，⛔ 不额外发命令；确认预算与
	/// <c>SwitchGpuMode</c> 同量级（最坏 250 + 2×300 = 850ms）。</para>
	/// </summary>
	public async Task<bool> SetIGpuOnlySaveMode(string mode)
	{
		if (!IgpuOnlySaveModeDecision.TryResolveTargetMode(mode, out int targetMode))
		{
			return false;
		}
		bool lockTaken = false;
		bool commandSent = false;
		int previousMode = _hw.IgpuOnlySaveMode;
		try
		{
			// ⚠️ 与 SwitchGpuMode 共用同一把切换锁：两条命令都会改 EC，⛔ 不能让它们交错。
			// 拿不到锁（另有切换在跑）就当"本次没做成"，⛔ 不排队、不硬闯。
			lockTaken = await AcquireSwitchLockAsync($"SetIGpuOnlySaveMode({mode})", CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			if (!lockTaken)
			{
				Logger.WriteLine($"SetIGpuOnlySaveMode({mode}) skipped: another switch is in progress");
				return false;
			}
			Dictionary<string, object> payload = new Dictionary<string, object>
			{
				["Action"] = IgpuOnlySaveModeDecision.ResolveActionName(mode),
				["SetToWMIEC"] = "OK"
			};
			if (payload["Action"] == null)
			{
				return false;
			}
			Logger.WriteLine($"SetIGpuOnlySaveMode({mode}) -> target={targetMode}, before={previousMode}");
			await _hw.Publish("Setting/Control", payload).ConfigureAwait(continueOnCapturedContext: false);
			commandSent = true;
			// 与 SwitchGpuMode 同款：先主动 GETSTATUS（GCU 被轮询时 ~3ms 就答），再等 250ms；
			// 之后最多 2 次 ×300ms 补查（事件驱动，一收到状态包立即返回、不等满）。
			await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
			bool confirmed = await _hw.WaitForStateAsync(() => IgpuOnlySaveModeDecision.IsTargetReached(_hw.IgpuOnlySaveMode, targetMode), TimeSpan.FromMilliseconds(250L)).ConfigureAwait(continueOnCapturedContext: false);
			for (int attempt = 0; attempt < 2 && !confirmed; attempt++)
			{
				await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
				confirmed = await _hw.WaitForStateAsync(() => IgpuOnlySaveModeDecision.IsTargetReached(_hw.IgpuOnlySaveMode, targetMode), TimeSpan.FromMilliseconds(300L)).ConfigureAwait(continueOnCapturedContext: false);
				if (!confirmed && IgpuOnlySaveModeDecision.ShouldResendOnAttempt(attempt))
				{
					await _hw.Publish("Setting/Control", payload).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			Logger.WriteLine($"SetIGpuOnlySaveMode confirmed={confirmed}: expected={targetMode} actual={_hw.IgpuOnlySaveMode} before={previousMode} sent={commandSent}");
			if (!confirmed)
			{
				// ⭐ 阶段 3（v）：未确认时**不在这里**自己回滚 —— 回滚是"调用方决定要不要恢复上一次状态"，
				// 由 SettingsForm 的强制路按 IgpuOnlySaveModeDecision.ShouldRollback 统一处置（含普通路）。
				Logger.WriteLine($"SetIGpuOnlySaveMode 未在等待预算内确认: expected={targetMode} actual={_hw.IgpuOnlySaveMode}");
			}
			return confirmed;
		}
		catch (OperationCanceledException)
		{
			Logger.WriteLine($"SetIGpuOnlySaveMode({mode}) cancelled");
			return false;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SetIGpuOnlySaveMode fail: " + ex.Message);
			return false;
		}
		finally
		{
			if (lockTaken)
			{
				_switchLock.Release();
			}
		}
	}

	/// <summary>
	/// 2026-10-13（用户批准）：核显省电的**专用重读**。发一次 GETSTATUS 并等一个新状态包
	/// （默认 1500ms，与 <see cref="RefreshGpuModeStatus" /> 同量级），返回是否收到新包。
	/// <para>⭐ 为什么需要它：<c>MechrevoHw.IgpuOnlySaveMode</c> 是**粘滞缓存**
	/// （<c>MechrevoHw.cs:597</c> 初值写死 1=OFF、<c>:4219-4223</c> 只在包里有该字段时才覆盖），
	/// 高亮读的就是这个缓存 ⇒ 尝试后必须"重新问一次"才能让高亮反映真实状态。</para>
	/// <para>⛔ 只发 GETSTATUS（只读查询），⛔ 不写 EC、⛔ 不改任何字段。</para>
	/// </summary>
	public async Task<bool> RecheckIgpuOnlySaveModeAsync(TimeSpan? timeout = null)
	{
		try
		{
			// 2026-10-15（C-1 第 2 步分类 ①）：走 GCU 通路（超频回退 / 液冷 LCHWOC·BT_LC）⇒ 真正需要 GCU。
			if (!_hw.IsGcuConnected)
			{
				return false;
			}
			long versionBefore = _hw.GpuModeStatusVersion;
			await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
			bool fresh = await _hw.WaitForStateAsync(() => _hw.GpuModeStatusVersion > versionBefore, timeout ?? TimeSpan.FromMilliseconds(1500L)).ConfigureAwait(continueOnCapturedContext: false);
			Logger.WriteLine($"RecheckIgpuOnlySaveMode fresh={fresh} mode={_hw.IgpuOnlySaveMode}");
			return fresh;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RecheckIgpuOnlySaveMode failed: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> ApplyDisplayEffectProfile(DisplayEffectProfile profile)
	{
		if (_hw.IsIntelNucMachine || _hw.IsUmiMachine)
		{
			return SoftwareColorEnhancement.ApplyProfile(profile.Name, profile.Brightness, profile.ColorTemp,
				profile.ColorR, profile.ColorG, profile.ColorB, profile.VibrantValue, profile.Constrast, profile.Gamma);
		}
		if (profile == null || string.IsNullOrWhiteSpace(profile.Name))
		{
			return false;
		}
		try
		{
			await _hw.Publish("Display/Control", new Dictionary<string, object>
			{
				["colorProfileName"] = profile.Name,
				["brightness"] = profile.Brightness,
				["ColorTemp"] = profile.ColorTemp,
				["ColorR"] = profile.ColorR,
				["ColorG"] = profile.ColorG,
				["ColorB"] = profile.ColorB,
				["VibrantValue"] = profile.VibrantValue,
				["Constrast"] = profile.Constrast,
				["Gamma"] = profile.Gamma
			});
			Logger.WriteLine($"ApplyDisplayEffectProfile({profile.Name}) b={profile.Brightness} ct={profile.ColorTemp} rgb={profile.ColorR}/{profile.ColorG}/{profile.ColorB} v={profile.VibrantValue} c={profile.Constrast} g={profile.Gamma}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ApplyDisplayEffectProfile fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> ResetDisplayEffectProfile(string mode)
	{
		if (_hw.IsIntelNucMachine || _hw.IsUmiMachine)
		{
			return SoftwareColorEnhancement.Reset();
		}
		if (string.IsNullOrWhiteSpace(mode))
		{
			return false;
		}
		try
		{
			await _hw.Publish("Display/Control", new Dictionary<string, object>
			{
				["Action"] = "Default",
				["Profile"] = mode
			});
			Logger.WriteLine($"ResetDisplayEffectProfile({mode})");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ResetDisplayEffectProfile fail: " + ex.Message);
			return false;
		}
	}

	public async Task RequestDisplayEffectStatus()
	{
		try
		{
			await _hw.Publish("Display/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RequestDisplayEffectStatus fail: " + ex.Message);
		}
	}

	private async Task<bool> ConfirmColorCalibrationAsync(int mode, long initialCalibrationStatusVersion)
	{
		bool expectedOn = mode != 0;
		// 2026-10-10（第 ③ 条同源修复 · 裁定口径与 SetColorCalibration 去重口径统一）：
		// 原来这里只判 `!ColorCalibrationModeSeen`，而 4s 裁定用的是注册表 ReadColorCalibrationOn()（GCU 侧开关
		// 上报完全不参与）。于是 40 系（Gen40 + sRGB 屏）在「GCU 上过报一次档位」或「注册表读失败」等情形下会
		// 落进 4s 轮询：命令其实已下发成功，但注册表粘滞值不回读/读不回来 ⇒ 4s 后判失败、UI 回弹（假失败）。
		// 现与 :1465 的 SupportsReadback(代际, ColorCalibrationModeSeen) 同一口径：**无回读能力 ⇒ 发送即成功，
		// 不进 4s 轮询**（GCU 侧重复下发幂等，且没有可信回读可等）。有回读能力时进入下方轮询，逻辑保持原样。
		if (!ColorCalibrationSwitchPolicy.SupportsReadback(_hw.DgpuGeneration, _hw.ColorCalibrationModeSeen))
		{
			return true;
		}
		Stopwatch elapsed = Stopwatch.StartNew();
		int poll = 0;
		// 2026-10-09（校色档位误判修复）：本次是否已见过「GCU 侧流程失败码」（见下方注释）。
		bool failureCodeSeen = false;
		while (elapsed.Elapsed < TimeSpan.FromSeconds(4L))
		{
			InvalidateColorCalibrationCache();
			bool actualOn = ResolveColorCalibrationOn(out string _);
			int actualMode = ReadColorCalibrationMode();
			bool flag = ColorCalibrationSwitchPolicy.StateMatches(expectedOn, actualOn, actualMode, mode, IsBinaryColorCalibration());
			bool flag2 = _hw.ColorCalibrationStatusVersion > initialCalibrationStatusVersion;
			int colorCalibrationResult = _hw.ColorCalibrationResult;
			bool flag3 = flag2 & flag;
			if (flag3)
			{
				bool flag4 = (uint)(colorCalibrationResult - -1) <= 1u;
				flag3 = flag4;
			}
			if (flag3)
			{
				return true;
			}
			// 2026-10-09（校色档位误判失败修复 · 真机证据：0.291.1 耀世 sent=2 后 242ms 就判死回退，但 GCU
			// 稍后已上报档位 2 ⇒ 界面与硬件不一致）：**不再一见到失败码就 return false**。
			// 这些码的语义来自 GCU 侧 `Define\ColorCalibrationResultCode`：
			//   2=NetworkFail 3=FileIsNotFound 4=ColorCalibrationFail 5=WebClientRequestFail 7=WebClientUnknownFail
			// 它们是 GCU 「ICC 下载/应用流程」的**异步回执码**（由 DownloadProfile 完成回调写入），不是
			// 「档位没生效」的终判 —— 档位/开关是否真的变了，只有回读（GCU 上报的 ColorCalibrationMode /
			// Switch）说了算。故这里只记录一次（并立刻补一发 GETSTATUS 催状态），流程继续按成功分支同样的
			// 节奏轮询到 4s 时间窗用尽；最终成败在循环末尾**只以回读结果**裁定。
			if (flag2 && !failureCodeSeen)
			{
				bool failureCode = (((uint)(colorCalibrationResult - 2) <= 3u || colorCalibrationResult == 7) ? true : false);
				if (failureCode)
				{
					failureCodeSeen = true;
					Logger.WriteLine($"SetColorCalibration(mode {mode}) result={colorCalibrationResult}（GCU 流程回执码），不立即判失败，继续轮询回读，最长 4s");
					await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
							// 40A 兜底：GCU 未上报校色开关/档位字段时，以 ColorCalibrationResultCode==1 视为应用成功
				if (!_hw.ColorCalibrationModeSeen && !_hw.ColorCalibrationSwitchSeen && _hw.ColorCalibrationResult == 1)
				{
					return true;
				}
// 兜底：GCUService 应用后可能只写注册表而未更新状态流，注册表状态匹配即视为成功
			if (flag)
			{
				return true;
			}
			await Task.Delay(120).ConfigureAwait(continueOnCapturedContext: false);
			int num = poll + 1;
			poll = num;
			if (num % 4 == 0)
			{
				await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		// 时间窗用尽（4s / 约 33 次 120ms 轮询 / 约 8 次 GETSTATUS）：**只以回读结果裁定成败** ——
		// 回读与期望一致 ⇒ 成功（即使过程里出现过 2–5/7 那些 GCU 流程回执码）；
		// 回读确实没生效 ⇒ 才算失败（调用方 SettingsForm 照原文案提示并回退下拉）。
		// 2026-10-10（第 ③ 条同源修复）：这里的回读也统一走 ResolveColorCalibrationOn —— GCU 上报过开关
		// （ColorCalibrationSwitchSeen）就用 GCU 上报值，否则才退回注册表值（原实现固定用注册表值，与 :1465/:1652
		// 的判定源不是同一套口径，是「假失败」的另一半来源）。
		InvalidateColorCalibrationCache();
		bool finalOn = ResolveColorCalibrationOn(out string finalSwitchSource);
		int finalMode = ReadColorCalibrationMode();
		bool finalMatch = ColorCalibrationSwitchPolicy.StateMatches(expectedOn, finalOn, finalMode, mode, IsBinaryColorCalibration());
		// 2026-10-10（第 ④ 条 · 只取证不改行为）：actualOn 的来源是**注册表**（而非 GCU 上报）时留一行证据，
		// 便于下次直接判定「假失败/静默短路是不是注册表粘滞值导致的」。频率 = 每次 ConfirmColorCalibrationAsync
		// 终判 0/1 行（无回读能力时第 ③ 条已提前返回，连这行都不会写），不会进轮询循环、不构成噪音。
		if (finalSwitchSource == "注册表")
		{
			Logger.WriteLine($"SetColorCalibration(mode {mode}) 开关实际值来源=注册表（GCU 未上报开关）switch={finalOn} 期望={expectedOn} 代际={_hw.DgpuGeneration}");
		}
		// ==========================================================================================
		// 2026-10-10（用户反馈 ①「校色功能有概率在校色成功了也提醒失败」）：
		// ⚠️ 本段是**在既有三条修复之上追加的小修**，一条都没有回退：
		//    * 判定源仍是 MySetting\DisplayFeatures\ColorCalibrationSwitch 优先 + UninstallCalibration 兜底
		//      （ResolveColorCalibrationOn / TryReadColorCalibrationOn）；
		//    * 三态 bool? 仍在（:1455-1472）—— 未知**不短路**，照常下发；
		//    * 同口径裁定仍在（:1654 与 :1465 都用 SupportsReadback(代际, ColorCalibrationModeSeen)）；
		//    * 既有取证日志一字未删（上面那行「来源=注册表」原样保留）。
		// 真机证据（诊断包 0.291.6 / Gen50，log_level=off ⇒ 只有内存环形缓冲可用）：
		//   23:53:54.821 SetColorCalibration(mode 3) 回读未生效，判为失败: actualMode=3 switch=False(来源=GCU上报) result=0 轮询=33 次(上限 4s)
		//   23:54:02.094 SetColorCalibration(mode 2) 回读未生效，判为失败: actualMode=2 switch=False(来源=GCU上报) result=0
		//   23:54:07.722 SetColorCalibration(mode 2) already applied   ← 同一个目标约 5s 后才回读为真
		// ⇒ 档位回读（GCU 上报的 ColorCalibrationMode）**已经等于目标档**，唯独同一次 GCU 上报里的
		//   开关位 ColorCalibrationSwitch 滞后 / 在本次会话内粘滞为假。ColorCalibrationSwitchPolicy.StateMatches
		//   的「开关优先」否决（ColorCalibrationSwitchPolicy.cs:58-62）把这种情况在 4s 处一刀切成失败 ⇒ 假失败。
		// 最小修法两处，**都只在终判这一刻生效**：绝不改进上方轮询循环的节奏，也绝不动 :1464-1481 的
		// AlreadyApplied 去重路径 —— 那条「开关优先」是防「档位残留 ⇒ 误判已应用」的，必须原样保留
		// （用户报的是"成功却报失败"，不是"没生效却报成功"，两者方向相反，不能一起放松）。
		//   ① 一次很短的宽限重读（2.4s），把 GCU 开关位的滞后吃掉；
		//   ② 宽限后仍只有开关位不一致，但「档位回读＝目标档 且 开关值来源=GCU上报 且 GCU 确实刷新过状态」
		//      ⇒ 按**已生效**处理并写明理由：多档设备上"当前是哪一档"才是权威状态，开关位只是同一次
		//      上报里的另一个字段；同时把这种情况与"档位确实没生效"在日志上**分开写**（语义区分）。
		// ==========================================================================================
		const int ColorCalibrationSwitchLagGraceMs = 2400;
		if (!finalMatch)
		{
			bool switchLagSuspected = expectedOn && !finalOn && finalSwitchSource == "GCU上报" && !IsBinaryColorCalibration() && finalMode == mode;
			if (switchLagSuspected)
			{
				Logger.WriteLine($"SetColorCalibration(mode {mode}) 终判不一致但档位回读已等于目标（actualMode={finalMode} switch=False 来源=GCU上报）⇒ 宽限重读 {ColorCalibrationSwitchLagGraceMs}ms（GCU 开关位滞后/会话内粘滞，不是档位未生效）");
				Stopwatch grace = Stopwatch.StartNew();
				int gracePoll = 0;
				while (grace.Elapsed < TimeSpan.FromMilliseconds(ColorCalibrationSwitchLagGraceMs))
				{
					await Task.Delay(120).ConfigureAwait(continueOnCapturedContext: false);
					gracePoll++;
					InvalidateColorCalibrationCache();
					bool graceOn = ResolveColorCalibrationOn(out string graceSource);
					int graceMode = ReadColorCalibrationMode();
					if (ColorCalibrationSwitchPolicy.StateMatches(expectedOn, graceOn, graceMode, mode, IsBinaryColorCalibration()))
					{
						finalOn = graceOn;
						finalMode = graceMode;
						finalSwitchSource = graceSource;
						finalMatch = true;
						Logger.WriteLine($"SetColorCalibration(mode {mode}) 宽限重读后回读一致 ⇒ 成功（多等 {grace.ElapsedMilliseconds}ms / {gracePoll} 次；GCU 开关位此前滞后）");
						break;
					}
					if (gracePoll % 4 == 0)
					{
						await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
				if (!finalMatch && finalMode == mode && _hw.ColorCalibrationModeSeen && _hw.ColorCalibrationStatusVersion > initialCalibrationStatusVersion)
				{
					Logger.WriteLine($"SetColorCalibration(mode {mode}) 宽限后 GCU 开关位仍未回读为真，但档位回读已等于目标且 GCU 已刷新过状态（actualMode={finalMode} switch={finalOn} 来源={finalSwitchSource} result={_hw.ColorCalibrationResult}）⇒ 按已生效处理（GCU 开关位在本次会话内粘滞/滞后；这条判失败就是用户报的「成功却提示失败」）");
					finalMatch = true;
				}
			}
		}
		if (!finalMatch)
		{
			// 与上面那类「档位回读已一致、只有开关位滞后」刻意用不同措辞：这一行才是"档位确实没生效"。
			Logger.WriteLine($"SetColorCalibration(mode {mode}) 回读未生效，判为失败: actualMode={finalMode} switch={finalOn}(来源={finalSwitchSource}) result={_hw.ColorCalibrationResult} 轮询={poll} 次(上限 4s)");
		}
		return finalMatch;
	}

	/// <summary>
	/// 裁定时用的「校色开关实际值」：GCU 上报过开关（<see cref="MechrevoHw.ColorCalibrationSwitchSeen"/>）就用
	/// GCU 上报值——注意 GCU 未上报时 <c>ColorCalibrationSwitchSeen</c> 也可能被注册表值置位（见 MechrevoHw 的
	/// ResolveColorCalibrationSwitch 调用），那种情况同样标记来源=注册表；GCU 侧从未上报才退回注册表读取。
	/// <paramref name="source"/> 仅用于取证日志（判定逻辑不受影响）。
	/// </summary>
	private bool ResolveColorCalibrationOn(out string source)
	{
		if (_hw.ColorCalibrationSwitchSeen)
		{
			source = "GCU上报";
			return _hw.ColorCalibrationSwitch;
		}
		source = "注册表";
		return _readColorCalibrationOn();
	}

	internal static bool? ResolveColorCalibrationSwitch(bool? registryState, bool? runtimeState)
	{
		return runtimeState ?? registryState;
	}

	private int ReadColorCalibrationMode()
	{
		if (!_hw.ColorCalibrationModeSeen)
		{
			return _readColorCalibrationMode();
		}
		return _hw.ColorCalibrationMode;
	}

	public static int GetColorCalibrationMode()
	{
		try
		{
			int num = ParseColorCalibrationMode(ReadCalibrationRegistryValue("ColorCalibration", "SOFTWARE\\OEM\\GamingCenter2\\MySetting\\DisplayFeatures", "SOFTWARE\\OEM\\GamingCenter2\\DisplayFeatures", "SOFTWARE\\OEM\\GamingCenter\\MySetting\\DisplayFeatures", "SOFTWARE\\OEM\\ControlCenter\\MySetting\\DisplayFeatures"));
			return (num >= 1 && num <= 4) ? num : 0;
		}
		catch
		{
			return 0;
		}
	}

	internal static bool? TryReadColorCalibrationOn()
	{
		lock (ColorCalibrationCacheLock)
		{
			if (Environment.TickCount64 < colorCalibrationCacheUntil)
			{
				return colorCalibrationCache;
			}
		}
		bool? result = ReadColorCalibrationOnFromRegistry();
		lock (ColorCalibrationCacheLock)
		{
			colorCalibrationCache = result;
			colorCalibrationCacheUntil = Environment.TickCount64 + (long)ColorCalibrationCacheTtl.TotalMilliseconds;
			return result;
		}
	}

	internal static void InvalidateColorCalibrationCache()
	{
		lock (ColorCalibrationCacheLock)
		{
			colorCalibrationCacheUntil = 0L;
		}
	}

	private static bool? ReadColorCalibrationOnFromRegistry()
	{
		try
		{
			// 2026-10-10（第 ① 条同源修复 · 判定源降级兜底）：读取顺序必须是
			// ① `MySetting\DisplayFeatures\ColorCalibrationSwitch` = 真正的校色开关键（用户开关的真状态）；
			// ② `SOFTWARE\OEM\GamingCenter2\UninstallCalibration` = 厂商「安装/卸载」标记，**它不是状态源**：
			//    它不随用户开关复位（本机实测恒 0 ⇒ 恒读成「已校准」，把开方向静默短路；若为 1 则相反，把关方向
			//    静默短路，镜像同样错）。故降级为兜底：仅在开关键缺值/为空/无法解析时才参考它，
			//    且其 0/1 语义按历史口径映射（0 = 校准生效）。
			// 保留在方法内部（而非移到调用方）以保证所有调用点同时获益，且注册表读取是只读幂等操作，无副作用。
			if (TryParseCalibrationRegistrySwitch(ReadCalibrationRegistryValue("ColorCalibrationSwitch", "SOFTWARE\\OEM\\GamingCenter2\\MySetting\\DisplayFeatures", "SOFTWARE\\OEM\\GamingCenter\\MySetting\\DisplayFeatures", "SOFTWARE\\OEM\\ControlCenter\\MySetting\\DisplayFeatures"), out var switchOn))
			{
				return switchOn;
			}
			object fallback = ReadCalibrationRegistryValue("UninstallCalibration", "SOFTWARE\\OEM\\GamingCenter2", "SOFTWARE\\OEM\\GamingCenter", "SOFTWARE\\OEM\\ControlCenter");
			if (fallback != null && int.TryParse(fallback.ToString(), out var uninstallFlag))
			{
				return uninstallFlag == 0;
			}
			return null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// 解析注册表读到的校色开关键原始值：支持 "ON"/"OFF"（不区分大小写，含 *_ON / *_OFF 形态）与整数（非 0 = 开）。
	/// 注意判断顺序：空值/空白、无法解析一律返回 false（= 不可用），由调用方继续走 `UninstallCalibration` 兜底。
	/// </summary>
	private static bool TryParseCalibrationRegistrySwitch(object raw, out bool on)
	{
		on = false;
		string text = "";
		if (raw != null)
		{
			text = raw.ToString() ?? "";
		}
		text = text.Trim();
		if (text.Length == 0)
		{
			return false;
		}
		if (text.Contains("OFF", StringComparison.OrdinalIgnoreCase))
		{
			on = false;
			return true;
		}
		if (text.Contains("ON", StringComparison.OrdinalIgnoreCase))
		{
			on = true;
			return true;
		}
		if (int.TryParse(text, out var numeric))
		{
			on = numeric != 0;
			return true;
		}
		return false;
	}

	public static bool IsColorCalibrationOn()
	{
		return TryReadColorCalibrationOn() == true;
	}

	private static object? ReadCalibrationRegistryValue(string valueName, params string[] subKeys)
	{
		RegistryView[] array = new RegistryView[2]
		{
			RegistryView.Registry64,
			RegistryView.Registry32
		};
		for (int i = 0; i < array.Length; i++)
		{
			RegistryView view = array[i];
			try
			{
				using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
				foreach (string name in subKeys)
				{
					using RegistryKey registryKey2 = registryKey.OpenSubKey(name);
					object obj = registryKey2?.GetValue(valueName);
					if (obj != null)
					{
						return obj;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Color calibration registry read failed: value=" + valueName + " view=" + view.ToString() + " " + ex.GetType().Name + " " + ex.Message);
			}
		}
		return null;
	}

	private static int ParseColorCalibrationMode(object? value)
	{
		if (value == null)
		{
			return 0;
		}
		if (int.TryParse(value.ToString(), out var result))
		{
			return result;
		}
		string text = value.ToString() ?? "";
		if (text.Contains("sRGB", StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		if (text.Contains("P3", StringComparison.OrdinalIgnoreCase))
		{
			return 3;
		}
		if (text.Contains("Adobe", StringComparison.OrdinalIgnoreCase))
		{
			return 4;
		}
		return text.Contains("Default", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
	}

	public static AdvancedColorState GetAdvancedColorState()
	{
		try
		{
			bool acm;
			return ResolveAdvancedColorState(ScreenCCD.GetHDRStatus(out acm, log: false), acm);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Advanced color state query failed: " + ex.Message);
			return AdvancedColorState.Off;
		}
	}

	public static bool IsHdrEnabled()
	{
		return GetAdvancedColorState() == AdvancedColorState.Hdr;
	}

	public static bool IsAdvancedColorEnabled()
	{
		return GetAdvancedColorState() != AdvancedColorState.Off;
	}

	internal static AdvancedColorState ResolveAdvancedColorState(bool hdr, bool acm)
	{
		if (!hdr)
		{
			if (!acm)
			{
				return AdvancedColorState.Off;
			}
			return AdvancedColorState.Acm;
		}
		return AdvancedColorState.Hdr;
	}

	/// <summary>
	/// 二分设备（40 系 + sRGB 屏，GCU 只上报 1 个校色档）只有裸 `COLOR_CALIBRATION_ON` / `COLOR_CALIBRATION_OFF`：
	/// 实测 `payload\40` 与 `payload\30` 的 GCUService.exe 命令名表里只有这两个
	/// （GCU 服务定义同样如此）。50 系 GCU 的命令名表里
	/// `COLOR_CALIBRATION_ON_DEFAULT` / `_SRGB` / `_P3` / `_ADOBERGB` 四档齐全，故多档设备 1:1 下发，不再折叠。
	/// </summary>
	private static string ColorCalibrationAction(int mode, bool binaryDevice)
	{
		if (binaryDevice)
		{
			return "COLOR_CALIBRATION_ON";
		}
		return mode switch
		{
			1 => "COLOR_CALIBRATION_ON_DEFAULT", 
			2 => "COLOR_CALIBRATION_ON_SRGB", 
			3 => "COLOR_CALIBRATION_ON_P3", 
			4 => "COLOR_CALIBRATION_ON_ADOBERGB", 
			_ => throw new ArgumentOutOfRangeException("mode"), 
		};
	}

	/// <summary>二分设备：GCU 上报 1 个校色档（40 系 + sRGB 屏），档位只有校准 / 不校准。</summary>
	private bool IsBinaryColorCalibration()
	{
		return ColorCalibrationSwitchPolicy.IsBinaryDevice(_hw.MqttColorCalibrationLevel, _hw.Capabilities.ColorCalibrationLevel, DisplayColorGamut.IsSrgbDisplay);
	}

	private static string ColorCalibrationFileName(int mode)
	{
		return mode switch
		{
			2 => "sRGB", 
			3 => "P3", 
			4 => "AdobeRGB", 
			_ => "Default", 
		};
	}

	public async Task<bool> SetCustomProfileName(string name)
	{
		try
		{
			MechrevoHw hw = _hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：GCU 通路 ⇒ 真正需要 GCU。
			if (hw == null || !hw.IsGcuConnected)
			{
				return false;
			}
			await _hw.Publish("Fan/Control", new Dictionary<string, object>
			{
				["Action"] = "SET_CUSTOM_PROFILE_OSD_STRING",
				["ProfileName"] = name
			});
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SetCustomProfileName fail: " + ex.Message);
			return false;
		}
	}

	internal static Dictionary<string, object> CreateGpuModePayload(int mode)
	{
		return mode switch
		{
			// 2026-10-05：mode 0（核显模式）用 DGPU_DIRECT_CONNECT_TOGGLE_IGPU ——
			// 与 LM 实测发送一致，也不再借用【核显省电】的 IGPU_ONLY_CONNECT_RB_ON（§9.6）。
			0 => new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" }, 
			// 2026-10-05：混合模式用 DGPU_DIRECT_CONNECT_TOGGLE_OFF ——
			// 与同文件 CreateGpuRestartTargetPayloads 的 supportsDgpuDirect 分支一致；
			// IGPU_ONLY_CONNECT_RB_* 归【核显省电】独立功能，三档模式不再借用（§9.6）。
			1 => new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_OFF" }, 
			2 => new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_ON" }, 
			_ => throw new ArgumentOutOfRangeException("mode"), 
		};
	}

	internal static Dictionary<string, object> CreateGpuSwitchPayload(int mode, bool supportsDgpuDirect, bool supportsIgpuOnly)
	{
		if (mode == 1 && !supportsIgpuOnly && supportsDgpuDirect)
		{
			return new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_OFF" };
		}
		if (!((mode == 0) & supportsDgpuDirect) || supportsIgpuOnly)
		{
			return CreateGpuModePayload(mode);
		}
		return new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" };
	}

	public async Task<bool> RequestGpuModeRestartAsync(int mode)
	{
		return await RequestGpuModeRestartOutcomeAsync(mode).ConfigureAwait(continueOnCapturedContext: false) == GpuRestartRequestOutcome.Requested;
	}

	internal async Task<GpuRestartRequestOutcome> RequestGpuModeRestartOutcomeAsync(int mode)
	{
		CancellationTokenSource requestCts = new CancellationTokenSource();
		Interlocked.Exchange(ref _gpuSwitchCts, requestCts)?.Cancel();
		bool lockTaken = false;
		try
		{
			bool flag = !(_hw?.IsConnected ?? false);
			if (!flag)
			{
				bool flag2 = ((mode < 0 || mode > 2) ? true : false);
				flag = flag2;
			}
			if (flag || !_hw.CanSwitchGpuMode(mode))
			{
				return GpuRestartRequestOutcome.Failed;
			}
			lockTaken = await AcquireSwitchLockAsync($"RequestGpuModeRestartAsync({mode})", requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (!lockTaken)
			{
				return GpuRestartRequestOutcome.Failed;
			}
			return await PublishGpuRestartAsync(mode, BuildGpuRestartRoute(mode), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (OperationCanceledException)
		{
			Logger.WriteLine($"GPU restart route superseded: target={mode}");
			return GpuRestartRequestOutcome.Failed;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine($"GPU restart route failed: target={mode} error={ex2.Message}");
			return GpuRestartRequestOutcome.Failed;
		}
		finally
		{
			if (lockTaken)
			{
				_switchLock.Release();
			}
			Interlocked.CompareExchange(ref _gpuSwitchCts, null, requestCts);
			requestCts.Dispose();
		}
	}

	private IReadOnlyList<Dictionary<string, object>> BuildGpuRestartRoute(int mode)
	{
		Func<int, bool, IReadOnlyList<Dictionary<string, object>>> gpuRestartRouteOverride = GpuRestartRouteOverride;
		if (gpuRestartRouteOverride == null)
		{
			return CreateGpuRestartTargetPayloads(mode, _hw.SupportsDgpuDirect, _hw.DgpuGeneration);
		}
		return gpuRestartRouteOverride(mode, _hw.SupportsDgpuDirect);
	}

	private async Task<GpuRestartRequestOutcome> PublishGpuRestartAsync(int mode, IReadOnlyList<Dictionary<string, object>> route, CancellationToken token)
	{
		if (route.Count == 0)
		{
			Logger.WriteLine($"GPU restart route is empty for target={mode} (supportsDgpuDirect={_hw.SupportsDgpuDirect}, supportsIgpuOnly={_hw.SupportsIgpuOnly}); " + "refusing to publish DGPU_DIRECT_CONNECT_RESTART so the machine is not rebooted for nothing.");
			return GpuRestartRequestOutcome.Unsupported;
		}
		List<string> sentActions = new List<string>(route.Count);
		foreach (Dictionary<string, object> item in route)
		{
			token.ThrowIfCancellationRequested();
			sentActions.Add(item["Action"].ToString() ?? "?");
			await _hw.Publish("Setting/Control", item).ConfigureAwait(continueOnCapturedContext: false);
		}
		Logger.WriteLine($"GPU restart route applied for target={mode}: {sentActions.Count} payload(s) [{string.Join(" -> ", sentActions)}]");
		await Task.Delay(800, token).ConfigureAwait(continueOnCapturedContext: false);
		AppConfig.Flush();
		token.ThrowIfCancellationRequested();
		await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_RESTART" }).ConfigureAwait(continueOnCapturedContext: false);
		Logger.WriteLine($"Requested GCU GPU restart route for target={mode}");
		return GpuRestartRequestOutcome.Requested;
	}

	internal static IReadOnlyList<Dictionary<string, object>> CreateGpuRestartTargetPayloads(int mode, bool supportsDgpuDirect, DgpuGenerationKind generation = DgpuGenerationKind.Unknown)
	{
		if (!DisplayRoutePolicy.AllowsAction(generation, "DGPU_DIRECT_CONNECT_RESTART"))
		{
			return Array.Empty<Dictionary<string, object>>();
		}
		IReadOnlyList<Dictionary<string, object>> readOnlyList3;
		switch (mode)
		{
		case 2:
			// 2026-10-05：原来是 3 条 [TOGGLE_ON -> CreateGpuModePayload(1) -> TOGGLE_ON]，
			// 中间那条唯一目的是"清核显省电"（属独立功能，已从三档移除）。
			// 极光 50 系(Gen50)实测：同类冷切换只有 1 条载荷 —— 收敛为 1 条。
			readOnlyList3 = new _003C_003Ez__ReadOnlySingleElementList<Dictionary<string, object>>(new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_ON" });
			break;
		case 1:
		{
			IReadOnlyList<Dictionary<string, object>> readOnlyList4;
			if (!supportsDgpuDirect)
			{
				IReadOnlyList<Dictionary<string, object>> readOnlyList = new _003C_003Ez__ReadOnlySingleElementList<Dictionary<string, object>>(CreateGpuModePayload(1));
				readOnlyList4 = readOnlyList;
			}
			else
			{
				IReadOnlyList<Dictionary<string, object>> readOnlyList = new _003C_003Ez__ReadOnlySingleElementList<Dictionary<string, object>>(new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_OFF" });
				readOnlyList4 = readOnlyList;
			}
			readOnlyList3 = readOnlyList4;
			break;
		}
		case 0:
		{
			IReadOnlyList<Dictionary<string, object>> readOnlyList5;
			if (!supportsDgpuDirect)
			{
				IReadOnlyList<Dictionary<string, object>> readOnlyList = new _003C_003Ez__ReadOnlySingleElementList<Dictionary<string, object>>(CreateGpuModePayload(0));
				readOnlyList5 = readOnlyList;
			}
			else
			{
				IReadOnlyList<Dictionary<string, object>> readOnlyList = new _003C_003Ez__ReadOnlySingleElementList<Dictionary<string, object>>(new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" });
				readOnlyList5 = readOnlyList;
			}
			readOnlyList3 = readOnlyList5;
			break;
		}
		default:
			throw new ArgumentOutOfRangeException("mode");
		}
		IReadOnlyList<Dictionary<string, object>> readOnlyList6 = readOnlyList3;
		if (!IsGenerationRestricted(generation))
		{
			return readOnlyList6;
		}
		return readOnlyList6.Where((Dictionary<string, object> payload) => DisplayRoutePolicy.AllowsAction(generation, ActionOf(payload))).ToArray();
	}

	private static bool IsGenerationRestricted(DgpuGenerationKind generation)
	{
		if ((uint)(generation - 2) <= 2u)
		{
			return true;
		}
		return false;
	}

	private static string ActionOf(Dictionary<string, object> payload)
	{
		if (!payload.TryGetValue("Action", out object value))
		{
			return "";
		}
		return value?.ToString() ?? "";
	}

	public async Task<GpuModeStatusReadback> RefreshGpuModeStatus()
	{
		try
		{
			// 2026-10-15（C-1 第 2 步分类 ①）：GPU 模式状态回读（GCU 上报）⇒ 真正需要 GCU。
			// ⭐⭐ 2026-10-16（宝龙达 · 修「显卡模式切换不行」⚠️ 真机症状：弹窗
			//   「未能读取当前显卡模式。为避免依据旧状态切换，请稍后重试。」❌）：
			//   ⚠️ 原来**第一句**就是这个 GCU 门槛 ⇒ ⭐ 宝龙达没有 GCU ⇒ 恒 Unavailable ❌
			//      ⇒ ⭐ 界面（`SettingsForm.SwitchGpuModeFromUi` 约 :10890 ✅）判「读不到当前模式」
			//      ⇒ ⭐ ⭐ **弹窗并直接 return ⇒ 切换被挡住** ❌
			//   ⇒ ⭐ 所以 ⭐ **在 GCU 门槛【之前】**补一条**非 GCU 后端**的读法 ✅
			//      ⭐ 我们那条读法**本来就读得通**（真机日志：⭐「宝龙达状态：… 显卡=独显直连(1) …」✅）
			//      ⭐ ⇒ ⭐ 这里把它同步进 `_hw.GpuMode` ⇒ ⭐ 下游（`CurrentGpuMode` / ⭐ 界面高亮 /
			//        ⭐ `GpuSwitchPolicy` ✅）**全部自然可用** ✅
			//   ⚠️ 非宝龙达 ⇒ ⭐ `UsesBaolongda` 为假 ⇒ ⭐ **不进这一支** ⇒
			//      ⭐ ⭐ **GCU 机型行为逐位不变** ✅（⭐ 下面那一整段一个字未改 ✅）
			if (_hw.UsesBaolongda)
			{
				bool ok = _hw.TrySyncGpuModeViaBackend();
				Logger.WriteLine($"RefreshGpuModeStatus（宝龙达路径）：⭐ 读功能号 9 ⇒ {(ok ? "成功" : "失败")}"
					+ $" ⇒ 当前模式={CurrentGpuMode}（⭐ 1=混合 · ⭐ 2=独显直连 ✅）"
					+ (ok ? " ⇒ ⭐ 放行切换 ✅" : " ⇒ ⚠️ 界面会如实报「读不到」（⛔ 不拿旧值冒充 ✅）"));
				return ok ? GpuModeStatusReadback.Fresh : GpuModeStatusReadback.Unavailable;
			}
			if (!_hw.IsGcuConnected)
			{
				return GpuModeStatusReadback.Unavailable;
			}
			long versionBeforeRequest = _hw.GpuModeStatusVersion;
			await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
			bool flag = await _hw.WaitForStateAsync(() => _hw.GpuModeStatusVersion > versionBeforeRequest, TimeSpan.FromMilliseconds(1500L)).ConfigureAwait(continueOnCapturedContext: false);
			int num;
			if (_hw.GpuModeStatusVersion > 0)
			{
				int currentGpuMode = CurrentGpuMode;
				num = ((currentGpuMode >= 0 && currentGpuMode <= 3) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			bool flag2 = (byte)num != 0;
			GpuModeStatusReadback gpuModeStatusReadback = (flag ? GpuModeStatusReadback.Fresh : (flag2 ? GpuModeStatusReadback.Cached : GpuModeStatusReadback.Unavailable));
			if (!flag & flag2)
			{
				Logger.WriteLine($"RefreshGpuModeStatus did not receive a new packet; using existing mode={CurrentGpuMode} for this request.");
			}
			Logger.WriteLine($"RefreshGpuModeStatus result={gpuModeStatusReadback} mode={CurrentGpuMode}");
			return gpuModeStatusReadback;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RefreshGpuModeStatus failed: " + ex.Message);
			return GpuModeStatusReadback.Unavailable;
		}
	}

	public async Task<bool> SwitchGpuMode(int mode, bool autoRestart = false)
	{
		CancellationTokenSource requestCts = new CancellationTokenSource();
		Interlocked.Exchange(ref _gpuSwitchCts, requestCts)?.Cancel();
		bool lockTaken = false;
		Stopwatch elapsed = Stopwatch.StartNew();
		long gpuModeStatusVersion;
		try
		{
			bool flag = !(_hw?.IsConnected ?? false);
			if (!flag)
			{
				bool flag2 = ((mode < 0 || mode > 2) ? true : false);
				flag = flag2;
			}
			if (flag || !_hw.CanSwitchGpuMode(mode))
			{
				return false;
			}
			lockTaken = await AcquireSwitchLockAsync($"SwitchGpuMode({mode})", requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			if (!lockTaken)
			{
				return false;
			}
			requestCts.Token.ThrowIfCancellationRequested();
			int modeBeforeSwitch = CurrentGpuMode;
			bool leavingDirect = CurrentGpuMode == 2 && mode != 2;
			// 2026-10-05：删除「自动切换显卡模式」(mode 3/AUTO) —— expectedAutoRuntime 整条链已移除。
			// 2026-10-03：热切换执行已删除（与核显省电功能重叠），切集显统一走重启后应用
			bool muxTargetRequest = mode == 0 && _hw.SupportsDgpuDirect && !_hw.SupportsIgpuOnly;
			gpuModeStatusVersion = _hw.GpuModeStatusVersion;
			GpuRouteCommand gpuRouteCommand = GpuRouteCommandLayer.BuildSwitchCommand(mode, _hw.SupportsDgpuDirect, _hw.SupportsIgpuOnly, _hw.DgpuGeneration);
			if ((object)gpuRouteCommand == null)
			{
				Logger.WriteLine($"SwitchGpuMode({mode}) rejected: action not in dGPU generation {_hw.DgpuGeneration} vocabulary");
				return false;
			}
			Dictionary<string, object> payload = gpuRouteCommand.Payload;
			string action = gpuRouteCommand.Action;
			bool leavingIgpuOnly = modeBeforeSwitch == 0 && mode != 0;
			Logger.WriteLine($"MechrevoService.SwitchGpuMode({mode}) -> {action}, leavingDirect={leavingDirect}, leavingIgpuOnly={leavingIgpuOnly}");
			// 2026-10-05：删除"切独显直连前先发 IGPU_ONLY_CONNECT_RB_OFF"——
			// 那是【核显省电】命令，已由独立按钮负责，三档模式不再发送（§9.6）。
			requestCts.Token.ThrowIfCancellationRequested();
			gpuModeStatusVersion = _hw.GpuModeStatusVersion;
			await _hw.Publish("Setting/Control", payload).ConfigureAwait(continueOnCapturedContext: false);
			if (leavingDirect && !muxTargetRequest)
			{
				await Task.Delay(300, requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = ((mode == 0) ? "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" : "DGPU_DIRECT_CONNECT_TOGGLE_OFF") }).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (leavingIgpuOnly && _hw.SupportsDgpuDirect)
			{
				await Task.Delay(300, requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_OFF" }).ConfigureAwait(continueOnCapturedContext: false);
			}
			// 2026-10-13（用户批准）：把「等 GCU 主动推送」改成「主动轮询」。
			// 旧实现：先空等 1200ms 等一个 GCU【从不主动推】的状态包（必然耗满），再最多 7 次 ×(GETSTATUS + 1800ms)，
			// 最坏 ≈1200 + 7×1800 ≈ 13.8s（实测独显直连 1203ms、混合 1511~1517ms）。
			// 新实现：先主动发一次 GETSTATUS（GCU 被轮询时 ~3ms 就答），再等 250ms；之后最多 2 次 ×300ms 补查。
			// 最坏等待预算 = 250 + 2×300 = 850ms（WaitForStateAsync 事件驱动，一收到状态包立即返回，不等满）。
			await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
			bool confirmed = await _hw.WaitForStateAsync(TargetReached, TimeSpan.FromMilliseconds(250L), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
			int confirmationAttempts = 2;
			TimeSpan confirmationPollDelay = TimeSpan.FromMilliseconds(300L);
			for (int attempt = 0; attempt < confirmationAttempts; attempt++)
			{
				if (confirmed)
				{
					break;
				}
				requestCts.Token.ThrowIfCancellationRequested();
				await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
				confirmed = await _hw.WaitForStateAsync(TargetReached, confirmationPollDelay, requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				bool flag2;
				switch (attempt)
				{
				case 1:
				case 3:
				case 5:
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				if (!confirmed & flag2)
				{
					await _hw.Publish("Setting/Control", payload).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			Logger.WriteLine($"SwitchGpuMode confirmed={confirmed}: expected={mode} actual={CurrentGpuMode} runtime={_hw.GpuSwitchResult} action={action} elapsed={elapsed.ElapsedMilliseconds}ms");
			if (!confirmed)
			{
				// 2026-10-13（用户批准）：不再按「失败」返回。切换命令已下发，模式要重启后才生效；
				// 调用方 SettingsForm.cs:10457 拿到 true 会置 pending、UI 显示「重启待应用」。
				// ⚠️ 取舍（用户已接受）：真失败时不再走 SettingsForm.cs:10471 的回滚 + 失败提示。
				Logger.WriteLine($"SwitchGpuMode 未在等待预算内确认，按「已下发、待重启生效」处理: expected={mode} actual={CurrentGpuMode} elapsed={elapsed.ElapsedMilliseconds}ms");
				ScheduleGpuModeReconfirmLog(mode, gpuModeStatusVersion);
			}
			if (autoRestart)
			{
				GpuRestartRequestOutcome gpuRestartRequestOutcome = await PublishGpuRestartAsync(mode, BuildGpuRestartRoute(mode), requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);
				if (gpuRestartRequestOutcome != GpuRestartRequestOutcome.Requested)
				{
					Logger.WriteLine($"SwitchGpuMode auto-restart refused: target={mode} outcome={gpuRestartRequestOutcome}");
					return false;
				}
			}
			return true;
		}
		catch (OperationCanceledException)
		{
			Logger.WriteLine($"SwitchGpuMode({mode}) superseded after {elapsed.ElapsedMilliseconds}ms");
			return false;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("SwitchGpuMode fail: " + ex2.Message);
			return false;
		}
		finally
		{
			if (lockTaken)
			{
				_switchLock.Release();
			}
			Interlocked.CompareExchange(ref _longRunningSwitchCts, null, requestCts);
			Interlocked.CompareExchange(ref _gpuSwitchCts, null, requestCts);
			requestCts.Dispose();
		}
		bool TargetReached()
		{
			if (CurrentGpuMode != mode)
			{
				return false;
			}
			if (_hw.GpuModeStatusVersion <= gpuModeStatusVersion)
			{
				return false;
			}
			return true;
		}
	}


	/// <summary>
	/// 2026-10-13：切换命令已下发、但未在等待预算内确认时的「廉价后台再确认」。
	/// ⛔ 不阻塞调用方、⛔ 不新增任何 EC/GCU 命令、⛔ 不改任何状态：2s 后只观察一次
	/// 已有的模式 / 状态版本并写日志，用来事后区分「上报慢」还是「真失败」。
	/// </summary>
	private void ScheduleGpuModeReconfirmLog(int mode, long versionBeforeRequest)
	{
		_ = ReconfirmGpuModeAfterDelayAsync(mode, versionBeforeRequest);
	}

	private async Task ReconfirmGpuModeAfterDelayAsync(int mode, long versionBeforeRequest)
	{
		try
		{
			await Task.Delay(2000).ConfigureAwait(continueOnCapturedContext: false);
			bool reached = CurrentGpuMode == mode && _hw.GpuModeStatusVersion > versionBeforeRequest;
			Logger.WriteLine($"SwitchGpuMode 后台再确认(+2000ms) reached={reached} expected={mode} actual={CurrentGpuMode} version={_hw.GpuModeStatusVersion}/{versionBeforeRequest}");
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("gpu-reconfirm-bg", "SwitchGpuMode 后台再确认失败: " + ex.Message);
		}
	}

	public async Task<bool> SwitchGpuPowerSaving()
	{
		try
		{
			await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GPU_POWERSAVEINGMODE" });
			Logger.WriteLine("SwitchGpuPowerSaving 已发送");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchGpuPowerSaving fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> SwitchAutoRefreshRate(bool on)
	{
		try
		{
			if (!_hw.DcHzSeen)
			{
				return false;
			}
			await _hw.Publish("Setting/Control", new Dictionary<string, object>
			{
				["Action"] = "GPU_DC_HZ",
				["Enable"] = on
			});
			bool flag = await ConfirmSettingAsync(() => _hw.DcHzSeen && _hw.DcHz == on);
			Logger.WriteLine($"SwitchAutoRefreshRate({on}) confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchAutoRefreshRate fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> SwitchLocalDimming(bool on)
	{
		try
		{
			if (!_hw.SupportsLocalDimming)
			{
				return false;
			}
			await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = (on ? "LOCALDIMMING_ON" : "LOCALDIMMING_OFF") });
			bool flag = await ConfirmSettingAsync(() => _hw.LocalDimmingSeen && _hw.LocalDimming == on);
			Logger.WriteLine($"SwitchLocalDimming({on}) confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchLocalDimming fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> SwitchLcdOverdrive(bool on)
	{
		try
		{
			if (!_hw.SupportsLcdOverdrive)
			{
				return false;
			}
			await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = (on ? "LCDOverdrive_ON" : "LCDOverdrive_OFF") });
			bool flag = await ConfirmSettingAsync(() => _hw.LcdOverdriveSeen && _hw.LcdOverdrive == on);
			Logger.WriteLine($"SwitchLcdOverdrive({on}) confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchLcdOverdrive fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> SwitchRefreshRate(int hz)
	{
		try
		{
			if (!_hw.SupportsDisplayRefresh || !_hw.HzList.Contains(hz))
			{
				return false;
			}
			await _hw.Publish("Setting/Control", new Dictionary<string, object>
			{
				["Action"] = "GPU_HZSETTING",
				["Hz"] = hz.ToString()
			});
			bool flag = await ConfirmSettingAsync(() => _hw.CurrentHz == hz);
			Logger.WriteLine($"SwitchRefreshRate({hz}) confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchRefreshRate fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> SwitchUsbCharger(bool on)
	{
		try
		{
			await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = (on ? "USB_CHARGER_ON" : "USB_CHARGER_OFF") });
			bool flag = await ConfirmSettingAsync(() => _hw.UsbCharger == on);
			if (!flag && !_hw.UsbChargerSeen)
			{
				// NUC/UMI 的 USB 供电没有状态回包；命令已下发就按成功处理（与原厂控制台一致）。
				_hw.QuickSwitches["usb"] = on;
				flag = true;
				Logger.WriteLine($"[quick] usb 无状态回读 ⇒ 已按下发成功处理({on})");
			}
			Logger.WriteLine($"SwitchUsbCharger({on}) confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchUsbCharger fail: " + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// 2026-10-11（用户拍板「要它，另开一轮做麦克风静音开关」）：麦克风静音 = **Windows CoreAudio 端点静音**，
	/// 不经过 EC/GCU 通道（⛔ 不发任何 MQTT 命令、⛔ 不写注册表）。
	/// <list type="bullet">
	/// <item><b>键名</b> <c>micmute</c>；<b>语义</b>：<paramref name="on" /> = true ⇒ 默认录音端点**已静音**
	/// （即 <c>IAudioEndpointVolume::SetMute(true)</c>），与 <c>QuickSwitches["micmute"]</c> 的读回口径一致。</item>
	/// <item>能力判据走 <see cref="MechrevoHw.SupportsQuickSwitch" />（= 本机有可静音的默认录音端点）——
	/// 判假直接返回 false，界面侧不会显示这个开关。</item>
	/// <item>确认口径与其它快捷开关一致：<c>SetMute</c> 之后**现场 GetMute 读回**，一致才算成功
	/// （见 <see cref="MechrevoLite.Helpers.AudioEndpointMic.TrySetMute" />）。</item>
	/// <item>⚠️ 同步执行（不走线程池）：CoreAudio 激活需要线程已初始化 COM，界面路径就是 STA 的 UI 线程；
	/// <c>SetMute</c> 本身是本地调用，耗时 &lt; 1ms。</item>
	/// </list>
	/// </summary>
	public Task<bool> SwitchMicMute(bool on)
	{
		try
		{
			if (!_hw.SupportsQuickSwitch("micmute"))
			{
				Logger.WriteLine("SwitchMicMute: 本机没有可用的默认录音端点（能力位为假，不做任何下发）");
				return Task.FromResult(result: false);
			}
			bool flag = MechrevoLite.Helpers.AudioEndpointMic.TrySetMute(on, out string note);
			// 现场 GetMute 确认后回灌镜像；否则下一次 Toggle 仍读旧值，表现为只能静音、再点无效。
			if (flag)
			{
				_hw.QuickSwitches["micmute"] = on;
			}
			Logger.WriteLine($"SwitchMicMute({on}) confirmed={flag} note={note}");
			return Task.FromResult(flag);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchMicMute fail: " + ex.Message);
			return Task.FromResult(result: false);
		}
	}

	public async Task<bool> SwitchQuick(string key, bool on)
	{
		try
		{
			if (!_hw.SupportsQuickSwitch(key))
			{
				return false;
			}
			// NUC/UMI 的 Fn 锁 / 摄像头 / Win 键锁走 ACPI EC，不经过 GCU 能力位。
			if (_hw.IsIntelNucMachine || _hw.IsUmiMachine)
			{
				if (string.Equals(key, "touchpad", StringComparison.Ordinal))
				{
					bool tpOk = NucQuickSwitches.TrySetTouchpad(on, out bool tpConfirmed);
					_hw.QuickSwitches["touchpad"] = tpConfirmed ? on : _hw.QuickSwitches.TryGetValue("touchpad", out bool oldTp) && oldTp;
					Logger.WriteLine($"SwitchQuick(touchpad,{on}) ⭐ NUC/UMI ACPI EC + PTP ⇒ 确认={tpConfirmed}");
					return tpConfirmed;
				}
				if (string.Equals(key, "usb", StringComparison.Ordinal))
				{
					bool usbCurrent = _hw.QuickSwitches.TryGetValue("usb", out bool usbOld) && usbOld;
					bool usbOk = NucQuickSwitches.TryPulseUsbCharging(on, usbCurrent, out bool usbConfirmed);
					_hw.QuickSwitches["usb"] = usbConfirmed ? on : usbCurrent;
					Logger.WriteLine($"SwitchQuick(usb,{on}) ⭐ NUC/UMI EC trigger bit4 脉冲 ⇒ 确认={usbConfirmed}");
					return usbOk;
				}
				if (string.Equals(key, "acrecovery", StringComparison.Ordinal))
				{
					bool acOk = NucQuickSwitches.TrySetAcRecovery(on, out bool acConfirmed);
					_hw.QuickSwitches["acrecovery"] = acConfirmed ? on : _hw.QuickSwitches.TryGetValue("acrecovery", out bool oldAc) && oldAc;
					Logger.WriteLine($"SwitchQuick(acrecovery,{on}) ⭐ NUC/UMI ACPI EC ⇒ 确认={acConfirmed}");
					return acConfirmed;
				}
				if (string.Equals(key, "fanboost", StringComparison.Ordinal))
				{
					bool fbOk = NucQuickSwitches.TrySetFanBoost(on, out bool fbConfirmed);
					_hw.QuickSwitches["fanboost"] = fbConfirmed ? on : _hw.QuickSwitches.TryGetValue("fanboost", out bool oldFb) && oldFb;
					Logger.WriteLine($"SwitchQuick(fanboost,{on}) ⭐ NUC/UMI ACPI EC ⇒ 确认={fbConfirmed}");
					return fbConfirmed;
				}
				if (string.Equals(key, "fnkey", StringComparison.Ordinal))
				{
					bool nucOk = NucQuickSwitches.TrySetFnLock(on, out bool nucConfirmed);
					_hw.QuickSwitches["fnkey"] = nucConfirmed ? on : _hw.QuickSwitches.TryGetValue("fnkey", out bool oldFn) && oldFn;
					Logger.WriteLine($"SwitchQuick(fnkey,{on}) ⭐ NUC/UMI ACPI EC ⇒ 确认={nucConfirmed}");
					return nucConfirmed;
				}
				if (string.Equals(key, "webcam", StringComparison.Ordinal))
				{
					// 优先做 Windows PnP 设备禁用/启用；EC 断电位只是辅助，
					// 单写 EC 位并不能保证相机设备从系统中消失。
					if (CameraPnpControl.HasCamera)
					{
						(bool camOk, string camDetail) = await Task.Run(() =>
						{
							bool ok = CameraPnpControl.TrySetEnabled(on, out bool _, out string detail);
							return (ok, detail);
						}).ConfigureAwait(continueOnCapturedContext: false);
						if (camOk)
						{
							_hw.QuickSwitches["webcam"] = on;
							// 同时同步 EC 断电位，保持硬件/系统状态一致。
							NucQuickSwitches.TrySetCamera(on, out bool ecConfirmed);
							Logger.WriteLine($"SwitchQuick(webcam,{on}) ⭐ NUC/UMI Windows PnP ⇒ 确认={camOk}（{camDetail}）EC={ecConfirmed}");
							return true;
						}
						Logger.WriteLine($"SwitchQuick(webcam,{on}) PnP 失败（{camDetail}）⇒ 尝试 EC 断电位");
					}
					bool nucOk = NucQuickSwitches.TrySetCamera(on, out bool nucConfirmed);
					if (nucOk)
					{
						_hw.QuickSwitches["webcam"] = on;
						Logger.WriteLine($"SwitchQuick(webcam,{on}) ⭐ NUC/UMI ACPI EC（PnP 不可用）⇒ 确认={nucConfirmed}");
						return nucConfirmed;
					}
					return false;
				}
				if (string.Equals(key, "winkey", StringComparison.Ordinal))
				{
					bool nucOk = NucQuickSwitches.TrySetWinKeyLock(on, out bool nucConfirmed);
					if (nucOk)
					{
						_hw.QuickSwitches["winkey"] = on;
					}
					Logger.WriteLine($"SwitchQuick(winkey,{on}) ⭐ NUC/UMI ACPI EC ⇒ 确认={nucConfirmed}");
					return nucConfirmed;
				}
			}
			// ⭐⭐ 2026-10-16（宝龙达 · 窄范围）：⭐ **Fn 键锁 / 触摸板 走 WMI**（⭐ 它没有 GCU ✅）
			//   ⭐ 依据：⭐ 真机诊断包已回读到 ⭐ `Fn锁=0` ✅ / ⭐ `触摸板=1` ✅ ⇒ ⭐ **硬件支持** ✅
			//   ⭐ 模板：⭐ 与档位/LOGO/键盘灯/功耗墙/显卡模式**同一套**（⭐ 非宝龙达恒 false ⇒ 下面原路 ✅）
			if (string.Equals(key, "fnkey", StringComparison.Ordinal)
				|| string.Equals(key, "touchpad", StringComparison.Ordinal))
			{
				bool baolongdaOk = false;
				bool handled = string.Equals(key, "fnkey", StringComparison.Ordinal)
					? _hw.TrySetFnLockViaBackend(on, out baolongdaOk)
					: _hw.TrySetTouchpadViaBackend(on, out baolongdaOk);
				if (handled)
				{
					Logger.WriteLine($"SwitchQuick({key},{on}) ⭐ 走宝龙达 WMI ⇒ 确认={baolongdaOk}（⛔ 未发任何 MQTT ✅）");
					return baolongdaOk;
				}
			}
			// ⭐⭐ 2026-10-17（WiFi / 蓝牙 · 本地开关）：⭐ 宝龙达没有 GCU ⇒ 走 Windows Radio ✅
			//   ⭐ `DeviceSwitchItem.cs` 用 `Windows.Devices.Radios` 切这两个 ✅
			//   ⚠️ 仅 `UsesBaolongda` 分派 ⇒ GCU 机型的 `WIFI_ON/OFF`、`BT_ON/OFF` 那条 MQTT 路径**逐位不变** ✅
			if (_hw.UsesBaolongda
				&& (string.Equals(key, "wifi", StringComparison.Ordinal) || string.Equals(key, "bt", StringComparison.Ordinal)))
			{
				RadioSwitchKind radioKind = string.Equals(key, "wifi", StringComparison.Ordinal)
					? RadioSwitchKind.Wifi
					: RadioSwitchKind.Bluetooth;
				if (RadioSwitch.Available(radioKind))
				{
					(bool radioOk, string radioNote) = await RadioSwitch.SetAsync(radioKind, on).ConfigureAwait(continueOnCapturedContext: false);
					Logger.WriteLine($"SwitchQuick({key},{on}) ⭐ 走本地 Radio（Windows.Devices.Radios）⇒ 确认={radioOk}（{radioNote}）");
					return radioOk;
				}
				Logger.WriteLine($"SwitchQuick({key},{on})：⚠️ 本地 Radio 不可用（未探测到或无访问权限）⇒ 不下发");
				return false;
			}
			// 2026-10-17（「高性能电源」解耦 GCU）： 无 GCU 时不再发注定失败的 MQTT
			bool preferGcuHighPerf = false;
			if (string.Equals(key, "highperf", StringComparison.Ordinal))
			{
				// 50 系 GCU 机型优先走 GCU 的 HIGHPERFORMANCEPOWERMODE_ON/OFF；
				// 非 50 系仍走纯 Windows 电源 Overlay。
				bool preferGcu = _hw.IsGcuConnected
					&& !_hw.UsesBaolongda
					&& !_hw.IsIntelNucMachine
					&& !_hw.IsUmiMachine
					&& _hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen50;
				preferGcuHighPerf = preferGcu;
				if (!preferGcu)
				{
					MechrevoLite.Mode.PerfModeService perf = MechrevoLite.Mode.PerfModeService.Instance;
					if (perf == null)
					{
						Logger.WriteLine("SwitchQuick(highperf)：⚠️ PerfModeService 不可用 ⇒ 不下发");
						return false;
					}
					perf.WindowsPowerOverlayEnabled = on;
					if (on)
					{
						perf.ApplyWindowsPowerOverlayForActiveMode();
						Logger.WriteError("SwitchQuick(highperf,True) ⭐ 已启用，并立即按当前模式应用 Windows 电源模式");
					}
					else
					{
						MechrevoLite.Mode.PowerNative.SetPowerMode(0);
						Logger.WriteError("SwitchQuick(highperf,False) ⭐ 已关闭联动，Windows 电源模式回到平衡");
					}
					_hw.QuickSwitches["highperf"] = on;
					return true;
				}
				// 50 系 GCU 自己控制高性能电源；LM 的 Windows Overlay 必须先关掉，
				// 避免后续切档/LM 自动化再次干预。
				MechrevoLite.AppConfig.Set("bd_power_overlay", 0);
				MechrevoLite.AppConfig.Flush();
				Logger.WriteError($"SwitchQuick(highperf,{on}) ⭐ 50 系 GCU 优先走 GCU；LM Windows 电源 Overlay 已禁用");
				// 继续走下面的 action/MQTT，确认后不再调用 GLM 的 SyncHighPerformancePowerMode。
			}
			// （APU 防缩缸）： 纯 Windows 电源策略（处理器性能提升模式 → 5）
			if (string.Equals(key, "apuboost", StringComparison.Ordinal))
			{
				MechrevoLite.Mode.PerfModeService perfBoost = MechrevoLite.Mode.PerfModeService.Instance;
				if (perfBoost == null)
				{
					Logger.WriteLine("SwitchQuick(apuboost)：⚠️ PerfModeService 不可用 ⇒ 不下发");
					return false;
				}
				perfBoost.ApuAntiDegradeEnabled = on;
				_hw.QuickSwitches["apuboost"] = on;
				Logger.WriteLine($"SwitchQuick(apuboost,{on}) ⭐ 走 Windows 电源策略（boost mode=5）");
				return true;
			}
			// （风扇强冷 · 宝龙达）： 没有 GCU ⇒ 本机通道：暂停曲线闭环 + 两路满转 + 回读校验
			if (_hw.UsesBaolongda && string.Equals(key, "fanboost", StringComparison.Ordinal))
			{
				// ⭐⭐ 2026-10-18（机主 UI 体验 · 乐观更新）：⭐ 强冷确认窗口最长 10 s（每 500ms 读 13）⇒
				//   ⭐ 放到**后台线程**执行，UI 立刻显示选中态、不阻塞；⛔ 写入/回读仍严格校验、一步不少 ✅
				(bool bdHandled, bool bdConfirmed) = await Task.Run(() =>
				{
					bool handled = _hw.TrySetFanBoostViaBackend(on, out bool confirmed);
					return (handled, confirmed);
				});
				if (bdHandled)
				{
					Logger.WriteLine($"SwitchQuick(fanboost,{on}) ⭐ 走**本机通道**（EC 目标档/掩码 + 13 轮询）⇒ 确认={bdConfirmed}（⛔ 未发 MQTT）");
					return bdConfirmed;
				}
			}
			// 不再落入 action==null 静默失败
			if (string.Equals(key, "usb", StringComparison.Ordinal))
			{
				return await SwitchUsbCharger(on).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (string.Equals(key, "micmute", StringComparison.Ordinal))
			{
				return await SwitchMicMute(on).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (string.Equals(key, "deepsleep", StringComparison.Ordinal))
			{
				return await SwitchDeepSleep(on).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (string.Equals(key, "gamewhitelist", StringComparison.Ordinal))
			{
				return await SwitchGameWhitelist(on).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (string.Equals(key, "cpuadvperf", StringComparison.Ordinal))
			{
				return await SwitchCpuAdvancedPerformance(on).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (string.Equals(key, "lightbar", StringComparison.Ordinal) || string.Equals(key, "logolight", StringComparison.Ordinal))
			{
				string lightTopic = ((key == "lightbar") ? "HidLightbar/Ctrl" : "HidLightbar_Logo/Ctrl");
				return await SetLightPower(lightTopic, on).ConfigureAwait(continueOnCapturedContext: false);
			}			// ⭐⭐⭐ 2026-10-18（摄像头开关 · 系统级 PnP 禁用/启用 · 机主已批准）：
			//   ⚠️ 宝龙达没有 GCU、也没有摄像头功能号 ⇒ 旧的 `WEBCAM_ON/OFF`（厂商 MQTT）**必然无效**；
			//      上一版的"摄像头隐私授权"只是系统隐私开关（隐私 Allow ≠ 设备没被禁用 ⚠️）。
			//   ⭐ 现在：`on=true` ⇒ **启用摄像头设备**；`on=false` ⇒ **禁用摄像头设备**（SetupAPI，通用）✅
			//   ⭐ 结果/回读都来自**设备状态**（见 `CameraPnpControl`），并回灌 `QuickSwitches["webcam"]` ✅
			//   ⚠️ 本机枚举不到可控制摄像头设备时才退回旧的隐私键兜底（保功能，⛔ 不假装成 PnP）；
			//   ⚠️ **GCU / 同方机型不进入本分支** ⇒ 下面 `WEBCAM_ON/OFF` 那条厂商路径**逐位不变** ✅
			if (_hw.UsesBaolongda && string.Equals(key, "webcam", StringComparison.Ordinal))
			{
				if (CameraPnpControl.HasCamera)
				{
					// ⭐ PnP 禁用/启用要等设备卸载/启动（数百 ms）⇒ 放**后台线程**，⛔ 不卡 UI（沿用 fanboost 同款做法）
					(bool camOk, string camDetail) = await Task.Run(() =>
					{
						bool ok = CameraPnpControl.TrySetEnabled(on, out bool _, out string detail);
						return (ok, detail);
					}).ConfigureAwait(continueOnCapturedContext: false);
					if (camOk)
					{
						// ⭐ 回读确认成立 ⇒ 把真值回灌镜像（界面/磁贴据此即时收敛）✅
						_hw.SyncCameraPnpQuickSwitch(on, "PnP 写入确认");
					}
					else if (CameraPnpControl.TryGetEnabled(out bool camNow, out string _))
					{
						// ⚠️ 未确认 ⇒ **按真实设备状态**回灌（⛔ 不拿意图当状态）⇒ 界面按真值回滚 ✅
						_hw.SyncCameraPnpQuickSwitch(camNow, "PnP 设备状态回读");
					}
					Logger.WriteLine($"SwitchQuick(webcam,{on}) ⭐ 走 **Windows PnP 摄像头设备**（禁用/启用）⇒ 确认={camOk}（{camDetail}）");
					return camOk;
				}
				if (CameraPrivacy.Available)
				{
					// ⭐ 兜底：本机没有可控制的摄像头设备（例如只有 Windows Hello 红外相机）⇒ 沿用旧隐私键（保功能）
					bool camOk = CameraPrivacy.TrySetDenied(!on, out string camNote);
					Logger.WriteLine($"SwitchQuick(webcam,{on}) ⚠️ 走**隐私键兜底**（无 PnP 摄像头设备 · ConsentStore）⇒ 确认={camOk}（{camNote}）");
					if (camOk)
					{
						_hw.SyncCameraPnpQuickSwitch(on, "隐私键写入确认（无 PnP 摄像头设备兜底）");
					}
					return camOk;
				}
				Logger.WriteLine($"SwitchQuick(webcam,{on})：⚠️ 既没有可控制的摄像头设备、隐私键也不可用 ⇒ 不下发");
				return false;
			}
			// ⭐⭐ 2026-10-16（宝龙达 · OSD 提示）：⭐ 走**本地实现**（⭐ 它连 WMI 都不用 ✅）
			//   ⭐ 机主原话「⭐ OSD显示开关不可用（⭐ LM好像有自己的OSD部分 ✅）」⇒ ⭐ 让这个开关管 LM 自己的 OSD ✅
			//   ⚠️ 原来那条 `OSD_HIDDEN_ON/OFF` **只能走 GCU 的 MQTT** ❌ ⇒ ⭐ 宝龙达恒失败 ⇒ ⭐ 点了没反应 ⚠️
			//   ⭐ 模板同上：⭐ 非宝龙达恒 false ⇒ ⭐ 下面 action 那条**原路不变** ✅（⭐ 逐位一致 ✅）
			if (string.Equals(key, "osd", StringComparison.Ordinal))
			{
				bool osdOk = false;
				if (_hw.TrySetOsdViaBackend(on, out osdOk))
				{
					Logger.WriteLine($"SwitchQuick(osd,{on}) ⭐ 走宝龙达**本地** OSD ⇒ 确认={osdOk}（⛔ 未发任何 MQTT ✅）");
					return osdOk;
				}
			}
			// ⭐⭐ 2026-10-17（大小写锁定 / 数字小键盘锁定）：走 **Windows 模拟按键**（纯 user32）
			//   ⚠️ 判据分两种，⛔ 不能混：
			//      · `capslock` —— **所有机型**都走这里。它是本轮**新增**的开关，纯系统级，
			//        不依赖任何后端（WMI/GCU/EC 都不参与）。
			//      · `numpad` —— **只宝龙达**走这里。老 GCU 机型的 `numpad` **必须保持**原 MQTT 路径
			//        （`NUMPAD_LOCK` / `NUMPAD_UNLOCK`），⛔ 一个字都不能动（逐位不变）。
			//   实现见 `MechrevoLite.Helpers.NativeLockKey`：读状态 `GetKeyState`（只看锁定位）、
			//   写状态 `keybd_event`（带扩展键标志，按下＋抬起一次），并且**幂等**（已到位就不发）。
			if (string.Equals(key, "capslock", StringComparison.Ordinal)
				|| (string.Equals(key, "numpad", StringComparison.Ordinal) && (_hw.UsesBaolongda || _hw.IsIntelNucMachine || _hw.IsUmiMachine)))
			{
				bool isCapsLock = string.Equals(key, "capslock", StringComparison.Ordinal);
				bool keyOk = isCapsLock
					? MechrevoLite.Helpers.NativeLockKey.TrySetCapsLock(on, out string keyNote)
					: MechrevoLite.Helpers.NativeLockKey.TrySetNumLock(on, out keyNote);
				Logger.WriteError($"SwitchQuick({key},{on}) ⭐ 走系统锁定键（模拟按键 · 纯 user32）⇒ ⭐ 回读确认={keyOk} · {keyNote}（⛔ 未发任何 MQTT ✅）");
				if (keyOk)
				{
					_hw.QuickSwitches[key] = on;
				}
				return keyOk;
			}
			string action = key switch
			{
				"touchpad" => (!on) ? "TOUCHPAD_OFF" : "TOUCHPAD_ON", 
				"wifi" => (!on) ? "WIFI_OFF" : "WIFI_ON", 
				"bt" => (!on) ? "BT_OFF" : "BT_ON", 
				"webcam" => (!on) ? "WEBCAM_OFF" : "WEBCAM_ON", 
				"winkey" => (!on) ? "WINKEY_UNLOCK" : "WINKEY_LOCK", 
				"fnkey" => (!on) ? "FNKEY_UNLOCK" : "FNKEY_LOCK", 
				"osd" => (!on) ? "OSD_HIDDEN_ON" : "OSD_HIDDEN_OFF", 
				"numpad" => (!on) ? "NUMPAD_UNLOCK" : "NUMPAD_LOCK", 
				"copilot" => (!on) ? "COPILOTKEY_UNLOCK" : "COPILOTKEY_LOCK", 
				"acrecovery" => (!on) ? "ACRECOVERY_TOGGLE_OFF" : "ACRECOVERY_TOGGLE_ON", 
				"batterylogo" => (!on) ? "BATTERYLOGO_TOGGLE_OFF" : "BATTERYLOGO_TOGGLE_ON", 
				"highperf" => (!on) ? "HIGHPERFORMANCEPOWERMODE_OFF" : "HIGHPERFORMANCEPOWERMODE_ON", 
				"touchpadtoggle" => (!on) ? "TOUCHPAD_TOGGLE_OFF" : "TOUCHPAD_TOGGLE_ON", 
				"singlecolorkb" => (!on) ? "SINGLE_COLOR_KBBL_STATUS_OFF" : "SINGLE_COLOR_KBBL_STATUS_ON", 
				"uni" => (!on) ? "Uni_OFF" : "Uni_ON", 
				"omni" => (!on) ? "Omni_OFF" : "Omni_ON", 
				"powerlight" => (!on) ? "PowerLight_OFF" : "PowerLight_ON", 
				_ => null, 
			};
			if (action == null)
			{
				// （防回归）： 不再静默 —— 缺动作映射必须默认可见地记 error
				Logger.WriteError("[quick] ⛔ SwitchQuick(" + key + ") 无下发动作映射 ⇒ 已返回 false（这会被 UI 当成失败）");
				return false;
			}
			bool confirmed = false;
			int sendAttempts = ((!(key == "highperf")) ? 1 : 2);
			long statusVersionBeforeCommand = _hw.SettingStatusVersion;
			for (int attempt = 0; attempt < sendAttempts; attempt++)
			{
				if (confirmed)
				{
					break;
				}
				Dictionary<string, object> dictionary = new Dictionary<string, object> { ["Action"] = action };
				if (key == "powerlight" && _hw.PowerLightBrightness >= 0)
				{
					dictionary["Brightness"] = _hw.PowerLightBrightness;
				}
				await _hw.Publish("Setting/Control", dictionary);
				confirmed = await ConfirmSettingAsync(() => _hw.QuickSwitches.TryGetValue(key, out var value) && value == on, statusVersionBeforeCommand);
				if (!confirmed && attempt + 1 < sendAttempts)
				{
					await Task.Delay(250);
				}
			}
			if (!confirmed && key == "acrecovery" && !_hw.AcRecoverySeen)
			{
				// NUC/UMI 没有 AcRecoverySwitch_Status 回包；命令已下发就按成功处理。
				_hw.QuickSwitches[key] = on;
				confirmed = true;
				Logger.WriteLine($"[quick] acrecovery 无状态回读 ⇒ 已按下发成功处理({on})");
			}
			if (!confirmed && key == "osd" && !_hw.OsdSeen)
			{
				// 部分 GCU 的 Setting/Status 不回 OSD 字段；命令已下发就按成功处理，
				// 否则 GCU-OSD 磁贴会被 UI 弹回，看起来“关不掉”。
				_hw.QuickSwitches[key] = on;
				confirmed = true;
				Logger.WriteLine($"[quick] osd 无 OSD 状态回读 ⇒ 已按下发成功处理({on})");
			}
			if (confirmed && key == "highperf" && !preferGcuHighPerf)
			{
				confirmed = await Task.Run(() => _syncHighPerformancePowerMode(on, _hw.OperatingMode)).ConfigureAwait(continueOnCapturedContext: false);
			}
			Logger.WriteLine($"SwitchQuick({key},{on}) confirmed={confirmed}");
			return confirmed;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchQuick fail: " + ex.Message);
			return false;
		}
	}

	public Task<bool> SwitchGameWhitelist(bool on)
	{
		return SwitchFanChannelToggle("gamewhitelist", on, new Dictionary<string, object>
		{
			["Action"] = "OPERATING_GAME_WHITE_LIST",
			["GameWhitelistSwitch"] = (on ? 1 : 0)
		});
	}

	public Task<bool> SwitchCpuAdvancedPerformance(bool on)
	{
		return SwitchFanChannelToggle("cpuadvperf", on, new Dictionary<string, object>
		{
			["Action"] = "SET_OPERATING_MODE_DETAIL",
			[on ? "CPUPerformanceAndOverClockMenuSwitch_ON" : "CPUPerformanceAndOverClockMenuSwitch_OFF"] = (on ? "1" : "0")
		});
	}

	private async Task<bool> SwitchFanChannelToggle(string key, bool on, Dictionary<string, object> payload)
	{
		try
		{
			if (!_hw.SupportsQuickSwitch(key))
			{
				return false;
			}
			long versionBefore = _hw.FanStatusVersion;
			await _hw.Publish("Fan/Control", payload);
			bool flag = Matches() && _hw.FanStatusVersion > versionBefore;
			for (int attempt = 0; attempt < 3; attempt++)
			{
				if (flag)
				{
					break;
				}
				await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
				flag = await _hw.WaitForStateAsync(() => Matches() && _hw.FanStatusVersion > versionBefore, TimeSpan.FromMilliseconds((attempt == 0) ? 500 : 800));
			}
			Logger.WriteLine($"SwitchFanChannelToggle({key},{on}) confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchFanChannelToggle(" + key + ") fail: " + ex.Message);
			return false;
		}
		bool Matches()
		{
			if (_hw.QuickSwitches.TryGetValue(key, out var value))
			{
				return value == on;
			}
			return false;
		}
	}

	internal static string? MutuallyExclusivePartner(string key)
	{
		if (!(key == "uni"))
		{
			if (key == "omni")
			{
				return "uni";
			}
			return null;
		}
		return "omni";
	}

	public async Task<bool> SwitchUniOmni(string key, bool on)
	{
		string partner = MutuallyExclusivePartner(key);
		if (partner == null)
		{
			return false;
		}
		bool value = default;
		bool partnerWasOn = (on && _hw.QuickSwitches.TryGetValue(partner, out value)) & value;
		bool flag = partnerWasOn;
		if (flag)
		{
			flag = !(await SwitchQuick(partner, on: false));
		}
		if (flag)
		{
			Logger.WriteLine($"SwitchUniOmni({key}) aborted: could not clear {partner} first.");
			return false;
		}
		if (await SwitchQuick(key, on))
		{
			return true;
		}
		if (partnerWasOn)
		{
			bool value2 = await SwitchQuick(partner, on: true);
			Logger.WriteLine($"SwitchUniOmni({key},{on}) failed; restored {partner}={value2}");
		}
		return false;
	}

	public async Task<bool> SetPowerLightBrightness(int brightness)
	{
		try
		{
			if (!_hw.SupportsPowerLightBrightness)
			{
				return false;
			}
			int target = Math.Clamp(brightness, 0, 100);
			long versionBefore = _hw.SettingStatusVersion;
			await _hw.Publish("Setting/Control", new Dictionary<string, object>
			{
				["Action"] = "PowerLight_Brightness",
				["Brightness"] = target
			});
			bool flag = await ConfirmSettingAsync(() => _hw.PowerLightBrightness == target, versionBefore);
			Logger.WriteLine($"SetPowerLightBrightness({target}) confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SetPowerLightBrightness fail: " + ex.Message);
			return false;
		}
	}

	internal static bool IsFreshSettingState(long versionBeforeCommand, long observedVersion, bool valueMatches)
	{
		return (observedVersion > versionBeforeCommand) & valueMatches;
	}

	private async Task<bool> ConfirmSettingAsync(Func<bool> isConfirmed, long? versionBeforeCommand = null)
	{
		await _settingLock.WaitAsync();
		try
		{
			if (IsConfirmedAfterCommand())
			{
				return true;
			}
			if (await _hw.WaitForStateAsync(IsConfirmedAfterCommand, TimeSpan.FromMilliseconds(180L)))
			{
				return true;
			}
			for (int attempt = 0; attempt < 3; attempt++)
			{
				await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
				if (await _hw.WaitForStateAsync(IsConfirmedAfterCommand, TimeSpan.FromMilliseconds((attempt == 0) ? 500 : 700)))
				{
					return true;
				}
			}
			return false;
		}
		finally
		{
			_settingLock.Release();
		}
		bool IsConfirmedAfterCommand()
		{
			if (versionBeforeCommand.HasValue)
			{
				return IsFreshSettingState(versionBeforeCommand.Value, _hw.SettingStatusVersion, isConfirmed());
			}
			return isConfirmed();
		}
	}

	private static bool SyncHighPerformancePowerMode(bool enabled, int operatingMode)
	{
		int value = AppConfig.Get("skip_powermode", 0);
		AppConfig.Set("skip_powermode", (!enabled) ? 1 : 0);
		bool flag = PowerNative.ApplyMechrevoPowerModeAutomation(enabled, operatingMode);
		if (!flag)
		{
			AppConfig.Set("skip_powermode", value);
		}
		return flag;
	}

	/// <summary>兼容旧入口：参数按分钟，转发到秒口径。</summary>
	public Task<bool> PublishKeyboardCloseTimer(int minutes) =>
		PublishKeyboardCloseTimerSeconds(minutes > 0 ? minutes * 60 : 0);

	/// <summary>GCU Setting/Control 的 Mins 字段实际按秒计数（10/15/.../600/3600）。</summary>
	public async Task<bool> PublishKeyboardCloseTimerSeconds(int seconds)
	{
		try
		{
			if (!_hw.SupportsKeyboard && !_hw.SupportsLightbar)
			{
				return false;
			}
			if (seconds > 0)
			{
				await _hw.Publish("Setting/Control", new Dictionary<string, object>
				{
					["Action"] = "KEYBOARD_LIGHTBAR_TIMER_ON",
					["Mins"] = seconds
				});
			}
			else
			{
				await _hw.Publish("Setting/Control", new Dictionary<string, object> { ["Action"] = "KEYBOARD_LIGHTBAR_TIMER_OFF" });
			}
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PublishKeyboardCloseTimer fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> SwitchCloseTimer(int minutes)
	{
		try
		{
			if (!(await PublishKeyboardCloseTimer(minutes).ConfigureAwait(continueOnCapturedContext: false)))
			{
				return false;
			}
			bool flag = await ConfirmSettingAsync(() => _hw.CloseTimerMinutes == Math.Max(0, minutes));
			Logger.WriteLine($"SwitchCloseTimer({minutes}) confirmed={flag}");
			return flag;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchCloseTimer fail: " + ex.Message);
			return false;
		}
	}

	public Task<bool> SwitchLcPump(int index)
	{
		return RunLiquidCoolingAsync($"SwitchLcPump({index})", LiquidCoolingWriteLockTimeout, async () =>
		{
			bool flag = !CanControlLiquidCooling;
			if (!flag)
			{
				bool flag2 = ((index < 0 || index > 2) ? true : false);
				flag = flag2;
			}
			return !flag && await PublishAndConfirmLiquidCoolingControlAsync(new Dictionary<string, object>
			{
				["Action"] = "LC_PumpCtrl",
				["PumpCtrl"] = index.ToString()
			}, () => _hw.LcPumpControl == index, $"SwitchLcPump({index})");
		});
	}

	public Task<bool> SwitchLcFan(int index)
	{
		return RunLiquidCoolingAsync($"SwitchLcFan({index})", LiquidCoolingWriteLockTimeout, async () =>
		{
			bool flag = !CanControlLiquidCooling;
			if (!flag)
			{
				bool flag2 = ((index < 0 || index > 3) ? true : false);
				flag = flag2;
			}
			return !flag && await PublishAndConfirmLiquidCoolingControlAsync(new Dictionary<string, object>
			{
				["Action"] = "LC_FanCtrl",
				["FanCtrl"] = index.ToString()
			}, () => _hw.LcFanControl == index, $"SwitchLcFan({index})");
		});
	}

	public Task<bool> SwitchLcFanAuto()
	{
		return RunLiquidCoolingAsync("SwitchLcFanAuto()", LiquidCoolingWriteLockTimeout, async () => CanControlLiquidCooling && await PublishAndConfirmLiquidCoolingControlAsync(new Dictionary<string, object>
		{
			["Action"] = "LC_FanCtrl",
			["FanCtrl"] = 4.ToString()
		}, () => _hw.LcFanControl == 4, "SwitchLcFanAuto()"));
	}

	private async Task<bool> RunLiquidCoolingAsync(string operation, TimeSpan lockTimeout, Func<Task<bool>> body)
	{
		if (!(await _liquidCoolingLock.WaitAsync(lockTimeout).ConfigureAwait(continueOnCapturedContext: false)))
		{
			Logger.WriteLineThrottled("lc-busy", operation + " skipped: another liquid-cooling operation is still in progress");
			return false;
		}
		try
		{
			return await body().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Logger.WriteLine(operation + " fail: " + ex.Message);
			return false;
		}
		finally
		{
			_liquidCoolingLock.Release();
		}
	}

	private async Task<bool> PublishAndConfirmLiquidCoolingControlAsync(Dictionary<string, object> command, Func<bool> targetReached, string operation)
	{
		long statusVersionBeforeCommand = _hw.LcStatusVersion;
		await _hw.Publish("BT_LC/Control", command);
		bool flag = await ConfirmLiquidCoolingStateAsync(statusVersionBeforeCommand, targetReached, operation);
		Logger.WriteLine($"{operation} confirmed={flag}");
		return flag;
	}

	private async Task<bool> ConfirmLiquidCoolingStateAsync(long statusVersionBeforeCommand, Func<bool> targetReached, string operation)
	{
		if (IsConfirmedAfterCommand())
		{
			return true;
		}
		if (await _hw.WaitForStateAsync(IsConfirmedAfterCommand, TimeSpan.FromMilliseconds(180L)))
		{
			return true;
		}
		for (int attempt = 0; attempt < 2; attempt++)
		{
			if (IsConfirmedAfterCommand())
			{
				return true;
			}
			long statusVersionBeforeQuery = _hw.LcStatusVersion;
			await _hw.Publish("BT_LC/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
			if (await _hw.WaitForStateAsync(() => _hw.LcStatusVersion > statusVersionBeforeQuery, TimeSpan.FromMilliseconds((attempt == 0) ? 500 : 700)))
			{
				if (targetReached())
				{
					return true;
				}
				Logger.WriteLine(operation + " status mismatch: expected command state was not reported");
			}
		}
		return false;
		bool IsConfirmedAfterCommand()
		{
			if (_hw.LcStatusVersion > statusVersionBeforeCommand)
			{
				return targetReached();
			}
			return false;
		}
	}

	public Task<bool> LcConnect()
	{
		return RunLiquidCoolingAsync("LcConnect", LiquidCoolingWriteLockTimeout, async () =>
		{
			// 2026-10-15（C-1 第 2 步分类 ①）：走 GCU 通路（超频回退 / 液冷 LCHWOC·BT_LC）⇒ 真正需要 GCU。
			if (!_hw.IsGcuConnected)
			{
				return false;
			}
			await ArmLiquidCoolingTargetAsync();
			await _hw.Publish("BT_LC/Control", new Dictionary<string, object> { ["Action"] = "Connect" });
			Logger.WriteLine("LcConnect 已发送");
			return true;
		});
	}

	private Task PublishLcDeviceMacAsync(string mac)
	{
		return _hw.Publish("BT_LC/Control", new Dictionary<string, object>
		{
			["Action"] = "DeviceMacSetting",
			["DeviceMac"] = mac
		});
	}

	private async Task ArmLiquidCoolingTargetAsync()
	{
		if (string.IsNullOrWhiteSpace(_hw.LcCurrentMac) && _hw.LcDeviceMacs.Count != 0)
		{
			string mac = _hw.LcDeviceMacs[0];
			await PublishLcDeviceMacAsync(mac);
			Logger.WriteLine("LcSelectDevice(" + mac + ") 已发送");
		}
	}

	public Task<bool> RefreshLiquidCoolingStatus()
	{
		return RunLiquidCoolingAsync("RefreshLiquidCoolingStatus", LiquidCoolingQueryLockTimeout, async () =>
		{
			// 2026-10-15（C-1 第 2 步分类 ①）：走 GCU 通路（超频回退 / 液冷 LCHWOC·BT_LC）⇒ 真正需要 GCU。
			if (!_hw.IsGcuConnected)
			{
				return false;
			}
			await _hw.Publish("BT_LC/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
			return true;
		});
	}

	public Task<bool> LcDisconnect()
	{
		return RunLiquidCoolingAsync("LcDisconnect", LiquidCoolingWriteLockTimeout, async () =>
		{
			if (!_hw.SupportsLiquidCooling)
			{
				return false;
			}
			await _hw.Publish("BT_LC/Control", new Dictionary<string, object> { ["Action"] = "Disconnect" });
			Logger.WriteLine("LcDisconnect 已发送");
			return true;
		});
	}

	public Task<bool> LcSelectDevice(string mac)
	{
		return RunLiquidCoolingAsync("LcSelectDevice(" + mac + ")", LiquidCoolingWriteLockTimeout, async () =>
		{
			if (!_hw.SupportsLiquidCooling || string.IsNullOrWhiteSpace(mac))
			{
				return false;
			}
			await PublishLcDeviceMacAsync(mac);
			Logger.WriteLine("LcSelectDevice(" + mac + ") 已发送");
			return true;
		});
	}

	public Task<bool> LcClearMac()
	{
		return RunLiquidCoolingAsync("LcClearMac", LiquidCoolingWriteLockTimeout, async () =>
		{
			if (!_hw.SupportsLiquidCooling)
			{
				return false;
			}
			await _hw.Publish("BT_LC/Control", new Dictionary<string, object> { ["Action"] = "ClearDevMAC" });
			Logger.WriteLine("LcClearMac 已发送");
			return true;
		});
	}

	public async Task<bool> LcLight(int ledMode)
	{
		_ = 4;
		try
		{
			bool flag = !CanControlLiquidCooling;
			if (!flag)
			{
				bool flag2 = (((uint)ledMode <= 2u || (uint)(ledMode - 4) <= 1u) ? true : false);
				flag = !flag2;
			}
			if (flag)
			{
				return false;
			}
			bool flag3 = ledMode switch
			{
				1 => await LcSetBreathingAsync(enabled: true), 
				2 => await LcSetColorfulAsync(enabled: true), 
				4 => await LcSetFanLightEffectAsync(4), 
				5 => await LcSetFanLightEffectAsync(5), 
				0 => await LcDisableAllLightEffectsAsync(), 
				_ => false, 
			};
			if (flag3)
			{
				Logger.WriteLine($"LcLight({ledMode}) 已发送");
			}
			return flag3;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("LcLight fail: " + ex.Message);
			return false;
		}
	}

	public Task<bool> LcApplyLightProfile(string profile, Color color, bool persist = true, bool requireReadback = false)
	{
		return RunLiquidCoolingAsync("LcApplyLightProfile(" + profile + ")", LiquidCoolingWriteLockTimeout, async () =>
		{
			if (!CanControlLiquidCooling)
			{
				return false;
			}
			Color effectiveColor = (color.IsEmpty ? Color.FromArgb(0, 255, 255) : color);
			long statusVersionBeforeCommand = _hw.LcStatusVersion;
			bool lightingReadbackWasSeen = _hw.LcLightingStatusSeen;
			bool ok = profile switch
			{
				"cyan_static" => await LcApplyStaticHeadLightAsync(Color.FromArgb(0, 255, 255)), 
				"cyan_breath" => await LcApplyBreathingHeadLightAsync(Color.FromArgb(0, 255, 255)), 
				"custom_static" => await LcApplyStaticHeadLightAsync(effectiveColor), 
				"custom_breath" => await LcApplyBreathingHeadLightAsync(effectiveColor), 
				"colorful" => await LcApplyColorfulHeadLightAsync(breathing: false), 
				"colorful_breath" => await LcApplyColorfulHeadLightAsync(breathing: true), 
				"fan_rotate" => await LcSetFanLightEffectAsync(4), 
				"fan_rainbow" => await LcSetFanLightEffectAsync(5), 
				"off" => await LcDisableAllLightEffectsAsync(), 
				_ => false, 
			};
			if (!ok)
			{
				return false;
			}
			bool flag = false;
			if (lightingReadbackWasSeen | requireReadback)
			{
				flag = await ConfirmLiquidCoolingStateAsync(statusVersionBeforeCommand, () => LiquidCoolingLightProfileMatches(profile, effectiveColor), "LcApplyLightProfile(" + profile + ")");
				if (!flag && _hw.LcLightingStatusSeen)
				{
					Logger.WriteLine("LcApplyLightProfile(" + profile + ") lighting readback not confirmed");
					return false;
				}
			}
			if (!flag)
			{
				Logger.WriteLine("LcApplyLightProfile(" + profile + ") sent without lighting readback");
			}
			if (ok & persist)
			{
				AppConfig.Set("lc_light_profile", profile);
				string text = profile;
				if ((text == "custom_static" || text == "custom_breath") ? true : false)
				{
					AppConfig.Set("lc_light_color", effectiveColor.ToArgb());
				}
			}
			return ok;
		});
	}

	private bool LiquidCoolingLightProfileMatches(string profile, Color effectiveColor)
	{
		return profile switch
		{
			"cyan_static" => _hw.LcHeadLightMode == 0 && ColorMatchesIfReported(), 
			"custom_static" => _hw.LcHeadLightMode == 0 && ColorMatches(), 
			"cyan_breath" => _hw.LcHeadLightMode == 1 && ColorMatchesIfReported(), 
			"custom_breath" => _hw.LcHeadLightMode == 1 && ColorMatches(), 
			"colorful" => _hw.LcHeadLightMode == 2, 
			"colorful_breath" => _hw.LcHeadLightMode == 3, 
			"fan_rotate" => _hw.LcFanLightMode == 4, 
			"fan_rainbow" => _hw.LcFanLightMode == 5, 
			"off" => _hw.LcHeadLightMode == 0 && (_hw.LcFanLightMode < 0 || _hw.LcFanLightMode == 0), 
			_ => false, 
		};
		bool ColorMatches()
		{
			if (_hw.LcLedRed < 0 || _hw.LcLedGreen < 0 || _hw.LcLedBlue < 0)
			{
				return false;
			}
			int num = ClampLcColor(effectiveColor.R, _hw.LcLedRedMinimum, _hw.LcLedRedMaximum);
			int num2 = ClampLcColor(effectiveColor.G, _hw.LcLedGreenMinimum, _hw.LcLedGreenMaximum);
			int num3 = ClampLcColor(effectiveColor.B, _hw.LcLedBlueMinimum, _hw.LcLedBlueMaximum);
			if (_hw.LcLedRed == num && _hw.LcLedGreen == num2)
			{
				return _hw.LcLedBlue == num3;
			}
			return false;
		}
		bool ColorMatchesIfReported()
		{
			if (_hw.LcLedRed >= 0 || _hw.LcLedGreen >= 0 || _hw.LcLedBlue >= 0)
			{
				return ColorMatches();
			}
			return true;
		}
	}

	public Task<bool> LcSetLightColor(Color color)
	{
		return RunLiquidCoolingAsync("LcSetLightColor", LiquidCoolingWriteLockTimeout, () => LcSetLightColorCoreAsync(color));
	}

	private async Task<bool> LcSetLightColorCoreAsync(Color color)
	{
		if (!CanControlLiquidCooling)
		{
			return false;
		}
		int red = ClampLcColor(color.R, _hw.LcLedRedMinimum, _hw.LcLedRedMaximum);
		int green = ClampLcColor(color.G, _hw.LcLedGreenMinimum, _hw.LcLedGreenMaximum);
		int blue = ClampLcColor(color.B, _hw.LcLedBlueMinimum, _hw.LcLedBlueMaximum);
		await _hw.Publish("BT_LC/Control", new Dictionary<string, object>
		{
			["Action"] = "LEDControl",
			["LCLED_R"] = red.ToString(),
			["LCLED_G"] = green.ToString(),
			["LCLED_B"] = blue.ToString()
		});
		Logger.WriteLine($"LcSetLightColor({red},{green},{blue}) 已发送");
		return true;
	}

	private static int ClampLcColor(int value, int minimum, int maximum)
	{
		if (minimum < 0 || maximum < minimum)
		{
			return Math.Clamp(value, 0, 255);
		}
		return Math.Clamp(Math.Clamp(value, 0, 255), Math.Clamp(minimum, 0, 255), Math.Clamp(maximum, 0, 255));
	}

	private Task<bool> LcSetBreathingAsync(bool enabled)
	{
		return LcPublishLightActionAsync("LEDBreathing", enabled ? 1 : 0);
	}

	private Task<bool> LcSetColorfulAsync(bool enabled)
	{
		return LcPublishLightActionAsync("LEDColorful", enabled ? 2 : 0);
	}

	private Task<bool> LcSetFanLightEffectAsync(int mode)
	{
		return LcPublishLightActionAsync("FanLEDEffect", mode);
	}

	private async Task<bool> LcPublishLightActionAsync(string action, int ledMode)
	{
		try
		{
			if (!CanControlLiquidCooling)
			{
				return false;
			}
			await _hw.Publish("BT_LC/Control", new Dictionary<string, object>
			{
				["Action"] = action,
				["LedMode"] = ledMode
			});
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"{action}({ledMode}) fail: {ex.Message}");
			return false;
		}
	}

	private async Task<bool> LcApplyStaticHeadLightAsync(Color color)
	{
		return await LcSetColorfulAsync(enabled: false) & await LcSetBreathingAsync(enabled: false) & await LcSetLightColorCoreAsync(color);
	}

	private async Task<bool> LcApplyBreathingHeadLightAsync(Color color)
	{
		return await LcSetColorfulAsync(enabled: false) & await LcSetLightColorCoreAsync(color) & await LcSetBreathingAsync(enabled: true);
	}

	private async Task<bool> LcApplyColorfulHeadLightAsync(bool breathing)
	{
		bool flag = ((!breathing) ? (await LcSetBreathingAsync(enabled: false)) : (await LcSetColorfulAsync(enabled: true)));
		bool breathingState = flag;
		flag = ((!breathing) ? (await LcSetColorfulAsync(enabled: true)) : (await LcSetBreathingAsync(enabled: true)));
		bool flag2 = flag;
		return breathingState & flag2;
	}

	private async Task<bool> LcDisableAllLightEffectsAsync()
	{
		return await LcSetBreathingAsync(enabled: false) & await LcSetColorfulAsync(enabled: false) & await LcSetFanLightEffectAsync(0);
	}

	public Task<bool> LcInputWater()
	{
		return RunLiquidCoolingAsync("LcInputWater", LiquidCoolingWriteLockTimeout, async () =>
		{
			if (!_hw.SupportsLiquidCooling)
			{
				return false;
			}
			await _hw.Publish("BT_LC/Control", new Dictionary<string, object> { ["Action"] = "InputWater" });
			Logger.WriteLine("LcInputWater 已发送");
			return true;
		});
	}

		/// <summary>
		/// 2026-10-09：50 系单分区键盘修复总开关（修复 a/b/c 共用，唯一具名常量）。
		/// 为什么需要它：SetLightEffect 里原来的 !IsGcu50 是 2026-10-04 **刻意加的兼容护栏**
		///   ——「排除 50 系（苍龙等单分区机型未实机验证，保持原行为）」，随后 GCU 自报 solution
		///   判据上线时又把它保留为「双保险」（LM 自带安装器装的 50 系有注册表标记，仍被排除）。
		///   原文见 修改日志.txt 的 [2026-10-04 20:40]（L440-444）与 [2026-10-04 20:55]（L464-480）。
		/// 本次放开它，故按"刻意的兼容护栏"处理：保留一行可回退的开关。
		/// 影响面：仅 KeyboardType∈{1,2} 且 HKLM\SOFTWARE\L-Mechrevo\GcuVariant=="50" 且
		///   GCU 自报 solution≠ITE 的机器（补 MonochromeIndex + ColorBuffer 每项 ID，
		///   且 SetEffectALL 携带 powerstatus=1，开机恢复不再走 HID 帧流）；
		///   30 系 / 40 系 / 通用路径与其他机型判据与产物一律不变。
		/// 如何回退：把本常量改成 false —— 三处修复同时回到旧行为，不需要改别的代码。
		/// </summary>
		public const bool EnableGcu50SingleZoneKeyboardFix = true;

		// 2026-10-09（40B≡40，用户原话见 GcuVariantFamily）：50 家族只含 "50" 本身 —— 40B 是 40 系载荷形态，
		// 不是 50 系，故**故意**不并家族（家族映射只定义在 GcuVariantFamily，本判据语义一字未改）。
		public static bool IsGcu50
		{
			get
			{
				// 登记缺失时用 GCU 包特征识别结果（见 GcuIdentity）
			return string.Equals(GcuIdentity.Variant, "50", StringComparison.OrdinalIgnoreCase);
			}
		}
		/// <summary>
	/// 是否 30 系 GCU（LM 自带安装器写 HKLM\SOFTWARE\L-Mechrevo\GcuVariant="30"）。
	/// 2026-10-04：与显卡代际 Gen30 一起作为「模式 × 配置」功能的判据。
	/// <para>2026-10-09（实验开关，用户原话「先在我这台机器上无条件显示30系性能模式 我看看40A GCU
	/// 能不能正确切30系的性能方式」）：<b>唯一</b>一处实验旁路 ——
	/// <c>Force30Perf.Enabled</c>（环境变量 <c>LMECHREVO_FORCE_30PERF=1</c> 或 config
	/// <c>实验_force_30perf=1</c>，<b>默认关</b>）为真时本属性强制判真，
	/// 从而让 <c>MechrevoHw.SupportsModeProfileConfig</c> 为真 ⇒ 切模式时
	/// <c>ResolveModeProfileIndex</c> 真的把 <c>ProfileIndex</c> 塞进 <c>Fan/Control</c> 下发
	/// （这正是本批要验证的字段）。</para>
	/// <para>⚠️ 默认关闭时的<b>逐字等价</b>证明：开关为 false 时 <c>Force30Perf.Enabled == false</c>，
	/// 下面第一行短路，执行路径与返回值与改动前<b>完全相同</b>（仍只看注册表 <c>GcuVariant=="30"</c>，
	/// 即 <c>v != null &amp;&amp; string.Equals(v.ToString(), "30", OrdinalIgnoreCase)</c>，
	/// 读失败仍返回 false）。⛔ 本属性<b>只读</b>注册表、<b>不写</b>注册表；
	/// <c>IsGcu40Keyboard</c>（同文件，读同一个 <c>GcuVariant</c>）的判据不由本开关改变
	/// （2026-10-09 起 40B 与 40 等价，见 <see cref="GcuVariantFamily"/>）⇒
	/// 本机 <c>GcuVariant="40"</c> / <c>"40B"</c> 的键盘灯 40 系协议分支不受本开关影响。</para>
	/// </summary>
	// 2026-10-09（40B≡40，用户原话见 GcuVariantFamily）：30 家族只含 "30" 本身 —— 40B 是 40 系载荷形态
	// （payload\40B，与 payload\40 同代），不能算 30 系，故**故意**不并家族；下面判据与 Force30Perf 旁路一字未动。
	public static bool IsGcu30
	{
		get
		{
			// 2026-10-09（实验开关）：唯一旁路。开关关闭 ⇒ 短路，下面与改动前逐字一致。
			if (Force30Perf.Enabled)
			{
				return true;
			}
			// 登记缺失时用 GCU 包特征识别结果（见 GcuIdentity）
		return string.Equals(GcuIdentity.Variant, "30", StringComparison.OrdinalIgnoreCase);
		}
	}

	/// <summary>
	/// 该变体是否属 40 家族（<c>"40"</c> 或 <c>"40B"</c>）—— 40 系行为分流的**唯一入口**，
	/// 实现见 <see cref="GcuVariantFamily"/>（家族映射只在那里定义一次）。
	/// <para>2026-10-09（用户原话「对了 LM里面好像有一些GCU判断条件 GCU40和40B应该等价」）：
	/// 原先 <c>IsGcu40Keyboard</c> 写死 <c>string.Equals(v, "40", OrdinalIgnoreCase)</c> ⇒ 装了 40B 的机器
	/// （payload\40B，10 文件 Uniwill 覆盖层）被判成"非 40 系" ⇒ 键盘灯掉进通用/30 系分支
	/// （<c>SetLightEffect</c> 的 40 系协议字段 powerstatus / MonochromeIndex / ColorBlocks=7，
	/// 以及 per-key 补 ID 的 <c>!isGcu40Keyboard</c> 排除条件，全部判错）。</para>
	/// <para>⚠️ 只归一<b>行为分流</b>：载荷身份（选覆盖层 / 校验 <c>GcuPayloadSha256</c> /
	/// 诊断按变体取落地基线 / 展示变体名）仍按注册表原值区分 40 与 40B —— 两者字节集不同。</para>
	/// </summary>
	public static bool IsGcu40Family(string variant)
	{
		return GcuVariantFamily.Is40(variant);
	}

	/// <summary>
	/// 本机键盘是否走 40 系 GCU 协议分支（读注册表 <c>GcuVariant</c>，判据 = <see cref="IsGcu40Family"/>）。
	/// 2026-10-09：40B 与 40 等价（改前只认字面 "40" ⇒ 40B 机器漏判 40 系协议）。
	/// </summary>
	private bool IsGcu40Keyboard
	{
		get
		{
			// 登记缺失时用 GCU 包特征识别结果（见 GcuIdentity）
		return IsGcu40Family(GcuIdentity.Variant);
		}
	}
	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达"全部接入"第 2 步 · 键盘灯）：⭐ **把"键盘灯"这一步分派给门面层** ✅
	/// <para>⭐ 返回 <c>true</c> ⇒ ⭐ 已由**非 GCU 后端**处理完（⭐ 调用方 ⛔ 不要再走 MQTT ✅）；
	/// ⭐ 返回 <c>false</c> ⇒ ⭐ 不归它管 ⇒ ⭐ 调用方**照原路走** ✅。</para>
	/// <para>⚠️ 效果映射：⭐ 宝龙达只有 ⭐ "关 / ⭐ RGB 固定色" ⚠️（⭐ 功能号 16 的 0 / 2 ✅）
	/// ⇒ ⭐ 我们那些复杂效果（⭐ 呼吸/波浪…）**统一降级成固定色** ✅ —— ⭐ 因为能力位只开了
	/// <c>Keyboard</c>、⛔ 没开 `Lightbar`/`RgbLightbar` ⇒ ⭐ UI 上不会出现那些效果 ✅
	/// （⚠️ 真机未验证项：⭐ 功能号 16/17/18 —— ⭐ 门面层内部会先读功能号 10，⭐ 读不到就拒绝写 ✅）</para>
	/// </summary>
	/// <summary>
	/// ⭐⭐ 2026-10-16（宝龙达"全部接入"第 3 步 · 功耗墙）：⭐ **把功耗墙那几项分派给门面层** ✅
	/// <para>⭐ 处理哪些键：⭐ `PL1`（⭐ PL1 瓦数 ✅）· ⭐ `PL2` ✅ · ⭐ `CpuTccOffset`（⭐ 温度墙 ✅）</para>
	/// <para>⭐ 返回 <c>true</c> ⇒ ⭐ 已由非 GCU 后端处理完（⭐ 调用方 ⛔ 不要再发 MQTT ✅）；
	/// ⭐ 返回 <c>false</c> ⇒ ⭐ 不归它管（⭐ GCU / 老同方 ⭐ **恒返回 false** ⇒ ⭐ 行为逐位不变 ✅）。</para>
	/// <para>⚠️ 数值语义（⭐ 2026-10-16 更正 —— 旧注释描述的是**已被删掉的双重换算** ⚠️）：
	/// ⭐ 界面的 PL1/PL2 是**瓦** ✅；⭐ 温度墙这一项 ⭐ 界面与门面层**同口径**、⭐ 都是**绝对温度 ℃** ✅
	/// ⇒ ⭐ 本方法 ⭐ **直接原样传下去**、⛔ **不做任何换算** ✅
	/// （⭐ 依据：⭐ 界面 slider 的范围就是 `[hw.TccMinimum, hw.TccMaximum]` = `[60,105]`、⭐ 当前值就是
	/// `hw.TccTarget` ✅；⭐ 且既有的校验 `"CpuTccOffset" => _hw.TccTarget == result` 也说明两者可直接比较 ✅）</para>
	/// <para>⭐⭐ 2026-10-16（"未设置"）：⭐ 未设置的项（⭐ `≤0` ✅）⭐ **原样传"未设置"下去**，
	/// ⛔ **不再**补一个"保守默认值"（⭐ 那组 45/65/95 是我们自己编的 ⚠️ —— ⭐ 见
	/// `MechrevoHw` 里 `BaolongdaUnset` 那段注释的依据 ✅）。</para>
	/// <para>⚠️ 实现取舍（⭐ 如实说明）：⭐ 这几个键是**分别**到达的 ⇒ ⭐ 每个都会**各触发一次写入** ⚠️
	/// ⇒ ⭐ 结果是"⭐ 最后一个生效 ✅、⭐ 总开销多几次只读回读 ✅" —— ⭐ 功能正确、⛔ 不是最优 ✅
	/// ⭐ （⭐ 更优做法是把它们**攒成一次**应用 ⚠️ ⇒ ⭐ 留待审查阶段优化 ✅）</para>
	/// </summary>
	private bool TryApplyPowerLimitViaBackend(string key, string rawValue)
	{
		MechrevoHw hw = _hw;
		bool intelMsrPower = hw != null && MechrevoLite.Hardware.CpuVendor.IsIntel
			&& !hw.IsIntelNucMachine && !hw.IsUmiMachine;
		if (hw == null || (!hw.UsesBaolongda && !hw.SmuCustomApu && !intelMsrPower))
		{
			return false; // ⭐ 不是宝龙达/无界 APU/Intel MSR 在管事 ⇒ ⭐ 原样交给 GCU ✅
		}
		bool isPl1 = string.Equals(key, "PL1", StringComparison.Ordinal);
		bool isPl2 = string.Equals(key, "PL2", StringComparison.Ordinal);
		bool isTempWall = string.Equals(key, "CpuTccOffset", StringComparison.Ordinal);
		// （30 系 · SMU 功耗时间窗）：新增键 SmuStapmTimeSeconds（单位**秒**）。
		bool isStapmTime = string.Equals(key, "SmuStapmTimeSeconds", StringComparison.Ordinal);
			bool isFastTime = string.Equals(key, "SmuFastTimeSeconds", StringComparison.Ordinal);
		bool isSlowLimit = string.Equals(key, "SmuSlowLimit", StringComparison.Ordinal);
		bool isSlowTime = string.Equals(key, "SmuSlowTimeSeconds", StringComparison.Ordinal);
		if (!isPl1 && !isPl2 && !isTempWall && !isStapmTime && !isSlowLimit && !isSlowTime && !isFastTime)
		{
			return false; // ⭐ 别的键不归本方法管 ✅
		}
		if (!int.TryParse(rawValue, out int result))
		{
			Logger.WriteLine($"宝龙达 功耗墙：⭐ 键 {key} 的值 \"{rawValue}\" 不是整数 ⇒ 跳过 ⚠️");
			return true; // ⭐ 已接管 ⇒ ⛔ 不让它走 GCU ✅
		}
		// 40 系（宝龙达 MRID6-23）有功能号 23 ⇒  继续走下面的原 WMI 路径，**行为不变**
		if (hw.IsBaolongda30Series || hw.SmuCustomApu)
		{
			// 不可用 ⇒ 如实 false + 日志（30 系**没有** WMI 23 可退回， 不假装成功）。
			if (!PerfModeCapabilities.UsesSmuForCustomPower(hw.UsesBaolongda, hw.IsBaolongda30Series, SmuAvailability.IsReady, hw.SmuCustomApu))
			{
				Logger.WriteError("[smu] 不可用 ⇒ 30 系功耗墙**未下发**（原因：" + SmuAvailability.Reason + "）⇒ 如实失败");
				return false;
			}
			return ApplyPowerLimitViaSmu(hw, isPl1, isPl2, isTempWall, isStapmTime, isSlowLimit, isSlowTime, isFastTime, result);
		}
		// ⭐ 三个值取"现值（⭐ 本地记忆 ✅）"，⭐ 只覆盖这次要改的那一个 ✅
		int pl1 = hw.Pl1;
		int pl2 = hw.Pl2;
		// ⭐⭐ 2026-10-16（修温度墙"双重换算"）：⭐ `hw.TccTarget` 存的就是**绝对温度 ℃** ✅
		//   ⭐ 依据：⭐ 宝龙达门面层 `TryApplyCpuPowerLimitViaBackend` 里写的是 `TccTarget = wall`
		//      （⭐ `wall` 是绝对温度 60~105 ✅）；⭐ 而 GCU 路径也把它夹在 `[TccMinimum, TccMaximum]`
		//      = `[85, 95]` 这种绝对温度区间里 ✅ —— ⭐ 两处一致 ✅
		//   ⚠️ 原来写 `TccMaximum - TccTarget` ⇒ ⭐ 等于**又减了一次** ⇒ ⭐ 第二次应用时
		//      温度墙会被算成 `105 − 95 = 10` ❌ ⇒ ⭐ 再被 `Math.Max(60, …)` 抬到 **60℃** ❌
		//      ⇒ ⭐ 用户设的 95℃ 被悄悄改成最保守的 60℃ ⚠️
		int tempWallC = (hw.TccTarget > 0) ? hw.TccTarget : 0;
		if (isPl1)
		{
			pl1 = result;
		}
		else if (isPl2)
		{
			pl2 = result;
		}
		else if (isTempWall)
		{
			// ⭐⭐ 2026-10-16（同上）：⭐ 界面那个 `CpuTccOffset` 与 `hw.TccTarget` **同口径** ——
			//   ⭐ 都是**绝对温度 ℃** ✅（⭐ 依据：⭐ 界面 slider 的范围就是 `[TccMinimum, TccMaximum]`
			//     = `[60, 105]`、⭐ 当前值就是 `TccTarget` ✅；⭐ 且 `MechrevoService` 的校验
			//     `"CpuTccOffset" => _hw.TccTarget == result` 也说明两者可直接比较 ✅）
			//   ⚠️ 原来这里把它当 offset 处理（⭐ `max - result` ⚠️）⇒ ⭐ 同上，⭐ 等于又减一次 ❌
			//   ⭐ 现在：⭐ `0` ⇒ ⭐ 不限制（⭐ 用上限 ✅）；⭐ 正数 ⇒ ⭐ 直接用，⭐ 并夹进 `[60, 105]` ✅
			int max = (hw.TccMaximum > 0) ? hw.TccMaximum : 105;
			tempWallC = (result <= 0) ? max : Math.Max(60, Math.Min(max, result));
		}
		// 即使本方法被"单键"调用（ PerfModeService 的逐字段下发就是这种）
		LinkPowerLimitPair(hw, isPl1, isPl2, ref pl1, ref pl2);
		// ⭐⭐ 2026-10-16（"未设置" · 用户指导「⭐ 开源的都有方案的不是吗」）：
		//   ⛔ **删掉了原来的"保守默认 45/65/95"** —— ⭐ 那是**我们自己编的**（⭐ 依据：⭐ 开源里
		//      没有任何一处完整支持那组数；⭐ 蛟龙自己两处还互相矛盾 ⚠️ ⇒ ⭐ 不能编 ✅）
		//   ⭐ 现在把"未设置"如实**传下去**（⭐ `≤0` = 未设置 ✅）⇒ ⭐ 门面层会**跳过**那一项、
		//      ⛔ 不拿编造值去凑满三项 ✅（⭐ 这正是斗战者的做法：⭐ 只写用户覆盖过的 ✅）
		//   ⭐ 全部未设置时 ⭐ 一个子命令都不发（⭐ 见 `TryApplyCpuPowerLimitViaBackend` ✅）
		_ = hw.TryApplyCpuPowerLimitViaBackend(enable: true, pl1, pl2, tempWallC, out bool confirmed);
		// ⭐⭐ 2026-10-16：⭐ 这条是**真机验证功耗墙下发**的主要证据 ⚠️ ⇒ ⭐ 必须用 `WriteError` ✅
		//   （⭐ `Logger.WriteLine` 的级别由**消息内容**判定 ⇒ ⭐ 默认「仅错误」级别下**可能不落盘** ❌）
		//   ⭐ 每次"应用"最多 3 行（⭐ PL1 / PL2 / 温度墙各一次 ✅）⇒ ⭐ 不需要额外去重 ✅
		//   ⚠️ 措辞如实：⭐ 打印的是"⭐ 写成功"（⭐ = WMI 没报错 ⚠️），⛔ **不写"已确认生效"** ✅
		Logger.WriteError($"宝龙达 功耗墙（⭐ 非 GCU 后端路径）：⭐ key={key}"
			+ $" · ⭐ 下发 PL1={ShowPowerValue(pl1)} PL2={ShowPowerValue(pl2)} 温度墙={ShowTempValue(tempWallC)}"
			+ $" · ⭐ 写成功={confirmed}（⚠️ ⛔ 非「数值已生效」）⇒ ⭐ 该项未发 MQTT ✅");
		return true;
	}
	// **单键写入路径的 PL1/PL2 联动兜底**（ WMI 23 与 SMU 两条路径共用）。
	private static void LinkPowerLimitPair(MechrevoHw hw, bool isPl1, bool isPl2, ref int pl1, ref int pl2)
	{
		if (pl1 <= 0 || pl2 <= 0 || pl1 <= pl2)
		{
			return;
		}
		int oldPl1 = pl1;
		int oldPl2 = pl2;
		string reason;
		if (isPl1 && !isPl2)
		{
			// ⭐ 用户抬高了 PL1 ⇒ 把 PL2 一起抬（⭐ 受 PL2 上限约束）
			pl2 = ((hw.Pl2Maximum > 0) ? Math.Min(pl1, hw.Pl2Maximum) : pl1);
			if (pl2 < pl1)
			{
				pl1 = pl2;
			}
			reason = "因 PL1=" + pl1.ToString(CultureInfo.InvariantCulture);
		}
		else
		{
			// ⭐ 用户在降 PL2（⭐ 或来源不明的键）⇒ 把 PL1 一起降（⭐ 安全方向）
			pl1 = pl2;
			reason = "因 PL2=" + pl2.ToString(CultureInfo.InvariantCulture);
		}
		string detail = string.Empty;
		if (pl1 != oldPl1)
		{
			detail += $"PL1 {oldPl1}→{pl1}";
		}
		if (pl2 != oldPl2)
		{
			if (detail.Length > 0)
			{
				detail += "、";
			}
			detail += $"PL2 {oldPl2}→{pl2}";
		}
		Logger.WriteError($"[pl] 联动：{detail}（{reason}）");
	}
	// 独立分支：只写这一条， 不碰 PL1/PL2/温度墙；回读语义为"已下发 / 已确认 / 未确认"（该 PM 字段
	private bool ApplyPowerLimitViaSmu(MechrevoHw hw, bool isPl1, bool isPl2, bool isTempWall, bool isStapmTime, bool isSlowLimit, bool isSlowTime, bool isFastTime, int result)
	{
		PawnIO.RyzenSmuService smu = MechrevoLite.Mode.ModeControl.SharedSmu;
		if (smu == null)
		{
			Logger.WriteError("[smu-30] 30 系功耗墙**未下发**：PawnIO/SMU 不可用（需要管理员权限、AMD 平台）⇒ 如实失败");
			return false;
		}
		// 语义：命令被 SMU 接受 = 已下发；回读**一致**才写"已确认"；不稳定/读不到 ⇒ 如实记「未确认」
		if (isFastTime)
		{
			// 本平台 SMU 只有 stapm_time / slow_time 两条时间窗命令，没有 fast-time
			// ⇒ 快速提升持续时间无法下发：如实记日志、不拿 stapm_time 冒充（不计失败）
			Logger.WriteError("[smu] fast 提升持续时间=" + result + "s：本平台 SMU 无 fast-time 命令 ⇒ 未生效（不写 stapm_time 冒充）");
			return true;
		}
		if (isSlowLimit)
		{
			int slowMin = (hw.SmuCustomApu ? WujieSmuGate.PlMinWatts : BaolongdaProfile.PlMinWatts);
			int slowMax = (hw.SmuCustomApu ? WujieSmuGate.PlMaxWatts : BaolongdaProfile.Pl2MaxWatts);
			if (result < slowMin || result > slowMax)
			{
				Logger.WriteError($"[smu] slow 限值拒绝下发：{result}W 不在 {slowMin}..{slowMax}W");
				return false;
			}
			PawnIO.SmuStatus slowStatus = smu.SetSlowLimit(result);
			if (slowStatus != PawnIO.SmuStatus.OK)
			{
				Logger.WriteError($"[smu] slow={result}W 下发失败={slowStatus}");
				return false;
			}
			Logger.WriteError($"[smu] slow={result}W 下发=OK");
			// 2026-10-21：慢速功率(sPPT) 回读诊断 —— 固件可能静默钳位/被 fast 覆盖，日志如实记录命令与回读
			try
			{
				System.Threading.Thread.Sleep(150);
				PawnIO.SmuPowerSnapshot slowSnap = smu.GetPowerSnapshot();
				if (slowSnap != null)
				{
					int rbSlow = (int)Math.Round(slowSnap.Slow);
					Logger.WriteError("[smu] slow 回读=" + rbSlow + "W（目标=" + result + "W）⇒ " + ((rbSlow == result) ? "已确认" : "未生效（固件可能钳位该平台 sPPT 下限，或被 fast 覆盖）"));
				}
				else
				{
					Logger.WriteError("[smu] slow 回读失败（PM 表读不到）⇒ 命令已接受但无法确认");
				}
			}
			catch (Exception exSlowRb)
			{
				Logger.WriteError("[smu] slow 回读异常（已吞掉）：" + exSlowRb.GetType().Name);
			}
			// Wujie：fast(fPPT) 必须 ≥ slow(GCU PL2)，否则固件可能忽略 fast ⇒ 自动抬 fast 并记日志
			if (hw.SmuCustomApu && hw.Pl2 > 0 && result > hw.Pl2)
			{
				PawnIO.SmuStatus raiseFast = smu.SetFastLimit(result);
				Logger.WriteError($"[smu] Wujie：slow={result}W > 当前 fast={hw.Pl2}W ⇒ 同步抬 fast={result}W（{raiseFast}）");
				if (raiseFast == PawnIO.SmuStatus.OK)
				{
					hw.NoteBaolongdaSmuLimitsApplied(0, result, 0);
				}
			}
			return true;
		}
		if (isSlowTime)
		{
			if (result < BaolongdaProfile.StapmTimeMinSeconds || result > BaolongdaProfile.StapmTimeMaxSeconds)
			{
				Logger.WriteError($"[smu] slow 时间窗拒绝下发：{result}s");
				return false;
			}
			PawnIO.SmuStatus slowTimeStatus = smu.SetSlowTimeSeconds(result);
			if (slowTimeStatus != PawnIO.SmuStatus.OK)
			{
				Logger.WriteError($"[smu] slow 时间窗={result}s 下发失败={slowTimeStatus}");
				return false;
			}
			Logger.WriteError($"[smu] slow 时间窗={result}s 下发=OK");
			// 2026-10-21：写后回读（PM 表 slow_time）—— 不确认就等于"不知道有没有生效"。
			hw.NoteSmuSlowTimeApplied(result);
			int rbSlowTime = 0;
			try
			{
				System.Threading.Thread.Sleep(120);
				PawnIO.SmuPowerSnapshot slowTimeRb = smu.GetPowerSnapshot();
				rbSlowTime = slowTimeRb?.SlowTimeSeconds ?? 0;
			}
			catch
			{
				rbSlowTime = 0;
			}
			if (rbSlowTime > 0 && Math.Abs(rbSlowTime - result) <= 1)
			{
				Logger.WriteError($"[smu] slow 时间窗={result}s · 回读={rbSlowTime}s（PM 表）⇒ 已确认 ✅");
			}
			else
			{
				Logger.WriteError($"[smu] slow 时间窗={result}s · 回读={(rbSlowTime > 0 ? rbSlowTime + "s" : "读不到")}"
					+ "（PM 表）⇒ 未确认（固件可能忽略该命令或该表版本不反映 slow_time；⛔ 不假装成功）");
			}
			return true;
		}
		if (isStapmTime)
		{
			if (result < BaolongdaProfile.StapmTimeMinSeconds || result > BaolongdaProfile.StapmTimeMaxSeconds)
			{
				Logger.WriteError($"[smu] STAPM 时间窗**拒绝下发**：{result}s 不在 UI/校验区间 "
					+ $"[{BaolongdaProfile.StapmTimeMinSeconds},{BaolongdaProfile.StapmTimeMaxSeconds}]s（⛔ 不猜语义）⇒ 如实失败");
				return false;
			}
			PawnIO.SmuStatus timeResult = smu.SetStapmTimeSeconds(result);
			if (timeResult != PawnIO.SmuStatus.OK)
			{
				Logger.WriteError($"[smu] STAPM 时间窗={result}s（0x18）· 下发失败={timeResult}(0x{(uint)timeResult:X2}) ⇒ 如实失败");
				return false;
			}
			// 回读确认（best-effort）：读 PM 表两次 ——
			//   * 两次都读到、彼此一致（±1）且 ≈ 目标 ⇒ 已确认；
			//   * 两次读数彼此不一致 ⇒ 字段不稳定 ⇒ **未确认**；
			//   * 读不到 ⇒ **未确认**。
			string readback;
			bool confirmed = TryVerifyStapmTimeWindow(smu, result, out readback);
			// ⭐ 本地记忆 = "我们下发的值"（与 PL 那几项的本地记忆同口径：**写成功后**就记，不等回读）。
			//   ⚠️ 回读是否一致只影响日志措辞（已确认/未确认），⛔ 不把"未确认"写成"成功"。
			hw.NoteBaolongdaSmuStapmTimeApplied(result);
			if (confirmed)
			{
				Logger.WriteError($"[smu] STAPM 时间窗={result}s（0x18 `stapm_time`）· 回读={readback} ⇒ 一致 ✅（已确认）");
			}
			else
			{
				Logger.WriteError($"[smu] STAPM 时间窗={result}s（0x18 `stapm_time`）· 回读={readback} "
					+ "⇒ 未确认（该 PM 表版本读数不稳定或读不到；命令已被 SMU 接受，⛔ 不假装已确认）");
			}
			// ⚠️ 返回 true = "已接管且命令被 SMU 接受"（调用方的字段步骤会显示"已下发，暂时确认不了"）；
			//    回读是否一致已在日志里如实区分，⛔ 不把"未确认"写成"成功"。
			return true;
		}
		// ① 先只读刷新当前值（30 s 内复用 UI 打开时读到的快照；读不到就现场读一次）。
		//    目的：⭐ 只改一项时，另外两项用**真实当前值**做基线，⛔ 不拿 0/编造值去覆盖。
		bool refreshed = hw.RefreshBaolongda30SeriesSmuPower();
		int pl1 = isPl1 ? result : hw.Pl1;
		int pl2 = isPl2 ? result : hw.Pl2;
		int tempWallC = isTempWall ? ((result <= 0) ? 0 : result) : ((hw.TccTarget > 0) ? hw.TccTarget : 0);
		// （机主：PL2 不应低于 PL1）： SMU 这条路径同样**最后兜底**一次
		LinkPowerLimitPair(hw, isPl1, isPl2, ref pl1, ref pl2);
		// ② 取值夹取（与 WMI 路径同一组范围；⛔ 不写 0 = "未设置"）
		// ⭐⭐ 无界 APU（机主口径）：PL1/PL2 上限 = 90W（下限沿用现有 SMU 常量 15W）；宝龙达 30 系保持原范围 ✅
		int plMin = (hw.SmuCustomApu ? WujieSmuGate.PlMinWatts : BaolongdaProfile.PlMinWatts);
		int pl1Max = (hw.SmuCustomApu ? WujieSmuGate.PlMaxWatts : BaolongdaProfile.Pl1MaxWatts);
		int pl2Max = (hw.SmuCustomApu ? WujieSmuGate.PlMaxWatts : BaolongdaProfile.Pl2MaxWatts);
		if (pl1 > 0)
		{
			pl1 = Math.Clamp(pl1, plMin, pl1Max);
		}
		if (pl2 > 0)
		{
			pl2 = Math.Clamp(pl2, plMin, pl2Max);
		}
		if (tempWallC > 0)
		{
			tempWallC = Math.Clamp(tempWallC, BaolongdaProfile.TccMinCelsius, BaolongdaProfile.TccMaxCelsius);
		}
		// Wujie：fast(PL2) < slow(PL1) ⇒ 自动把 fast 抬到 slow（记日志提示）
		if (hw.SmuCustomApu && pl1 > 0 && pl2 > 0 && pl2 < pl1)
		{
			Logger.WriteError("[smu] Wujie 应用：PL2(" + pl2 + ") < PL1(" + pl1 + ") ⇒ fast 自动抬到 slow=" + pl1 + "（固件要求 fast ≥ slow 才不被忽略）");
			pl2 = pl1;
		}

		// ③ 一条都不发（三项都未设置）⇒ 与 WMI 路径同口径：已接管、不假装写了
		if (pl1 <= 0 && pl2 <= 0 && tempWallC <= 0)
		{
			Logger.WriteError("[smu-30] 30 系功耗墙：⚠️ PL1／PL2／温度墙都未设置 ⇒ 本次**不下发任何 SMU 命令**"
				+ "（⛔ 不编造默认值）—— 这是如实行为、⛔ 不是失败");
			return true;
		}
		// ④ 逐项下发；每一条都必须回 OK
		bool ok = true;
		bool wrotePl1 = false;
		bool wrotePl2 = false;
		bool wroteWall = false;
		string pl1Status;
		string pl2Status;
		if (hw.SmuCustomApu)
		{
			if (Logger.TryMarkChanged("wujie-smu-uxte", "1"))
			{
				Logger.WriteError("[smu] 无界功耗控制：UXTU 口径 —— 单次下发、不回读、不补发、不守护");
			}
			// ⭐ Wujie：**先写 slow(PL1) → stapm，再写 fast(PL2=fPPT)**（顺序固定；两条命令都回 OK 才算写成功）✅
			pl1Status = "未写";
			pl2Status = "未写";
			if (pl1 > 0)
			{
				// STAPM 行只写 STAPM（对应 GCU 的 PL1）；慢速对应 GCU PL2，由独立「慢速功率限制」行走 SetSlowLimit
				PawnIO.SmuStatus staple = smu.SetStapmLimit(pl1);
				pl1Status = $"stapm={staple}";
				wrotePl1 = staple == PawnIO.SmuStatus.OK;
				ok &= wrotePl1;
				if (wrotePl1)
				{
					hw.NoteBaolongdaSmuLimitsApplied(pl1, 0, 0);
				}
			}
			if (pl2 > 0)
			{
				// （机主真机：只拉 PL2 不生效 ⇒ fPPT=fast 是唯一生效限值）
				if (pl1 <= 0)
				{
					try
					{
						PawnIO.SmuPowerSnapshot preSnap = smu.GetPowerSnapshot(writeLog: false);
						if (preSnap != null)
						{
							int curSlow = (int)Math.Round((double)preSnap.Slow);
							int curStapm = (int)Math.Round((double)preSnap.Stapm);
							if (curSlow > pl2)
							{
								smu.SetSlowLimit(pl2);
								Logger.WriteError("[smu] Wujie 应用：当前 slow=" + curSlow + " > PL2=" + pl2 + " ⇒ 同步压到 slow=" + pl2 + "（保持 slow ≤ fast）");
							}
							if (curStapm > pl2)
							{
								smu.SetStapmLimit(pl2);
								Logger.WriteError("[smu] Wujie 应用：当前 stapm=" + curStapm + " > PL2=" + pl2 + " ⇒ 同步压到 stapm=" + pl2 + "（保持 stapm ≤ fast）");
							}
						}
					}
					catch
					{
					}
				}

				PawnIO.SmuStatus fastSt = smu.SetFastLimit(pl2);
				pl2Status = fastSt.ToString();
				wrotePl2 = fastSt == PawnIO.SmuStatus.OK;
				ok &= wrotePl2;
				if (wrotePl2)
				{
					hw.NoteBaolongdaSmuLimitsApplied(0, pl2, 0);
				}
			}
		}
		else if (pl1 > 0 && pl2 > 0)
		{
			// ⭐ 正常路径（两项都已知）：按任务书口径一次下发三项 —— PL1 同时写 slow 与 stapm
			smu.SetAllLimits(pl1, pl2, pl1, out PawnIO.SmuStatus stapmStatus, out PawnIO.SmuStatus fastStatus, out PawnIO.SmuStatus slowStatus);
			pl1Status = $"stapm={stapmStatus}/slow={slowStatus}";
			pl2Status = fastStatus.ToString();
			ok = stapmStatus == PawnIO.SmuStatus.OK && fastStatus == PawnIO.SmuStatus.OK && (hw.SmuCustomApu || slowStatus == PawnIO.SmuStatus.OK);
			// 无界：PL1 只以 STAPM 为准（slow 由「慢速功率限制」行单独管理），避免回读 slow 被固件钳住导致整体判失败
			wrotePl1 = stapmStatus == PawnIO.SmuStatus.OK && (hw.SmuCustomApu || slowStatus == PawnIO.SmuStatus.OK);
			wrotePl2 = fastStatus == PawnIO.SmuStatus.OK;
			if (!ok)
			{
				Logger.WriteError($"[smu-30] PL 下发未全部成功：stapm={stapmStatus}(0x{(uint)stapmStatus:X2}) "
					+ $"fast={fastStatus}(0x{(uint)fastStatus:X2}) slow={slowStatus}(0x{(uint)slowStatus:X2})");
			}
			if (wrotePl1 || wrotePl2)
			{
				hw.NoteBaolongdaSmuLimitsApplied(wrotePl1 ? pl1 : 0, wrotePl2 ? pl2 : 0, 0);
			}
		}
		else
		{
			// ⭐ 只知一项（读基线失败 / 另一项确实未设置）：只写这一项，⛔ 不拿 0 去覆盖另一项
			pl1Status = "未写";
			pl2Status = "未写";
			if (pl1 > 0)
			{
				PawnIO.SmuStatus st = smu.SetStapmLimit(pl1);
				PawnIO.SmuStatus sl = smu.SetSlowLimit(pl1);
				pl1Status = $"stapm={st} slow={sl}";
				wrotePl1 = st == PawnIO.SmuStatus.OK && (hw.SmuCustomApu || sl == PawnIO.SmuStatus.OK);
				ok &= wrotePl1;
				if (wrotePl1)
				{
					hw.NoteBaolongdaSmuLimitsApplied(pl1, 0, 0);
				}
			}
			if (pl2 > 0)
			{
				PawnIO.SmuStatus sf = smu.SetFastLimit(pl2);
				pl2Status = sf.ToString();
				wrotePl2 = sf == PawnIO.SmuStatus.OK;
				ok &= wrotePl2;
				if (wrotePl2)
				{
					hw.NoteBaolongdaSmuLimitsApplied(0, pl2, 0);
				}
			}
		}
		// 温度墙（Tctl 上限，SMU 0x19）
		string wallStatus = "未写";
		if (tempWallC > 0)
		{
			PawnIO.SmuStatus wallResult = smu.SetTctlTemp(tempWallC);
			wallStatus = wallResult.ToString();
			wroteWall = wallResult == PawnIO.SmuStatus.OK;
			ok &= wroteWall;
			if (wroteWall)
			{
				hw.NoteBaolongdaSmuLimitsApplied(0, 0, tempWallC);
			}
			else
			{
				Logger.WriteError($"[smu-30] 温度墙下发失败：tctl={tempWallC}℃ ⇒ SMU {wallResult}(0x{(uint)wallResult:X2})");
			}
		}
		// ⑤ **写入后回读确认**（任务硬性约束：任何写入后必须回读确认）——
		//   ⭐ 用 PM 表再读一次（强制刷新），逐项比对刚下发的值。
		//   ⚠️ 回读失败 / 对不上 ⇒ 如实返回 false（⛔ 不把"命令回 OK"当成"值已生效"）。
		//   ⚠️ STAPM 时间窗（0x18）走上面的**独立分支**（该字段读数不稳定，语义为"已下发 / 未确认"）。
		bool verified = false;
		string verifyDetail;
		if (!ok)
		{
			verifyDetail = "未回读（有下发失败项）";
		}
		else if (!wrotePl1 && !wrotePl2 && !wroteWall)
		{
			verified = true;
			verifyDetail = "无写入（无需回读）";
		}
		else if (hw.SmuCustomApu)
		{
			// 2026-10-21（机主：行为对齐 UXTU；UXTU 用 RyzenAdj 只写不读回）：
			// 兼容模式下命令被 SMU 接受即视为已下发，不再拿 PM 表回读去判失败。
			verified = true;
			verifyDetail = "UXTU 口径：不回读（命令被 SMU 接受即视为已下发）";
			Logger.WriteError("[smu] Wujie 应用（UXTU 口径）：PL1=" + pl1 + "W · PL2=" + pl2 + "W · tctl=" + tempWallC
				+ "℃ · 命令已接受，不做回读校验");
		}
		else
		{
			verified = VerifyBaolongdaSmuPower(hw, wrotePl1 ? pl1 : 0, wrotePl2 ? pl2 : 0,
				wroteWall ? tempWallC : 0, out verifyDetail);
		}
		Logger.WriteError($"[smu-30] 30 系功耗墙（SMU 路径 · 无 WMI 23）：基线刷新={refreshed} "
			+ $"⇒ PL1(slow)={ShowPowerValue(pl1)} [{pl1Status}] · PL2(fast)={ShowPowerValue(pl2)} [{pl2Status}] "
			+ $"· 温度墙(tctl)={ShowTempValue(tempWallC)} [{wallStatus}] "
			+ $"⇒ 下发全部 OK={ok} · 回读确认={verified}（{verifyDetail}）"
			+ (ok && verified ? "" : " ⇒ ⚠️ 如实返回 false"));
		return ok && verified;
	}
	// （机主：「是持续功耗时间（STAPM time），不是 STAPM 功耗上限」）
	private bool TryVerifyStapmTimeWindow(PawnIO.RyzenSmuService smu, int targetSeconds, out string readback)
	{
		readback = "读不到";
		// 给 SMU 一点时间把新值刷进 PM 表，再连续读两次判断"稳定/一致"。
		Thread.Sleep(200);
		PawnIO.SmuPowerSnapshot first = smu.GetPowerSnapshot();
		Thread.Sleep(200);
		PawnIO.SmuPowerSnapshot second = smu.GetPowerSnapshot();
		int? firstTime = first != null ? first.StapmTimeSeconds : ((int?)null);
		int? secondTime = second != null ? second.StapmTimeSeconds : ((int?)null);
		if (!firstTime.HasValue && !secondTime.HasValue)
		{
			readback = "读不到（该 PM 表版本没有该字段/读表失败）";
			return false;
		}
		if (!firstTime.HasValue || !secondTime.HasValue)
		{
			readback = $"只读到一次（{(firstTime.HasValue ? firstTime.Value : secondTime.Value)}s）";
			return false;
		}
		if (Math.Abs(firstTime.Value - secondTime.Value) > 1)
		{
			readback = $"{firstTime.Value}s → {secondTime.Value}s（字段不稳定）";
			return false;
		}
		readback = $"{firstTime.Value}s";
		return Math.Abs(firstTime.Value - targetSeconds) <= 1;
	}
	// （30 系 · SMU 功耗墙）：**写入后回读确认** —— 重新读 PM 表，逐项比对刚下发的值
	private bool VerifyBaolongdaSmuPower(MechrevoHw hw, int pl1, int pl2, int tempWallC, out string detail)
	{
		detail = string.Empty;
		// 先给 SMU 一点时间把新值刷进 PM 表；再最多读 3 次（对不上就再等一等）。
		for (int attempt = 0; attempt < 3; attempt++)
		{
			if (attempt > 0)
			{
				Thread.Sleep(200);
			}
			// 2026-10-21（无界）：刷新接口/字段口径都不同 —— PL1 写的是 STAPM（不是 slow），fPPT=Fast。
			if (hw.SmuCustomApu)
			{
				hw.RefreshWujieSmuSnapshot();
			}
			else if (!hw.RefreshBaolongda30SeriesSmuPower(force: true))
			{
				detail = $"第 {attempt + 1} 次回读失败（PM 表读不到）";
				continue;
			}
			PawnIO.SmuPowerSnapshot snapshot = hw.BaolongdaSmuSnapshot;
			if (snapshot == null)
			{
				detail = $"第 {attempt + 1} 次回读失败（没有快照）";
				continue;
			}
			int readPl1 = (int)Math.Round(hw.SmuCustomApu ? snapshot.Stapm : snapshot.Slow, MidpointRounding.AwayFromZero);
			int readPl2 = (int)Math.Round(snapshot.Fast, MidpointRounding.AwayFromZero);
			int readWall = (int)Math.Round(snapshot.TctlTemp, MidpointRounding.AwayFromZero);
			bool matchPl1 = (pl1 <= 0) || Math.Abs(readPl1 - pl1) <= 1;
			bool matchPl2 = (pl2 <= 0) || Math.Abs(readPl2 - pl2) <= 1;
			bool matchWall = (tempWallC <= 0) || Math.Abs(readWall - tempWallC) <= 1;
			detail = $"读回 PL1{(hw.SmuCustomApu ? "(stapm)" : "(slow)")}={readPl1}W PL2(fast)={readPl2}W tctl={readWall}℃ "
				+ $"⇒ 一致 PL1={matchPl1} PL2={matchPl2} 温度墙={matchWall}（第 {attempt + 1} 次）";
			if (matchPl1 && matchPl2 && matchWall)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>⭐ 把"未设置"（⭐ ≤0）如实打成「未设置」、⛔ 不打成 0 ✅。</summary>
	private static string ShowPowerValue(int watts)
	{
		return (watts > 0) ? (watts + "W") : "未设置";
	}

	/// <summary>⭐ 同 <see cref="ShowPowerValue" />：⭐ 温度墙 ✅。</summary>
	private static string ShowTempValue(int celsius)
	{
		return (celsius > 0) ? (celsius + "℃") : "未设置";
	}

	/// <summary>
	/// ⭐⭐ 2026-10-17（真机「颜色改不了 / 下发恒 #FFFFFF」）：⭐ 键盘灯单色（ARGB）的**唯一解析入口** ✅
	/// <para>⚠️ 根因：⭐ 宝龙达 WMI 只有功能号 17 的 3 字节 RGB ⇒ 界面选「预设色板」时只存
	/// <c>MonochromeIndex</c>（1~29），直接读 <c>ColorArgb</c> 会拿到**默认白** ❌
	/// ⇒ ⭐ 必须换成调色板里的真实颜色（<see cref="LightChannelSettings.EffectiveColorArgb" />）✅。</para>
	/// <para>⭐ 非宝龙达**逐位不变**：仍只在 <c>Single</c> 时带 <c>ColorArgb</c> ✅。</para>
	/// </summary>
	private Color? ResolveKeyboardColor(string effect, LightChannelSettings settings)
	{
		if (string.IsNullOrWhiteSpace(effect) || MechrevoHw.IsLightEffectOff(effect))
		{
			return null;
		}
		if (_hw != null && _hw.UsesBaolongda)
		{
			// ⭐ 宝龙达：固定色（模式 2）全靠功能号 17 的 RGB ⇒ 无论效果 Id 叫什么，都带真实颜色 ✅
			return new Color?(Color.FromArgb(settings.EffectiveColorArgb()));
		}
		return (string.Equals(effect, "Single", StringComparison.Ordinal)
			? new Color?(Color.FromArgb(settings.ColorArgb))
			: (Color?)null);
	}

	private bool TryApplyKeyboardViaBackend(string effect, int light0to4, Color? singleColor)
	{
		MechrevoHw hw = _hw;
		if (hw == null)
		{
			return false;
		}
		// ⭐⭐ 2026-10-16（键盘灯「亮度不对」修正）：⭐ 亮度换算收归**唯一来源** ✅
		//   ⚠️ 旧代码 `light0to4 - 1` 会把界面第 1 档（⭐ 用户想要"最暗"）算成硬件 0
		//   ⇒ ⭐ 而硬件 0 **就是灭灯**（⭐ 参考实现界面原文「背光亮度设置为 0 时将关闭键盘灯光」✅）
		//   ⇒ ⭐ 用户在第一档区间看到的是"灯灭"而不是"变暗" ❌（⭐ "亮度不能调节"的一个真实成因 ⚠️）
		//   ⇒ ⭐ 现在统一走 <see cref="MechrevoHw.BaolongdaBrightnessFromUi" />（⭐ 0 ⇒ 0；正数 ⇒ 1~3 ✅）
		byte level = MechrevoHw.BaolongdaBrightnessFromUi(light0to4);
		// ⭐⭐ 2026-10-16（键盘灯模式）：⭐ 效果 Id → 模式（功能号 16）用**同一张表** ✅
		//   ⚠️ 2026-10-17 收敛：⭐ 只有 2（固定色）／1（自动循环）可写；⛔ 3（自定义颜色）
		//     本机固件写不进（真机写 3 回读仍为 2）⇒ 映射表里已撤掉，Manual 会落到下面的降级（模式 2）✅
		byte mode;
		if (!MechrevoHw.TryMapBaolongdaKeyboardMode(effect, out mode))
		{
			mode = 2;
		}
		bool on = (level > 0) && !MechrevoHw.IsLightEffectOff(effect);
		// ⭐⭐ 2026-10-17（真机「每次下发都是白」）：⭐ 调用方没带颜色时**不再把白色写下去** ❌
		//   ⇒ ⭐ 宝龙达下从已保存配置解析真实颜色（预设色板 → ARGB）；
		//      开关类入口（SetKeyboardPower）也走这里 ✅；⚠️ 非宝龙达不会用到这枚颜色（下行返回 false）✅
		int argb = singleColor?.ToArgb() ?? unchecked((int)0xFFFFFFFF);
		if (!singleColor.HasValue && hw.UsesBaolongda)
		{
			LightChannelSettings storedColor = LightingSettingsStore.Load("Keyboard/Ctrl", "Single");
			argb = ResolveKeyboardColor(effect, storedColor)?.ToArgb() ?? storedColor.EffectiveColorArgb();
			if (on)
			{
				Logger.WriteError("宝龙达 键盘灯：⚠️ 调用方没带颜色 ⇒ ⭐ 用**已保存的用户颜色** #"
					+ (argb & 0xFFFFFF).ToString("X6") + " ✅（⛔ 不再默认白色 ✅）");
			}
		}
		if (!hw.TrySetKeyboardLightingViaBackend(on, argb, level, mode, out bool confirmed))
		{
			return false; // ⭐ 不是非 GCU 后端在管事 ⇒ 交回原路 ✅
		}
		Logger.WriteLine($"SetKeyboardEffect（⭐ 非 GCU 后端路径）：⭐ on={on} · ⭐ effect={effect} · ⭐ light={light0to4}"
			+ $" · ⭐ mode={mode} · ⭐ level={level} · ⭐ 回读一致={confirmed} ⇒ ⭐ 未发任何 MQTT ✅");
		return true;
	}

public Task<bool> SetKeyboardEffect(string effect, int light = 4, int speed = 1, string direction = "None", Color? singleColor = null, bool save = false)
	{
		// ⭐⭐ 2026-10-16（宝龙达"全部接入"第 2 步 · 键盘灯 · ⭐ 前后端分离模板）：
		//   ⭐ 先问门面层"这次归你管吗" ✅ ⇒ ⭐ 非 GCU 后端在管事就**处理完直接返回**（⭐ 一条 MQTT 不发 ✅）
		//   ⭐ 否则 ⭐ **下面原来那两行一行未改** ✅（⭐ 回归风险 = 0 ✅）
		if (TryApplyKeyboardViaBackend(effect, light, singleColor))
		{
			return Task.FromResult(result: true);
		}
		// 2026-10-09（kbdctrl，用户原话「只搁置用户模式」⇒「给 LM 接通键盘灯」这一条开工）：开关打开时
		// 改走新的 Keyboard/Ctrl 直发链路（PublishKeyboardCtrlEffectAsync，跳过 Keyboard/Status 判定）；
		// 开关关闭（默认）时**逐字**执行下面原来那一行 ⇒ 行为与改前完全一致。
		if (KeyboardCtrlPublishSwitch.Enabled)
		{
			return PublishKeyboardCtrlEffectAsync(effect, light, speed, singleColor);
		}
		return SetLightEffect("Keyboard/Ctrl", effect, light, speed, direction, singleColor, save);
	}

	public Task<bool> SetKeyboardBrightnessPreservingEffect(int level0to4)
	{
		if (ShouldUseEcKeyboardLight())
		{
			// 2026-10-22（实测：白键盘 EC 亮度码 0=关、1=暗、2=亮，只有两档亮度）：
			// UI 0~4 档映射：0 => 关；1~2 => 暗；3~4 => 亮。
			int whiteLevel = (level0to4 <= 0) ? 0 : ((level0to4 >= 3) ? 2 : 1);
			bool ok = EcKeyboardLight.TryApply(whiteLevel > 0, whiteLevel);
			if (ok && whiteLevel > 0)
			{
				// 2026-10-22（机主：无界灯光档位要记忆）：记住实际 EC 档位（0=关/1=暗/2=亮），下次启动恢复。
				AppConfig.Set("wujie_kbd_level", whiteLevel);
			}
			Logger.WriteError("[kbd-ec] 白键盘亮度=" + level0to4 + " ⇒ " + (ok ? "已生效" : "失败"));
			return Task.FromResult(ok);
		}
		// 直接跳过（不写亮度、 不把亮度写成非 0、 不改电源镜像）
		if (_hw != null && _hw.UsesBaolongda
			&& (_hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30
				|| _hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40))
		{
			bool intentOn = _hw.KeyboardPower;
			if (!intentOn && _hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30
				&& _hw.TryReadKeyboardPowerFromEc(out bool ecOn, out int _))
			{
				intentOn = ecOn;
			}
			if (!intentOn)
			{
				Logger.WriteError("[kbd] 跳过保持亮度：当前=关灯 ⇒ 不写亮度（⛔ 不点亮）");
				return Task.FromResult(result: true);
			}
		}
		// ⭐⭐ 2026-10-16（宝龙达"全部接入"第 2 步）：⭐ 亮度调整同样先问门面层 ✅
		//   ⭐ 非 GCU 后端 ⇒ ⭐ 读出当前效果/颜色（⭐ 沿用下面的读取逻辑）后走 WMI ✅；
		//   ⭐ 否则 ⭐ 原样往下走 ✅
		if (_hw != null)
		{
			int speed0 = 1;
			Color? singleColor0 = null;
			string effect0;
			if (LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var settings0) && KeyboardFirmwareEffects.Contains(settings0.Effect))
			{
				effect0 = settings0.Effect;
				speed0 = settings0.Speed;
				// ⭐ 2026-10-17：⭐ 预设色板 → 真实 ARGB（宝龙达 WMI 只认 3 字节 RGB）✅
				singleColor0 = ResolveKeyboardColor(effect0, settings0);
			}
			else
			{
				effect0 = (string.IsNullOrWhiteSpace(_hw.KeyboardEffect) ? "Single" : _hw.KeyboardEffect);
			}
			if (TryApplyKeyboardViaBackend(effect0, level0to4, singleColor0))
			{
				return Task.FromResult(result: true);
			}
			_ = speed0;
		}
		// 2026-10-09（kbdctrl）：开关打开时亮度也走新的 Keyboard/Ctrl 链路；关闭时逐字走原路径。
		if (KeyboardCtrlPublishSwitch.Enabled)
		{
			return PublishKeyboardCtrlBrightnessAsync(level0to4);
		}
		int speed = 1;
		Color? singleColor = null;
		string text;
		if (LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var settings) && KeyboardFirmwareEffects.Contains(settings.Effect))
		{
			text = settings.Effect;
			speed = settings.Speed;
			// ⭐ 2026-10-17：⭐ 预设色板 → 真实 ARGB（宝龙达 WMI 只认 3 字节 RGB）✅
			singleColor = ResolveKeyboardColor(text, settings);
		}
		else
		{
			text = (string.IsNullOrWhiteSpace(_hw.KeyboardEffect) ? "Single" : _hw.KeyboardEffect);
		}
		return SetLightingLevelAsync("Keyboard/Ctrl", text, level0to4, speed, singleColor);
	}

	public async Task<bool> SetLightingLevelAsync(string topic, string effect, int light, int speed, Color? singleColor)
	{
		try
		{
			if (!SupportsLightTopic(topic))
			{
				return false;
			}
			// ⭐⭐ 2026-10-16（宝龙达 · 灯光效果）：⭐ 把**颜色 ／ 亮度 ／ 效果**这条也分派给非 GCU 后端 ✅
			//    ⚠️ 三个调用点（⭐ SettingsForm ／ Program ／ LightForm ✅）都**直接**调本方法
			//    ⇒ 它原来**直发 MQTT** ⇒ 宝龙达上换颜色 ／ 调亮度发不出去 ⚠️
			//    ⭐ 非宝龙达 ⇒ 分派返回 false ⇒ ⭐ 下面原路**逐位不变** ✅
			if (_hw.TrySetLightEffectViaBackend(topic, effect, light,
				singleColor.HasValue ? singleColor.Value.ToArgb() : (int?)null, out bool _))
			{
				return true;
			}
			Dictionary<string, object> color = new Dictionary<string, object>();
			// ⚠️ 2026-10-16（owner 明确指示「键盘灯是正常的不用动」）：⭐ 本方法**只服务键盘**
			// （唯一调用方是 SetKeyboardBrightnessPreservingEffect ⇒ topic 恒为 "Keyboard/Ctrl"）
			// ⇒ 这里**保持原样**（`isCircular = true` + 无颜色时那份七彩），⛔ 不跟随灯条/Logo 的改动。
			// 灯条/Logo 的协议校正见 SetLightEffect（那条路才是本次问题②的对象）。
			color["isCircular"] = true;
			if (string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal) && string.Equals(effect, "Manual", StringComparison.Ordinal))
			{
				int[] manualIndexes = LightChannelSettings.DefaultManualIndexes;
				if (LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var manualSettings) && manualSettings.ManualIndexes != null && manualSettings.ManualIndexes.Length >= 6)
				{
					manualIndexes = manualSettings.ManualIndexes;
				}
				color["ColorBlocks"] = 6;
				object[] manualBuffer = new object[6];
				for (int i = 0; i < 6; i++)
				{
					int paletteIndex = Math.Clamp(manualIndexes[i], 1, 29) - 1;
					int paletteColor = LightChannelSettings.ManualPalette[paletteIndex];
					manualBuffer[i] = new Dictionary<string, object>
					{
						["R"] = (paletteColor >> 16) & 255,
						["G"] = (paletteColor >> 8) & 255,
						["B"] = paletteColor & 255
					};
				}
				color["ColorBuffer"] = manualBuffer;
			}
			else
			{
				// ⚠️ owner 指示「键盘灯是正常的不用动」⇒ 本分支**逐字保持原样**（1 段色 / 无颜色时 7 段七彩），
				// ⛔ 不跟随灯条/Logo 的协议校正。灯条/Logo 的改动只在 SetLightEffect 里。
				color["ColorBlocks"] = (singleColor.HasValue ? 1 : 7);
				color["ColorBuffer"] = ((!singleColor.HasValue) ? new object[7]
				{
					new Dictionary<string, object> { ["R"] = 255, ["G"] = 0, ["B"] = 0 },
					new Dictionary<string, object> { ["R"] = 255, ["G"] = 165, ["B"] = 0 },
					new Dictionary<string, object> { ["R"] = 255, ["G"] = 255, ["B"] = 0 },
					new Dictionary<string, object> { ["R"] = 0, ["G"] = 255, ["B"] = 0 },
					new Dictionary<string, object> { ["R"] = 0, ["G"] = 255, ["B"] = 255 },
					new Dictionary<string, object> { ["R"] = 0, ["G"] = 0, ["B"] = 255 },
					new Dictionary<string, object> { ["R"] = 255, ["G"] = 0, ["B"] = 255 }
				} : new object[1]
				{
					new Dictionary<string, object>
					{
						["R"] = (int)singleColor.Value.R,
						["G"] = (int)singleColor.Value.G,
						["B"] = (int)singleColor.Value.B
					}
				});
			}
			Dictionary<string, object> payload = new Dictionary<string, object>
			{
				["function"] = "SetLightingLevel",
				["mode"] = "Lighting",
				["effect"] = effect,
				["light"] = light.ToString(),
				["speed"] = speed.ToString(),
				["direction"] = "None",
				["nv_save"] = "SAVE",
				["color"] = color,
				["MqttID"] = null,
				["backgroundcolor"] = 0,
				["alphabet"] = null,
				["solution"] = 0,
				["level"] = 0,
				["powerstatus"] = 0,
				["type"] = 0,
				["ManualIndex1"] = LightChannelSettings.DefaultManualIndexes[0].ToString(),
				["ManualIndex2"] = LightChannelSettings.DefaultManualIndexes[1].ToString(),
				["ManualIndex3"] = LightChannelSettings.DefaultManualIndexes[2].ToString(),
				["ManualIndex4"] = LightChannelSettings.DefaultManualIndexes[3].ToString(),
				["ManualIndex5"] = LightChannelSettings.DefaultManualIndexes[4].ToString(),
				["ManualIndex6"] = LightChannelSettings.DefaultManualIndexes[5].ToString(),
				["ManualInterval"] = (string.Equals(effect, "Manual", StringComparison.Ordinal)) ? speed.ToString() : "3",
				["BreathingIndex"] = ((string.Equals(effect, "Breathing", StringComparison.Ordinal) && KeyboardFirmwareEffects.IsSingleZoneRgb(_hw.Capabilities.KeyboardType)) ? (((LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var levelBreathingSettings) && levelBreathingSettings.BreathingIndex >= 1 && levelBreathingSettings.BreathingIndex <= 4) ? levelBreathingSettings.BreathingIndex : 1)) : 1),
				["DefaultData"] = "",
				["AuroraSwitch"] = false
			};
			await _hw.Publish(topic, payload);
			Logger.WriteLine($"SetLightingLevel({topic}, {effect}, light={light}, speed={speed}) 已发送");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SetLightingLevel fail: " + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// 2026-10-09：ColorBuffer 的「带 ID」构造。ID 是 GCU 认这一条 SetEffectALL 的必要字段 ——
	/// 缺 ID 时整条指令不被采用（灯效 / 颜色静默无效；电源走 SetPower、亮度走 SetLightingLevel，
	/// 属独立函数，所以一直正常）。原先这段只写在「40 系键盘」分支内，单分区键盘分支另抄了一份；
	/// 本轮把它抽成唯一入口，并给灯带 / Logo（HidLightbar*_/Ctrl）也用上。
	/// ⚠️ 键盘各分支的既有调用点与产物**一字未动**（同一个循环原样搬进来，键顺序 ID,R,G,B 不变）。
	/// </summary>
	private static object[] BuildColorBufferWithIds(object[]? source, int count)
	{
		object[] result = new object[count];
		for (int i = 0; i < count; i++)
		{
			int r = 255, g = 0, b = 0;
			if (source != null && i < source.Length && source[i] is Dictionary<string, object> item)
			{
				if (item.ContainsKey("R")) r = Convert.ToInt32(item["R"]);
				if (item.ContainsKey("G")) g = Convert.ToInt32(item["G"]);
				if (item.ContainsKey("B")) b = Convert.ToInt32(item["B"]);
			}
			result[i] = new Dictionary<string, object> { ["ID"] = i, ["R"] = r, ["G"] = g, ["B"] = b };
		}
		return result;
	}

	/// <summary>固件自带配色的灯效：不主动下发用户颜色。</summary>
	private static bool IsFirmwareColoredEffect(string effect)
	{
		return effect is "Rainbow" or "Aurora" or "Gaming";
	}

	/// <summary>2026-10-29：灯带/Logo 的厂商七色板 ColorBuffer（7 段，Rainbow 专用）。</summary>
	private static object[] BuildLightbarVendorRainbowBuffer()
	{
		int[] palette = LightbarColorPolicy.VendorRainbowPalette;
		object[] buffer = new object[palette.Length];
		for (int i = 0; i < palette.Length; i++)
		{
			int argb = palette[i];
			buffer[i] = new Dictionary<string, object>
			{
				["R"] = (argb >> 16) & 0xFF,
				["G"] = (argb >> 8) & 0xFF,
				["B"] = argb & 0xFF
			};
		}
		return buffer;
	}

	public async Task<bool> SetLightEffect(string topic, string effect, int light = 4, int speed = 1, string direction = "None", Color? singleColor = null, bool save = true)
	{
		try
		{
			if (!SupportsLightTopic(topic))
			{
				return false;
			}
			// ⭐⭐ 2026-10-16（宝龙达 · 灯光效果）：⭐ 把**颜色 ／ 亮度 ／ 效果**这条也分派给非 GCU 后端 ✅
			//    ⚠️ 三个调用点（⭐ SettingsForm ／ Program ／ LightForm ✅）都**直接**调本方法
			//    ⇒ 它原来**直发 MQTT** ⇒ 宝龙达上换颜色 ／ 调亮度发不出去 ⚠️
			//    ⭐ 非宝龙达 ⇒ 分派返回 false ⇒ ⭐ 下面原路**逐位不变** ✅
			if (_hw.TrySetLightEffectViaBackend(topic, effect, light,
				singleColor.HasValue ? singleColor.Value.ToArgb() : (int?)null, out bool _))
			{
				return true;
			}
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			// ⭐⭐ 2026-10-16（0.291.8.1 · 已校正）：`isCircular` **不能恒为 true**。
			// 厂商口径（LM_ITE_RGB.Set_ITE_Effect_Type_FwMode）：
			//   ColorBlocks>0 && isCircular  ⇒ b2=8（多段循环，逐段下发）
			//   ColorBlocks>0 && !isCircular ⇒ b2=1（**单色**，只取 ColorBuffer[0]）
			//   ColorBlocks==0               ⇒ b2=0（不发颜色，固件自带色）
			// ⇒ 我们要发"1 段用户色"时，**必须** isCircular=false，否则就是"1 段色却按多段循环发"的自相矛盾组合
			//   （真机实测的"呼吸变红 / 混合关灯"正是这类不匹配）。
			// ⚠️⚠️ 2026-10-16（owner 明确指示「键盘灯是正常的不用动」）：
			//   ⭐ 本方法**同时服务键盘**（`SetKeyboardEffect` 在 `KeyboardCtrlPublishSwitch` 关闭时会调它，
			//   而该开关**默认关闭** ⇒ 键盘效果实际走的就是这条路）。
			//   ⇒ ⭐ 下面这套新口径**只对非键盘主题生效**；⭐ `Keyboard/Ctrl` 一律**逐字保持原样**
			//   （`isCircular = true` + 无颜色时那份七彩）⇒ ⭐ 键盘行为零回归 ✅
			bool isKeyboardTopic = string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal);
			// 呼吸=多段循环；混色=多段彩色（需 7 段调色板），两者都按循环下发
			bool isBreathingEffect = string.Equals(effect, "Breathing", StringComparison.OrdinalIgnoreCase);
			bool isMixEffect = string.Equals(effect, "Mix", StringComparison.OrdinalIgnoreCase);
			bool isRainbowEffect = LightbarColorPolicy.UsesVendorRainbowPalette(effect);
			if (isKeyboardTopic)
			{
				dictionary["isCircular"] = true;
			}
			else if (LightCompatibilitySwitches.LightbarMixFixEnabled)
			{
				dictionary["isCircular"] = LightbarColorPolicy.IsCircular(topic, effect);
			}
			else
			{
				dictionary["isCircular"] = isKeyboardTopic || isBreathingEffect || isMixEffect;
			}
			if (string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal) && string.Equals(effect, "Manual", StringComparison.Ordinal))
			{
				int[] manualIndexes = LightChannelSettings.DefaultManualIndexes;
				if (LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var manualSettings) && manualSettings.ManualIndexes != null && manualSettings.ManualIndexes.Length >= 6)
				{
					manualIndexes = manualSettings.ManualIndexes;
				}
				dictionary["ColorBlocks"] = 6;
				// ⭐ 2026-10-16（厂商口径）：Manual 是**多段**（6 段）⇒ isCircular=true ⇒ 对应 b2=8 的"逐段下发"。
				dictionary["isCircular"] = true;
				object[] manualBuffer = new object[6];
				for (int i = 0; i < 6; i++)
				{
					int paletteIndex = Math.Clamp(manualIndexes[i], 1, 29) - 1;
					int paletteColor = LightChannelSettings.ManualPalette[paletteIndex];
					manualBuffer[i] = new Dictionary<string, object>
					{
						["R"] = (paletteColor >> 16) & 255,
						["G"] = (paletteColor >> 8) & 255,
						["B"] = paletteColor & 255
					};
				}
				dictionary["ColorBuffer"] = manualBuffer;
			}
			else
			{
				// ⭐⭐ 2026-10-16（0.291.8.1 · 已校正 · 见 docs\notes\GCU灯效协议-按校正.md）：
				// 厂商对**灯带/Logo**的颜色模式由 `isCircular` 决定，落到 HID 的颜色模式字节 b2：
				//   ColorBlocks==0                ⇒ b2=0（**不发颜色**，用固件自带色）
				//   ColorBlocks>0 && isCircular   ⇒ b2=8（逐段下发，多段循环）
				//   其余（有块数、非循环）        ⇒ b2=1（**只取 ColorBuffer[0]**，单色）
				// ⚠️ 旧实现在"没有颜色"时发了一份**硬编码七彩 7 段**（红/橙/黄/绿/青/蓝/品红）——
				//    那既不是用户颜色、也不是固件自带色，实测后果就是"呼吸变红、混合把 logo 关掉"❌。
				// ⇒ 现在（⭐ 仅非键盘主题）：有颜色 ⇒ 1 段（b2=1 语义，单色）✅；
				//    无颜色 ⇒ **ColorBlocks=0 + 空 ColorBuffer**（b2=0 语义：让固件用它自己的配色，
				//    对应 Rainbow/Aurora/Gaming 这类"固件自动变色"的灯效）✅
				// ⚠️ 键盘主题（Keyboard/Ctrl）**不走这里**的新口径，见上面 isKeyboardTopic 的说明 ✅
				if (isKeyboardTopic)
				{
					// ⭐ 键盘：逐字保持原样（有颜色 1 段 / 无颜色 7 段七彩）—— owner「键盘灯是正常的不用动」。
					dictionary["ColorBlocks"] = (singleColor.HasValue ? 1 : 7);
					dictionary["ColorBuffer"] = ((!singleColor.HasValue) ? new object[7]
					{
						new Dictionary<string, object> { ["R"] = 255, ["G"] = 0, ["B"] = 0 },
						new Dictionary<string, object> { ["R"] = 255, ["G"] = 165, ["B"] = 0 },
						new Dictionary<string, object> { ["R"] = 255, ["G"] = 255, ["B"] = 0 },
						new Dictionary<string, object> { ["R"] = 0, ["G"] = 255, ["B"] = 0 },
						new Dictionary<string, object> { ["R"] = 0, ["G"] = 255, ["B"] = 255 },
						new Dictionary<string, object> { ["R"] = 0, ["G"] = 0, ["B"] = 255 },
						new Dictionary<string, object> { ["R"] = 255, ["G"] = 0, ["B"] = 255 }
					} : new object[1]
					{
						new Dictionary<string, object>
						{
							["R"] = (int)singleColor.Value.R,
							["G"] = (int)singleColor.Value.G,
							["B"] = (int)singleColor.Value.B
						}
					});
				}
				else if (isRainbowEffect && LightCompatibilitySwitches.LightbarMixFixEnabled)
				{
					// 七彩兜底只给 Rainbow：厂商七色板 + isCircular（灯带通道）⇒ b2=8 逐段下发。
					dictionary["ColorBlocks"] = 7;
					dictionary["ColorBuffer"] = BuildLightbarVendorRainbowBuffer();
				}
				else if (isMixEffect && !LightCompatibilitySwitches.LightbarMixFixEnabled)
				{
					// 回退态：逐字回到旧实现（品红收尾的 7 段七彩）。
					dictionary["ColorBlocks"] = 7;
					dictionary["ColorBuffer"] = new object[7]
					{
						new Dictionary<string, object> { ["R"] = 255, ["G"] = 0, ["B"] = 0 },
						new Dictionary<string, object> { ["R"] = 255, ["G"] = 165, ["B"] = 0 },
						new Dictionary<string, object> { ["R"] = 255, ["G"] = 255, ["B"] = 0 },
						new Dictionary<string, object> { ["R"] = 0, ["G"] = 255, ["B"] = 0 },
						new Dictionary<string, object> { ["R"] = 0, ["G"] = 255, ["B"] = 255 },
						new Dictionary<string, object> { ["R"] = 0, ["G"] = 0, ["B"] = 255 },
						new Dictionary<string, object> { ["R"] = 255, ["G"] = 0, ["B"] = 255 }
					};
				}
				else if (singleColor.HasValue && !IsFirmwareColoredEffect(effect))
				{
					dictionary["ColorBlocks"] = 1;
					dictionary["ColorBuffer"] = new object[1]
					{
						new Dictionary<string, object>
						{
							["R"] = (int)singleColor.Value.R,
							["G"] = (int)singleColor.Value.G,
							["B"] = (int)singleColor.Value.B
						}
					};
				}
				else
				{
					dictionary["ColorBlocks"] = 0;
					dictionary["ColorBuffer"] = Array.Empty<object>();
				}
			}
			Dictionary<string, object> value = dictionary;
			bool isLogoLightbar = string.Equals(topic, "HidLightbar_Logo/Ctrl", StringComparison.Ordinal);
			bool isLightbarCtrl = string.Equals(topic, "HidLightbar/Ctrl", StringComparison.Ordinal);
			// 官方灯效协议先只用于 40 系同方（50 系保持原逻辑）
			bool applyOfficialLogo = (isLogoLightbar || isLightbarCtrl) && !_hw.UsesBaolongda && _hw.DgpuGeneration == DgpuGenerationKind.Gen40;
			if (applyOfficialLogo)
			{
				// Logo 通道按效果分流：单色=1 段，呼吸/彩虹=7 段循环，其余效果由固件配色
				int logoColorMode = LightCompatibilitySwitches.LightbarMixFixEnabled
					? LightbarColorPolicy.ResolveLogoColorMode(effect)
					: LightForm.LightEffectColorMode(effect);
				if (logoColorMode == 7)
				{
					dictionary["isCircular"] = true;
					dictionary["ColorBlocks"] = 7;
					int[] mixPalette = LightingSettingsStore.Load(topic, "ColorMarquee").MixColors ?? LightChannelSettings.DefaultMixColors;
					object[] mixBuffer = new object[7];
					for (int mi = 0; mi < 7; mi++)
					{
						int argbValue = (mi < mixPalette.Length) ? mixPalette[mi] : LightChannelSettings.DefaultMixColors[mi];
						mixBuffer[mi] = new Dictionary<string, object>
						{
							["R"] = (argbValue >> 16) & 0xFF,
							["G"] = (argbValue >> 8) & 0xFF,
							["B"] = argbValue & 0xFF
						};
					}
					dictionary["ColorBuffer"] = mixBuffer;
				}
				else if (logoColorMode == 1)
				{
					dictionary["isCircular"] = false;
					dictionary["ColorBlocks"] = 1;
					Color one = singleColor ?? Color.White;
					dictionary["ColorBuffer"] = new object[1]
					{
						new Dictionary<string, object>
						{
							["R"] = (int)one.R,
							["G"] = (int)one.G,
							["B"] = (int)one.B
						}
					};
				}
				else
				{
					dictionary["isCircular"] = LightCompatibilitySwitches.LightbarMixFixEnabled
						? LightbarColorPolicy.IsCircular(topic, effect)
						: true;
					dictionary["ColorBlocks"] = 0;
					dictionary["ColorBuffer"] = Array.Empty<object>();
				}
			}
			if (!string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal))
			{
				string paletteLog = "";
				if (applyOfficialLogo && dictionary["ColorBuffer"] is object[] paletteBuf && paletteBuf.Length > 0)
				{
					paletteLog = " palette=" + string.Join(",", paletteBuf.Take(7).Select(item =>
					{
						if (item is Dictionary<string, object> rgb)
						{
							return Convert.ToInt32(rgb["R"]).ToString("X2") + Convert.ToInt32(rgb["G"]).ToString("X2") + Convert.ToInt32(rgb["B"]).ToString("X2");
						}
						return "--";
					}));
				}
				string payloadLog = $"[light] 下发 topic={topic} effect={effect} light={light} speed={speed}"
					+ $" ColorBlocks={dictionary["ColorBlocks"]} isCircular={dictionary["isCircular"]}"
					+ $" singleColor={(singleColor.HasValue ? singleColor.Value.ToArgb().ToString("X6") : "null")}"
					+ paletteLog;
				if (Logger.TryMarkChanged("light-payload-" + topic, payloadLog))
				{
					Logger.WriteError(payloadLog);
				}
			}
			Dictionary<string, object> payload = new Dictionary<string, object>
			{
				["function"] = "SetEffectALL",
				["mode"] = "Lighting",
				["speed"] = speed.ToString(),
				["light"] = light.ToString(),
				["effect"] = effect,
				["direction"] = direction,
				// 官方控制台 Logo 通道发 "0"（不写 NVRAM）
				["nv_save"] = (applyOfficialLogo ? "0" : (save ? "SAVE" : "NOT_SAVE")),
				["color"] = value
			};
			if (string.Equals(topic, "Keyboard/Ctrl", StringComparison.Ordinal))
			{
				bool isGcu40Keyboard = IsGcu40Keyboard;
				bool isSingleZoneKeyboard = KeyboardFirmwareEffects.IsSingleZoneRgb(_hw.Capabilities.KeyboardType);
				Logger.WriteLine("SetLightEffect gcu40=" + isGcu40Keyboard + ", effect=" + effect);
				// 2026-10-04：单色颜色来源 —— 预设色板索引 1..29 直发索引；自定义色发 33，颜色走 ColorBuffer
				string monoIndexValue = "33";
				if (LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var monoPresetSettings)
					&& monoPresetSettings.UsePresetColor
					&& monoPresetSettings.MonochromeIndex >= 1
					&& monoPresetSettings.MonochromeIndex <= 29)
				{
					monoIndexValue = monoPresetSettings.MonochromeIndex.ToString();
				}
				if (isGcu40Keyboard)
				{
					payload["powerstatus"] = 1;
					payload["nv_save"] = null;
					payload["direction"] = null;
					payload["MonochromeIndex"] = monoIndexValue;
					value["ColorBlocks"] = 7;
					if (value["ColorBuffer"] is object[] cbOld)
					{
						object[] cb7 = new object[7];
						for (int ci = 0; ci < 7; ci++)
						{
							int r = 255, g = 0, b = 0;
							if (ci < cbOld.Length && cbOld[ci] is Dictionary<string, object> cd)
							{
								if (cd.ContainsKey("R")) r = Convert.ToInt32(cd["R"]);
								if (cd.ContainsKey("G")) g = Convert.ToInt32(cd["G"]);
								if (cd.ContainsKey("B")) b = Convert.ToInt32(cd["B"]);
							}
							cb7[ci] = new Dictionary<string, object> { ["ID"] = ci, ["R"] = r, ["G"] = g, ["B"] = b };
						}
						value["ColorBuffer"] = cb7;
					}
					Logger.WriteLine("gcu40 payload: " + System.Text.Json.JsonSerializer.Serialize(payload));
					}
				// 2026-10-03：单分区判断只认硬件能力（KeyboardType 上报 1/2）；GCU 版本（IsGcu40Keyboard）仅作指令协议分流（上方 40 系字段），
				// 40 系高端单键 RGB 机型（KeyboardType 非 1/2）走 per-key payload，不再被强制单分区
				// 2026-10-04：MonochromeIndex("33"=颜色走 ColorBuffer 自定义色) 与 ColorBuffer 每项的 ID
				// 是单分区 GCU 的公共要求，原先只写在 40 系分支内 → 30 系/其他单分区 GCU 掉进
				// 通用路径时这两个字段全缺，整条 SetEffectALL 不被采用（灯效/颜色无效；电源走
				// SetPower、亮度走 SetLightingLevel，属独立函数，故一直正常）。
				// 已用 tools\gcu_kbd_send.py 测试 B/E 实测验证（MonochromeIndex 状态 2→33→7）。
				// 2026-10-04 收窄：排除 40 系（上方分支已处理）、50 系（注册表标记），
				// 以及 GCU 自报 solution=ITE（键盘灯走 ITE/HID 而非 EC）的单分区机型 —— 这些保持原行为。
				// solution 未上报/为空时不排除，避免首个包因状态未到而丢失修复。
				//
				// 2026-10-09（50 系单分区键盘修复 a）：**放开 50 系排除**，让 50 系单分区复用本块
				// （MonochromeIndex + ColorBuffer 每项 ID），不再掉进缺这两个字段的通用 payload。
				// 来历：「!IsGcu50」是前任刻意加的兼容护栏 —— 修改日志.txt [2026-10-04 20:40] L440-444
				//   原文「收窄：新补的 30 系分支改为 if (isSingleZoneKeyboard && !isGcu40Keyboard && !IsGcu50)
				//   —— 排除 50 系（苍龙等单分区机型未实机验证，保持原行为）」；[2026-10-04 20:55] L476-480
				//   原文条件 `... && !IsGcu50 && !kbIteSolution`，取舍「保留 !IsGcu50 作双保险
				//   （LM 安装器装的 50 系有注册表标记，仍被排除）」。即：原因是"50 系单分区当时未实机验证"，
				//   并非已证实 50 系固件不吃这两个字段；因此本次按"可回退地放开"处理。
				// 影响面（严格限定）：仅 KeyboardType∈{1,2} 且 GcuVariant=="50" 且 solution≠ITE；
				//   30 系（GcuVariant=30/无标记）、40 系、通用路径的判据与产物一字未改。
				// 回退：把 EnableGcu50SingleZoneKeyboardFix 置 false，本块对 50 系重新关闭。
				bool isGcu50Keyboard = IsGcu50;
				bool kb50SingleZoneFixOn = isGcu50Keyboard && EnableGcu50SingleZoneKeyboardFix;
				bool kbIteSolution = string.Equals(_hw.KeyboardSolution, "ITE", StringComparison.OrdinalIgnoreCase);
				if (isSingleZoneKeyboard && !isGcu40Keyboard && (!isGcu50Keyboard || kb50SingleZoneFixOn) && !kbIteSolution)
				{
					payload["MonochromeIndex"] = monoIndexValue;
					bool isManualEffect = string.Equals(effect, "Manual", StringComparison.Ordinal);
					int szBlocks = (isManualEffect ? 6 : 7);
					value["ColorBlocks"] = szBlocks;
					if (value["ColorBuffer"] is object[] cbOldSz)
					{
						int r0 = 255, g0 = 0, b0 = 0;
						if (cbOldSz.Length > 0 && cbOldSz[0] is Dictionary<string, object> cd0)
						{
							if (cd0.ContainsKey("R")) r0 = Convert.ToInt32(cd0["R"]);
							if (cd0.ContainsKey("G")) g0 = Convert.ToInt32(cd0["G"]);
							if (cd0.ContainsKey("B")) b0 = Convert.ToInt32(cd0["B"]);
						}
						object[] cb7Sz = new object[szBlocks];
						for (int ci = 0; ci < szBlocks; ci++)
						{
							int r = r0, g = g0, b = b0;
							if (ci < cbOldSz.Length && cbOldSz[ci] is Dictionary<string, object> cdSz)
							{
								if (cdSz.ContainsKey("R")) r = Convert.ToInt32(cdSz["R"]);
								if (cdSz.ContainsKey("G")) g = Convert.ToInt32(cdSz["G"]);
								if (cdSz.ContainsKey("B")) b = Convert.ToInt32(cdSz["B"]);
							}
							cb7Sz[ci] = new Dictionary<string, object> { ["ID"] = ci, ["R"] = r, ["G"] = g, ["B"] = b };
						}
						value["ColorBuffer"] = cb7Sz;
					}
					Logger.WriteLine("single-zone payload(30/generic): " + System.Text.Json.JsonSerializer.Serialize(payload));
					// 2026-10-09（修复 a 的日志）：50 系单分区走本块时单独打一行 —— 便于 50 系真机
					// 回传日志核对「是否真的进了带 ID/MonochromeIndex 的构造」；非 50 系机不会出现此行。
					if (kb50SingleZoneFixOn)
					{
						Logger.WriteLine("single-zone payload(gcu50): " + System.Text.Json.JsonSerializer.Serialize(payload));
					}
				}
				if (isSingleZoneKeyboard)
				{
				payload["MqttID"] = null;
				payload["backgroundcolor"] = 0;
				payload["alphabet"] = null;
				payload["solution"] = 0;
				payload["level"] = 0;
				// 2026-10-09（50 系单分区键盘修复 b）：50 系 GCU 不响应单独的 Keyboard SetPower，
				// 故 50 系单分区把 powerstatus=1 **一并带进这条 SetEffectALL**，让"开效果顺带供电"
				// （对齐上方 40 系分支的 payload["powerstatus"] = 1 做法）。
				// 语义保留：nv_save(Save/NotSave) 与 direction 一律不动；非 50 系仍写 0（原语义不变）；
				// solution=ITE 的 50 系（键盘灯走 ITE/HID，不进上方分支）同样保持 0。
				// 回退：EnableGcu50SingleZoneKeyboardFix=false 或去掉括号内条件即回到 0。
				payload["powerstatus"] = (kb50SingleZoneFixOn && !kbIteSolution) ? 1 : 0;
				payload["type"] = 0;
				int[] currentIndexes = LightChannelSettings.DefaultManualIndexes;
				if (LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var indexSettings) && indexSettings.ManualIndexes != null && indexSettings.ManualIndexes.Length >= 6)
				{
					currentIndexes = indexSettings.ManualIndexes;
				}
				payload["ManualIndex1"] = currentIndexes[0].ToString();
				payload["ManualIndex2"] = currentIndexes[1].ToString();
				payload["ManualIndex3"] = currentIndexes[2].ToString();
				payload["ManualIndex4"] = currentIndexes[3].ToString();
				payload["ManualIndex5"] = currentIndexes[4].ToString();
				payload["ManualIndex6"] = currentIndexes[5].ToString();
				payload["ManualInterval"] = (string.Equals(effect, "Manual", StringComparison.Ordinal)) ? speed.ToString() : "10";
				int breathingIndexValue = 1;
				if (LightingSettingsStore.TryLoad("Keyboard/Ctrl", out var breathingSettings) && breathingSettings.BreathingIndex >= 1 && breathingSettings.BreathingIndex <= 4)
				{
					breathingIndexValue = breathingSettings.BreathingIndex;
				}
				payload["BreathingIndex"] = ((string.Equals(effect, "Breathing", StringComparison.Ordinal) && isSingleZoneKeyboard) ? breathingIndexValue.ToString() : "1");
				payload["DefaultData"] = "";
				payload["AuroraSwitch"] = false;
				}
				else
				{
					// 2026-10-09（① 通用 per-key 分支补 ID， 对齐）：作者 SetLightEffect 对
					// **所有主题、每一项**都构造 ["ID"]=i（顺序 ID,R,G,B，取值来源＝该项在 ColorBuffer 中的序号，
					// 0-based）。我们原先只有 40 系分支（上方，产物一字未动）与单分区分支带 ID，非 40 系的
					// per-key 键盘（单键 RGB 机型 HID 掉线后回落 MQTT）落到这里 ⇒ 无 ID ⇒ 按我们自己的诊断口径
					// 「无 ID ⇒ 整条 SetEffectALL 不生效」⇒ 灯效/颜色静默失效。
					// 这里复用 BuildColorBufferWithIds（同一实现，不另造一套）：count 取 ColorBlocks
					// （Single ⇒ 1、Manual ⇒ 6、否则 7），只重建 color.ColorBuffer/ColorBlocks，其它键不动。
					// 回退：LMECHREVO_PERKEY_IDFIX=0 ⇒ 本块不执行，逐字回到旧行为（无 ID 通用帧 + 只打下面那行日志）。
					if (LightCompatibilitySwitches.PerKeyColorBufferIdFixEnabled && !isGcu40Keyboard)
					{
						int perKeyBlocks = (value["ColorBlocks"] is int cbCount && cbCount > 0) ? cbCount : 7;
						value["ColorBlocks"] = perKeyBlocks;
						value["ColorBuffer"] = BuildColorBufferWithIds(value["ColorBuffer"] as object[], perKeyBlocks);
						Logger.WriteLine("per-key payload(with ID" + (KeyboardFirmwareEffects.IsFourZoneRgb(_hw.Capabilities.KeyboardType) ? ", four-zone" : "") + "): " + System.Text.Json.JsonSerializer.Serialize(payload));
					}
					Logger.WriteLine("SetLightEffect per-key clean payload: " + System.Text.Json.JsonSerializer.Serialize(payload));
				}
			}
			// 2026-10-09：灯带 / Logo 的 SetEffectALL 也补 ColorBuffer 的 ID。
			// 实测结论（diagnostics\30系键盘灯GCU协议_抓包分析.md：「无 ID ⇒ 整条不生效」）是
			// GCU 的通用要求，不是键盘专属：同一条灯带上"能开关、能调亮度、但效果/颜色选了不生效"
			// 正是缺 ID 的表现。⚠️ 只对下列灯条主题生效，键盘（Keyboard/Ctrl）各分支一字未动。
			// 2026-10-09（灯带 bisect 开关）：外面再套 LightbarIdFixEnabled（默认 true）。
			// ⚠️ 该开关为 false 时**整块不执行** ⇒ 这些主题一个字段都不改，仍发通用 7 段
			// 「无 ID」ColorBuffer 帧（键只有 R/G/B）＝与 18.6 逐字一致，且不再打 lightbar payload 行。
			// 2026-10-09（③ 扩展到铰链 / 同步， 核对）：作者 SetLightEffect 对这四个灯条主题
			// **一视同仁**都带 ID（没有主题白名单），且他的 SupportsLightTopic 认 hingelight/synclight；
			// 我们原先只补了灯带/Logo，且 SupportsLightTopic 直接拒收 Hinge/Sync ⇒ 这两行连报文都发不出去。
			// 现把补 ID 扩到 4 个主题；主题可达性见下方 SupportsLightTopic/LightTopicToQuickSwitchKey。
			if (LightCompatibilitySwitches.LightbarIdFixEnabled
				&& (string.Equals(topic, "HidLightbar/Ctrl", StringComparison.Ordinal)
					|| string.Equals(topic, "HidLightbar_Logo/Ctrl", StringComparison.Ordinal)
					|| string.Equals(topic, "HidLightbar_Hinge/Ctrl", StringComparison.Ordinal)
					|| string.Equals(topic, "HidLightbar_Sync/Ctrl", StringComparison.Ordinal)))
			{
				int lightbarBlocks = (value["ColorBlocks"] is int blocks && blocks > 0) ? blocks : 7;
				value["ColorBlocks"] = lightbarBlocks;
				value["ColorBuffer"] = BuildColorBufferWithIds(value["ColorBuffer"] as object[], lightbarBlocks);
				// ⭐ 2026-10-16（0.291.8.1）：把「这次到底带没带颜色」也写清楚 ——
				// 上一轮真机就是靠 payload 里 ColorBlocks:7 + 硬编码七彩 才看出"呼吸发红、混合关灯"的根因，
				// 补这一行后下次诊断包一眼可判：singleColor=null ⇒ 落进了多段（七彩）分支。
				Logger.WriteLine($"lightbar color decision({topic}): singleColor={(singleColor.HasValue ? $"{singleColor.Value.R},{singleColor.Value.G},{singleColor.Value.B}" : "null")} effect={effect} ColorBlocks={lightbarBlocks}");
				Logger.WriteLine("lightbar payload(" + topic + "): " + System.Text.Json.JsonSerializer.Serialize(payload));
			}
			await _hw.Publish(topic, payload);
			Logger.WriteLine($"SetLightEffect({topic}, {effect}, light={light}, speed={speed}, save={save}) 已发送");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SetLightEffect fail: " + ex.Message);
			return false;
		}
	}

	/// <summary>2026-10-22（无界白键盘）：GCU 不报键盘通道（Keyboard/Status solution=UNKNOWN）时，
	/// 键盘背光在 EC 0x78C（bit1=开关、bit4~7=亮度）⇒ 走 EC 直控；其它机型/暴风雪 RGB 版仍走原链路。</summary>
	private bool ShouldUseEcKeyboardLight()
	{
		if (_hw == null || !_hw.WujieSmuApu || !_hw.KeyboardStatusSeen)
		{
			return false;
		}
		return string.Equals(_hw.KeyboardSolution, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
	}

	public Task<bool> SetKeyboardPower(bool on)
	{
		if (ShouldUseEcKeyboardLight())
		{
			bool ok = EcKeyboardLight.TrySetPower(on);
			Logger.WriteError("[kbd-ec] 白键盘电源 on=" + on + " ⇒ " + (ok ? "已生效" : "失败"));
			return Task.FromResult(ok);
		}
		// ⭐⭐ 2026-10-17（真机反馈「logo 和键盘开关都无效」+ 用户澄清「宝龙达键盘灯其实关就是亮度 0，
		//   不存在真的开关」）：⭐ **宝龙达没有独立的"电源开/关"命令** ⚠️
		//   ⭐ 它的关灯手段就是**把亮度（功能号 18）写成 0** ✅（参考实现界面原文
		//      「背光亮度设置为 0 时将关闭键盘灯光」；斗战者也全仓没有独立关灯操作，值域 0~3）。
		//   ⚠️ 而原先本方法**没有任何宝龙达分派** ⇒ 永远落到 `SetLightPower("Keyboard/Ctrl")`（MQTT/GCU 通道）
		//      ⇒ ⭐ 宝龙达（无 GCU）**永远无效** ❌ —— 这就是「键盘开关点了没反应」的根因。
		//   ⇒ ⭐ 宝龙达下**转发到 `SetKeyboardEffect`**（它有分派、内部即 `TryApplyKeyboardViaBackend`）：
		//      ⭐ 开 ⇒ 用「单色 + 正常亮度」；⭐ 关 ⇒ 用「关（Off）+ 亮度 0」✅
		//      ⭐ 那里 `on = (level > 0) && !IsLightEffectOff(effect)` ⇒ 关时写亮度 0，与上面事实完全一致 ✅
		//   ⚠️ 非宝龙达 ⇒ `UsesBaolongda` 恒 false ⇒ 下面原来的两行**逐字不变** ✅
		if (_hw != null && _hw.UsesBaolongda)
		{
			// （机主：开关直接跟亮度走 + 关灯记忆上次亮度）
			bool baolongdaBrightnessModel = _hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen30
				|| _hw.DgpuGeneration == MechrevoLite.Gpu.DgpuGenerationKind.Gen40;
			int powerOnLight = baolongdaBrightnessModel ? KeyboardEcBrightness.GetLastLevel() : 4;
			return SetKeyboardEffect(on ? "Single" : "Off", on ? powerOnLight : 0);
		}
		// 2026-10-09（kbdctrl）：开关打开时开关灯也走新的 Keyboard/Ctrl 链路；关闭时逐字走原路径。
		if (KeyboardCtrlPublishSwitch.Enabled)
		{
			return PublishKeyboardCtrlPowerAsync(on);
		}
		return SetLightPower("Keyboard/Ctrl", on);
	}

	// ================================================================================================
	// 2026-10-09（kbdctrl）：Keyboard/Ctrl 键盘灯**直发**链路（新增；默认关闭，见 KeyboardCtrlPublishSwitch）。
	// 与老路径的区别只有两点：① 不再被 Keyboard/Status 的 solution/power 判定挡住；② payload 走
	// KeyboardCtrlPayload 的**无条件全字段**组装（与 2026-10-09 官方 30 系控制台抓包的首个 SetEffectALL 同构）。
	// ⛔ 灯带（HidLightbar/Ctrl 及 Logo/Hinge/Sync）与用户模式相关逻辑一行未动。
	// ================================================================================================

	/// <summary>
	/// 新的键盘灯效果下发入口：<c>Keyboard/Ctrl</c> + <c>SetEffectALL</c>。
	/// 开关关闭时**直接返回 false、不组包、不发帧**（保证关闭态与改前逐字一致）。
	/// </summary>
	public async Task<bool> PublishKeyboardCtrlEffectAsync(string effect, int light, int speed, Color? singleColor = null, int powerstatus = 0)
	{
		if (!KeyboardCtrlPublishSwitch.Enabled)
		{
			return false;
		}
		try
		{
			LightChannelSettings settings = LightingSettingsStore.Load(KeyboardCtrlPayload.Topic, "Single");
			int monoIndex = ResolveKeyboardCtrlMonochromeIndex(settings, singleColor.HasValue);
			// 2026-10-09（kbdctrl②）：段数由 KeyboardCtrlBlockCountSwitch 给（默认 official=7 段，
			// 与官方实测帧逐字节一致；只有显式设了 LMECHREVO_KB_BLOCKS=1 才注入 1 段做 A/B）。
			(int BlockCount, string Source) blocks = KeyboardCtrlBlockCountSwitch.Resolve();
			LogKeyboardCtrlBlockCount(blocks);
			int[] colorBuffer = singleColor.HasValue ? KeyboardCtrlPayload.SingleColorBuffer(singleColor.Value.ToArgb(), blocks.BlockCount) : null;
			Dictionary<string, object> payload = KeyboardCtrlPayload.BuildSetEffectAll(
				effect: effect,
				light: light,
				speed: speed,
				powerstatus: powerstatus,
				monochromeIndex: monoIndex,
				manualIndexes: settings.ManualIndexes,
				manualInterval: string.Equals(effect, "Manual", StringComparison.Ordinal) ? speed : 10,
				breathingIndex: settings.BreathingIndex,
				colorBufferArgb: colorBuffer,
				colorBlocks: blocks.BlockCount);
			await _hw.Publish(KeyboardCtrlPayload.Topic, payload).ConfigureAwait(continueOnCapturedContext: false);
			Logger.WriteError($"[kbd] Keyboard/Ctrl SetEffectALL effect={effect} light={light} speed={speed} mono={monoIndex} color={DescribeKeyboardCtrlColor(singleColor)} 结果=已发送 payload={KeyboardCtrlPayload.Serialize(payload)}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"[kbd] Keyboard/Ctrl SetEffectALL effect={effect} light={light} speed={speed} color={DescribeKeyboardCtrlColor(singleColor)} 结果=异常 err={ex.Message}");
			return false;
		}
	}

	/// <summary>开关灯：<c>Keyboard/Ctrl</c> + <c>SetPower</c>（<c>{"function":"SetPower","powerstatus":1|0}</c>）。</summary>
	public async Task<bool> PublishKeyboardCtrlPowerAsync(bool on)
	{
		if (!KeyboardCtrlPublishSwitch.Enabled)
		{
			return false;
		}
		try
		{
			Dictionary<string, object> payload = KeyboardCtrlPayload.BuildSetPower(on);
			await _hw.Publish(KeyboardCtrlPayload.Topic, payload).ConfigureAwait(continueOnCapturedContext: false);
			Logger.WriteError($"[kbd] Keyboard/Ctrl SetPower on={on} 结果=已发送 payload={KeyboardCtrlPayload.Serialize(payload)}");
			// 2026-10-21（无界键盘灯）：50 GCU 单分区只在 powerStatus=On 后才把效果写进 EC，
			// 冷启动 GCU 报 powerStatus=Off；开灯后补一次全字段 SetEffectALL，颜色/效果才会恢复。
			if (on && _hw != null && _hw.WujieSmuApu)
			{
				await Task.Delay(200).ConfigureAwait(continueOnCapturedContext: false);
				string fillEffect = "Single";
				int fillSpeed = 1;
				Color? fillColor = null;
				if (LightingSettingsStore.TryLoad(KeyboardCtrlPayload.Topic, out var fillSaved) && KeyboardFirmwareEffects.Contains(fillSaved.Effect))
				{
					fillEffect = fillSaved.Effect;
					fillSpeed = fillSaved.Speed;
					fillColor = ResolveKeyboardColor(fillEffect, fillSaved);
				}
				if (!IsWujieKeyboardEffect(fillEffect))
				{
					fillEffect = "Single";
				}
				int fillLight = KeyboardEcBrightness.GetLastLevel();
				if (fillLight <= 0)
				{
					fillLight = 4;
				}
				bool fillOk = await PublishKeyboardCtrlEffectAsync(fillEffect, fillLight, fillSpeed, fillColor, powerstatus: 0).ConfigureAwait(continueOnCapturedContext: false);
				Logger.WriteError($"[kbd] Keyboard/Ctrl 开灯后补发 SetEffectALL effect={fillEffect} light={fillLight} speed={fillSpeed} 结果={(fillOk ? "已发送" : "失败")}");
			}
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"[kbd] Keyboard/Ctrl SetPower on={on} 结果=异常 err={ex.Message}");
			return false;
		}
	}

	/// <summary>亮度：<c>Keyboard/Ctrl</c> + <c>SetLightingLevel</c>（沿用老路径的"保留当前灯效"口径来取 effect/颜色）。</summary>
	public async Task<bool> PublishKeyboardCtrlBrightnessAsync(int level0to4)
	{
		if (!KeyboardCtrlPublishSwitch.Enabled)
		{
			return false;
		}
		try
		{
			// 2026-10-21（无界键盘灯 · 50 GCU 协议）：50 GCU 单分区键盘的亮度入口是 SetEffectLight
			//（只读 mode + light，没有 SetLightingLevel）；无界改发它，其它机型保持原样。
			if (_hw != null && _hw.WujieSmuApu)
			{
				Dictionary<string, object> effectLightPayload = KeyboardCtrlPayload.BuildSetEffectLight(level0to4);
				await _hw.Publish(KeyboardCtrlPayload.Topic, effectLightPayload).ConfigureAwait(continueOnCapturedContext: false);
				Logger.WriteError($"[kbd] Keyboard/Ctrl SetEffectLight light={level0to4}（50 GCU 单分区亮度入口）结果=已发送 payload={KeyboardCtrlPayload.Serialize(effectLightPayload)}");
				return true;
			}
			int speed = 1;
			Color? singleColor = null;
			string effect;
			if (LightingSettingsStore.TryLoad(KeyboardCtrlPayload.Topic, out var settings) && KeyboardFirmwareEffects.Contains(settings.Effect))
			{
				effect = settings.Effect;
				speed = settings.Speed;
				// ⭐ 2026-10-17：⭐ 预设色板 → 真实 ARGB（与宝龙达 WMI 同口径）✅
				singleColor = ResolveKeyboardColor(effect, settings);
			}
			else
			{
				effect = (string.IsNullOrWhiteSpace(_hw.KeyboardEffect) ? "Single" : _hw.KeyboardEffect);
			}
			LightChannelSettings current = LightingSettingsStore.Load(KeyboardCtrlPayload.Topic, "Single");
			int monoIndex = ResolveKeyboardCtrlMonochromeIndex(current, singleColor.HasValue);
			(int BlockCount, string Source) blocks = KeyboardCtrlBlockCountSwitch.Resolve();
			LogKeyboardCtrlBlockCount(blocks);
			int[] colorBuffer = singleColor.HasValue ? KeyboardCtrlPayload.SingleColorBuffer(singleColor.Value.ToArgb(), blocks.BlockCount) : null;
			Dictionary<string, object> payload = KeyboardCtrlPayload.BuildSetLightingLevel(
				effect: effect,
				light: level0to4,
				speed: speed,
				monochromeIndex: monoIndex,
				manualIndexes: current.ManualIndexes,
				manualInterval: string.Equals(effect, "Manual", StringComparison.Ordinal) ? speed : 10,
				breathingIndex: current.BreathingIndex,
				colorBufferArgb: colorBuffer,
				colorBlocks: blocks.BlockCount);
			await _hw.Publish(KeyboardCtrlPayload.Topic, payload).ConfigureAwait(continueOnCapturedContext: false);
			Logger.WriteLine($"[kbd] Keyboard/Ctrl SetLightingLevel light={level0to4} effect={effect} mono={monoIndex} color={DescribeKeyboardCtrlColor(singleColor)} 结果=已发送 payload={KeyboardCtrlPayload.Serialize(payload)}");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"[kbd] Keyboard/Ctrl SetLightingLevel light={level0to4} 结果=异常 err={ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// 单色索引口径与老路径 <c>SetLightEffect("Keyboard/Ctrl")</c> 逐字一致：自定义色（ColorBuffer 带色）⇒ 33；
	/// 否则若 <c>keyboard.cfg</c> 里存的是 1..29 的预设色板索引，就用它。
	/// </summary>
	/// <summary>2026-10-21：无界 EC 单分区键盘只支持 Single / Breathing / Rainbow / Manual（其它效果映射过去无反应）。</summary>
	private static bool IsWujieKeyboardEffect(string effect)
	{
		return effect == "Single" || effect == "Breathing" || effect == "Rainbow" || effect == "Manual";
	}

	private static int ResolveKeyboardCtrlMonochromeIndex(LightChannelSettings settings, bool hasCustomColor)
	{
		if (hasCustomColor)
		{
			return KeyboardCtrlPayload.CustomColorMonochromeIndex;
		}
		if (settings.UsePresetColor && settings.MonochromeIndex >= 1 && settings.MonochromeIndex <= 29)
		{
			return settings.MonochromeIndex;
		}
		return KeyboardCtrlPayload.CustomColorMonochromeIndex;
	}

	/// <summary>
	/// 2026-10-09（kbdctrl②）：每次下发前把"实际按几段发、依据是什么"打一行，便于真机核对。
	/// 格式：<c>[kbd] Keyboard/Ctrl 分区数=7（来源=official（默认：未设 LMECHREVO_KB_BLOCKS））</c>。
	/// ⚠️ 这里的"分区数"是**帧里的 ColorBlocks 段数**，不是硬件分区数 —— 官方对单分区键盘也发 7 段。
	/// </summary>
	private static void LogKeyboardCtrlBlockCount((int BlockCount, string Source) blocks)
	{
		Logger.WriteLine($"[kbd] Keyboard/Ctrl 分区数={blocks.BlockCount}（来源={blocks.Source}）");
	}

	/// <summary>日志用：把颜色写成 <c>R,G,B</c>（无自定义色时写 <c>default</c>，不打任何私有信息）。</summary>
	private static string DescribeKeyboardCtrlColor(Color? singleColor)
	{
		if (!singleColor.HasValue)
		{
			return "default";
		}
		return $"{singleColor.Value.R},{singleColor.Value.G},{singleColor.Value.B}";
	}

	public async Task<bool> PublishLightPower(string topic, bool on)
	{
		// ⭐⭐ 2026-10-16（宝龙达 · 灯光电源开关 · ⭐ 机主反馈「键盘灯 logo灯不可用」）：
		//   ⚠️ 根因（⭐ 真机诊断包实证）：本方法**直发 MQTT** ⇒ 宝龙达**没有 GCU** ⇒
		//      日志刷「PublishLightPower fail: 发布到 Keyboard/Ctrl 失败：MQTT 未连接」❌
		//      ⇒ **完全没经过门面层** ⇒ 键盘灯/LOGO 灯都点不动 ⚠️
		//   ⇒ ⭐ 先问门面层「这次归你管吗」✅ ⇒ 非 GCU 后端处理完就**直接返回**（⭐ 一条 MQTT 不发 ✅）
		//   ⭐ 否则 ⭐ **下面原来那几行一行未改** ✅（⭐ 回归风险 = 0 ✅）
		if (_hw != null)
		{
			bool handled;
			bool confirmed;
			try
			{
				handled = _hw.TrySetLightPowerViaBackend(topic, on, out confirmed);
			}
			catch (Exception ex2)
			{
				// ⚠️ 分派本身异常 ⇒ ⭐ 如实记录，⛔ 但**不让它影响原路**（⭐ 继续往下走 ✅）
				Logger.WriteError("宝龙达 灯光：⚠️ 分派异常 ⇒ 交回原路：" + ex2.Message);
				handled = false;
				confirmed = false;
			}
			if (handled)
			{
				if (confirmed)
				{
					ObserveLightPower(topic, on);
				}
				Logger.WriteError($"PublishLightPower（⭐ 非 GCU 后端路径）：⭐ topic={topic} · ⭐ on={on}"
					+ $" · ⭐ 确认={confirmed} ⇒ ⭐ 未发任何 MQTT ✅");
				return confirmed;
			}
		}
		try
		{
			if (!SupportsLightTopic(topic))
			{
				return false;
			}
			await _hw.Publish(topic, new Dictionary<string, object>
			{
				["function"] = "SetPower",
				["powerstatus"] = (on ? 1 : 0)
			});
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PublishLightPower fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> ConfirmLightPower(string topic, bool on)
	{
		// ⭐⭐ 2026-10-16（宝龙达 · 灯光「关了又开」BUGFIX）：
		//   ⚠️ 根因（⭐ 机主诊断包实证）：下面这条确认路**只认 GCU/MQTT** —— 它靠"等
		//      `KeyboardPower`／`QuickSwitches[key]` 变成目标值"判定 ✅，等不到就发 `GETSTATUS` ❌。
		//      宝龙达下①`KeyboardPower` **从不更新**（⭐ 只在 GCU payload 解析里赋值 ⚠️）
		//      ②`QuickSwitches["logolight"]` 只每 3 s 刷一次（⭐ 180/500/700 ms 等不到 ✅）
		//      ⇒ 它**恒判"未确认"** ⇒ 发 MQTT ⇒ 日志刷「ConfirmLightPower fail: …MQTT 未连接」❌
		//      ⇒ 返回 **false** ⇒ ⭐ `SetLightPower` 返回 false
		//      ⇒ ⭐ **界面把开关拨回去 = 用户看到的「关了又开」** ✅
		//   ⇒ ⭐ 先问门面层「这次归你管吗」✅ ⇒ 非 GCU 后端**自己读硬件回读确认** ✅
		//      ⇒ ⭐ 处理完**直接返回**（⭐ 一条 MQTT 不发 ✅）；⭐ 否则**下面原来那几行一行未改** ✅
		//      （⭐ 非宝龙达恒 `false` ⇒ 逐位不变 ✅）
		if (_hw != null)
		{
			bool handled;
			bool backendConfirmed;
			try
			{
				handled = _hw.TryConfirmLightPowerViaBackend(topic, on, out backendConfirmed);
			}
			catch (Exception ex2)
			{
				// ⚠️ 分派本身异常 ⇒ ⭐ 如实记录，⛔ 但**不让它影响原路**（⭐ 继续往下走 ✅）
				Logger.WriteError("宝龙达 灯光：⚠️ 确认分派异常 ⇒ 交回原路：" + ex2.Message);
				handled = false;
				backendConfirmed = false;
			}
			if (handled)
			{
				Logger.WriteError($"ConfirmLightPower（⭐ 非 GCU 后端路径）：⭐ topic={topic} · ⭐ on={on}"
					+ $" · ⭐ 硬件回读确认={backendConfirmed} ⇒ ⭐ 未发任何 MQTT ✅");
				return backendConfirmed;
			}
		}
		string lightKey;
		try
		{
			if (!SupportsLightTopic(topic))
			{
				return false;
			}
			lightKey = LightTopicToQuickSwitchKey(topic);
			if (await _hw.WaitForStateAsync(IsConfirmed, TimeSpan.FromMilliseconds(180L)))
			{
				return true;
			}
			for (int attempt = 0; attempt < 3; attempt++)
			{
				await _hw.Publish(topic, new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
				if (await _hw.WaitForStateAsync(IsConfirmed, TimeSpan.FromMilliseconds((attempt == 0) ? 500 : 700)))
				{
					Logger.WriteLine($"SetLightPower({topic}, {on}) confirmed");
					return true;
				}
			}
			// 2026-10-22（耀世：开机灯带/Logo 亮一会儿自动灭）：GCU 不回报该主题 Status 时（statusSeen=n），
			// 确认路径永远等不到值 ⇒ 不要把它当失败（否则恢复被回滚）。命令已下发，按 best-effort 处理。
			bool noStatusChannel = topic.IndexOf("Logo", StringComparison.OrdinalIgnoreCase) >= 0
				? !_hw.LogoLightStatusSeen
				: !_hw.LightbarStatusSeen;
			if (noStatusChannel)
			{
				Logger.WriteLine($"SetLightPower({topic}, {on}) 无状态回读通道 ⇒ 按已下发处理（best-effort）");
				return true;
			}
			Logger.WriteLine($"SetLightPower({topic}, {on}) not confirmed");
			return false;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ConfirmLightPower fail: " + ex.Message);
			return false;
		}
		bool IsConfirmed()
		{
			if (!topic.StartsWith("Keyboard/", StringComparison.OrdinalIgnoreCase))
			{
				if (lightKey != null && _hw.QuickSwitches.TryGetValue(lightKey, out var value))
				{
					return value == on;
				}
				return false;
			}
			return _hw.KeyboardPower == on;
		}
	}

	public void ObserveLightPower(string topic, bool on)
	{
		Task.Run(() => ConfirmLightPower(topic, on));
	}

	public async Task<bool> SetLightPower(string topic, bool on)
	{
		if (!(await PublishLightPower(topic, on).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		return await ConfirmLightPower(topic, on).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<bool> IssueLightPower(string topic, bool on)
	{
		bool result = await PublishLightPower(topic, on).ConfigureAwait(continueOnCapturedContext: false);
		ObserveLightPower(topic, on);
		return result;
	}

	public async Task RequestLightStatus(string topic)
	{
		// ⭐⭐ 2026-10-16（宝龙达 · 灯光「关了又开」BUGFIX 的配套）：
		//   ⭐ 本主题由非 GCU 后端本地接管 ⇒ ⭐ 这里**没有任何 MQTT 可问** ⚠️ ⇒ 直接返回 ✅
		//   （⛔ 否则会刷一条注定失败的「RequestLightStatus fail: …MQTT 未连接」❌ ——
		//    ⭐ 而真实状态本来就能由本后端用功能号本地回读 ✅）
		if (_hw != null && _hw.HandlesLightTopicViaBackend(topic))
		{
			Logger.WriteError($"RequestLightStatus（⭐ 非 GCU 后端路径）：⭐ topic={topic} ⇒ ⭐ 跳过 MQTT ✅"
				+ "（⭐ 状态由本后端用功能号回读 ✅）");
			return;
		}
		try
		{
			await _hw.Publish(topic, new Dictionary<string, object> { ["Action"] = "GETSTATUS" }).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("RequestLightStatus fail: " + ex.Message);
		}
	}

	public async Task<bool> SwitchDeepSleep(bool on, int secs = 0)
	{
		try
		{
			bool flag = !_hw.DeepSleepSeen;
			if (!flag)
			{
				bool flag2 = secs != 0;
				if (flag2)
				{
					bool flag3;
					switch (secs)
					{
					case 900:
					case 1200:
					case 1500:
					case 1800:
						flag3 = true;
						break;
					default:
						flag3 = false;
						break;
					}
					flag2 = !flag3;
				}
				flag = flag2;
			}
			if (flag)
			{
				return false;
			}
			Dictionary<string, object> dictionary = new Dictionary<string, object> { ["Action"] = (on ? "DEEPSLEEP_ON" : "DEEPSLEEP_OFF") };
			if (on && secs > 0)
			{
				dictionary["Secs"] = secs.ToString();
			}
			await _hw.Publish("Setting/Control", dictionary);
			bool flag4 = await ConfirmSettingAsync(() => _hw.QuickSwitches.TryGetValue("deepsleep", out var value) && value == on && (!on || secs <= 0 || _hw.DeepSleepTime == secs));
			Logger.WriteLine($"SwitchDeepSleep({on}, secs={secs}) confirmed={flag4}");
			return flag4;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchDeepSleep fail: " + ex.Message);
			return false;
		}
	}

	public async Task<bool> SwitchTurboSubMode(bool silent, CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 3;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			MechrevoHw hw = _hw;
			// 2026-10-15（C-1 第 2 步分类 ①）：Silent/Turbo 那套是 GCU 上报的能力，下发也走 GCU ⇒ 真正需要 GCU。
		if (hw == null || !hw.IsGcuConnected || _hw.Capabilities.SilentTurboAvailability != FeatureAvailability.Supported)
			{
				return false;
			}
			if (ReadSilentTurboState() == silent)
			{
				if (ShouldApplyTurboGpuOverclockDefaultsOnSubMode(silent))
				{
					await ApplyTurboGpuOverclockDefaults().ConfigureAwait(continueOnCapturedContext: false);
				}
				return true;
			}
			string value = (silent ? "SET_CPU_CORE_OFFSET_SILENT" : "SET_CPU_CORE_OFFSET_EXTREME");
			await _hw.Publish("Fan/Control", new Dictionary<string, object>
			{
				["Action"] = value,
				[silent ? "SILENT" : "EXTREME"] = 1
			});
			for (int attempt = 0; attempt < 25; attempt++)
			{
				await Task.Delay(100, cancellationToken);
				if (ReadSilentTurboState() == silent)
				{
					Logger.WriteLine($"SwitchTurboSubMode(silent={silent}) confirmed");
					if (ShouldApplyTurboGpuOverclockDefaultsOnSubMode(silent))
					{
						await ApplyTurboGpuOverclockDefaults().ConfigureAwait(continueOnCapturedContext: false);
					}
					return true;
				}
			}
			Logger.WriteLine($"SwitchTurboSubMode(silent={silent}) not confirmed");
			return false;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("SwitchTurboSubMode fail: " + ex2.Message);
			return false;
		}
	}

	public async Task<bool> SwitchFanBoost(bool enable)
	{
		try
		{
			// 宝龙达没有 GCU 强冷能力位，走本机 EC 目标/掩码路径。
			if (_hw.UsesBaolongda)
			{
				(bool handled, bool confirmed) = await Task.Run(() =>
				{
					bool ok = _hw.TrySetFanBoostViaBackend(enable, out bool confirmedLocal);
					return (ok, confirmedLocal);
				}).ConfigureAwait(continueOnCapturedContext: false);
				Logger.WriteLine($"SwitchFanBoost({enable}) 宝龙达本机通道 ⇒ handled={handled} confirmed={confirmed}");
				return handled && confirmed;
			}
			// NUC/UMI 的强冷是本地 EC 0x751 bit6，不走 GCU。
			if (_hw.IsIntelNucMachine || _hw.IsUmiMachine)
			{
				bool ok = NucQuickSwitches.TrySetFanBoost(enable, out bool confirmed);
				Logger.WriteLine($"SwitchFanBoost({enable}) NUC/UMI EC ⇒ ok={ok} confirmed={confirmed}");
				return ok && confirmed;
			}
			// ⭐⭐ 无界（GCU 后端）恢复强冷入口：能力位刷新前也允许走 GCU 话题；GCU 未连接则如实失败 ✅
			if (!_hw.SupportsFanBoost && !_hw.WujieSmuApu)
			{
				return false;
			}
			await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = (enable ? "FAN_BOOST_ON" : "FAN_BOOST_OFF") });
			if (await _hw.WaitForStateAsync(() => _hw.FanBoost == enable, TimeSpan.FromMilliseconds(180L)))
			{
				return true;
			}
			for (int attempt = 0; attempt < 3; attempt++)
			{
				await _hw.Publish("Fan/Control", new Dictionary<string, object> { ["Action"] = "GETSTATUS" });
				if (await _hw.WaitForStateAsync(() => _hw.FanBoost == enable, TimeSpan.FromMilliseconds((attempt == 0) ? 500 : 700)))
				{
					return true;
				}
			}
			Logger.WriteLine($"SwitchFanBoost({enable}) not confirmed, actual={_hw.FanBoost}");
			return false;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("SwitchFanBoost fail: " + ex.Message);
			return false;
		}
	}

	public Task RefreshAll()
	{
		return _hw.RequestInitialStateAsync();
	}

	private bool SupportsLightTopic(string topic)
	{
		if (topic.StartsWith("Keyboard/", StringComparison.OrdinalIgnoreCase))
		{
			return _hw.SupportsKeyboard;
		}
		// 2026-10-09（③， 对齐）：作者对四个灯条主题分别判各自的 Supports*
		// 我们原先只认 logolight/lightbar ⇒ HidLightbar_Hinge/Ctrl 与 HidLightbar_Sync/Ctrl 被
		// 整条拒收（SetLightPower/SetLightEffect 都返回 false）＝这两行**开关和效果都发不出去**，
		// 不只是"能开关不能改效果"。现补上 hinge/sync 两个键与能力位。
		// 回退：删掉下面两行 switch 分支即回到旧行为（Hinge/Sync 再次被拒收）。
		return LightTopicToQuickSwitchKey(topic) switch
		{
			"logolight" => _hw.SupportsLogoLight,
			"lightbar" => _hw.SupportsLightbar,
			"hingelight" => _hw.SupportsHingeLight,
			"synclight" => _hw.SupportsSyncLight,
			_ => false
		};
	}

	internal static string? LightTopicToQuickSwitchKey(string topic)
	{
		if (topic.Contains("Logo", StringComparison.OrdinalIgnoreCase))
		{
			return "logolight";
		}
		// 2026-10-09（③， LightTopicToQuickSwitchKey 的顺序：Logo → Hinge → _Sync → HidLightbar
		if (topic.Contains("Hinge", StringComparison.OrdinalIgnoreCase))
		{
			return "hingelight";
		}
		if (topic.Contains("_Sync", StringComparison.OrdinalIgnoreCase))
		{
			return "synclight";
		}
		if (topic.StartsWith("HidLightbar/", StringComparison.OrdinalIgnoreCase))
		{
			return "lightbar";
		}
		return null;
	}
}
