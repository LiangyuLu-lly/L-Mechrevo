using System;
using Microsoft.Win32;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-15（igpu-gen50-amd）：**CPU 厂商**的硬件事实判据（独立、可复用）。
///
/// 为什么必须新写一个：本仓库在此之前**没有任何**「CPU 厂商」判据 —— 现有两条都不可用：
///   * <c>MechrevoLite.Hardware.MechrevoHw.IsAmdPlatformForLimits</c>（→ <c>AmdPlatformReported</c> /
///     <c>Capabilities.AmdPlatform</c>）读的是 **GCU 上报的能力位**（MQTT <c>IsAMDPlatform</c>）。
///     能力位是**可被改动/可能出错**的软件结论，不是硬件事实 ⇒ 拿它当"代际平台级"判据，
///     诊断包会因为能力位被改而前后不一致（用户原话：「代际平台级优先能力位，要不然诊断因为能力被改会不准确」）。
///   * <c>PawnIO.CpuInfo.IsAMD</c> / <c>RuntimeCpuTuning.IsIntel()</c> 走 **CPUID**（真硬件事实），
///     但两者都是**布尔**、没有"未知"态，且 <c>IsAMD</c> 是 <c>readonly</c> 字段、
///     <c>IsIntel()</c> 是 **internal** —— 不满足"读不到必须返回未知、绝不默认当 AMD"的要求。
///
/// 判据来源（免 WMI、启动早期即可读）：
///   <c>HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0</c> 的 <c>VendorIdentifier</c>
///   —— <c>"AuthenticAMD"</c> = AMD，<c>"GenuineIntel"</c> = Intel。
///   ⚠️ 这是**只读**读取，不写任何注册表。
///
/// ⛔ 安全红线：读不到 / 不认识 / 抛异常 ⇒ 一律 <see cref="CpuVendorKind.Unknown" />。
/// **绝不默认当 AMD** —— 否则会把 Intel 机型的「纯核显模式」误藏。Unknown 在下游一律按
/// "不成立"处理（判据是 <c>== Amd</c>，不是 <c>!= Intel</c>）。
/// </summary>
internal static class CpuVendor
{
	/// <summary>本机 CPU 厂商（进程内缓存一次；见 <see cref="Detect" /> 的 Lazy）。</summary>
	internal static CpuVendorKind Kind => Cached.Value;

	/// <summary>是不是 **确认的** AMD。读不到 ⇒ false（⛔ 不是"因为不是 Intel 所以是 AMD"）。</summary>
	internal static bool IsAmd => Kind == CpuVendorKind.Amd;

	/// <summary>是不是 **确认的** Intel（读不到 ⇒ false）。</summary>
	internal static bool IsIntel => Kind == CpuVendorKind.Intel;

	/// <summary>
	/// 注册表里读到的**原始厂商串**（仅用于日志/诊断；读不到时为空串）。
	/// ⚠️ 不含任何序列号/用户名/机器名/MAC/GUID —— 只有厂商串本身。
	/// </summary>
	internal static string RawIdentifier => CachedRaw.Value;

	private const string CpuRegKey = "HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0";

	private const string VendorValueName = "VendorIdentifier";

	// 读一次即可（CPU 厂商在进程生命周期内不会变）。LazyThreadSafetyMode.ExecutionAndPublication
	// 保证多线程首次并发只跑一次 —— 属性会被 UI 线程与 GCU 回包线程同时读。
	private static readonly Lazy<CpuVendorKind> Cached = new Lazy<CpuVendorKind>(Detect, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

	private static readonly Lazy<string> CachedRaw = new Lazy<string>(DetectRaw, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

	private static CpuVendorKind Detect()
	{
		string raw = RawIdentifier;
		CpuVendorKind kind;
		if (string.Equals(raw, "AuthenticAMD", StringComparison.OrdinalIgnoreCase))
		{
			kind = CpuVendorKind.Amd;
		}
		else if (string.Equals(raw, "GenuineIntel", StringComparison.OrdinalIgnoreCase))
		{
			kind = CpuVendorKind.Intel;
		}
		else
		{
			// ⛔ 读不到（空串）或认不出的厂商串 ⇒ 未知。**不得**退化成 AMD。
			kind = CpuVendorKind.Unknown;
		}
		// 同一取值只写一次，不刷屏（与 GpuGenerationProvider / gcu 门同一套做法）。
		Logger.WriteLineIfChanged("cpu-vendor", $"cpu-vendor: VendorIdentifier={Describe(raw)} => isAmd={kind == CpuVendorKind.Amd} kind={kind}");
		return kind;
	}

	private static string DetectRaw()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(CpuRegKey);
			object value = registryKey?.GetValue(VendorValueName);
			if (value == null)
			{
				return "";
			}
			return (value as string)?.Trim() ?? value.ToString()?.Trim() ?? "";
		}
		catch (Exception ex)
		{
			// 读注册表失败（权限/异常）⇒ 空串 ⇒ 未知。⛔ 绝不外抛：本属性在判据链上被频繁读。
			Logger.WriteLineIfChanged("cpu-vendor-error", "cpu-vendor: read failed (" + ex.GetType().Name + ") => unknown");
			return "";
		}
	}

	private static string Describe(string raw)
	{
		return string.IsNullOrEmpty(raw) ? "(none)" : raw;
	}
}
