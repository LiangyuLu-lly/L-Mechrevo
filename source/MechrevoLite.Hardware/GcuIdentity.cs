using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using MechrevoLite.Helpers;

namespace MechrevoLite.Hardware;

/// <summary>
/// GCU 变体的**统一来源**：优先用安装器登记（HKLM\SOFTWARE\L-Mechrevo\GcuVariant），
/// 登记缺失时按已安装 GCU 包的特征识别（服务 exe 版本 / 体积 / 目录形态）。
/// <para>特征口径（2026-10-20 定）：</para>
/// <list type="bullet">
/// <item>1.2.x ⇒ 50 系 GCU；</item>
/// <item>1.0.2.70 且 exe 体积大（&gt; 5 MB）⇒ 40 家族（40 / 40B 同形态）；</item>
/// <item>1.0.2.70 且 exe 体积小（30 系那份很散、exe 约 1.7 MB）⇒ 30 系；</item>
/// <item>更老版本（小体积）⇒ 10/16/20 系（1020）。</item>
/// </list>
/// <para>注册表登记存在时一律以登记为准；识别结果只做兜底，读不到时返回空串（不猜）。</para>
/// </summary>
internal static class GcuIdentity
{
	private static readonly Lazy<string> CachedRegistry = new Lazy<string>(ReadRegistryVariant);

	private static readonly Lazy<string> CachedDetected = new Lazy<string>(DetectFromInstalledPackage);

	/// <summary>安装器登记的变体（读不到 ⇒ 空串）。</summary>
	internal static string RegistryVariant => CachedRegistry.Value;

	/// <summary>按已安装 GCU 包特征识别的变体（识别不出 ⇒ 空串）。</summary>
	internal static string DetectedVariant => CachedDetected.Value;

	/// <summary>生效变体：登记优先，缺失时用特征识别结果。</summary>
	internal static string Variant
	{
		get
		{
			string text = RegistryVariant;
			string text2 = DetectedVariant;
			// 2026-10-22（机主：装的是 50 GCU，注册表却写 40）：安装器登记的变体与**实际载荷特征**冲突时，
			// 以实际载荷为准（否则所有「仅 50 系」判据都被注册表挡住：显卡超频/单分区键盘等）。
			// 只在识别结果明确为 50、登记不是 50 时覆盖；其余情况仍以登记为准（零回归）。
			if (text.Length != 0 && !string.Equals(text, "50", StringComparison.OrdinalIgnoreCase)
				&& string.Equals(text2, "50", StringComparison.OrdinalIgnoreCase))
			{
				Logger.WriteError("[gcu] 变体冲突：注册表 GcuVariant=" + text + " 但实际载荷识别=50 ⇒ 以实际载荷为准（按 50 系处理）");
				// 实际载荷已经是 50 时同步安装标记，避免每次启动重复报冲突。
				PersistCorrectedVariant("50");
				return "50";
			}
			if (text.Length != 0)
			{
				return text;
			}
			return text2;
		}
	}

	private static int _correctedVariantPersisted;

	/// <summary>把安装器登记的 GcuVariant 修正为实际载荷变体（只做一次，失败只记日志）。</summary>
	private static void PersistCorrectedVariant(string variant)
	{
		if (Interlocked.Exchange(ref _correctedVariantPersisted, 1) != 0)
		{
			return;
		}
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\L-Mechrevo", writable: true);
			if (key == null)
			{
				return;
			}
			key.SetValue("GcuVariant", variant, RegistryValueKind.String);
			Logger.WriteLine("[gcu] 已同步注册表 GcuVariant=" + variant + "（与实际载荷一致）");
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[gcu] 同步注册表 GcuVariant 失败（忽略）：" + ex.Message);
		}
	}

	private static string ReadRegistryVariant()
	{
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\L-Mechrevo");
			return (key?.GetValue("GcuVariant") as string)?.Trim() ?? "";
		}
		catch
		{
			return "";
		}
	}

	/// <summary>已安装 GCU 的 GCUService.exe 候选路径（落盘目录名以 AiStoneService 为准，兼容旧 UniwillService）。
	/// 除 LM 自带 GCU 目录外，也检查厂商控制台在 Program Files\OEM 下的安装位置；40 系装 50 GCU
	/// 或用户保留官方控制台时，实际载荷可能只在官方目录里。</summary>
	private static string[] ServiceExeCandidates()
	{
		List<string> candidates = new List<string>();
		string root = Path.Combine(AppContext.BaseDirectory, "GCU");
		candidates.Add(Path.Combine(root, "AiStoneService", "MyControlCenter", "GCUService.exe"));
		candidates.Add(Path.Combine(root, "UniwillService", "MyControlCenter", "GCUService.exe"));
		candidates.Add(Path.Combine(root, "GCUService.exe"));
		try
		{
			// WPF 便携/alt 构建可能不在安装目录，但机器上装有 LM 的 GCU；补查安装目录。
			string installedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "L-Mechrevo", "GCU");
			candidates.Add(Path.Combine(installedRoot, "AiStoneService", "MyControlCenter", "GCUService.exe"));
			candidates.Add(Path.Combine(installedRoot, "UniwillService", "MyControlCenter", "GCUService.exe"));
			candidates.Add(Path.Combine(installedRoot, "GCUService.exe"));
		}
		catch
		{
		}
		try
		{
			string oem = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OEM");
			if (Directory.Exists(oem))
			{
				foreach (string dir in Directory.EnumerateDirectories(oem))
				{
					candidates.Add(Path.Combine(dir, "AiStoneService", "MyControlCenter", "GCUService.exe"));
					candidates.Add(Path.Combine(dir, "UniwillService", "MyControlCenter", "GCUService.exe"));
					candidates.Add(Path.Combine(dir, "GCUService.exe"));
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[gcu] 枚举官方控制台目录失败（忽略）：" + ex.Message);
		}
		return candidates.ToArray();
	}

	private static string DetectFromInstalledPackage()
	{
		try
		{
			foreach (string path in ServiceExeCandidates())
			{
				if (!File.Exists(path))
				{
					continue;
				}
				FileInfo fileInfo = new FileInfo(path);
				FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(path);
				string version = (versionInfo.FileVersion ?? "").Trim();
				long size = fileInfo.Length;
				string variant;
				if (version.StartsWith("1.2.", StringComparison.Ordinal))
				{
					variant = "50";
				}
				else if (size > 5L * 1024 * 1024)
				{
					variant = "40";
				}
				else if (version.StartsWith("1.0.2.70", StringComparison.Ordinal))
				{
					variant = "30";
				}
				else
				{
					variant = "1020";
				}
				Logger.WriteLineIfChanged("gcu-identity", "[gcu] 变体特征识别：path=" + path
					+ " version=" + (version.Length == 0 ? "(无)" : version)
					+ " size=" + (size / 1024 / 1024) + "MB ⇒ " + variant
					+ "（注册表登记=" + (CachedRegistry.IsValueCreated ? (RegistryVariant.Length == 0 ? "(无)" : RegistryVariant) : "未读") + "）");
				return variant;
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[gcu] 变体特征识别失败（忽略）：" + ex.Message);
		}
		return "";
	}

	/// <summary>启动时预热：只做一次特征识别并落一行日志（失败静默）。</summary>
	internal static void Prewarm()
	{
		try
		{
			string text = Variant;
			Logger.WriteLineIfChanged("gcu-identity-final", "[gcu] 变体采用："
				+ (text.Length == 0 ? "(未知)" : text)
				+ "（登记=" + (RegistryVariant.Length == 0 ? "(无)" : RegistryVariant)
				+ " 特征=" + (DetectedVariant.Length == 0 ? "(无)" : DetectedVariant) + "）");
		}
		catch
		{
		}
	}
}
