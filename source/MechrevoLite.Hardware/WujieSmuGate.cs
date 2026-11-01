// **无界 APU 判据**（唯一判据，探测命令与 UI 共用；本文件**只读**）。
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
	string SmuReason,
	string HitField,
	string HitValue)
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
				list.Add("四个字段(Name/Family/SKU/Board)均不含「无界/WUJIE」");
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
// （机主：无界 APU 要能用 SMU 自定义）：**只读判据 + 默认可见日志** 。
internal static class WujieSmuGate
{
	/// <summary>机型名关键字（⛔ 不含模具号；大小写不敏感）。</summary>
	private static readonly string[] WujieModelKeywords = new string[3] { "无界", "WUJIE", "WJ" };
	// 范围常量（UI/写入**同一口径**）：PL1/PL2 = [15, 90]W；温度墙沿用 60–105℃。
	internal const int PlMinWatts = 15;

	internal const int PlMaxWatts = 90;

	internal const int TccMinCelsius = 60;

	internal const int TccMaxCelsius = 105;

	/// <summary>
	/// ⭐ **无界 APU 判据**（不含 SMU 可用性）：机型名含无界/WUJIE/WJ && CpuVendor.IsAmd && 无 NVIDIA 独显 ✅。
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
	/// ⭐ 星耀 AMD（XINGYAO / P916F）**窄判据**：星耀平台 + AMD + 无 NVIDIA 独显（不含 SMU 就绪）。
	/// <para>与 <see cref="IsWujieApu" /> **互斥**：无界机型继续走原判据，星耀不改变
	/// <c>WujieSmuApu</c> 语义，也不打开无界专属的风扇 EC／EC 限充／键盘协议。</para>
	/// </summary>
	internal static bool IsXingyaoAmdApu()
	{
		try
		{
			if (!XingyaoPmc2.IsXingyaoPlatform())
			{
				return false;
			}
			WujieSmuVerdict verdict = Evaluate();
			// 无界机型保持原判据（避免 WJ 载荷同时命中时把无界当星耀）
			return !verdict.IsWujie && verdict.IsAmd && !verdict.HasNvidia;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>⭐ 星耀 AMD **且** SMU 通路可用 ⇒ 允许「SMU 自定义」（窄判据全真）。</summary>
	internal static bool IsXingyaoSmuAllowed()
	{
		try
		{
			return IsXingyaoAmdApu() && SmuAvailability.IsReady;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// ⭐ 只读诊断日志（去重）：星耀 AMD 平台才打印，便于机主确认「SMU 自定义」是否开放；
	/// ⛔ 不改变任何能力/不写硬件。
	/// </summary>
	internal static void LogXingyaoPredicate()
	{
		try
		{
			if (!XingyaoPmc2.IsXingyaoPlatform())
			{
				return;
			}
			WujieSmuVerdict verdict = Evaluate();
			bool xingyao = !verdict.IsWujie && verdict.IsAmd && !verdict.HasNvidia;
			bool allowed = xingyao && verdict.SmuReady;
			string message = "[smu-ui] 星耀 AMD 判据：model=" + verdict.Model
				+ " · xingyao=" + xingyao + " · amd=" + verdict.IsAmd
				+ " · noNvidia=" + (!verdict.HasNvidia) + " · smuReady=" + verdict.SmuReady
				+ " ⇒ SMU自定义=" + (allowed ? "True" : "False")
				+ (allowed ? "（仅 SMU 自定义；无界专属风扇EC/限充/键盘保持关闭）" : "");
			if (Logger.TryMarkChanged("xingyao-smu-gate", message))
			{
				Logger.WriteError(message);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[smu-ui] 星耀 AMD 判据异常（已吞掉，按不开放处理）：" + ex.Message);
		}
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
				+ "（model=" + verdict.Model + " · 命中字段=" + verdict.HitField + " · 值=" + verdict.HitValue
				+ " · code=" + code + " · smuVer=" + version + "）· PL上限=" + PlMaxWatts + "W";
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
	// <para> 返回命中字段/值（供日志：15X 上来时一眼看出靠哪个字段命中）。</para>
	internal static bool IsWujieMachine(string model, string family, string sku, string board, out string hitField, out string hitValue)
	{
		string[] fields = new string[4] { "Name", "Family", "SKU", "Board" };
		string[] values = new string[4]
		{
			model ?? "",
			family ?? "",
			sku ?? "",
			board ?? ""
		};
		for (int i = 0; i < values.Length; i++)
		{
			string normalized = values[i].Replace(" ", "").Replace("\u3000", "");
			if (normalized.Length == 0)
			{
				continue;
			}
			foreach (string keyword in WujieModelKeywords)
			{
				if (normalized.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					hitField = fields[i];
					hitValue = values[i];
					return true;
				}
			}
		}
		hitField = "";
		hitValue = "";
		return false;
	}

	/// <summary>⭐ 兼容旧入口：只按 SystemProductName 判（内部/诊断用）。</summary>
	internal static bool IsWujieModel(string model)
	{
		return IsWujieMachine(model, "", "", "", out string _, out string _);
	}
	/// <summary>⭐ 执行只读判据（进程内不缓存结果本身，但底层探测各自有缓存/只读）。</summary>
	internal static WujieSmuVerdict Evaluate()
	{
		MechrevoDeviceCapabilities caps = MechrevoDeviceCapabilities.Current;
		string model = caps.Model ?? "";
		string board = caps.BaseboardProduct ?? "";
		string family = caps.SystemFamily ?? "";
		string sku = caps.SystemSku ?? "";
		string hitField;
		string hitValue;
		bool isWujie = IsWujieMachine(model, family, sku, board, out hitField, out hitValue);
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
			isWujie, smuReady, smuReason, hitField, hitValue);
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
				+ " ⇒ SMU自定义=" + (verdict.Allowed ? "True" : "False")
				+ "（命中字段=" + (string.IsNullOrEmpty(verdict.HitField) ? "无" : verdict.HitField) + " · 值=" + (string.IsNullOrEmpty(verdict.HitValue) ? "-" : verdict.HitValue) + "）";
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
