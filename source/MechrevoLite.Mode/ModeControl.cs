using System;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Forms;
using MechrevoLite.Gpu.NVidia;
using MechrevoLite.Hardware;
using MechrevoLite.Helpers;
using MechrevoLite.Properties;
using PawnIO;

namespace MechrevoLite.Mode;

public class ModeControl : IDisposable
{
	private static SettingsForm settings = Program.settingsForm;

	private static bool customFans = false;

	private static int customPower = 0;

	private int _cpuUV;

	private int _igpuUV;

	private int _cpuTemp = CpuInfo.DefaultTemp;

	private bool _ryzenPower;

	private static RyzenSmuService? _smu;

	private static readonly object _smuLock = new object();

	private static System.Timers.Timer? reapplyTimer;

	private static System.Timers.Timer modeToggleTimer = null;

	private static CancellationTokenSource? _modeCts = new CancellationTokenSource();

	private static Task _modeTask = Task.CompletedTask;

	private static RyzenSmuService? GetSmu()
	{
		lock (_smuLock)
		{
			if (_smu != null && _smu.IsInitialized)
			{
				return _smu;
			}
			_smu?.Dispose();
			_smu = new RyzenSmuService();
			if (!_smu.Initialize(Assembly.GetExecutingAssembly()))
			{
				_smu.Dispose();
				_smu = null;
			}
			else
			{
				Logger.WriteLine($"SMU Init: {_smu.CpuCodeName} ({_smu.Family}), SMU v{_smu.SmuVersion >> 16}.{(_smu.SmuVersion >> 8) & 0xFF}.{_smu.SmuVersion & 0xFF}");
			}
			return _smu;
		}
	}

	public static bool IsPawnInstalled()
	{
		return RyzenSmuService.IsPawnInstalled();
	}
	// 同一进程里**多个实例同时存在**是没必要的风险面（模块/句柄归属、以及 Global\Access_PCI
	internal static RyzenSmuService SharedSmu => GetSmu();

	public ModeControl()
	{
		int num = AppConfig.Get("reapply_time");
		if (num < 0)
		{
			num = ((AppConfig.IsApplyUV() && IsReapplyTempRequired()) ? 30 : 0);
		}
		if (num > 0)
		{
			reapplyTimer = new System.Timers.Timer(num * 1000);
			reapplyTimer.Elapsed += ReapplyTimer_Elapsed;
		}
	}

	private static bool IsReapplyTempRequired()
	{
		RyzenSmuService smu = GetSmu();
		bool flag = smu != null;
		if (flag)
		{
			CpuFamily family = smu.Family;
			// 2026-10-10 fmax-family-guard: 白名单（RyzenSmuService.LegacyFamilyCommandsApproved）判据
			// 与 SetStapm/SetFast/SetSlow/SetThm 那道闸门同一把尺子。取 Rome/Naples/Genoa/... 这些
			// "家族刚变已知"的代号时：改前 GetSmu() 返回 null -> 这里恒为 false；加判据后与改前逐字一致。
			bool flag2 = ((family == CpuFamily.Renoir || family == CpuFamily.Mobile) ? true : false) && RyzenSmuService.LegacyFamilyCommandsApproved(smu.CpuCodeName);
			flag = flag2;
		}
		return flag;
	}

	private static bool IsReapplyRyzenRequired()
	{
		RyzenSmuService smu = GetSmu();
		if (smu != null)
		{
			// 2026-10-10 fmax-family-guard: 同上 —— Raphael 家族里只有改前就已知（白名单内）的代号
			// 才走"5 秒后再重放一次"；Genoa/StormPeak/Bergamo/Turin/TurinD 保留改前的 false。
			return smu.Family == CpuFamily.Raphael && RyzenSmuService.LegacyFamilyCommandsApproved(smu.CpuCodeName);
		}
		return false;
	}

	private static void SetReapplyEnabled(bool enabled)
	{
		if (reapplyTimer != null)
		{
			reapplyTimer.Enabled = enabled;
		}
	}

	private void ReapplyTimer_Elapsed(object? sender, ElapsedEventArgs e)
	{
		SetCPUTemp(AppConfig.GetMode("cpu_temp"));
		SetRyzenPower();
	}

	public void WaitForApply()
	{
		try
		{
			_modeTask.Wait(5000);
		}
		catch
		{
		}
	}

	// 2026-10-09（修「切自定义档被弹回静音/平衡」，档位路径）：触发源＝开机 / 唤醒 / 电源变化
	// （Program.SetAutoModes、OnPowerSettled、GCU 连接恢复的重放）与显卡模式切换（GPUModeControl）。
	// 老引擎这条路上有两处会把**用户的自定义档**顶掉：
	//   ① `Modes.Exists(3)` 在 perf_customs（新引擎档位）时代恒为 false —— 没有 `mode_base_3`
	//      ⇒ `SetPerformanceMode(3)` 会被 `if (!Modes.Exists(mode)) mode = 0;` **静默降级成 0（平衡）**，
	//      硬件真的被切到 Gaming，档位键也被改写成 0；
	//   ② `ShouldPreserveCustomMode` 要求 `connected`，GCU 一时未连接时即便用户/硬件在自定义档也不保留，
	//      于是去读「按电源来源的老档位键」`performance_0/1`（拔电那条常是 2=静音）。
	// 修法：把「用户当前是不是自定义档」的判据扩到**新引擎档位**（+老键意图 +硬件回读），是就一律走保留分支
	// （按该档固件槽位重放一次），绝不交给老引擎降级；并打一行决策日志，一眼看出"这次是谁把档位判成什么"。
	public void AutoPerformance(bool powerChanged = false, bool preserveActiveCustom = false)
	{
		int current = Modes.GetCurrent();
		int hardwareMode = Program.service?.CurrentMode ?? (-1);
		bool connected = Program.hw?.IsConnected ?? false;
		int storedMode = AppConfig.Get("performance_" + Program.PerformanceKey());
		PerfModeService perf = PerfModeService.Instance;
		bool customActive = IsCustomModeActive(perf, storedMode, current, hardwareMode, connected);
		bool preserve = customActive || ShouldPreserveCustomMode(powerChanged, connected, current, hardwareMode, preserveActiveCustom);
		Logger.WriteLine($"AutoPerformance: powerChanged={powerChanged} preserveArg={preserveActiveCustom} connected={connected} perfActive={perf?.ActiveModeId ?? "-"} perfDisplayed={perf?.DisplayedModeId ?? "-"} oldKey={current} storedKey={storedMode} hwMode={hardwareMode} customActive={customActive} -> {(preserve ? "保留自定义档" : "下发档位键")}");
		if (preserve)
		{
			int num2 = ResolveCustomProfileIndex(perf);
			string value = (powerChanged ? "power source changed" : "custom mode preserved on init/wake");
			Logger.WriteLine($"Reapplying custom profile {num2 + 1} ({value}). [source={(customActive ? "新引擎/老键意图/硬件回读判定为自定义档" : "ShouldPreserveCustomMode")} slot={num2}]");
			ReapplyCustomProfileAsync(num2);
		}
		else
		{
			int num3 = storedMode;
			int target = ((num3 == -1) ? current : num3);
			// 老引擎表示不了**自定义档**（perf_customs 时代没有 mode_base_3）⇒ 绝不交给 SetPerformanceMode
			// （`if (!Modes.Exists(mode)) mode = 0;` 会把它静默降级成 0=平衡）。
			// ⚠️ 只拦"自定义档"这一种；其余"表示不了"的值（例如全新配置的 -1）一律保持原行为，不动。
			if (target == 3 && HasCustomModes(perf))
			{
				int num4 = ResolveCustomProfileIndex(perf);
				Logger.WriteLine($"AutoPerformance: 目标档 3（自定义档）老引擎表示不了（Modes.Exists(3)={Modes.Exists(3)}）⇒ 改按新引擎自定义档重放 slot={num4}，避免静默降级成平衡");
				ReapplyCustomProfileAsync(num4);
				return;
			}
			Logger.WriteLine($"{Program.currentSource} Performance Mode: {Modes.GetName(target)} [oldKey={current} storedKey={num3} hwMode={hardwareMode} connected={connected}]");
			if (num3 != -1)
			{
				SetPerformanceMode(num3, powerChanged);
			}
			else
			{
				SetPerformanceMode(current);
			}
		}
	}

	/// <summary>
	/// 2026-10-09（修「切自定义档被弹回静音/平衡」）：当前是不是「自定义档」。三路证据取或：
	/// ① 新引擎（PerfModeService / perf_* 键）的当前档位是自定义档 —— 编辑器与主界面两个入口都会同步它；
	/// ② 老引擎的档位键是 3（自定义档的意图；新引擎时代老引擎表示不了它，见 AutoPerformance 注释）；
	/// ③ 硬件回读的档位是 3（GCU 在线时）。
	/// ⚠️ 只读，不改任何档位/配置；判为自定义档后的动作只是"按该档槽位重放一次"，由用户在编辑器里的选择决定目标。
	/// </summary>
	private static bool IsCustomModeActive(PerfModeService? perf, int storedMode, int current, int hardwareMode, bool connected)
	{
		PerfModeDefinition mode = perf?.Active;
		if ((object)mode != null && mode.IsCustom)
		{
			return true;
		}
		if ((storedMode == 3 || current == 3) && HasCustomModes(perf))
		{
			return true;
		}
		return connected && hardwareMode == 3;
	}

	private static bool HasCustomModes(PerfModeService? perf)
	{
		if (perf == null)
		{
			return false;
		}
		foreach (PerfModeDefinition mode in perf.Modes)
		{
			if (mode.IsCustom)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 2026-10-09：自定义档的固件槽位（=配置号）。优先用新引擎的档位归属（与 B1 修法同一口径，
	/// 见 PerfModeService.ApplySlotPowerProfile），新引擎没给出时退回原来的 hw.CustomProfileIndex / custom_last_profile。
	/// </summary>
	private static int ResolveCustomProfileIndex(PerfModeService? perf)
	{
		PerfModeDefinition mode = perf?.Active;
		if ((object)mode != null && mode.IsCustom)
		{
			int? slot = perf.SlotOf(mode.Id);
			if (slot.HasValue)
			{
				return slot.Value;
			}
		}
		int num = Program.hw?.CustomProfileIndex ?? (-1);
		return ((num >= 0 && num <= 3) ? num : Math.Clamp(AppConfig.Get("custom_last_profile", 0), 0, 3));
	}

	internal static bool ShouldPreserveCustomMode(bool powerChanged, bool connected, int selectedMode, int hardwareMode, bool preserveActiveCustom = false)
	{
		if ((powerChanged | preserveActiveCustom) & connected)
		{
			if (selectedMode != 3)
			{
				return hardwareMode == 3;
			}
			return true;
		}
		return false;
	}

	private static async Task ReapplyCustomProfileAsync(int profile)
	{
		try
		{
			// 2026-10-21（机主：无界根本没有原生自定义档，切自定义不应下发 GCU 命令）：
			// Wujie 的自定义档完全走本机 SMU ⇒ 这里只重放本机配置，⛔ 不发 SwitchCustomProfile（GCU/MQTT）。
			if (Program.hw?.WujieSmuApu == true)
			{
				PerfModeService perf = PerfModeService.Instance;
				string activeId = perf?.ActiveModeId;
				PerfModeDefinition activeMode = (perf != null && !string.IsNullOrEmpty(activeId)) ? perf.Find(activeId) : null;
				if (activeMode != null && activeMode.IsCustom)
				{
					Logger.WriteError("[perfmode] 无界 APU：自定义档重放走本机 SMU（跳过 GCU SwitchCustomProfile）· mode=" + activeMode.Id);
					_ = perf.ActivateAsync(activeMode.Id, "wujie auto replay", forceParameters: true);
				}
				else
				{
					Logger.WriteLine("[perfmode] 无界 APU：无自定义档激活 ⇒ 跳过 GCU SwitchCustomProfile");
				}
				return;
			}
			MechrevoService service = Program.service;
			if (service != null)
			{
				// 2026-10-09（取证）：这一行把「谁在重放、重放前后新老档位各是什么、GCU 在不在线」一次写全，
				// 与 AutoPerformance 的决策行、PerfModes 的 activate/未切换行连起来就能看出"被谁切回"。
				string trace = $"[perf={PerfModeService.Instance?.ActiveModeId ?? "-"} oldKey={Modes.GetCurrent()} hwMode={Program.hw?.OperatingMode ?? (-1)} connected={Program.hw?.IsConnected ?? false}]";
				if (await service.SwitchCustomProfile(profile))
				{
					Logger.WriteLine((WinPowerPlan.ApplyProfile(profile) ? $"Custom profile {profile + 1} reapplied." : $"Custom profile {profile + 1} reapplied, but Windows power settings were not confirmed.") + " " + trace);
				}
				else
				{
					Logger.WriteLine($"Custom profile {profile + 1} was not confirmed by the service. " + trace);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Custom profile power transition failed: " + ex.Message);
		}
	}

	public void Toast()
	{
		Program.toast.RunToast(Modes.GetCurrentName(), (SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online) ? ToastIcon.Charger : ToastIcon.Battery);
	}

	internal static void NotifySwitchOutcome(bool success, bool cancelled)
	{
		if (!(success | cancelled))
		{
			ToastForm.ShowFailure(Strings.PerfSwitchFailed);
		}
	}

	internal static void ReportApplyFault(Exception ex, int mode)
	{
		if (ex is OperationCanceledException)
		{
			Logger.WriteLine($"SetPerformanceMode cancelled (mode {mode})");
			NotifySwitchOutcome(success: false, cancelled: true);
		}
		else
		{
			Logger.WriteLine($"SetPerformanceMode failed (mode {mode}): {ex.Message}");
			NotifySwitchOutcome(success: false, cancelled: false);
		}
	}

	public void SetPerformanceMode(int mode = -1, bool notify = false)
	{
		int current = Modes.GetCurrent();
		if (mode < 0)
		{
			mode = current;
		}
		if (!Modes.Exists(mode))
		{
			mode = 0;
		}
		Logger.WriteLine($"SetPerformanceMode(mode={mode} old={current} exists={Modes.Exists(mode)})");
		settings.ShowMode(mode);
		Modes.SetCurrent(mode);
		CancellationTokenSource nextCts = new CancellationTokenSource();
		CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(ref _modeCts, nextCts);
		cancellationTokenSource?.Cancel();
		if (_modeTask.IsCompleted)
		{
			cancellationTokenSource?.Dispose();
		}
		CancellationToken ct = nextCts.Token;
		_modeTask = Task.Run(async () =>
		{
			_ = 3;
			try
			{
				customFans = false;
				customPower = 0;
				SetModeLabel();
				ct.ThrowIfCancellationRequested();
				await Program.acpi.SetPerformanceMode(AppConfig.IsManualModeRequired() ? 4 : Modes.GetBase(mode));
				ct.ThrowIfCancellationRequested();
				SetGPUClocks();
				await Task.Delay(TimeSpan.FromMilliseconds(100L), ct);
				ct.ThrowIfCancellationRequested();
				AutoFans();
				MechrevoHw hw = Program.hw;
				await Task.Delay(TimeSpan.FromMilliseconds((hw != null && hw.IsConnected) ? 50 : 250), ct);
				ct.ThrowIfCancellationRequested();
				await AutoPower(launchAsAdmin: false, ct);
				string modeString = AppConfig.GetModeString("mode_command");
				if (modeString != null)
				{
					Logger.WriteLine("Running mode command: " + modeString);
					RestrictedProcessHelper.RunAsRestrictedUser(modeString);
				}
			}
			catch (OperationCanceledException ex)
			{
				ReportApplyFault(ex, mode);
			}
			catch (Exception ex2)
			{
				ReportApplyFault(ex2, mode);
			}
			finally
			{
				if (Volatile.Read(in _modeCts) != nextCts)
				{
					nextCts.Dispose();
				}
			}
		});
		if (notify)
		{
			Toast();
		}
		if (!AppConfig.Is("skip_powermode"))
		{
			if (AppConfig.GetModeString("powermode") != null)
			{
				PowerNative.SetPowerMode(AppConfig.GetModeString("powermode"));
			}
			else
			{
				PowerNative.SetPowerMode(Modes.GetBase(mode));
			}
			if (AppConfig.IsAutoASPM())
			{
				PowerNative.SetBalancedASPM();
			}
		}
		if (AppConfig.GetMode("auto_boost") != -1)
		{
			PowerNative.SetCPUBoost(AppConfig.GetMode("auto_boost"));
		}
	}

	private void ModeToggleTimer_Elapsed(object? sender, ElapsedEventArgs e)
	{
		modeToggleTimer.Stop();
		Logger.WriteLine($"Hotkey mode: {Modes.GetCurrent()}");
		SetPerformanceMode();
	}

	public void AutoFans(bool force = false)
	{
		customFans = false;
		if (AppConfig.IsApplyFans() | force)
		{
			MechrevoHw hw = Program.hw;
			if (hw != null && hw.IsConnected)
			{
				customFans = true;
				return;
			}
		}
		SetModeLabel();
	}

	public async Task AutoPower(bool launchAsAdmin = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		customPower = 0;
		bool applyPower = AppConfig.IsApplyPower();
		bool flag = AppConfig.IsApplyFans();
		MechrevoHw hw;
		if (applyPower && !flag && AppConfig.IsFanRequired())
		{
			AutoFans(force: true);
			hw = Program.hw;
			if (hw == null || !hw.IsConnected)
			{
				await Task.Delay(500, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		if (applyPower)
		{
			await SetPower(launchAsAdmin, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		hw = Program.hw;
		if (hw == null || !hw.IsConnected)
		{
			await Task.Delay(500, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		cancellationToken.ThrowIfCancellationRequested();
		AutoRyzen();
		if (IsReapplyRyzenRequired())
		{
			ReapplyRyzenAfterDelayAsync(cancellationToken);
		}
	}

	private async Task ReapplyRyzenAfterDelayAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(5000, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			AutoRyzen();
			ReadRyzenLimits();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Delayed Ryzen settings reapply failed: " + ex2.Message);
		}
	}

	public static void SyncExternalModeStatic(int mode)
	{
		Modes.SetCurrent(mode);
		AppConfig.Set("performance_mode", mode);
	}

	public void SetModeLabel()
	{
		settings.SetModeLabel(Strings.PerformanceMode + ": " + Modes.GetCurrentName() + (customFans ? "+" : "") + ((customPower > 0) ? (" " + customPower + "W") : ""));
	}

	public void SetRyzenPower(bool init = false)
	{
		if (init)
		{
			_ryzenPower = true;
		}
		if (!_ryzenPower || !AppConfig.IsApplyPower())
		{
			return;
		}
		RyzenSmuService smu = GetSmu();
		if (smu == null)
		{
			return;
		}
		int mode = AppConfig.GetMode("limit_total");
		int mode2 = AppConfig.GetMode("limit_slow", mode);
		int mode3 = AppConfig.GetMode("limit_fast", mode2);
		if (mode <= 210 && mode >= 5)
		{
			smu.SetAllLimits(mode, mode3, mode2, out var stapm, out var fast, out var slow);
			if (init)
			{
				Logger.WriteLine($"STAPM: {mode}W {stapm} | SLOW: {mode2}W {slow} | FAST: {mode3}W {fast}");
			}
		}
	}

	public async Task SetPower(bool launchAsAdmin = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		bool flag = Program.acpi.IsAllAmdPPT();
		bool isAMD = CpuInfo.IsAMD;
		int limit_total = AppConfig.GetMode("limit_total");
		int mode = AppConfig.GetMode("limit_cpu");
		int num = AppConfig.GetMode("limit_slow");
		int mode2 = AppConfig.GetMode("limit_fast");
		if ((num < 0) | flag)
		{
			num = limit_total;
		}
		if (limit_total > 210 || limit_total < 5 || mode > 210 || mode < 5 || mode2 > 210 || mode2 < 5 || num > 210 || num < 5)
		{
			return;
		}
		MechrevoHw hw = Program.hw;
		if (hw != null && hw.IsConnected)
		{
			int pl = ((num >= 0) ? num : limit_total);
			int pl2 = ((mode2 >= 0) ? mode2 : limit_total);
			cancellationToken.ThrowIfCancellationRequested();
			await ApplyMechrevoPowerLimitsAsync(pl, pl2).ConfigureAwait(continueOnCapturedContext: false);
			customPower = limit_total;
			SetModeLabel();
			return;
		}
		if (isAMD)
		{
			if (ProcessHelper.IsUserAdministrator())
			{
				SetRyzenPower(init: true);
			}
			else if (launchAsAdmin)
			{
				ProcessHelper.RunAsAdmin("cpu");
				return;
			}
		}
		if (flag)
		{
			customPower = mode;
		}
		SetModeLabel();
	}

	public void SetGPUClocks(bool launchAsAdmin = false, bool reset = false)
	{
		Task.Run(() =>
		{
			int num = AppConfig.GetMode("gpu_core");
			int num2 = AppConfig.GetMode("gpu_memory");
			int num3 = AppConfig.GetMode("gpu_clock_limit");
			if (reset)
			{
				num = (num2 = (num3 = 0));
			}
			if (num != -1 || num2 != -1 || num3 != -1)
			{
				if (Program.acpi.DeviceGet(0u) == 1)
				{
					Logger.WriteLine("Clocks: Eco");
				}
				else if (HardwareControl.GpuControl == null)
				{
					Logger.WriteLine("Clocks: NoGPUControl");
				}
				else
				{
					if (HardwareControl.GpuControl.IsNvidia)
					{
						NvidiaGpuControl nvidiaGpuControl = (NvidiaGpuControl)HardwareControl.GpuControl;
						try
						{
							int num4 = nvidiaGpuControl.SetClocks(num, num2);
							if ((nvidiaGpuControl.SetMaxGPUClock(num3) != 0 || num4 != 0) & launchAsAdmin)
							{
								ProcessHelper.RunAsAdmin("gpu");
							}
							return;
						}
						catch (Exception ex)
						{
							Logger.WriteLine("Clocks Error:" + ex.ToString());
							return;
						}
					}
					Logger.WriteLine("Clocks: NotNvidia");
				}
			}
		});
	}

	public SmuStatus? SetCPUTemp(int cpuTemp, bool log = false)
	{
		if (cpuTemp < CpuInfo.MinTemp || cpuTemp > CpuInfo.DefaultTemp)
		{
			return null;
		}
		if (cpuTemp == CpuInfo.DefaultTemp && _cpuTemp == CpuInfo.DefaultTemp)
		{
			return null;
		}
		RyzenSmuService smu = GetSmu();
		if (smu == null)
		{
			return null;
		}
		SmuStatus smuStatus = smu.SetThm(cpuTemp);
		if (log)
		{
			Logger.WriteLine($"CPU Temp: {cpuTemp}°C {smuStatus}");
		}
		if (smuStatus == SmuStatus.OK)
		{
			_cpuTemp = cpuTemp;
		}
		return smuStatus;
	}

	public void SetUV(int cpuUV)
	{
		// 2026-10-05 step7A (upstream 0.290.6 C-11): serialise against the manual CPU tuning
		// window, and do not overwrite a Curve Optimizer value the user set there by hand.
		lock (RuntimeCpuTuning.SyncRoot)
		{
			if (RuntimeCpuTuning.OwnsCurve || !CpuInfo.IsSupportedUV() || cpuUV < CpuInfo.MinCPUUV || cpuUV > CpuInfo.MaxCPUUV)
			{
				return;
			}
			RyzenSmuService smu = GetSmu();
			if (smu != null)
			{
				SmuStatus smuStatus = smu.SetCoAll(cpuUV);
				Logger.WriteLine($"UV: {cpuUV} {smuStatus}");
				if (smuStatus == SmuStatus.OK)
				{
					_cpuUV = cpuUV;
				}
			}
		}
	}

	public void SetUViGPU(int igpuUV)
	{
		if (!CpuInfo.IsSupportedUViGPU() || igpuUV < CpuInfo.MinIGPUUV || igpuUV > CpuInfo.MaxIGPUUV)
		{
			return;
		}
		RyzenSmuService smu = GetSmu();
		if (smu != null)
		{
			SmuStatus smuStatus = smu.SetCoGfx(igpuUV);
			Logger.WriteLine($"iGPU UV: {igpuUV} {smuStatus}");
			if (smuStatus == SmuStatus.OK)
			{
				_igpuUV = igpuUV;
			}
		}
	}

	public string SetRyzen(bool launchAsAdmin = false)
	{
		if (!ProcessHelper.IsUserAdministrator())
		{
			if (launchAsAdmin)
			{
				ProcessHelper.RunAsAdmin("uv");
			}
			return string.Empty;
		}
		RyzenSmuService smu = GetSmu();
		if (smu == null)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			int mode = AppConfig.GetMode("cpu_uv", 0);
			int mode2 = AppConfig.GetMode("igpu_uv", 0);
			int mode3 = AppConfig.GetMode("cpu_temp");
			lock (RuntimeCpuTuning.SyncRoot)
			{
				if (!RuntimeCpuTuning.OwnsCurve && CpuInfo.IsSupportedUV() && mode >= CpuInfo.MinCPUUV && mode <= CpuInfo.MaxCPUUV)
				{
					SmuStatus smuStatus = smu.SetCoAll(mode);
					Logger.WriteLine($"UV: {mode} {smuStatus}");
					if (smuStatus == SmuStatus.OK)
					{
						_cpuUV = mode;
					}
					StringBuilder stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder3 = stringBuilder2;
					StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(9, 2, stringBuilder2);
					handler.AppendLiteral("CPU UV ");
					handler.AppendFormatted(mode);
					handler.AppendLiteral(": ");
					handler.AppendFormatted(smuStatus);
					stringBuilder3.AppendLine(ref handler);
				}
			}
			if (CpuInfo.IsSupportedUViGPU() && mode2 >= CpuInfo.MinIGPUUV && mode2 <= CpuInfo.MaxIGPUUV)
			{
				SmuStatus smuStatus2 = smu.SetCoGfx(mode2);
				Logger.WriteLine($"iGPU UV: {mode2} {smuStatus2}");
				if (smuStatus2 == SmuStatus.OK)
				{
					_igpuUV = mode2;
				}
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder4 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(10, 2, stringBuilder2);
				handler.AppendLiteral("iGPU UV ");
				handler.AppendFormatted(mode2);
				handler.AppendLiteral(": ");
				handler.AppendFormatted(smuStatus2);
				stringBuilder4.AppendLine(ref handler);
			}
			SmuStatus? value = SetCPUTemp(mode3, log: true);
			if (value.HasValue)
			{
				StringBuilder stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder5 = stringBuilder2;
				StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 2, stringBuilder2);
				handler.AppendLiteral("CPU Temp ");
				handler.AppendFormatted(mode3);
				handler.AppendLiteral("°C: ");
				handler.AppendFormatted(value);
				stringBuilder5.AppendLine(ref handler);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("UV Error: " + ex.ToString());
		}
		SetReapplyEnabled(AppConfig.IsApplyUV());
		return stringBuilder.ToString().TrimEnd();
	}

	public string ReadRyzenLimits()
	{
		RyzenSmuService smu = GetSmu();
		if (smu == null)
		{
			return string.Empty;
		}
		try
		{
			PowerLimits powerLimits = smu.GetPowerLimits();
			if (powerLimits == null)
			{
				return string.Empty;
			}
			string text = $"SPL: {powerLimits.Stapm:F1}W | sPPT {powerLimits.Slow:F1}W | fPPT {powerLimits.Fast:F1}W";
			if (powerLimits.ApuSlow.HasValue)
			{
				text += $" | APU {powerLimits.ApuSlow.Value:F1}W";
			}
			text += $", Temp: {powerLimits.TctlTemp:F0}°C";
			Logger.WriteLine("Ryzen Limits: " + text);
			return text;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ReadRyzenLimits Error: " + ex.ToString());
			return string.Empty;
		}
	}

	public void ResetRyzen()
	{
		if (_cpuUV != 0)
		{
			SetUV(0);
		}
		if (_igpuUV != 0)
		{
			SetUViGPU(0);
		}
		if (_cpuTemp != CpuInfo.DefaultTemp)
		{
			SetCPUTemp(CpuInfo.DefaultTemp, log: true);
		}
		SetReapplyEnabled(enabled: false);
	}

	public void AutoRyzen()
	{
		if (CpuInfo.IsAMD)
		{
			if (AppConfig.IsApplyUV())
			{
				SetRyzen();
			}
			else
			{
				ResetRyzen();
			}
		}
	}

	public void AutoCPUTemp()
	{
		if (!CpuInfo.IsAMD || !AppConfig.IsApplyUV() || !ProcessHelper.IsUserAdministrator())
		{
			return;
		}
		try
		{
			SetCPUTemp(AppConfig.GetMode("cpu_temp"), log: true);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("AutoCPUTemp Error: " + ex.Message);
		}
	}

	private async Task ApplyMechrevoPowerLimitsAsync(int pl1, int pl2)
	{
		try
		{
			MechrevoHw hw = Program.hw;
			if (hw != null && hw.IsConnected && !(await Program.hw.SetPl1Pl2(pl1, pl2)))
			{
				Logger.WriteLine($"PL1/PL2 update not confirmed: requested={pl1}/{pl2}, actual={Program.hw.Pl1}/{Program.hw.Pl2}");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PL1/PL2 update failed: " + ex.Message);
		}
	}

	public void Dispose()
	{
		CancellationTokenSource cancellationTokenSource = Interlocked.Exchange(ref _modeCts, null);
		cancellationTokenSource?.Cancel();
		if (_modeTask.IsCompleted)
		{
			cancellationTokenSource?.Dispose();
		}
		reapplyTimer?.Stop();
		reapplyTimer?.Dispose();
		reapplyTimer = null;
		modeToggleTimer?.Stop();
		modeToggleTimer?.Dispose();
		lock (_smuLock)
		{
			_smu?.Dispose();
			_smu = null;
		}
	}
}
