using System;
using System.Diagnostics;
using System.IO;
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
			if (text.Length != 0)
			{
				return text;
			}
			return DetectedVariant;
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

	/// <summary>已安装 GCU 的 GCUService.exe 候选路径（落盘目录名以 AiStoneService 为准，兼容旧 UniwillService）。</summary>
	private static string[] ServiceExeCandidates()
	{
		string root = Path.Combine(AppContext.BaseDirectory, "GCU");
		return new string[]
		{
			Path.Combine(root, "AiStoneService", "MyControlCenter", "GCUService.exe"),
			Path.Combine(root, "UniwillService", "MyControlCenter", "GCUService.exe"),
			Path.Combine(root, "GCUService.exe")
		};
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
