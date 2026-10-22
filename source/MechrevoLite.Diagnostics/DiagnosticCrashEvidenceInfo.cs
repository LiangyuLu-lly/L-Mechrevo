using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MechrevoLite.Diagnostics;

/// <summary>
/// 2026-10-22（机主：0xc0000005 这种原生崩溃来不及写 crash.txt）：只读采集 Windows 侧崩溃证据 ——
/// Application Error 1000 / .NET Runtime 1026、WER LocalDumps 配置与转储文件清单、WER ReportArchive 目录。
/// 不读取转储内容、不修改任何注册表/文件。
/// </summary>
internal static class DiagnosticCrashEvidenceInfo
{
	private const string AppName = "L-Mechrevo.exe";

	internal static string Build()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[崩溃现场（WER / LocalDumps，只读）]");
		try
		{
			AppendLocalDumps(sb);
			AppendDumpFiles(sb, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"));
			AppendWerReports(sb);
			AppendEventLog(sb);
		}
		catch (Exception ex)
		{
			sb.AppendLine("⚠️ 崩溃现场采集异常（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
		}
		sb.AppendLine();
		return sb.ToString();
	}

	private static void AppendLocalDumps(StringBuilder sb)
	{
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\" + AppName);
			if (key == null)
			{
				sb.AppendLine("LocalDumps 配置：未设置（HKLM\\...\\LocalDumps\\" + AppName + " 不存在）");
				return;
			}
			string folder = key.GetValue("DumpFolder")?.ToString() ?? "";
			object dumpType = key.GetValue("DumpType");
			sb.AppendLine("LocalDumps 配置：DumpFolder=" + (folder.Length == 0 ? "(未设置)" : folder)
				+ " · DumpType=" + (dumpType?.ToString() ?? "(未设置)"));
			if (folder.Length > 0)
			{
				AppendDumpFiles(sb, folder);
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("LocalDumps 配置读取失败：" + ex.Message);
		}
	}

	private static void AppendDumpFiles(StringBuilder sb, string folder)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
			{
				sb.AppendLine("转储目录：" + (folder.Length == 0 ? "(未设置)" : folder + "（不存在）"));
				return;
			}
			List<FileInfo> files = new List<FileInfo>();
			foreach (string pattern in new string[2] { "L-Mechrevo*.dmp", "*.dmp" })
			{
				try
				{
					foreach (string path in Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly))
					{
						FileInfo info = new FileInfo(path);
						if (info.Name.IndexOf("L-Mechrevo", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							files.Add(info);
						}
					}
				}
				catch
				{
				}
				if (files.Count > 0)
				{
					break;
				}
			}
			files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
			sb.AppendLine("转储目录：" + folder + " ⇒ 命中 " + files.Count + " 个 L-Mechrevo 转储");
			int shown = 0;
			foreach (FileInfo file in files)
			{
				if (shown++ >= 10)
				{
					break;
				}
				sb.AppendLine("  · " + file.Name + "  " + file.Length + " B  " + file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("转储目录读取失败：" + ex.Message);
		}
	}

	private static void AppendWerReports(StringBuilder sb)
	{
		try
		{
			string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				@"Microsoft\Windows\WER\ReportArchive");
			if (!Directory.Exists(root))
			{
				sb.AppendLine("WER ReportArchive：目录不存在");
				return;
			}
			int count = 0;
			List<string> lines = new List<string>();
			foreach (string dir in Directory.GetDirectories(root, "*L-Mechrevo*", SearchOption.TopDirectoryOnly))
			{
				count++;
				if (lines.Count < 10)
				{
					lines.Add("  · " + Path.GetFileName(dir));
				}
			}
			sb.AppendLine("WER ReportArchive：命中 " + count + " 个 L-Mechrevo 报告");
			foreach (string line in lines)
			{
				sb.AppendLine(line);
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("WER ReportArchive 读取失败：" + ex.Message);
		}
	}

	private static void AppendEventLog(StringBuilder sb)
	{
		string output = RunWevtutil();
		if (string.IsNullOrWhiteSpace(output))
		{
			sb.AppendLine("Application 事件 1000/1026：读取失败或无输出（需要管理员权限/wevtutil 可用）");
			return;
		}
		if (output.IndexOf("L-Mechrevo", StringComparison.OrdinalIgnoreCase) < 0)
		{
			sb.AppendLine("Application 事件 1000/1026：最近 20 条里没有 L-Mechrevo 相关记录");
			return;
		}
		sb.AppendLine("Application 事件 1000/1026（含 L-Mechrevo，最多 20 条，已打码）：");
		sb.AppendLine(DiagnosticGcuEvidenceInfo.Mask(Truncate(output, 12000)));
	}

	private static string RunWevtutil()
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = "wevtutil.exe",
				Arguments = "qe Application /q:\"*[System[(EventID=1000 or EventID=1026)]]\" /c:20 /rd:true /f:text",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			};
			using Process process = Process.Start(psi);
			if (process == null)
			{
				return "";
			}
			string text = process.StandardOutput.ReadToEnd();
			process.WaitForExit(8000);
			return text.Trim();
		}
		catch
		{
			return "";
		}
	}

	private static string Truncate(string text, int max)
	{
		if (string.IsNullOrEmpty(text) || text.Length <= max)
		{
			return text;
		}
		return text.Substring(0, max) + "…（已截断，原长 " + text.Length + "）";
	}
}
