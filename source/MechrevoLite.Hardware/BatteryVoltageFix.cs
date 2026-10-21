using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>
/// 8000 系无界的电池充电电压修复（外挂式）：
/// 调用用户本机已安装的 JG_BAT 工具（batteryctl.py）执行 hold-full，
/// 覆盖 EC 的辅助老化降额（17600mV → 16600mV）。
/// <para>LM 自身不注入任何驱动，只调用外部 Python 工具；找不到工具/依赖时如实返回失败。</para>
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
		}
	}

	internal static bool IsSupportedMachine()
	{
		MechrevoHw hw = Program.hw;
		return hw != null && hw.WujieSmuApu;
	}

	internal static string DescribeAvailability()
	{
		if (FindTool(out string detail) != null)
		{
			return "工具可用：" + detail;
		}
		return "工具不可用：" + detail;
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

	/// <summary>执行 batteryctl.py 的一个子命令，返回是否成功与输出摘要。</summary>
	internal static (bool Ok, string Detail) Run(string argument, int timeoutMs = 30000)
	{
		string tool = FindTool(out string findDetail);
		if (tool == null)
		{
			return (Ok: false, Detail: findDetail);
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
				return (Ok: false, Detail: "超时（" + timeoutMs + "ms）");
			}
			proc.WaitForExit();
			string text = sb.ToString().Trim();
			bool ok = proc.ExitCode == 0;
			string detail = text.Length == 0 ? ("退出码=" + proc.ExitCode) : "退出码=" + proc.ExitCode + " · " + Truncate(text, 600);
			return (Ok: ok, Detail: detail);
		}
		catch (Exception ex)
		{
			return (Ok: false, Detail: ex.GetType().Name + ": " + ex.Message);
		}
	}

	/// <summary>只读状态（batteryctl.py status），用于界面显示当前电压/是否降额。</summary>
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
