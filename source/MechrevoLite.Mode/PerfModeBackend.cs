using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MechrevoLite.Display;
using MechrevoLite.Gpu;
using MechrevoLite.Hardware;

namespace MechrevoLite.Mode;

internal sealed class PerfModeBackend : IPerfModeBackend
{
	private readonly MechrevoService _service;

	private readonly MechrevoHw _hw;

	public PerfModeBackend(MechrevoService service, MechrevoHw hw)
	{
		_service = service ?? throw new ArgumentNullException("service");
		_hw = hw ?? throw new ArgumentNullException("hw");
	}

	public static PerfModeCapabilities CapabilitiesFrom(MechrevoHw hw)
	{
		ArgumentNullException.ThrowIfNull(hw, "hw");
		// ⭐⭐ 2026-10-19（30 系 · SMU 功耗控制）：先按**静态判据短路**，再问 SMU 通路是否可用 ——
		//   `SmuAvailability.IsReady` 会做一次 PawnIO 探测（进程内缓存）；⛔ 不能让 GCU/40 系机器也白跑一次。
		// ⭐⭐ 2026-10-19（机主：后端初始化不要挂在设置窗打开路径上）：
		//   ⭐ 这里改成 **只读缓存值** `IsReadyCached`（⛔ 不触发探测）——
		//      能力位/规划器可能在"打开设置/构建行"时被调用，SMU 探测必须留到真正打开自定义模式窗口
		//      （`CustomModeForm` 里显式 `SmuAvailability.IsReady`）或真正需要 STAPM 时 ✅
		//   ⚠️ 未探测前 `SupportsSmuPowerControls=false` ⇒ 规划器不生成 SMU 步骤；
		//      自定义模式窗口打开后会重新算能力位，语义不变 ✅
		bool wujieApu = hw.WujieSmuApu;
		bool smuReady = (hw.IsBaolongda30Series || wujieApu) && SmuAvailability.IsReadyCached;
		return new PerfModeCapabilities
		{
			UsesAmdPowerFields = hw.UsesAmdPowerFields,
			SupportsPl1 = hw.Pl1Adjustable,
			SupportsPl2 = hw.Pl2Adjustable,
			SupportsPl4 = hw.Pl4Adjustable,
			SupportsTcc = hw.TccAdjustable,
			SupportsTgp = hw.GpuTgpAdjustable,
			SupportsDynamicBoost = hw.GpuDynamicBoostAdjustable,
			SupportsGpuOverclock = hw.SupportsGpuOverclock,
			SupportsFanCurve = hw.FanCurveSeen,
			SupportsFanBoost = hw.SupportsFanBoost,
			SupportsFanSwitchSpeed = hw.SupportsFanSwitchSpeed,
			// 2026-10-15（用户拍板：刷新率这条**整个改走 Windows API**，不再依赖 GCU）：
			// 原来判的是 GCU 能力位 + 固件的「自动刷新率」字段（`hw.SupportsDisplayRefresh && !hw.DcHz`）
			// ⇒ 非 GCU 机型（老接口 / 将来宝龙达）永远拿不到这一步。
			// 现在判据 = **Win32 侧到底有没有可切的档位**（内屏实测最大档 > MIN_RATE ⇒ 至少两档），
			// 并保留"自动刷新率开着时不做固定档"这条语义 —— 但改读**我们自己的**配置键 `screen_auto`。
			SupportsRefreshRate = (ScreenGateProbe.Win32MaxRefreshRate() > ScreenControl.MIN_RATE) && !AppConfig.Is("screen_auto"),
			SupportsTurboSubMode = (hw.Capabilities.SilentTurboAvailability == FeatureAvailability.Supported),
			// ⭐⭐ 2026-10-16（宝龙达 · 自定义档诚实性）：⭐ 固件自定义档【只有 GCU 后端能写】✅。
			//   ⭐ 依据：⭐ 档位步 `SwitchFirmwareSlot` ⇒ `MechrevoService.SwitchCustomProfileWithResend`（⭐ `:768`/`:801`
			//     两道 `!IsGcuConnected` 闸 ❌）· ⭐ 字段步 `WriteFirmwareField` ⇒ `SetCustomDetail`（⭐ 同样是 MQTT ❌）
			//   ⭐ 真机日志（⭐ 宝龙达 `MRID6-23` ✅）：⭐ `SwitchCustomProfile: GCU 未连接` ＋ `SwitchFirmwareSlot:0 -> Failed` ⚠️
			//   ⇒ ⭐ 所以按后端【如实申报】：⭐ 宝龙达 ⇒ false ⇒ ⭐ 规划器不生成那两步 ✅
			//      ⇒ ⭐ 不再出现"注定失败"的档位步（⭐ 它会让 Runner 提前 break、把后面所有步骤一起废掉 ❌）✅
			//   ⚠️ 非宝龙达 ⇒ `UsesBaolongda == false` ⇒ true ⇒ ⭐ 步骤列表【一个字节都不变】✅
			SupportsFirmwareSlot = !hw.UsesBaolongda && !hw.WujieSmuApu,
			// ⭐⭐ 2026-10-16（宝龙达 · 自定义档 · 功耗墙真下发）：⭐ 功耗墙三项（PL1 / PL2 / 温度墙）
			//   ⭐ 在宝龙达上【不走固件槽】、而是走 WMI 功能号 23 ✅
			//      （⭐ 通路：⭐ `PerfModeBackend.WriteDetailFieldAsync` ⇒ `MechrevoService.SetCustomDetail`
			//        ⇒ 它开头那个 `TryApplyPowerLimitViaBackend` ⇒ `TryApplyCpuPowerLimitViaBackend` ✅）
			//   ⇒ ⭐ 所以单独申报：⭐ 宝龙达 ⇒ true ⇒ ⭐ 规划器额外生成那三步 ✅
			//   ⚠️ 非宝龙达 ⇒ false ⇒ ⭐ 那三步仍留在原固件字段块里 ⇒ ⭐ 步骤列表【一个字节都不变】✅
			// ⭐⭐ 2026-10-19（无界 APU）：功耗墙走本机 SMU ⇒ 与宝龙达同样单独生成 PL/温度墙步骤 ✅
			SupportsWmiPowerFields = hw.UsesBaolongda || hw.WujieSmuApu,
			// ⭐⭐ 2026-10-19（30 系 · SMU 功耗控制 · 机主「持续功耗 STAPM 能不能放进去」）：
			//   ⭐ 本后端自定义档的功耗**到底走不走 SMU** —— 唯一判据 `PerfModeCapabilities.UsesSmuForCustomPower`
			//      （宝龙达 + 30 系 + SMU 通路真的可用；`SmuAvailability.IsReady` 进程内只探测一次）✅
			//   ⭐ 规划器据此生成 `StapmLimitW` / `SmuStapmTimeSeconds` 两步；UI 也只用同一个函数决定建不建行
			//      （⛔ 不再只按"机型是 Gen30"这种静态判据）✅
			//   ⚠️ 40 系 / GCU / 老接口 / SMU 不可用 ⇒ false ⇒ ⭐ 步骤列表【一个字节都不变】✅
			SupportsSmuPowerControls = PerfModeCapabilities.UsesSmuForCustomPower(hw.UsesBaolongda, hw.IsBaolongda30Series, smuReady, wujieApu),
			// ⭐⭐ 2026-10-19（机主：「显卡超频貌似没用」）：⭐ 宝龙达没有 GCU ⇒ GPU 偏移只能走本机 NVAPI P0
			//   （⭐ `PerfModeBackend.ApplyGpuOverclockAsync` 的宝龙达分支）。
			//   ⚠️ 非宝龙达 ⇒ false ⇒ 规划器行为**逐位不变** ✅（GCU 机型那条 `WriteFirmwareField` 路径照旧）。
			SupportsDirectGpuOverclock = hw.UsesBaolongda
		};
	}

	public async Task<bool> SwitchBuiltInAsync(PerfModeKind kind, CancellationToken ct)
	{
		return await _service.SwitchMode(PerfModeMapping.ToServiceMode(kind)) == MechrevoService.ModeSwitchResult.Ok;
	}

	public Task<bool> SwitchTurboSubModeAsync(bool silent, CancellationToken ct)
	{
		return _service.SwitchTurboSubMode(silent, ct);
	}

	public Task<bool> SwitchFirmwareSlotAsync(int slotIndex, CancellationToken ct)
	{
		return _service.SwitchCustomProfileWithResend(slotIndex, ct);
	}

	public Task<bool> WriteDetailFieldAsync(string wireKey, string value, CancellationToken ct)
	{
		return _service.SetCustomDetail(new Dictionary<string, string> { [wireKey] = value });
	}

	public async Task<bool> WriteFanCurveAsync(bool cpu, int[] duty, CancellationToken ct)
	{
		try
		{
			// ⭐⭐ 2026-10-17（宝龙达 · 自定义档风扇曲线）：⭐ 没有 GCU ⇒ 不写固件表，
			//   改成"占空比 % → RPM → 存进 BaolongdaFanCurveStore 并启用"（本机 3 s 软件闭环下发 20/21）✅
			//   ⚠️ 非宝龙达仍走原来的 `SetFanCurve`（GCU）⇒ 逐位不变 ✅
			if (_hw.UsesBaolongda)
			{
				return ApplyBaolongdaFanCurve(cpu, duty);
			}
			await _hw.SetFanCurve((!cpu) ? 1 : 0, duty).ConfigureAwait(continueOnCapturedContext: false);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModeBackend.WriteFanCurve(" + (cpu ? "CPU" : "GPU") + ") failed: " + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-17（宝龙达 · 自定义档风扇曲线）：⭐ 把"16 点占空比 %"换成 RPM 存进
	/// <see cref="BaolongdaFanCurveSettings" /> 并**启用**（3 s 定时器按它下发功能号 20/21）✅
	/// <para>⚠️ 通道映射（owner 已确认）：<c>ch0 = FanType 0 = GPU</c> · <c>ch1 = FanType 1 = CPU</c>
	/// ⇒ `cpu=true` 写 ch1、`cpu=false` 写 ch0 ✅</para>
	/// <para>⚠️ 换算分母用**存档里的** <c>MinRpm/MaxRpm</c>（与曲线窗口同一口径），⛔ 不另编一个分母 ✅</para>
	/// <para>⚠️ 存档读不懂（旧格式）⇒ 返回 false，⛔ 不拿默认值顶替 ✅</para>
	/// </summary>
	private bool ApplyBaolongdaFanCurve(bool cpu, int[] dutyPercent)
	{
		if (dutyPercent == null || dutyPercent.Length != 16)
		{
			return false;
		}
		BaolongdaFanCurveSettings current = _hw.LoadBaolongdaFanCurve();
		if (current.IsUnreadableArchive)
		{
			Logger.WriteLine("[fan] 宝龙达本地曲线：⚠️ 存档读不懂 ⇒ 本轮不写（⛔ 不拿默认值顶替）");
			return false;
		}
		int min = current.MinRpm;
		int max = current.MaxRpm;
		double span = Math.Max(0, max - min);
		int[] rpms = new int[dutyPercent.Length];
		for (int i = 0; i < dutyPercent.Length; i++)
		{
			int pct = Math.Clamp(dutyPercent[i], 0, 100);
			rpms[i] = min + (int)Math.Round(pct * span / 100.0);
		}
		BaolongdaFanCurveSettings next = new BaolongdaFanCurveSettings
		{
			Enabled = true,
			FormatVersion = current.FormatVersion,
			// ⭐ 2026-10-19：双风扇联动开关随曲线一起保存（⛔ 写档位曲线不能顺手把它改回默认）✅
			SyncEnabled = current.SyncEnabled,
			HysteresisC = current.HysteresisC,
			IntervalMs = current.IntervalMs,
			MinRpm = current.MinRpm,
			MaxRpm = current.MaxRpm,
			Channel0Temperatures = current.Channel0Temperatures,
			Channel0Rpms = cpu ? current.Channel0Rpms : rpms,
			Channel1Temperatures = current.Channel1Temperatures,
			Channel1Rpms = cpu ? rpms : current.Channel1Rpms
		};
		// ⭐⭐ 2026-10-18（多自定义档各自曲线）：⭐ 写回**当前档位**对应存档（旧全局作为兜底）✅
		string modeId = PerfModeService.Instance?.ActiveModeId ?? "default";
		bool ok = _hw.SaveBaolongdaFanCurve(modeId, next);
		Logger.WriteLine($"[fan] 宝龙达本地曲线已保存（档位={modeId} · {(cpu ? "CPU/ch1" : "GPU/ch0")} · {min}~{max} RPM · enabled=true）⇒ ok={ok}");
		return ok;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-19（显卡功耗上限）：⭐ 走本机 **NVML**（`nvmlDeviceSetPowerManagementLimit`），
	/// 由 <c>NvmlHelper</c> 负责"读约束→钳制→写→回读"✅
	/// <para>⚠️ 只对"本机后端"有意义；GCU 机型仍走厂商字段 `GpuConfigurableTGPTarget`（不在本方法里）✅</para>
	/// </summary>
	public Task<bool> ApplyGpuPowerLimitAsync(int watts, CancellationToken ct)
	{
		bool ok = NvmlHelper.TrySetPowerLimitWatts(watts, out int applied);
		Logger.WriteLine($"[gpu-power] 设功耗上限 {watts}W ⇒ ok={ok} · 回读={applied}W");
		return Task.FromResult(ok);
	}

	public Task<bool> ApplyGpuOverclockAsync(bool on, int core, int memory, CancellationToken ct)
	{
		// ⭐⭐ 2026-10-19（宝龙达 · GPU 偏移真正生效）：宝龙达没有 GCU ⇒ 原来的 `ApplyGcuGpuOverclock`
		//   走的是厂商 MQTT `LCHWOC` 字段，必然失败（机主实测：核心滑块值恒 0）。
		//   这里改走 NVAPI P-States 20 的 P0 时钟偏移（core + mem 一起写、写完回读、不一致如实报失败）。
		//   ⚠️ 非宝龙达仍走原来的 `ApplyGcuGpuOverclock`，调用与参数**逐字不变**。
		if (_hw.UsesBaolongda)
		{
			int targetCore = (on ? core : 0);
			int targetMemory = (on ? memory : 0);
			// ⭐⭐ 2026-10-19（机主：「显卡超频设置了但频率没变化」· 可观测性②）：
			//   ⭐ **执行入口**先落一行（默认级别可见）：请求值 = 规划步骤真正传下来的值 ✅
			Logger.WriteError("[gpu-oc] 应用入口（宝龙达 ⇒ NVAPI P0）：on=" + on + " · 请求 core=" + core + " mem=" + memory + "MHz"
				+ " ⇒ 目标 core=" + targetCore + " mem=" + targetMemory + "MHz");
			// ⭐⭐ 2026-10-19（机主：「显卡超频貌似没用」）：⭐ 先把**驱动回读后端**准备好 ——
			//   ① 写后校验（`VerifyAsync` ⇒ `DriverGpuOverclockFieldMatches`）只在
			//      `DirectGpuOverclockBackendPresent` 时才会去回读；② 自定义模式窗口显示的
			//      `EffectiveGpuCoreClockOffset` 也走同一条回读。⇒ 不先 Ensure 的话，
			//      真机日志里会出现"写成功但验证读不到"的假象（⛔ 不是写失败，是回读后端没起）。
			//   ⚠️ 本方法失败也不影响真正的写：下面是独立的 NVAPI P0 通路；异常都在 Ensure 内部吞掉 ✅
			bool readbackReady = _hw.EnsureDirectGpuOverclock();
			if (!readbackReady)
			{
				// ⭐ 失败要可见（原来 Ensure 只写 info 级日志 ⇒ 默认「仅错误」看不到）✅
				Logger.WriteError("[gpu-oc] ⚠️ EnsureDirectGpuOverclock=false ⇒ 驱动回读后端未就绪"
					+ "（NVAPI P0 写入通路不依赖它，下面照写；`VerifyAsync` 的回读校验会按\"读不到\"处理）");
			}
			bool ok = NvapiPStates.TrySetP0Offsets(targetCore, targetMemory, out int appliedCore, out int appliedMemory);
			string detail = NvapiPStates.LastError;
			// ⭐ 写后回读值**原样进日志**（⭐ 机主需要凭这一行判断"到底写没写进去"）✅
			Logger.WriteError($"[gpu-oc] 宝龙达 NVAPI P0：请求 core={targetCore} mem={targetMemory} MHz"
				+ $" ⇒ ⭐ 写成功={ok} · 驱动回读 core={appliedCore} mem={appliedMemory} MHz"
				+ $" · 回读后端就绪={readbackReady}"
				+ (string.IsNullOrEmpty(detail) ? "" : " · " + detail));
			// ⭐ 再单独比一次"配置目标 vs 驱动回读值"：⭐ 一致 = 已生效；不一致 = 如实报失败（⛔ 不假装）✅
			bool confirmed = ok && appliedCore == targetCore && appliedMemory == targetMemory;
			if (ok && !confirmed)
			{
				Logger.WriteError($"[gpu-oc] ⚠️ 驱动接受了写入，但回读与目标不一致：目标 core={targetCore} mem={targetMemory}"
					+ $" · 回读 core={appliedCore} mem={appliedMemory} MHz ⇒ 如实按失败处理");
			}
			return Task.FromResult(confirmed);
		}
		return _service.ApplyGcuGpuOverclock(on, core, memory);
	}

	public Task<bool> SetSlotNameAsync(string name, CancellationToken ct)
	{
		return _service.SetCustomProfileName(name);
	}

	public Task<bool> SetFanBoostAsync(bool on, CancellationToken ct)
	{
		if (_hw.FanBoost != on)
		{
			return _service.SwitchFanBoost(on);
		}
		return Task.FromResult(result: true);
	}

	public Task<bool> SetRefreshRateAsync(int hz, CancellationToken ct)
	{
		// 2026-10-15（用户拍板：刷新率这条整个改走 Windows API）：不再下发 GCU 的 GPU_HZSETTING。
		// 唯一动作 = ScreenControl.SetScreen(hz) ⇒ ScreenNative.SetRefreshRate ⇒ ChangeDisplaySettingsEx；
		// 回读也用 Win32（EnumDisplaySettingsEx），±2Hz 视为同一档（规避 59.94 取整）。
		try
		{
			string screen = ScreenNative.FindLaptopScreen(log: false);
			int current = ScreenNative.GetRefreshRate(screen);
			if (current > 0 && Math.Abs(current - hz) <= 2)
			{
				return Task.FromResult(result: true);
			}
			ScreenControl.SetScreen(hz);
			int readback = ScreenNative.GetRefreshRate(screen);
			return Task.FromResult(readback > 0 && Math.Abs(readback - hz) <= 2);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("PerfModeBackend.SetRefreshRate(" + hz + ") failed: " + ex.Message);
			return Task.FromResult(result: false);
		}
	}

	public bool SetPowerPlan(string guid)
	{
		return WinPowerPlan.SetActivePlan(guid);
	}

	public bool SetCpuBoost(int index)
	{
		return WinPowerPlan.SetBoost(index);
	}

	public bool SetPowerOverlay(int mode)
	{
		PowerOverlayResult powerOverlayResult = PowerNative.SetOverlayConfirmed(mode);
		if (powerOverlayResult == PowerOverlayResult.Confirmed || (uint)(powerOverlayResult - 2) <= 1u)
		{
			return true;
		}
		return false;
	}

	public Task DelayAsync(int milliseconds, CancellationToken ct)
	{
		return Task.Delay(milliseconds, ct);
	}

	public async Task<bool?> VerifyAsync(PerfApplyStep step, CancellationToken ct)
	{
		switch (step.Verify)
		{
		case PerfApplyVerify.ModeReadback:
			return await VerifyModeAsync(step, ct).ConfigureAwait(continueOnCapturedContext: false);
		case PerfApplyVerify.PowerOverlay:
			return PowerNative.IsOverlayActive(step.Number);
		case PerfApplyVerify.PowerPlan:
		{
			string activePlan = WinPowerPlan.GetActivePlan();
			return (activePlan.Length == 0) ? ((bool?)null) : new bool?(string.Equals(activePlan, step.Value, StringComparison.OrdinalIgnoreCase));
		}
		case PerfApplyVerify.CpuBoost:
		{
			int ac;
			int dc;
			return WinPowerPlan.TryGetBoost(out ac, out dc) ? new bool?(ac == step.Number && dc == step.Number) : ((bool?)null);
		}
		case PerfApplyVerify.RefreshRate:
		{
			// 2026-10-15（刷新率整条改走 Windows API）：回读也走 Win32 —— 原来读 GCU 的 `_hw.CurrentHz`，
			// 非 GCU 机型上恒为 0/-1 ⇒ 这一步会被判成"假失败"。
			int hzNow = ScreenNative.GetRefreshRate(ScreenNative.FindLaptopScreen(log: false));
			return (hzNow > 0) ? new bool?(Math.Abs(hzNow - step.Number) <= 2) : ((bool?)null);
		}
		// (a)(b)（2026-10-09，用户原话「OK 按你说的」）：驱动/NVAPI 回读滞后可达 2~4 秒 ——
		// 此刻"对不上"**不再判「设下去了，但读回来对不上」（那是 GPU 假失败）**，改回 null（= 已下发·待确认 Sent）；
		// 多拍重读（300ms/2s/4s + 每次 GCU 状态刷新）由 PerfLazyVerifier 在后台跑，读到一致自动升级为已确认。
		case PerfApplyVerify.GpuClockOffset:
			if (!_hw.DirectGpuOverclockBackendPresent)
			{
				return null;
			}
			if (step.Kind == PerfApplyStepKind.ApplyGpuOverclock)
			{
				bool matchOffsets = _hw.DriverGpuOverclockFieldMatches("GpuCoreClockOffsetOC", step.GpuOffsets?.Core ?? 0) && _hw.DriverGpuOverclockFieldMatches("GpuMemoryClockOffsetOC", step.GpuOffsets?.Memory ?? 0);
				return matchOffsets ? new bool?(true) : ((bool?)null);
			}
			bool matchField = _hw.DriverGpuOverclockFieldMatches(step.WireKey, step.Number);
			return matchField ? new bool?(true) : ((bool?)null);
		case PerfApplyVerify.CpuPackagePower:
			return VerifyCpuPowerLimit(step.Number);
		case PerfApplyVerify.GpuPowerLimit:
		case PerfApplyVerify.FanDuty:
			return null;
		default:
			return null;
		}
	}

	private async Task<bool?> VerifyModeAsync(PerfApplyStep step, CancellationToken ct)
	{
		if (step.Kind == PerfApplyStepKind.SwitchFirmwareSlot)
		{
			return await _hw.WaitForStateAsync(() => _hw.OperatingMode == 3 && _hw.CustomProfileIndex == step.Number, TimeSpan.FromMilliseconds(1200L), ct).ConfigureAwait(continueOnCapturedContext: false) || (_hw.OperatingMode == 3 && _hw.CustomProfileIndex == step.Number);
		}
		int expected = PerfModeMapping.ToFirmwareOperatingMode((PerfModeKind)step.Number);
		return await _hw.WaitForStateAsync(() => _hw.OperatingMode == expected, TimeSpan.FromMilliseconds(1200L), ct).ConfigureAwait(continueOnCapturedContext: false) || _hw.OperatingMode == expected;
	}

	internal static bool? VerifyCpuPowerLimit(int watts)
	{
		return EvaluateCpuPowerLimit(watts, HardwareControl.cpuPower);
	}

	internal static bool? EvaluateCpuPowerLimit(int watts, float? measured)
	{
		if (watts > 0 && measured.HasValue)
		{
			float valueOrDefault = measured.GetValueOrDefault();
			if (!(valueOrDefault <= 0f))
			{
				double num = (double)watts * 1.15 + 5.0;
				if ((double)valueOrDefault > num)
				{
					return false;
				}
				if ((double)valueOrDefault >= (double)watts * 0.85)
				{
					return true;
				}
				return null;
			}
		}
		return null;
	}
}
