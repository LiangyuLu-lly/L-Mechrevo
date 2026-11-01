using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace MechrevoLite.Helpers;

/// <summary>
/// 2026-10-10（用户要求「把禁用 Windows 更新功能加入电源与系统」）：
/// 「禁用 Windows 更新」开关 —— 动作完全复刻用户提供的 Windows更新启用禁用.bat
/// （停/启 wuauserv + UsoSvc、策略注册表 NoAutoUpdate/DisableWindowsUpdateAccess、
/// WaaSMedicSvc Start=4、禁用/启用三个 WindowsUpdate 计划任务），不走 GCU。
/// 开关语义：开 = 禁用 Windows 自动更新，关 = 恢复自动更新。
/// 依赖管理员权限：LM 以 requireAdministrator 运行，正常恒为管理员。
/// </summary>
/// <remarks>
/// 2026-10-10（修用户反馈「禁用Windows更新开关打不开」）：用户报「打不开」，并说明
/// 「是参考这个脚本的 脚本是可以用的」，而命令/键/任务名与参考脚本逐字一致 ⇒ 差异只在退出码处理。
/// 原实现用 &amp;&amp; 短路串起全部动作，第一步 net stop wuauserv 一失败就整条中断
/// （证据：日志 11 次 allOk=False、零异常、wuauserv 的 Start 仍=2 ⇒ 第 2 步从未执行），
/// 返回 false 后 UI 又把勾静默拨回，用户看到的就是「打不开」。现改为与 .bat 等价的语义：
/// 逐项无条件执行、逐项收结果，某项失败不影响后续项；任务不存在记为跳过（本机 sih/sihboot 不存在）；
/// 并把「必需项」与「可选项」分开判定 <see cref="Result.Ok" />。
/// </remarks>
internal static class WindowsUpdateGuard
{
	private const string WuPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
	private const string WuPolicyAuKey = WuPolicyKey + @"\AU";
	private const string WaaSMedicKey = @"SYSTEM\CurrentControlSet\Services\WaaSMedicSvc";
	private const string WuauservKey = @"SYSTEM\CurrentControlSet\Services\wuauserv";

	private static readonly string[] UpdateTasks =
	{
		@"Microsoft\Windows\WindowsUpdate\Scheduled Start",
		@"Microsoft\Windows\WindowsUpdate\sih",
		@"Microsoft\Windows\WindowsUpdate\sihboot"
	};

	/// <summary>
	/// 2026-10-10：一次开关动作的结果 —— <see cref="Ok" /> + 失败项 + 跳过项。
	/// 判定：<see cref="Failed" /> 为空即 <see cref="Ok" />（必需项全生效 = 与 .bat 等效）；
	/// <see cref="Skipped" /> 只用于可选项（本机不存在的计划任务 / 受保护的 WaaSMedicSvc），不计入失败。
	/// </summary>
	internal sealed class Result
	{
		private readonly List<string> _failed = new List<string>();
		private readonly List<string> _skipped = new List<string>();
		private readonly List<string> _done = new List<string>();

		/// <summary>必需项是否全部生效（<see cref="Failed" /> 为空）。</summary>
		public bool Ok => _failed.Count == 0;

		/// <summary>失败的必需项（"动作：原因"）。</summary>
		public IReadOnlyList<string> Failed => _failed;

		/// <summary>被跳过的可选项（"动作：原因"），不影响 <see cref="Ok" />。</summary>
		public IReadOnlyList<string> Skipped => _skipped;

		/// <summary>已生效的必需项。</summary>
		public IReadOnlyList<string> Done => _done;

		/// <summary>记录一项必需动作的结果。</summary>
		public void Add(string step, bool ok, string detail)
		{
			if (ok)
			{
				_done.Add(step);
				return;
			}
			_failed.Add(step + "：" + detail);
		}

		/// <summary>记录一项可选动作被跳过（不计入失败）。</summary>
		public void Skip(string step, string why)
		{
			_skipped.Add(step + "：" + why);
		}

		/// <summary>写日志用的一行摘要（成功项数 / 失败项 / 跳过项）。</summary>
		public string Summary()
		{
			string text = $"ok={_done.Count} failed={((_failed.Count == 0) ? "无" : string.Join("；", _failed))}";
			if (_skipped.Count > 0)
			{
				text = text + " skipped=" + string.Join("；", _skipped);
			}
			return text;
		}

		/// <summary>给用户看的一行明细：成功的必需项数 / 失败项 / 跳过项。</summary>
		public string UserDetail()
		{
			string text = $"成功 {_done.Count} 项必需动作";
			if (_failed.Count > 0)
			{
				text = text + "；失败：" + string.Join("；", _failed);
			}
			if (_skipped.Count > 0)
			{
				text = text + "；跳过（可选项）：" + string.Join("；", _skipped);
			}
			return text;
		}

		public override string ToString()
		{
			return "Ok=" + Ok + " " + Summary();
		}
	}

	/// <summary>当前是否处于「禁用 Windows 自动更新」状态：任一信号命中即视为禁用中。</summary>
	public static bool? IsDisabled()
	{
		try
		{
			using RegistryKey? au = Registry.LocalMachine.OpenSubKey(WuPolicyAuKey);
			if (au?.GetValue("NoAutoUpdate") is int noAuto && noAuto == 1)
			{
				return true;
			}
			using RegistryKey? wu = Registry.LocalMachine.OpenSubKey(WuPolicyKey);
			if (wu?.GetValue("DisableWindowsUpdateAccess") is int noAccess && noAccess == 1)
			{
				return true;
			}
			if (GetServiceStart(WuauservKey) == 4 || GetServiceStart(WaaSMedicKey) == 4)
			{
				return true;
			}
			return false;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Windows update state read failed: " + ex.Message);
			return null;
		}
	}

	/// <summary>
	/// 开(禁用)：停服务 + 写策略注册表 + 禁计划任务；关(恢复)：全部还原。
	/// 2026-10-10：逐项执行、逐项收结果，不再用 &amp;&amp; 短路（某项失败不影响后续项，与参考 .bat 一致）；
	/// 必需项/可选项的划分见 <see cref="CollectSteps" /> 的注释。
	/// </summary>
	public static Result SetDisabled(bool disable)
	{
		Result result = new Result();
		try
		{
			foreach ((string Label, Action<Result> Step) step in CollectSteps(disable))
			{
				try
				{
					step.Step(result);
				}
				catch (Exception ex)
				{
					// 单项异常只记这一项失败，后续项照常执行（对齐 .bat 的「忽略失败、继续下一条」）。
					result.Add(step.Label, ok: false, ex.Message);
					Logger.WriteLine($"Windows update step {step.Label} threw: " + ex.Message);
				}
			}
			Logger.WriteLine($"WindowsUpdateGuard.SetDisabled({disable}) allOk={result.Ok} {result.Summary()}");
			return result;
		}
		catch (Exception ex)
		{
			result.Add("开关动作", ok: false, ex.Message);
			Logger.WriteLine("Windows update switch failed: " + ex.Message);
			return result;
		}
	}

	/// <summary>
	/// 2026-10-10：把一次开关（禁用 / 还原）拆成有序的「必需项」清单，逐项独立执行、互不短路。
	/// 必需项（决定 <see cref="Result.Ok" />）：wuauserv 停/启 + 启动类型、UsoSvc 停/启 + 启动类型、
	/// 策略注册表（NoAutoUpdate / DisableWindowsUpdateAccess，还原时删键）、三个计划任务的启用状态。
	/// 可选项（失败只警告、记 <see cref="Result.Skipped" />，不影响 Ok）：WaaSMedicSvc 的 Start 值，
	/// 以及本机不存在的计划任务（sih / sihboot，schtasks 退出码 1/2）。
	/// </summary>
	private static IEnumerable<(string Label, Action<Result> Step)> CollectSteps(bool disable)
	{
		if (disable)
		{
			yield return ("停止 wuauserv", delegate (Result r)
			{
				r.Add("停止 wuauserv", Run("net", "stop", "wuauserv") == 0, "退出码非 0");
			});
			yield return ("禁用 wuauserv 启动类型", delegate (Result r)
			{
				r.Add("禁用 wuauserv 启动类型", Run("sc", "config", "wuauserv", "start=disabled") == 0, "退出码非 0");
			});
			yield return ("停止 UsoSvc", delegate (Result r)
			{
				r.Add("停止 UsoSvc", Run("net", "stop", "UsoSvc") == 0, "退出码非 0");
			});
			yield return ("禁用 UsoSvc 启动类型", delegate (Result r)
			{
				r.Add("禁用 UsoSvc 启动类型", Run("sc", "config", "UsoSvc", "start=disabled") == 0, "退出码非 0");
			});
			yield return ("写入 Windows 更新策略", delegate (Result r)
			{
				WritePolicy(r, disabled: true);
				OptionalWaasMedicStart(r, start: 4);
			});
			yield return ("禁用计划任务", delegate (Result r)
			{
				SetTasksEnabled(r, enable: false);
			});
		}
		else
		{
			yield return ("恢复 wuauserv 启动类型", delegate (Result r)
			{
				r.Add("恢复 wuauserv 启动类型 auto", Run("sc", "config", "wuauserv", "start=auto") == 0, "退出码非 0");
			});
			yield return ("启动 wuauserv", delegate (Result r)
			{
				r.Add("启动 wuauserv", Run("net", "start", "wuauserv") == 0, "退出码非 0");
			});
			yield return ("恢复 UsoSvc 启动类型", delegate (Result r)
			{
				r.Add("恢复 UsoSvc 启动类型 auto", Run("sc", "config", "UsoSvc", "start=auto") == 0, "退出码非 0");
			});
			yield return ("启动 UsoSvc", delegate (Result r)
			{
				r.Add("启动 UsoSvc", Run("net", "start", "UsoSvc") == 0, "退出码非 0");
			});
			yield return ("清除 Windows 更新策略", delegate (Result r)
			{
				WritePolicy(r, disabled: false);
				RestoreWaasMedicStart(r);
			});
			yield return ("启用计划任务", delegate (Result r)
			{
				SetTasksEnabled(r, enable: true);
			});
		}
	}

	/// <summary>
	/// 2026-10-10：策略注册表按必需项逐条写、逐条收结果（原实现只返回常量 true，
	/// 写失败会被当成成功；现在失败进失败清单，但同样不影响其它项）。
	/// </summary>
	private static void WritePolicy(Result result, bool disabled)
	{
		if (disabled)
		{
			TryWritePolicy("策略 NoAutoUpdate=1", result, delegate
			{
				using RegistryKey au = Registry.LocalMachine.CreateSubKey(WuPolicyAuKey);
				au.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
			});

			TryWritePolicy("策略 DisableWindowsUpdateAccess=1", result, delegate
			{
				using RegistryKey wu = Registry.LocalMachine.CreateSubKey(WuPolicyKey);
				wu.SetValue("DisableWindowsUpdateAccess", 1, RegistryValueKind.DWord);
			});
			return;
		}
		TryDeletePolicyKey(result);
	}

	/// <summary>写一条策略注册表值；成功记成功项，失败把原因记进失败清单。</summary>
	private static void TryWritePolicy(string label, Result result, Action write)
	{
		try
		{
			write();
			result.Add(label, ok: true, "");
		}
		catch (Exception ex)
		{
			result.Add(label, ok: false, "写入注册表失败或被拒绝：" + ex.Message);
			Logger.WriteLine("Windows update policy write failed: " + ex.Message);
		}
	}

	/// <summary>还原时删掉整棵 WindowsUpdate 策略键树；失败把原因记进失败清单。</summary>
	private static void TryDeletePolicyKey(Result result)
	{
		try
		{
			Registry.LocalMachine.DeleteSubKeyTree(WuPolicyKey, throwOnMissingSubKey: false);
			result.Add("清除 WindowsUpdate 策略键", ok: true, "");
		}
		catch (Exception ex)
		{
			result.Add("清除 WindowsUpdate 策略键", ok: false, "删除注册表键失败或被拒绝：" + ex.Message);
			Logger.WriteLine("Windows update policy key removal failed: " + ex.Message);
		}
	}

	/// <summary>
	/// 2026-10-10（修「开关打不开」）：可选项 —— WaaSMedicSvc 是受保护服务（LaunchProtected=2），
	/// 写 Start 可能被系统拒绝；失败只警告并记 skip，不计入整体失败（对齐 .bat 里被忽略的那条）。
	/// </summary>
	private static void OptionalWaasMedicStart(Result result, int start)
	{
		try
		{
			using RegistryKey medic = Registry.LocalMachine.CreateSubKey(WaaSMedicKey);
			medic.SetValue("Start", start, RegistryValueKind.DWord);
		}
		catch (Exception ex)
		{
			result.Skip("WaaSMedicSvc Start=" + start, "受保护服务（LaunchProtected=2）拒绝写入：" + ex.Message);
			Logger.WriteLine("Windows update WaaSMedicSvc Start write skipped: " + ex.Message);
		}
	}

	/// <summary>还原路径：清掉 WaaSMedicSvc\Start（可选项，受保护服务失败只警告）。</summary>
	private static void RestoreWaasMedicStart(Result result)
	{
		try
		{
			using RegistryKey? medic = Registry.LocalMachine.OpenSubKey(WaaSMedicKey, writable: true);
			medic?.DeleteValue("Start", throwOnMissingValue: false);
		}
		catch (Exception ex)
		{
			result.Skip("WaaSMedicSvc Start 还原", "受保护服务（LaunchProtected=2）拒绝写入：" + ex.Message);
			Logger.WriteLine("Windows update WaaSMedicSvc Start restore skipped: " + ex.Message);
		}
	}

	/// <summary>
	/// 2026-10-10：三个计划任务逐个处理 —— 成功记成功，<b>任务不存在记 skip（不算失败）</b>
	/// （本机只有 Scheduled Start，sih / sihboot 不存在），其余失败才记失败项。
	/// </summary>
	private static void SetTasksEnabled(Result result, bool enable)
	{
		string flag = enable ? "/ENABLE" : "/DISABLE";
		foreach (string task in UpdateTasks)
		{
			int exitCode = Run("schtasks", "/Change", "/TN", task, flag);
			if (exitCode == 0)
			{
				result.Add("计划任务 " + task + " -> " + flag, ok: true, "");
				Logger.WriteLine($"Windows update task {task} -> {flag} ok");
			}
			else if (IsTaskMissingExitCode(exitCode))
			{
				result.Skip("计划任务 " + task, "本机不存在（schtasks 退出码 " + exitCode + "）");
				Logger.WriteLine($"Windows update task {task} missing, skipped (exit {exitCode})");
			}
			else
			{
				result.Add("计划任务 " + task + " -> " + flag, ok: false, "schtasks 退出码 " + exitCode);
				Logger.WriteLine($"Windows update task {task} -> {flag} failed, exit {exitCode}");
			}
		}
	}

	/// <summary>schtasks 的「任务不存在」退出码：1(ERROR_INVALID_FUNCTION) / 2(ERROR_FILE_NOT_FOUND)。</summary>
	private static bool IsTaskMissingExitCode(int exitCode)
	{
		return exitCode == 1 || exitCode == 2;
	}

	private static int? GetServiceStart(string keyPath)
	{
		using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath);
		return key?.GetValue("Start") is int start ? start : null;
	}

	/// <summary>
	/// 2026-10-10：改为返回退出码并重定向 stdout/stderr，把 sc / net / schtasks 的原始输出写进日志
	/// （便于事后列出「哪几项成功 / 哪几项失败」）；启动或执行失败返回 -1。
	/// </summary>
	private static int Run(string exe, params string[] args)
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo
			{
				FileName = exe,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			foreach (string arg in args)
			{
				psi.ArgumentList.Add(arg);
			}
			using Process? p = Process.Start(psi);
			if (p == null)
			{
				Logger.WriteLine($"{exe} {string.Join(' ', args)} start failed: process is null");
				return -1;
			}
			string stdout = p.StandardOutput.ReadToEnd();
			string stderr = p.StandardError.ReadToEnd();
			p.WaitForExit();
			int exitCode = p.ExitCode;
			string text = $"{exe} {string.Join(' ', args)} exit={exitCode}";
			if (stdout.Trim().Length > 0)
			{
				text = text + " out=" + Flatten(stdout);
			}
			if (stderr.Trim().Length > 0)
			{
				text = text + " err=" + Flatten(stderr);
			}
			Logger.WriteLine(text);
			return exitCode;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"{exe} run failed: " + ex.Message);
			return -1;
		}
	}

	/// <summary>把命令输出压成日志里的一行（换行换成分隔符）。</summary>
	private static string Flatten(string text)
	{
		return text.Trim().Replace("\r\n", " | ").Replace('\r', ' ').Replace('\n', ' ');
	}
}
