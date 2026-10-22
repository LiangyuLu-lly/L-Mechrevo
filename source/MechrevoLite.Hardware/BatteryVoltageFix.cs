using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>
/// 8000 系无界的电池充电电压修复：
/// <list type="number">
/// <item>优先 <b>内置</b>：直接经本机已安装的 inpoutx64 驱动访问 I2EC（不需要 Python / 外部脚本）；</item>
/// <item>回退 <b>外挂</b>：调用用户本机 JG_BAT 工具（batteryctl.py），需要 Python。</item>
/// </list>
/// <para>目标：覆盖 EC 辅助老化降额（17600mV → 16600mV），周期保持 17600mV/2816mA + 解停充门，停止时恢复原值。</para>
/// </summary>
internal static class BatteryVoltageFix
{
	internal const string ConfigKey = "battery_voltage_fix";

	private const string ToolFileName = "batteryctl.py";

	private static readonly string[] CandidateDirs =
	{
		@"E:\JG_BAT",
		@"D:\JG_BAT",
		@"C:\JG_BAT",
		Path.Combine(AppContext.BaseDirectory, "JG_BAT")
	};

	internal static bool Enabled
	{
		get
		{
			try
			{
				return AppConfig.Get(ConfigKey, 0) == 1;
			}
			catch
			{
				return false;
			}
		}
		set
		{
			AppConfig.Set(ConfigKey, value ? 1 : 0);
			AppConfig.Flush();
			if (!value)
			{
				// 关闭时停掉内置覆盖并恢复 EC 原值（外挂模式无需恢复：脚本已自行恢复）
				Stop();
			}
		}
	}

	internal static bool IsSupportedMachine()
	{
		MechrevoHw hw = Program.hw;
		return hw != null && hw.WujieSmuApu;
	}

	/// <summary>停掉内置覆盖（幂等）。</summary>
	internal static void Stop()
	{
		try
		{
			I2ecInpOut.Stop();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-fix] 停止内置覆盖异常（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
		}
	}

	internal static string DescribeAvailability()
	{
		if (I2ecInpOut.TryReadStatus(out string nativeDetail))
		{
			return "内置可用（不需要 Python）：\n" + nativeDetail;
		}
		if (FindTool(out string detail) != null)
		{
			return "内置不可用（" + I2ecInpOut.InitDetail + "）\n外挂工具可用：" + detail;
		}
		return "内置不可用（" + I2ecInpOut.InitDetail + "）；外挂工具不可用：" + detail;
	}

	private static string FindTool(out string detail)
	{
		foreach (string dir in CandidateDirs)
		{
			try
			{
				if (dir == null || !Directory.Exists(dir))
				{
					continue;
				}
				string py = Path.Combine(dir, ToolFileName);
				if (File.Exists(py))
				{
					string python = FindPython();
					if (python != null)
					{
						detail = py + "（python=" + python + "）";
						return py;
					}
					detail = py + " 存在，但找不到 python.exe（PATH）";
					return null;
				}
			}
			catch
			{
			}
		}
		detail = "未找到 " + ToolFileName + "（查找：E:\\JG_BAT / D:\\JG_BAT / C:\\JG_BAT / 程序目录\\JG_BAT）";
		return null;
	}

	private static string FindPython()
	{
		string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		foreach (string dir in path.Split(';'))
		{
			if (string.IsNullOrWhiteSpace(dir))
			{
				continue;
			}
			try
			{
				string exe = Path.Combine(dir.Trim(), "python.exe");
				if (File.Exists(exe))
				{
					return exe;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	/// <summary>
	/// 执行修复动作：<c>hold-full</c> 优先内置；<c>status</c> 优先内置；内置不可用时回退外挂 Python 工具。
	/// </summary>
	internal static (bool Ok, string Detail) Run(string argument, int timeoutMs = 30000)
	{
		// 2026-10-22（无界集成）：内置通道要求 inpoutx64；随包 DLL/sys 在但驱动没装时先自动安装一次。
		if (!I2ecInpOut.IsAvailable)
		{
			InpOutDriverInstaller.TryEnsureInstalled(out string installDetail);
			Logger.WriteLine("[battery-fix] inpoutx64 自动安装结果：" + installDetail);
		}
		if (string.Equals(argument, "hold-full", StringComparison.OrdinalIgnoreCase))
		{
			if (I2ecInpOut.TryStart(out string nativeStartDetail))
			{
				return (Ok: true, Detail: "内置（inpoutx64）：" + nativeStartDetail);
			}
			Logger.WriteLine("[battery-fix] 内置不可用 ⇒ 回退外挂 JG_BAT：" + nativeStartDetail);
		}
		else if (string.Equals(argument, "status", StringComparison.OrdinalIgnoreCase))
		{
			if (I2ecInpOut.TryReadStatus(out string nativeStatus))
			{
				return (Ok: true, Detail: nativeStatus);
			}
		}
		string tool = FindTool(out string findDetail);
		if (tool == null)
		{
			string nativeInfo = I2ecInpOut.InitDetail;
			return (Ok: false, Detail: "内置不可用（" + nativeInfo + "）；外挂工具不可用：" + findDetail);
		}
		string python = FindPython();
		if (python == null)
		{
			return (Ok: false, Detail: "找不到 python.exe");
		}
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = python,
				Arguments = "\"" + tool + "\" " + argument,
				WorkingDirectory = Path.GetDirectoryName(tool) ?? AppContext.BaseDirectory,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			};
			using Process proc = Process.Start(psi);
			if (proc == null)
			{
				return (Ok: false, Detail: "进程启动失败");
			}
			string text = RunExternal(proc, timeoutMs);
			bool ok = proc.ExitCode == 0;
			string detail = text.Length == 0 ? ("退出码=" + proc.ExitCode) : "退出码=" + proc.ExitCode + " · " + Truncate(text, 600);
			return (Ok: ok, Detail: detail);
		}
		catch (Exception ex)
		{
			return (Ok: false, Detail: ex.GetType().Name + ": " + ex.Message);
		}
	}

	private static string RunExternal(Process proc, int timeoutMs)
	{
		StringBuilder sb = new StringBuilder();
		proc.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				sb.AppendLine(e.Data);
			}
		};
		proc.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				sb.AppendLine(e.Data);
			}
		};
		proc.BeginOutputReadLine();
		proc.BeginErrorReadLine();
		bool exited = proc.WaitForExit(timeoutMs);
		if (!exited)
		{
			try
			{
				proc.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			return "(超时 " + timeoutMs + "ms)";
		}
		proc.WaitForExit();
		return sb.ToString().Trim();
	}

	/// <summary>只读状态（内置优先，其次外挂 batteryctl.py status）。</summary>
	internal static (bool Ok, string Detail) ReadStatus()
	{
		return Run("status", 20000);
	}

	internal static void RunOnStartupIfEnabled()
	{
		if (!Enabled || !IsSupportedMachine())
		{
			return;
		}
		_ = Task.Run(delegate
		{
			try
			{
				(bool ok, string detail) = Run("hold-full", 30000);
				Logger.WriteLine("[battery-fix] 开机 hold-full " + (ok ? "成功" : "失败") + " · " + detail);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[battery-fix] 开机 hold-full 异常（已吞掉）：" + ex.Message);
			}
		});
	}

	private static string Truncate(string text, int max)
	{
		string flat = text.Replace("\r", " ").Replace("\n", " | ");
		return flat.Length <= max ? flat : flat.Substring(0, max) + "…";
	}
}
