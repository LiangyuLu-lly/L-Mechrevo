using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MechrevoLite;
using MechrevoLite.Helpers;
using MechrevoLite.Properties;
using Microsoft.Win32.TaskScheduler;

public class Startup
{
	internal enum StartupTaskAction
	{
		None,
		Register
	}

	private static string taskName = "LMechrevo";

	private static string chargeTaskName = taskName + "Charge";

	// 2026-10-22（无界电池电压修复）：开机保持任务（SYSTEM；防止 EC 老化降额重启后弹回）
	private static string batteryFixTaskName = taskName + "BatteryFix";

	private static readonly string strExeFilePath = ResolvePersistentExecutablePath();

	private static readonly string currentUserSid = ResolveCurrentUserSid();

	private static string userTaskName = taskName + "_" + currentUserSid;

	private static readonly string[] legacyTaskNames = new string[3]
	{
		"GHelper",
		"GHelperCharge",
		"GHelper_" + currentUserSid
	};

	internal const string RegisterAutostartArgument = "--register-autostart";

	internal static Func<bool?> ReadScheduledState { get; set; } = ReadScheduledStateFromSystem;

	internal static Func<bool, bool> WriteScheduledState { get; set; } = WriteScheduledStateToSystem;

	internal static string ScheduledExecutablePath => strExeFilePath;

	internal static Action<string> AutostartFailureSink { get; set; } = (string reason) =>
	{
		Logger.WriteLine("Autostart registration failed: " + reason);
	};

	private static string ResolveCurrentUserSid()
	{
		try
		{
			using WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent();
			string text = windowsIdentity.User?.Value;
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
			Logger.WriteLine("Current identity has no user SID; falling back to the account name for task naming.");
			return SanitizeTaskNameFragment(windowsIdentity.Name);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't resolve the current user SID: " + ex.Message);
			return "unknown";
		}
	}

	internal static bool ApplyScheduledState(bool enabled)
	{
		if (ReadScheduledState() == enabled)
		{
			return true;
		}
		return WriteScheduledState(enabled);
	}

	private static bool? ReadScheduledStateFromSystem()
	{
		try
		{
			return IsScheduled();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't read autostart state: " + ex.Message);
			return null;
		}
	}

	private static bool WriteScheduledStateToSystem(bool enabled)
	{
		if (!enabled)
		{
			return UnSchedule();
		}
		return Schedule();
	}

	internal static string NormalizeExecutablePath(string? exePath)
	{
		return (exePath ?? string.Empty).Trim().Trim('"');
	}

	internal static string ResolvePersistentExecutablePath()
	{
		return ResolvePersistentExecutablePath(Environment.ProcessPath, Application.ExecutablePath);
	}

	internal static string ResolvePersistentExecutablePath(string? processPath, string? executablePath)
	{
		string text = NormalizeExecutablePath(processPath);
		if (text.Length <= 0)
		{
			return NormalizeExecutablePath(executablePath);
		}
		return text;
	}

	internal static bool IsTransientExecutablePath(string? exePath)
	{
		return IsTransientExecutablePath(exePath, Path.GetTempPath());
	}

	internal static bool IsTransientExecutablePath(string? exePath, string? tempRoot)
	{
		if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(tempRoot))
		{
			return false;
		}
		try
		{
			string value = Path.GetFullPath(tempRoot).TrimEnd(new char[2]
			{
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			}) + Path.DirectorySeparatorChar;
			return Path.GetFullPath(NormalizeExecutablePath(exePath)).StartsWith(value, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	internal static string SanitizeTaskNameFragment(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "unknown";
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			bool flag = char.IsLetterOrDigit(c);
			if (!flag)
			{
				bool flag2 = ((c == '-' || c == '.') ? true : false);
				flag = flag2;
			}
			stringBuilder2.Append(flag ? c : '_');
		}
		return stringBuilder.ToString();
	}

	private static bool IsOwnedTask(Task? task)
	{
		try
		{
			ExecAction execAction = task?.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
			if (execAction == null)
			{
				return false;
			}
			string path = Environment.ExpandEnvironmentVariables(execAction.Path).Trim('"');
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
			return Path.GetFullPath(path).Equals(Path.GetFullPath(strExeFilePath), StringComparison.OrdinalIgnoreCase) || fileNameWithoutExtension.Equals("L-Mechrevo", StringComparison.OrdinalIgnoreCase) || fileNameWithoutExtension.StartsWith("L-Mechrevo-beta", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static void RemoveOwnedLegacyTasks(TaskService taskService)
	{
		string[] array = legacyTaskNames;
		foreach (string text in array)
		{
			try
			{
				if (IsOwnedTask(taskService.GetTask(text)))
				{
					taskService.RootFolder.DeleteTask(text, exceptionOnNotExists: false);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Can't remove legacy task " + text + ": " + ex.Message);
			}
		}
	}

	private static Task? GetUserTask(TaskService taskService)
	{
		try
		{
			Task task = taskService.GetTask(userTaskName);
			if (IsOwnedTask(task))
			{
				return task;
			}
			string[] array = legacyTaskNames;
			foreach (string taskPath in array)
			{
				task = taskService.GetTask(taskPath);
				if (IsOwnedTask(task))
				{
					return task;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't read startup task: " + ex.Message);
		}
		return null;
	}

	public static bool IsScheduled()
	{
		try
		{
			using TaskService taskService = new TaskService();
			return GetUserTask(taskService) != null;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't check startup task status: " + ex.Message);
			return false;
		}
	}

	public static void ReScheduleAdmin()
	{
		if (ProcessHelper.IsUserAdministrator() && IsScheduled())
		{
			Schedule();
		}
	}

	public static void StartupCheck()
	{
		RunStartupCheckSafely(StartupCheckCore, (Exception ex) =>
		{
			Logger.WriteLine("Startup task check failed: " + ex.Message);
		});
	}

	internal static bool RunStartupCheckSafely(System.Action check, Action<Exception> report)
	{
		try
		{
			check();
			return true;
		}
		catch (Exception obj)
		{
			try
			{
				report(obj);
			}
			catch
			{
			}
			return false;
		}
	}

	private static void StartupCheckCore()
	{
		if (IsTransientExecutablePath(strExeFilePath))
		{
			Logger.WriteLine("Skipping startup task check: the running image is transient: " + strExeFilePath);
			return;
		}
		bool startupEnabled = IsAutostartPreferred();
		if (!AppConfig.Exists("startup_enabled"))
		{
			AppConfig.Set("startup_enabled", 1);
		}
		using TaskService taskService = new TaskService();
		Task userTask = GetUserTask(taskService);
		bool flag = userTask != null;
		bool matchesPlan = false;
		if (flag)
		{
			try
			{
				matchesPlan = MatchesCurrentUserStartupTask(userTask.Definition);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Can't check startup task: " + ex.Message);
			}
		}
		if (RunStartupTaskCheck(flag, matchesPlan, startupEnabled, Schedule))
		{
			ReportAutostartFailure("could not register the autostart task for " + strExeFilePath);
		}
		if (flag && taskService.RootFolder.AllTasks.FirstOrDefault((Task t) => t.Name == chargeTaskName) == null)
		{
			if (ProcessHelper.IsUserAdministrator())
			{
				ScheduleCharge();
			}
			else
			{
				Logger.WriteLine("Charge limit task is missing; skipping automatic repair because administrator rights are required.");
				NotifyChargeTaskNeedsElevation();
			}
		}
	}

	public static bool ShouldAutoRepairStartupTask(bool needsReschedule, bool _)
	{
		return needsReschedule;
	}

	internal static bool IsAutostartPreferred()
	{
		return AppConfig.IsNotFalse("startup_enabled");
	}

	internal static bool ShouldRestoreEnabledStartupTask(bool taskMissing, bool startupEnabled)
	{
		return taskMissing & startupEnabled;
	}

	internal static StartupTaskAction DecideStartupTaskAction(bool taskExists, bool matchesPlan, bool startupEnabled)
	{
		bool needsReschedule = taskExists && !matchesPlan;
		bool flag = ShouldRestoreEnabledStartupTask(!taskExists, startupEnabled);
		if (!(ShouldAutoRepairStartupTask(needsReschedule, _: false) | flag))
		{
			return StartupTaskAction.None;
		}
		return StartupTaskAction.Register;
	}

	internal static bool ShouldReportAutostartFailure(bool startupEnabled, bool registrationSucceeded)
	{
		if (startupEnabled)
		{
			return !registrationSucceeded;
		}
		return false;
	}

	internal static void ReportAutostartFailure(string reason)
	{
		AutostartFailureSink(reason);
	}

	internal static bool RunStartupTaskCheck(bool taskExists, bool matchesPlan, bool startupEnabled, Func<bool> register)
	{
		if (DecideStartupTaskAction(taskExists, matchesPlan, startupEnabled) != StartupTaskAction.Register)
		{
			return false;
		}
		return ShouldReportAutostartFailure(startupEnabled, register());
	}

	internal static StartupTaskPlan GetUserStartupTaskPlan()
	{
		return new StartupTaskPlan(TimeSpan.FromSeconds(10L), 3, TimeSpan.FromMinutes(1L));
	}

	internal static TaskRunLevel GetUserStartupTaskRunLevel()
	{
		return TaskRunLevel.Highest;
	}

	private static bool MatchesCurrentUserStartupTask(TaskDefinition definition)
	{
		ExecAction? execAction = definition.Actions.OfType<ExecAction>().FirstOrDefault();
		string path = Environment.ExpandEnvironmentVariables(execAction?.Path ?? "").Trim('"');
		string text = execAction?.Arguments?.Trim() ?? "";
		if (Path.GetFullPath(strExeFilePath).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) && text.Equals("startup", StringComparison.OrdinalIgnoreCase))
		{
			return MatchesUserStartupPlan(definition);
		}
		return false;
	}

	internal static bool MatchesUserStartupPlan(TaskDefinition definition)
	{
		StartupTaskPlan plan = GetUserStartupTaskPlan();
		bool flag = definition.Triggers.OfType<LogonTrigger>().Any((LogonTrigger trigger) => trigger.Delay >= plan.TriggerDelay);
		bool flag2 = definition.Triggers.OfType<SessionStateChangeTrigger>().Any((SessionStateChangeTrigger trigger) => trigger.StateChange == TaskSessionStateChangeType.ConsoleConnect && trigger.Delay >= plan.TriggerDelay);
		TaskRunLevel runLevel = definition.Principal.RunLevel;
		bool flag3 = (uint)runLevel <= 1u;
		bool flag4 = flag3;
		if ((((flag | flag2) && definition.Principal.LogonType == TaskLogonType.InteractiveToken) & flag4) && definition.Settings.RestartCount >= plan.RestartCount && definition.Settings.RestartInterval >= plan.RestartInterval)
		{
			return definition.Settings.StartWhenAvailable;
		}
		return false;
	}

	internal static void ConfigureUserStartupTask(TaskDefinition definition, string userName)
	{
		StartupTaskPlan userStartupTaskPlan = GetUserStartupTaskPlan();
		definition.RegistrationInfo.Description = "L-Mechrevo Auto Start";
		definition.Triggers.Add(new LogonTrigger
		{
			UserId = userName,
			Delay = userStartupTaskPlan.TriggerDelay
		});
		definition.Triggers.Add(new SessionStateChangeTrigger
		{
			StateChange = TaskSessionStateChangeType.ConsoleConnect,
			UserId = userName,
			Delay = userStartupTaskPlan.TriggerDelay
		});
		definition.Actions.Add(strExeFilePath, "startup");
		definition.Principal.UserId = userName;
		definition.Principal.LogonType = TaskLogonType.InteractiveToken;
		definition.Principal.RunLevel = GetUserStartupTaskRunLevel();
		definition.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
		definition.Settings.StopIfGoingOnBatteries = false;
		definition.Settings.DisallowStartIfOnBatteries = false;
		definition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
		definition.Settings.StartWhenAvailable = true;
		definition.Settings.RestartCount = userStartupTaskPlan.RestartCount;
		definition.Settings.RestartInterval = userStartupTaskPlan.RestartInterval;
	}

	public static void UnscheduleCharge()
	{
		using TaskService taskService = new TaskService();
		try
		{
			taskService.RootFolder.DeleteTask(chargeTaskName);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't remove charge limit task: " + ex.Message);
		}
	}

	private const string ChargeTaskPromptDismissedKey = "charge_task_location_prompt_dismissed";

	private static int _chargeTaskLocationPromptShown;

	private static int _chargeTaskElevationNoticeShown;

	/// <summary>
	/// 2026-10-09（exportperm）：SYSTEM 充电上限开机任务注册需要提权，而当前进程没有管理员权限时的
	/// 如实提示（进程内只提示一次，不落盘、不形成常驻打扰）。文案直接说清"要以管理员身份运行"。
	/// 注：app.manifest 是 requireAdministrator，正常运行恒为管理员，这段基本只为兜底。
	/// </summary>
	private static void NotifyChargeTaskNeedsElevation()
	{
		if (Interlocked.Exchange(ref _chargeTaskElevationNoticeShown, 1) == 1)
		{
			return;
		}
		try
		{
			ToastForm.ShowNotice(Strings.ChargeTaskElevationNotice);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Charge task elevation notice failed: " + ex.Message);
		}
	}

	/// <summary>
	/// 2026-10-04：安装目录不满足"仅管理员可写"时，原来只写日志就放弃注册充电上限开机任务，
	/// 用户完全无感知（表现为彻底断电后充电上限悄悄回到 100%）。
	/// 现改为一次性提示，并支持一键收紧目录权限；选择"否"后不再打扰。
	/// 无可用窗口时保持原行为（只记日志），等下次有窗口时再提示。
	/// </summary>
	private static void OfferChargeTaskLocationFix(string reason)
	{
		try
		{
			if (AppConfig.Is(ChargeTaskPromptDismissedKey))
			{
				return;
			}
			if (Interlocked.Exchange(ref _chargeTaskLocationPromptShown, 1) == 1)
			{
				return;
			}
			SettingsForm settingsForm = Program.settingsForm;
			if (settingsForm == null || settingsForm.IsDisposed || !settingsForm.IsHandleCreated)
			{
				Interlocked.Exchange(ref _chargeTaskLocationPromptShown, 0);
				return;
			}
			settingsForm.BeginInvoke((System.Action)delegate
			{
				try
				{
					DialogResult dialogResult = MessageBox.Show(settingsForm, string.Format(Strings.ChargeTaskLocationBody, reason), Strings.ChargeTaskLocationTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
					if (dialogResult != DialogResult.Yes)
					{
						AppConfig.Set(ChargeTaskPromptDismissedKey, 1);
						Logger.WriteLine("Charge task location hardening declined by the user; will not ask again.");
						return;
					}
					if (ExecutableTrust.TryHardenImageDirectory(out string fixMessage))
					{
						Logger.WriteLine("Charge task location hardened: " + fixMessage);
						ToastForm.ShowNotice(Strings.ChargeTaskLocationFixed);
						ScheduleCharge();
					}
					else
					{
						Logger.WriteLine("Charge task location hardening failed: " + fixMessage);
						ToastForm.ShowFailure(Strings.ChargeTaskLocationFailed);
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Charge task location prompt failed: " + ex.Message);
				}
			});
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Charge task location prompt dispatch failed: " + ex2.Message);
		}
	}

	/// <summary>2026-10-22：注册/更新无界电池电压修复的开机保持任务（SYSTEM 常驻，防 EC 重启后弹回）。</summary>
	public static void ScheduleBatteryFix()
	{
		if (strExeFilePath == null || !ProcessHelper.IsUserAdministrator())
		{
			return;
		}
		try
		{
			using TaskDefinition taskDefinition = TaskService.Instance.NewTask();
			taskDefinition.RegistrationInfo.Description = "L-Mechrevo Battery Voltage Fix";
			taskDefinition.Triggers.Add(new BootTrigger());
			taskDefinition.Triggers.Add(new EventTrigger
			{
				Subscription = "<QueryList><Query Id='0' Path='System'><Select Path='System'>*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot'] and EventID=27]]</Select></Query></QueryList>"
			});
			taskDefinition.Actions.Add(strExeFilePath, "batteryfix");
			taskDefinition.Principal.UserId = "SYSTEM";
			taskDefinition.Principal.LogonType = TaskLogonType.ServiceAccount;
			taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
			taskDefinition.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
			taskDefinition.Settings.StopIfGoingOnBatteries = false;
			taskDefinition.Settings.DisallowStartIfOnBatteries = false;
			taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
			TaskService.Instance.RootFolder.RegisterTaskDefinition(batteryFixTaskName, taskDefinition);
			Logger.WriteLine("Battery voltage fix task scheduled: " + strExeFilePath);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't create a battery voltage fix task: " + ex.Message);
		}
	}

	/// <summary>移除电池电压修复开机任务（关闭功能时调用）。</summary>
	public static void UnscheduleBatteryFix()
	{
		try
		{
			using TaskService taskService = new TaskService();
			Task running = taskService.GetTask(batteryFixTaskName);
			try
			{
				running?.Stop();
			}
			catch (Exception stopEx)
			{
				Logger.WriteLine("Can't stop battery voltage fix task process: " + stopEx.Message);
			}
			taskService.RootFolder.DeleteTask(batteryFixTaskName, exceptionOnNotExists: false);
			foreach (Task item in taskService.RootFolder.AllTasks)
			{
				bool byName = item.Name.IndexOf("BatteryFix", StringComparison.OrdinalIgnoreCase) >= 0;
				bool byAction = false;
				try
				{
					foreach (Microsoft.Win32.TaskScheduler.Action action in item.Definition.Actions)
					{
						if (action is ExecAction exec && exec.Arguments != null
							&& exec.Arguments.IndexOf("batteryfix", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							byAction = true;
							break;
						}
					}
				}
				catch
				{
				}
				if (!byName && !byAction)
				{
					continue;
				}
				try
				{
					item.Stop();
				}
				catch
				{
				}
				try
				{
					taskService.RootFolder.DeleteTask(item.Name, exceptionOnNotExists: false);
					Logger.WriteLine("Removed legacy battery fix task: " + item.Name);
				}
				catch (Exception legacyEx)
				{
					Logger.WriteLine("Can't remove legacy battery fix task " + item.Name + ": " + legacyEx.Message);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't remove battery voltage fix task: " + ex.Message);
		}
	}

	public static void ScheduleCharge()
	{
		if (strExeFilePath == null)
		{
			return;
		}
		if (!ProcessHelper.IsUserAdministrator())
		{
			// 2026-10-09（exportperm）：注册 SYSTEM 计划任务必须提权。这里原来只写一行日志、
			// 界面上完全无感；现在如实说明"需要以管理员身份运行"，不再落到安装位置那套文案
			// （以前各种权限问题都表现为「充电上限 · 安装位置」提示，用户看到的就是"充电权限"报错）。
			Logger.WriteLine("Charge limit task not registered: administrator rights are required; run L-Mechrevo as administrator to enable charge-limit restore after a full power loss.");
			NotifyChargeTaskNeedsElevation();
			return;
		}
		if (!ExecutableTrust.IsCurrentImageInProtectedLocation(out string reason))
		{
			Logger.WriteLine("Refusing to register the SYSTEM charge limit task: " + reason + ". Install into a directory that only administrators can write (for example Program Files).");
			OfferChargeTaskLocationFix(reason);
			return;
		}
		Logger.WriteLine("Charge limit task location check passed: " + reason);
		using TaskDefinition taskDefinition = TaskService.Instance.NewTask();
		taskDefinition.RegistrationInfo.Description = "L-Mechrevo Charge Limit";
		taskDefinition.Triggers.Add(new BootTrigger());
		taskDefinition.Triggers.Add(new EventTrigger
		{
			Subscription = "<QueryList><Query Id='0' Path='System'><Select Path='System'>*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot'] and EventID=27]]</Select></Query></QueryList>"
		});
		taskDefinition.Actions.Add(strExeFilePath, "charge");
		taskDefinition.Principal.UserId = "SYSTEM";
		taskDefinition.Principal.LogonType = TaskLogonType.ServiceAccount;
		taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
		taskDefinition.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
		taskDefinition.Settings.StopIfGoingOnBatteries = false;
		taskDefinition.Settings.DisallowStartIfOnBatteries = false;
		taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.FromSeconds(30L);
		try
		{
			TaskService.Instance.RootFolder.RegisterTaskDefinition(chargeTaskName, taskDefinition);
			RemoveOwnedLegacyTasks(TaskService.Instance);
			Logger.WriteLine("Charge limit task scheduled: " + strExeFilePath);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't create a charge limit task: " + ex.Message);
		}
	}

	internal static bool ScheduleUserTaskOnly()
	{
		if (IsTransientExecutablePath(strExeFilePath))
		{
			Logger.WriteLine("Refusing to register the startup task from a transient location: " + strExeFilePath);
			return false;
		}
		if (!ProcessHelper.IsUserAdministrator())
		{
			return RegisterAutostartElevated();
		}
		try
		{
			using TaskService taskService = new TaskService();
			using TaskDefinition definition = taskService.NewTask();
			ConfigureUserStartupTask(definition, WindowsIdentity.GetCurrent().Name);
			taskService.RootFolder.RegisterTaskDefinition(userTaskName, definition);
			Task userTask = GetUserTask(taskService);
			if (userTask == null || !MatchesCurrentUserStartupTask(userTask.Definition))
			{
				Logger.WriteLine("Startup task registration could not be verified.");
				return false;
			}
			RemoveOwnedLegacyTasks(taskService);
			Logger.WriteLine("Startup task scheduled: " + strExeFilePath);
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't create startup task: " + ex.Message);
			return false;
		}
	}

	internal static bool RegisterAutostartElevated()
	{
		try
		{
			using Process process = Process.Start(new ProcessStartInfo
			{
				FileName = strExeFilePath,
				Arguments = "--register-autostart",
				UseShellExecute = true,
				Verb = "runas",
				WorkingDirectory = (Path.GetDirectoryName(strExeFilePath) ?? AppContext.BaseDirectory),
				WindowStyle = ProcessWindowStyle.Hidden
			});
			if (process == null)
			{
				Logger.WriteLine("Can't start elevated autostart registration.");
				return false;
			}
			process.WaitForExit();
			bool result = process.ExitCode == 0;
			Logger.WriteLine("Elevated autostart registration exit=" + process.ExitCode);
			return result;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			Logger.WriteLine("Autostart elevation cancelled.");
			return false;
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Can't elevate autostart registration: " + ex2.Message);
			return false;
		}
	}

	public static bool Schedule()
	{
		if (!ScheduleUserTaskOnly())
		{
			return false;
		}
		ScheduleCharge();
		return true;
	}

	public static bool UnSchedule()
	{
		bool result = true;
		using (TaskService taskService = new TaskService())
		{
			try
			{
				if (taskService.GetTask(userTaskName) != null)
				{
					taskService.RootFolder.DeleteTask(userTaskName, exceptionOnNotExists: false);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Can't remove startup task: " + ex.Message);
				result = false;
			}
			RemoveOwnedLegacyTasks(taskService);
		}
		if (ProcessHelper.IsUserAdministrator())
		{
			UnscheduleCharge();
		}
		else
		{
			Logger.WriteLine("Keeping the SYSTEM charge limit task: it cannot be re-registered without administrator rights.");
		}
		return result;
	}
}
