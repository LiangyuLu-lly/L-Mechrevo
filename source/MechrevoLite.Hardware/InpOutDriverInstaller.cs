using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-22（无界集成）：inpoutx64 驱动自动安装。
/// LM 安装包会带 `inpoutx64.dll` + `inpoutx64/inpoutx64.sys`；安装器已在无界机型注册服务。
/// 本类用于「便携运行 / 安装器未跑 / 驱动被卸载」时兜底：把随包 sys 复制到 System32\drivers 并注册/启动服务。
/// 仅无界机型允许执行；需要管理员权限（LM 本身以管理员运行）。
/// </summary>
internal static class InpOutDriverInstaller
{
	private const string ServiceName = "inpoutx64";

	/// <summary>确保 I2EC 可用：已可用直接返回；否则尝试安装随包驱动。失败只返回 false，不抛异常。</summary>
	internal static bool TryEnsureInstalled(out string detail)
	{
		if (I2ecInpOut.IsAvailable)
		{
			detail = "inpoutx64 已可用";
			return true;
		}
		if (!IsWujieMachine())
		{
			detail = "非无界机型 ⇒ 不安装 inpoutx64 驱动";
			return false;
		}
		try
		{
			string sysSource = FindDriverSource();
			if (string.IsNullOrEmpty(sysSource))
			{
				detail = "随包缺少 inpoutx64.sys（应在 {app}\\inpoutx64\\inpoutx64.sys）";
				return false;
			}
			string sysTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "inpoutx64.sys");
			File.Copy(sysSource, sysTarget, overwrite: true);
			string createArgs = "create " + ServiceName + " type= kernel start= auto binPath= " + sysTarget;
			if (!RunSc(createArgs, out string createDetail) || createDetail.IndexOf("1056", StringComparison.Ordinal) >= 0 || createDetail.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				// 服务已存在（1056）或创建失败但可能已存在 ⇒ 尝试修正 binPath 后再启动。
				RunSc("config " + ServiceName + " binPath= " + sysTarget, out _);
			}
			RunSc("start " + ServiceName, out string startDetail);
			Thread.Sleep(400);
			I2ecInpOut.ResetInitialization();
			bool ok = I2ecInpOut.IsAvailable;
			detail = (ok ? "驱动已安装并可用" : "驱动安装后仍不可用（start: " + startDetail.Trim() + "）");
			Logger.WriteLine("[inpout] 自动安装驱动：" + detail + " · sys=" + sysSource);
			return ok;
		}
		catch (Exception ex)
		{
			detail = "驱动安装异常：" + ex.GetType().Name + " " + ex.Message;
			Logger.WriteLine("[inpout] " + detail);
			return false;
		}
	}

	private static bool IsWujieMachine()
	{
		try
		{
			if (Program.hw?.WujieSmuApu == true)
			{
				return true;
			}
			string model = MechrevoDeviceCapabilities.Current.Model ?? "";
			return model.IndexOf("WUJIE", StringComparison.OrdinalIgnoreCase) >= 0
				|| model.IndexOf("无界", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch
		{
			return false;
		}
	}

	private static string FindDriverSource()
	{
		string[] candidates = new string[]
		{
			Path.Combine(AppContext.BaseDirectory, "inpoutx64", "inpoutx64.sys"),
			Path.Combine(AppContext.BaseDirectory, "inpoutx64.sys")
		};
		foreach (string candidate in candidates)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		return "";
	}

	private static bool RunSc(string arguments, out string output)
	{
		output = "";
		try
		{
			using Process process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"),
					Arguments = arguments,
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				}
			};
			process.Start();
			output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
			process.WaitForExit(5000);
			return process.ExitCode == 0;
		}
		catch (Exception ex)
		{
			output = ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}
}
