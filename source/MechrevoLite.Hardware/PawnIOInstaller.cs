// 2026-10-18（机主：设置页加 PawnIO 安装入口）：⭐⭐ **PawnIO 状态检测 + 安装入口（复用 Install-PawnIO.ps1）** ⭐⭐
//
// ⭐ 状态判据（机主要求）：注册表服务键 `HKLM\SYSTEM\CurrentControlSet\Services\PawnIO` 是否存在；
//    版本从卸载表 `...\Uninstall\*` 的 DisplayName 含 "PawnIO" 的 DisplayVersion 读取（64/32 位都查）。
// ⭐ 安装入口：**复用 CpuTuningForm 同一套** `{app}\Install-PawnIO.ps1` → powershell.exe -File（runas）；
//    ⛔ 不另写下载逻辑 ✅

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ PawnIO 驱动状态/安装（仅检测与启动现有脚本，不实现下载）✅。</summary>
internal static class PawnIOInstaller
{
	private const string ServiceKeyPath = "SYSTEM\\CurrentControlSet\\Services\\PawnIO";

	internal readonly record struct Status(bool Installed, string Version);

	internal static Status GetStatus()
	{
		bool installed = false;
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKeyPath, writable: false);
			installed = key != null;
		}
		catch
		{
			installed = false;
		}
		string version = "";
		try
		{
			version = FindVersion(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
				?? FindVersion(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
				?? "";
		}
		catch
		{
			version = "";
		}
		// 注册表键存在≠驱动真的能连上（卸载残留/服务未启动/指纹不符都会这样）。
		// 以实际 Connect 结果作为“已安装”判据，避免 UI 显示“已安装”但 EC 写入全失败。
		try
		{
			using PawnIO.PawnIOWrapper probe = new();
			installed = probe.Connect() == PawnIO.PawnIOWrapper.ConnectResult.OK;
		}
		catch
		{
			installed = false;
		}
		return new Status(installed, version);
	}

	private static string FindVersion(string uninstallRoot)
	{
		using RegistryKey root = Registry.LocalMachine.OpenSubKey(uninstallRoot, writable: false);
		if (root == null)
		{
			return null;
		}
		foreach (string subName in root.GetSubKeyNames())
		{
			try
			{
				using RegistryKey sub = root.OpenSubKey(subName, writable: false);
				string displayName = sub?.GetValue("DisplayName") as string ?? "";
				if (displayName.IndexOf("PawnIO", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					string displayVersion = (sub?.GetValue("DisplayVersion") as string ?? "").Trim();
					if (displayVersion.Length > 0)
					{
						return displayVersion;
					}
				}
			}
			catch
			{
				// 单个卸载项读不到就跳过（不阻塞状态显示）
			}
		}
		return null;
	}

	/// <summary>
	/// ⭐ 以管理员启动现有 `Install-PawnIO.ps1`（与 CpuTuningForm.InstallDriverAsync 同一入口/同一脚本）✅。
	/// <returns>脚本退出码；异常返回 -1（调用方如实提示）。</returns>
	/// </summary>
	internal static async Task<int> RunInstallerAsync()
	{
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo("powershell.exe")
			{
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden
			};
			startInfo.ArgumentList.Add("-NoProfile");
			startInfo.ArgumentList.Add("-ExecutionPolicy");
			startInfo.ArgumentList.Add("Bypass");
			startInfo.ArgumentList.Add("-File");
			startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Install-PawnIO.ps1"));
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				return -1;
			}
			await process.WaitForExitAsync();
			return process.ExitCode;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[pawnio] 启动 Install-PawnIO.ps1 失败：" + ex.GetType().Name + " " + ex.Message);
			return -1;
		}
	}
}
