// ⭐⭐ 2026-10-19（机主：无界系列 + AMD APU（8745H/8845H/H255/Ryzen AI 365/AI 400 等）
//   要能用「SMU 自定义」= PL/温度墙/曲线那套，与宝龙达 30 系同源）：
//
//   **无界 APU 判据**（唯一判据，探测命令与 UI 共用；本文件**只读**）。
//
// 判据（三条全真才允许 SMU 自定义）：
//   ① 机型名 = SystemProductName（BIOS 注册表）包含「无界」/ WUJIE / WJXY（大小写不敏感）；
//      ⛔ 不用 BaseBoardProduct（那是模具号）。
//   ② APU = CPU 厂商确认为 AMD（CpuVendor == Amd，真硬件事实）且**没有 NVIDIA 独显**（注册表适配器枚举；
//      AMD 内置显卡 ⇒ 不命中 NVIDIA ⇒ ApuOk=true）。
//   ③ SMU 通路真的可读：SmuAvailability.IsReady（PawnIO 探测，进程内缓存一次）。
//
// ⛔ 只读：不写 EC/SMU/注册表/配置；任何一条读不到 ⇒ 按"不成立"处理（⛔ 绝不默认放行）。
using System;
using System.Collections.Generic;
using System.Linq;
using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

/// <summary>⭐ 无界 APU 判据结果（只读快照；供探测命令与 UI/日志共用）。</summary>
internal sealed record WujieSmuVerdict(
	string Model,
	string Board,
	string CpuName,
	string CpuVendorRaw,
	bool IsAmd,
	bool HasNvidia,
	bool ApuOk,
	bool IsWujie,
	bool SmuReady,
	string SmuReason)
{
	/// <summary>⭐⭐ 三条全真 ⇒ 允许「SMU 自定义」。</summary>
	internal bool Allowed => IsWujie && ApuOk && SmuReady;

	/// <summary>⭐ 未满足的判据（逐条列出，给日志/报告用）。</summary>
	internal string UnmetReasons
	{
		get
		{
			List<string> list = new List<string>(3);
			if (!IsWujie)
			{
				list.Add("机型名不含「无界/WUJIE/WJXY」");
			}
			if (!ApuOk)
			{
				list.Add(IsAmd ? "有 NVIDIA 独显（不是 APU-only）" : "CPU 厂商不是 AMD");
			}
			if (!SmuReady)
			{
				list.Add("SMU 通路不可用（" + SmuReason + "）");
			}
			return (list.Count == 0) ? "无" : string.Join("；", list);
		}
	}
}

/// <summary>
/// ⭐⭐ 2026-10-19（机主：无界 APU 要能用 SMU 自定义）：**只读判据 + 默认可见日志** ✅。
/// <para>⚠️ 本类只做判据/日志，⛔ 不做任何写入；探测命令 <c>--wujie-smu-probe</c> 与本类同一口径。</para>
/// </summary>
internal static class WujieSmuGate
{
	/// <summary>机型名关键字（⛔ 不含模具号；大小写不敏感）。</summary>
	private static readonly string[] WujieModelKeywords = new string[3] { "无界", "WUJIE", "WJXY" };

	// ⭐⭐ 2026-10-19（机主拍板：无界 APU 直接开放 SMU 自定义，CPU 功耗最大 90W）：
	//   ⭐ 范围常量（UI/写入**同一口径**）：PL1/PL2 = [15, 90]W；温度墙沿用 60–105℃。
	internal const int PlMinWatts = 15;

	internal const int PlMaxWatts = 90;

	internal const int TccMinCelsius = 60;

	internal const int TccMaxCelsius = 105;

	/// <summary>
	/// ⭐ **无界 APU 判据**（不含 SMU 可用性）：机型名含无界/WUJIE/WJXY && CpuVendor.IsAmd && 无 NVIDIA 独显 ✅。
	/// <para>⚠️ 只读；SMU 是否可用由调用方另加 `SmuAvailability.IsReady`（与 `<see cref="PerfModeCapabilities.UsesSmuForCustomPower" />` 同源）。</para>
	/// </summary>
	internal static bool IsWujieApu()
	{
		WujieSmuVerdict verdict = Evaluate();
		return verdict.IsWujie && verdict.ApuOk;
	}

	/// <summary>⭐ 无界 APU **且** SMU 通路可用 ⇒ 允许 SMU 自定义（会触发一次 SMU 探测，已缓存）。</summary>
	internal static bool IsWujieSmuAllowed()
	{
		return IsWujieApu() && SmuAvailability.IsReady;
	}

	/// <summary>
	/// ⭐⭐ 机主要求的默认可见日志（去重）：
	/// `[smu-ui] 无界 APU：SMU自定义=True（model=… · code=… · smuVer=…）· PL上限=90W` ✅
	/// </summary>
	internal static void LogAllowed()
	{
		try
		{
			WujieSmuVerdict verdict = Evaluate();
			if (!verdict.IsWujie || !verdict.ApuOk)
			{
				return;
			}
			PawnIO.RyzenSmuService smu = null;
			try { smu = MechrevoLite.Mode.ModeControl.SharedSmu; } catch { }
			string code = (smu != null) ? smu.CpuCodeName.ToString() : "读不到";
			string version = (smu != null) ? ("0x" + smu.SmuVersion.ToString("X")) : "读不到";
			string message = "[smu-ui] 无界 APU：SMU自定义=" + (SmuAvailability.IsReady ? "True" : "False")
				+ "（model=" + verdict.Model + " · code=" + code + " · smuVer=" + version + "）· PL上限=" + PlMaxWatts + "W";
			if (Logger.TryMarkChanged("wujie-smu-allowed", message))
			{
				Logger.WriteError(message);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[smu-ui] 无界 APU 允许日志异常（已吞掉）：" + ex.Message);
		}
	}
	/// <summary>⭐ 机型名判据：SystemProductName 含「无界」/ WUJIE / WJXY（大小写不敏感）。</summary>
	internal static bool IsWujieModel(string model)
	{
		if (string.IsNullOrWhiteSpace(model))
		{
			return false;
		}
		foreach (string keyword in WujieModelKeywords)
		{
			if (model.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>⭐ 执行只读判据（进程内不缓存结果本身，但底层探测各自有缓存/只读）。</summary>
	internal static WujieSmuVerdict Evaluate()
	{
		MechrevoDeviceCapabilities caps = MechrevoDeviceCapabilities.Current;
		string model = caps.Model ?? "";
		string board = caps.BaseboardProduct ?? "";
		string cpuName = "";
		string cpuVendorRaw = "";
		bool isAmd = false;
		bool hasNvidia = false;
		try
		{
			cpuName = PawnIO.CpuInfo.Name ?? "";
		}
		catch
		{
		}
		try
		{
			cpuVendorRaw = CpuVendor.RawIdentifier;
			isAmd = CpuVendor.IsAmd;
		}
		catch
		{
		}
		try
		{
			IReadOnlyList<GpuAdapter> adapters = GpuAdapterReader.ReadFromRegistry(out bool _);
			hasNvidia = adapters.Any((GpuAdapter adapter) => adapter.IsNvidia);
		}
		catch
		{
			// 读不到适配器 ⇒ ⛔ 不能证明"没有 NVIDIA 独显" ⇒ 按不成立处理（ApuOk=false）。
			hasNvidia = true;
		}
		bool apuOk = isAmd && !hasNvidia;
		bool smuReady = false;
		string smuReason = "";
		try
		{
			smuReady = SmuAvailability.IsReady;
			smuReason = SmuAvailability.Reason;
		}
		catch (Exception ex)
		{
			smuReason = "探测异常：" + ex.GetType().Name;
		}
		return new WujieSmuVerdict(model, board, cpuName, cpuVendorRaw, isAmd, hasNvidia, apuOk,
			IsWujieModel(model), smuReady, smuReason);
	}

	/// <summary>
	/// ⭐⭐ 机主要求的默认可见日志（去重）：
	/// <c>[smu-ui] 无界 APU 判据：model=… · amd=… · apu=… · smuReady=… ⇒ SMU自定义=True/False</c> ✅
	/// </summary>
	internal static void LogPredicate()
	{
		try
		{
			WujieSmuVerdict verdict = Evaluate();
			string message = "[smu-ui] 无界 APU 判据：model=" + verdict.Model
				+ " · amd=" + verdict.IsAmd
				+ " · apu=" + verdict.ApuOk
				+ " · smuReady=" + verdict.SmuReady
				+ " ⇒ SMU自定义=" + (verdict.Allowed ? "True" : "False");
			if (!verdict.Allowed)
			{
				message += "（不满足：" + verdict.UnmetReasons + "）";
			}
			if (Logger.TryMarkChanged("wujie-smu-gate", message))
			{
				Logger.WriteError(message);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[smu-ui] 无界 APU 判据异常（已吞掉，按不开放处理）：" + ex.Message);
		}
	}
}
