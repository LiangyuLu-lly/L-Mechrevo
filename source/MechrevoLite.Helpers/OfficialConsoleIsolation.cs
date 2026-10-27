using System;
using MechrevoLite.Properties;using System.Collections.Generic;
using MechrevoLite.Properties;using System.ComponentModel;
using MechrevoLite.Properties;using System.Diagnostics;
using MechrevoLite.Properties;using System.IO;
using MechrevoLite.Properties;using System.Linq;
using MechrevoLite.Properties;using System.Runtime.InteropServices;
using MechrevoLite.Properties;using System.Security.AccessControl;
using MechrevoLite.Properties;using System.Security.Principal;
using MechrevoLite.Properties;using System.Text;
using MechrevoLite.Properties;using System.Text.Json;
using MechrevoLite.Properties;using System.Threading;
using MechrevoLite.Properties;using System.Threading.Tasks;
using MechrevoLite.Properties;using System.Windows.Forms;
using MechrevoLite.Properties;using Microsoft.Win32;
using MechrevoLite.Properties;using Microsoft.Win32.TaskScheduler;
using MechrevoLite.Properties;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
namespace MechrevoLite.Helpers;

public static class OfficialConsoleIsolation
{
	public sealed record IsolationStatus(bool Isolated, bool OfficialUiRunning, bool OfficialUiInstalled, bool GcuRunning, string Detail);

	public sealed record OperationResult(bool Success, string Message, int AffectedItems);

	private sealed class IsolationSnapshot
	{
		public int Version { get; set; } = 1;

		public bool Isolated { get; set; }

		public bool OfficialUiWasRunning { get; set; }

		public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

		public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

		public List<RegistryStartupEntry> RegistryEntries { get; set; } = new List<RegistryStartupEntry>();

		public List<ScheduledTaskEntry> ScheduledTasks { get; set; } = new List<ScheduledTaskEntry>();

		public List<StartupFileEntry> StartupFiles { get; set; } = new List<StartupFileEntry>();
	}

	private sealed class RegistryStartupEntry
	{
		public string Hive { get; set; } = string.Empty;

		public RegistryView View { get; set; }

		public string KeyPath { get; set; } = string.Empty;

		public string Name { get; set; } = string.Empty;

		public string Value { get; set; } = string.Empty;

		public RegistryValueKind Kind { get; set; } = RegistryValueKind.String;
	}

	private sealed class ScheduledTaskEntry
	{
		public string Path { get; set; } = string.Empty;

		public bool WasEnabled { get; set; }
	}

	private sealed class StartupFileEntry
	{
		public string OriginalPath { get; set; } = string.Empty;

		public string DisabledPath { get; set; } = string.Empty;
	}

	internal readonly record struct RestoreOutcome(int Restored, int Skipped, int Failed, int Expected)
	{
		internal bool IsSilentNoOp
		{
			get
			{
				if (Expected > 0)
				{
					return Restored == 0;
				}
				return false;
			}
		}
	}

	private enum SnapshotReadStatus
	{
		Missing,
		Loaded,
		Unreadable
	}

	private readonly record struct SnapshotReadResult(SnapshotReadStatus Status, IsolationSnapshot? Snapshot);

	private struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private const int SnapshotVersion = 1;

	internal const string DisabledStartupSuffix = ".lmechrevo-disabled";

	private static readonly string[] ProtectedTaskFolders = new string[1] { "\\Microsoft\\" };

	private const string OfficialAppId = "{6ea3ce12-b991-4b65-9f8d-b148eaaecd87}_is1";

	private const string PackageAumid = "CCU.WinUI_wrbgcf7aesyd8!App";

	private static readonly string[] UiProcessNames = new string[5] { "CCUWinUI", "SystrayComponent", "ControlCenterU", "GamingCenterU", "GCUUI" };

	private static readonly string[] OfficialMarkers = new string[7] { "CCUWinUI.exe", "SystrayComponent.exe", "ControlCenterU.exe", "GamingCenterU.exe", "GCUUI.exe", "CCU.WinUI_", "CCU.WinUI_wrbgcf7aesyd8" };

	private static readonly string[] RunKeyPaths = new string[2] { "Software\\Microsoft\\Windows\\CurrentVersion\\Run", "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce" };

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private static readonly object StateLock = new object();

	private static readonly object InstallationProbeLock = new object();

	private static System.Threading.Timer? guardTimer;

	private static int guardRunning;

	private static bool? installedCache;

	private static long installedCacheUntil;

	private static readonly string[] ContentScannableStartupExtensions = new string[6] { ".lnk", ".url", ".cmd", ".bat", ".vbs", ".ps1" };

	private const long MaxScannedStartupFileBytes = 1048576L;

	public static string SnapshotPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L-Mechrevo", "OfficialConsoleState.json");

	private static int CurrentSessionId
	{
		get
		{
			try
			{
				using Process process = Process.GetCurrentProcess();
				return process.SessionId;
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Can't read the current session id: " + ex.Message);
				return -1;
			}
		}
	}

	internal static bool IsManageableTaskPath(string? taskPath)
	{
		if (string.IsNullOrWhiteSpace(taskPath))
		{
			return false;
		}
		string text = taskPath.Replace('/', '\\');
		if (!text.StartsWith('\\'))
		{
			text = "\\" + text;
		}
		string[] protectedTaskFolders = ProtectedTaskFolders;
		foreach (string value in protectedTaskFolders)
		{
			if (text.StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		return true;
	}

	public static IsolationStatus GetStatus(bool runtimeGcuConnected = false)
	{
		bool isolated = TryReadSnapshot()?.Isolated ?? false;
		bool flag = IsOfficialUiRunning();
		bool flag2 = IsOfficialUiInstalled();
		if (!flag2)
		{
			// 2026-10-06：包未注册时，WindowsApps 下可能只剩旧目录残留 —— 判定仍是"未检测到"，
			// 但把残留写进日志，避免与安装器 Install-Gcu.ps1 的 "no official console package found" 口径看起来矛盾。
			LogWindowsAppsResidueIfAny();
		}
		bool gcuRunning = runtimeGcuConnected || (IsProcessRunning("GCUBridge") && IsProcessRunning("GCUService"));
		string detail = DescribeStatus(isolated, flag, flag2, gcuRunning);
		return new IsolationStatus(isolated, flag, flag2, gcuRunning, detail);
	}

	internal static string DescribeStatus(bool isolated, bool uiRunning, bool installed, bool gcuRunning)
	{
		if (!isolated)
		{
			if (!(uiRunning | installed))
			{
				return Strings.OffConsoleNotDetected;
			}
			return Strings.OffConsoleNoUninstall;
		}
		if (!uiRunning)
		{
			if (!gcuRunning)
			{
				return Strings.OffConsoleIsolatedWaiting;
			}
			return Strings.OffConsoleIsolatedGcuOnly;
		}
		return Strings.OffConsoleIsolationError;
	}

	public static OperationResult Enable()
	{
		EnsureAdministrator();
		lock (StateLock)
		{
			SnapshotReadResult snapshotReadResult = ReadSnapshot();
			if (snapshotReadResult.Status == SnapshotReadStatus.Unreadable)
			{
				Logger.WriteLine("Refusing to isolate: the existing snapshot can't be read and must not be overwritten.");
				return new OperationResult(Success: false, string.Format(Strings.OffConsoleSnapshotUnreadable, SnapshotPath), 0);
			}
			if (snapshotReadResult.Status == SnapshotReadStatus.Loaded)
			{
				IsolationSnapshot? snapshot = snapshotReadResult.Snapshot;
				if (snapshot != null && snapshot.Isolated)
				{
					int affectedItems = StopOfficialUiProcesses();
					return new OperationResult(Success: true, Strings.OffConsoleAlreadyIsolated, affectedItems);
				}
			}
			IsolationSnapshot isolationSnapshot = CaptureSnapshot();
			SaveSnapshot(isolationSnapshot);
			int num = 0;
			try
			{
				num += DisableRegistryEntries(isolationSnapshot.RegistryEntries);
				num += DisableScheduledTasks(isolationSnapshot.ScheduledTasks);
				num += DisableStartupFiles(isolationSnapshot.StartupFiles);
				num += StopOfficialUiProcesses();
				isolationSnapshot.Isolated = true;
				isolationSnapshot.UpdatedUtc = DateTime.UtcNow;
				SaveSnapshot(isolationSnapshot);
				Logger.WriteLine($"Official console isolated. Changed items: {num}");
				return new OperationResult(Success: true, Strings.OffConsoleIsolatedGcuOnly, num);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Official console isolation failed: " + ex);
				bool flag = false;
				try
				{
					RestoreOutcome restoreOutcome = RestoreSnapshot(isolationSnapshot);
					flag = !restoreOutcome.IsSilentNoOp && restoreOutcome.Failed == 0;
					Logger.WriteLine($"Isolation rollback: restored={restoreOutcome.Restored} skipped={restoreOutcome.Skipped} failed={restoreOutcome.Failed}");
				}
				catch (Exception ex2)
				{
					Logger.WriteLine("Official console isolation rollback failed: " + ex2);
				}
				try
				{
					isolationSnapshot.Isolated = !flag;
					isolationSnapshot.UpdatedUtc = DateTime.UtcNow;
					SaveSnapshot(isolationSnapshot);
				}
				catch (Exception ex3)
				{
					Logger.WriteLine("Can't persist the isolation rollback state: " + ex3.Message);
				}
				return new OperationResult(Success: false, flag ? (Strings.OffConsoleIsolationFailedRestored + ex.Message) : (Strings.OffConsoleIsolationFailedRollback + ex.Message), num);
			}
		}
	}

	public static OperationResult Restore()
	{
		EnsureAdministrator();
		lock (StateLock)
		{
			SnapshotReadResult snapshotReadResult = ReadSnapshot();
			if (snapshotReadResult.Status == SnapshotReadStatus.Missing)
			{
				return new OperationResult(Success: true, Strings.OffConsoleNothingToRestore, 0);
			}
			if (snapshotReadResult.Status == SnapshotReadStatus.Unreadable || snapshotReadResult.Snapshot == null)
			{
				return new OperationResult(Success: false, Strings.OffConsoleRestoreUnreadable + SnapshotPath, 0);
			}
			IsolationSnapshot snapshot = snapshotReadResult.Snapshot;
			try
			{
				RestoreOutcome restoreOutcome = RestoreSnapshot(snapshot);
				Logger.WriteLine($"Official console restore: restored={restoreOutcome.Restored} skipped={restoreOutcome.Skipped} failed={restoreOutcome.Failed} expected={restoreOutcome.Expected}");
				if (restoreOutcome.IsSilentNoOp)
				{
					return new OperationResult(Success: false, string.Format(Strings.OffConsoleNothingRestored, restoreOutcome.Expected, restoreOutcome.Skipped, restoreOutcome.Failed), 0);
				}
				if (restoreOutcome.Failed > 0)
				{
					snapshot.UpdatedUtc = DateTime.UtcNow;
					SaveSnapshot(snapshot);
					return new OperationResult(Success: false, string.Format(Strings.OffConsolePartialRestoreFailed, restoreOutcome.Restored, restoreOutcome.Failed), restoreOutcome.Restored);
				}
				snapshot.Isolated = false;
				snapshot.UpdatedUtc = DateTime.UtcNow;
				SaveSnapshot(snapshot);
				return new OperationResult(Success: true, Strings.OffConsoleRestoreComplete, restoreOutcome.Restored);
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Official console restore failed: " + ex);
				return new OperationResult(Success: false, Strings.OffConsoleRestoreFailed + ex.Message, 0);
			}
		}
	}

	public static async Task<OperationResult> SetIsolationAsync(bool isolate)
	{
		if (IsAdministrator())
		{
			return await System.Threading.Tasks.Task.Run(() => (!isolate) ? Restore() : Enable());
		}
		try
		{
			using Process helper = Process.Start(new ProcessStartInfo
			{
				FileName = Application.ExecutablePath,
				Arguments = (isolate ? "--official-isolate" : "--official-restore"),
				UseShellExecute = true,
				Verb = "runas",
				WorkingDirectory = AppContext.BaseDirectory,
				WindowStyle = ProcessWindowStyle.Hidden
			});
			if (helper == null)
			{
				return new OperationResult(Success: false, Strings.OffConsoleCannotLaunchElevated, 0);
			}
			await helper.WaitForExitAsync();
			bool flag = helper.ExitCode == 0;
			bool success = flag;
			string message;
			if (flag)
			{
				message = (isolate ? Strings.OffConsoleIsolatedGcuOnly : Strings.OffConsoleRestoreComplete);
			}
			else
			{
				message = Strings.OffConsoleElevationIncomplete;
			}
			return new OperationResult(success, message, 0);
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			return new OperationResult(Success: false, Strings.OffConsoleElevationCancelled, 0);
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Official console elevated action failed: " + ex2);
			return new OperationResult(Success: false, Strings.OffConsoleElevationFailed + ex2.Message, 0);
		}
	}

	public static void StartGuardIfNeeded()
	{
		StopGuard();
		IsolationSnapshot? isolationSnapshot = TryReadSnapshot();
		if (isolationSnapshot == null || !isolationSnapshot.Isolated)
		{
			return;
		}
		StopOfficialUiProcesses();
		guardTimer = new System.Threading.Timer((object? _) =>
		{
			if (Interlocked.Exchange(ref guardRunning, 1) != 0)
			{
				return;
			}
			try
			{
				IsolationSnapshot? isolationSnapshot2 = TryReadSnapshot();
				if (isolationSnapshot2 != null && isolationSnapshot2.Isolated)
				{
					StopOfficialUiProcesses();
				}
			}
			finally
			{
				Volatile.Write(ref guardRunning, 0);
			}
		}, null, TimeSpan.FromSeconds(1L), TimeSpan.FromSeconds(2L));
	}

	public static void StopGuard()
	{
		guardTimer?.Dispose();
		guardTimer = null;
	}

	internal static async Task<bool> WaitForUiSettledAsync(TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken))
	{
		IsolationSnapshot? isolationSnapshot = TryReadSnapshot();
		if (isolationSnapshot == null || !isolationSnapshot.Isolated)
		{
			return true;
		}
		long deadline = Environment.TickCount64 + Math.Max(250L, (long)timeout.TotalMilliseconds);
		int quietPasses = 0;
		while (Environment.TickCount64 < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (IsOfficialUiRunning())
			{
				quietPasses = 0;
				StopOfficialUiProcesses();
			}
			else
			{
				int num = quietPasses + 1;
				quietPasses = num;
				if (num >= 2)
				{
					return true;
				}
			}
			await System.Threading.Tasks.Task.Delay(250, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		return !IsOfficialUiRunning();
	}

	public static bool LaunchOfficialUi()
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "explorer.exe",
				Arguments = "shell:AppsFolder\\CCU.WinUI_wrbgcf7aesyd8!App",
				UseShellExecute = true
			});
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't launch packaged official console: " + ex.Message);
		}
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OEM");
			string[] array = new string[2] { "ControlCenterU.exe", "GamingCenterU.exe" };
			foreach (string searchPattern in array)
			{
				string text = (Directory.Exists(path) ? Directory.EnumerateFiles(path, searchPattern, SearchOption.AllDirectories).FirstOrDefault() : null);
				if (text != null)
				{
					Process.Start(new ProcessStartInfo(text)
					{
						UseShellExecute = true
					});
					return true;
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Can't launch legacy official console: " + ex2.Message);
		}
		return false;
	}

	private static IsolationSnapshot CaptureSnapshot()
	{
		IsolationSnapshot isolationSnapshot = new IsolationSnapshot
		{
			OfficialUiWasRunning = IsOfficialUiRunning()
		};
		CaptureRegistryEntries(isolationSnapshot.RegistryEntries);
		CaptureScheduledTasks(isolationSnapshot.ScheduledTasks);
		CaptureStartupFiles(isolationSnapshot.StartupFiles);
		return isolationSnapshot;
	}

	private static void CaptureRegistryEntries(List<RegistryStartupEntry> entries)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		(RegistryHive, string)[] array = new (RegistryHive, string)[2]
		{
			(RegistryHive.CurrentUser, "HKCU"),
			(RegistryHive.LocalMachine, "HKLM")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(RegistryHive, string) tuple = array[i];
			RegistryHive item = tuple.Item1;
			string item2 = tuple.Item2;
			RegistryView[] array2 = new RegistryView[2]
			{
				RegistryView.Registry64,
				RegistryView.Registry32
			};
			foreach (RegistryView registryView in array2)
			{
				string[] runKeyPaths = RunKeyPaths;
				foreach (string text in runKeyPaths)
				{
					try
					{
						using RegistryKey registryKey = RegistryKey.OpenBaseKey(item, registryView);
						using RegistryKey registryKey2 = registryKey.OpenSubKey(text, writable: true);
						if (registryKey2 == null)
						{
							continue;
						}
						string[] valueNames = registryKey2.GetValueNames();
						foreach (string text2 in valueNames)
						{
							if (registryKey2.GetValue(text2, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string text3 && IsOfficialReference(text2 + " " + text3))
							{
								string item3 = $"{item2}|{registryView}|{text}|{text2}";
								if (hashSet.Add(item3))
								{
									entries.Add(new RegistryStartupEntry
									{
										Hive = item2,
										View = registryView,
										KeyPath = text,
										Name = text2,
										Value = text3,
										Kind = registryKey2.GetValueKind(text2)
									});
								}
							}
						}
					}
					catch (Exception ex)
					{
						Logger.WriteLine($"Can't inspect official startup key {item2} {text}: {ex.Message}");
					}
				}
			}
		}
	}

	private static void CaptureScheduledTasks(List<ScheduledTaskEntry> entries)
	{
		try
		{
			using TaskService taskService = new TaskService();
			foreach (Microsoft.Win32.TaskScheduler.Task allTask in taskService.AllTasks)
			{
				try
				{
					if (IsManageableTaskPath(allTask.Path))
					{
						string text = string.Join(" ", from a in allTask.Definition.Actions.OfType<ExecAction>()
							select a.Path + " " + a.Arguments);
						if (IsOfficialReference(allTask.Name + " " + text))
						{
							entries.Add(new ScheduledTaskEntry
							{
								Path = allTask.Path,
								WasEnabled = allTask.Enabled
							});
						}
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Can't inspect scheduled task " + allTask.Path + ": " + ex.Message);
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Can't enumerate official scheduled tasks: " + ex2.Message);
		}
	}

	private static void CaptureStartupFiles(List<StartupFileEntry> entries)
	{
		foreach (string item in new string[2]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.Startup),
			Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
		}.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (!Directory.Exists(item))
			{
				continue;
			}
			foreach (string item2 in Directory.EnumerateFiles(item))
			{
				try
				{
					if (IsOfficialStartupFile(item2))
					{
						entries.Add(new StartupFileEntry
						{
							OriginalPath = item2,
							DisabledPath = item2 + ".lmechrevo-disabled"
						});
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Can't inspect startup file " + item2 + ": " + ex.Message);
				}
			}
		}
	}

	internal static bool IsOfficialStartupFile(string path)
	{
		if (IsOfficialReference(Path.GetFileName(path)))
		{
			return true;
		}
		string extension = Path.GetExtension(path);
		if (!Array.Exists(ContentScannableStartupExtensions, (string allowed) => string.Equals(allowed, extension, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		FileInfo fileInfo = new FileInfo(path);
		if (!fileInfo.Exists || fileInfo.Length > 1048576)
		{
			return false;
		}
		byte[] bytes = File.ReadAllBytes(path);
		if (!IsOfficialReference(Encoding.ASCII.GetString(bytes)))
		{
			return IsOfficialReference(Encoding.Unicode.GetString(bytes));
		}
		return true;
	}

	private static int DisableRegistryEntries(IEnumerable<RegistryStartupEntry> entries)
	{
		int num = 0;
		foreach (RegistryStartupEntry entry in entries)
		{
			if (!IsRestorableRegistryEntry(entry.Hive, entry.KeyPath, entry.Name, entry.Kind))
			{
				Logger.WriteLine($"Skipping an out-of-policy registry entry: {entry.Hive}\\{entry.KeyPath}\\{entry.Name}");
				continue;
			}
			try
			{
				using RegistryKey registryKey = RegistryKey.OpenBaseKey((entry.Hive == "HKLM") ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, entry.View);
				using RegistryKey registryKey2 = registryKey.OpenSubKey(entry.KeyPath, writable: true);
				if (registryKey2?.GetValue(entry.Name) != null)
				{
					registryKey2.DeleteValue(entry.Name, throwOnMissingValue: false);
					num++;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine($"Can't disable registry entry {entry.Hive}\\{entry.KeyPath}\\{entry.Name}: {ex.Message}");
			}
		}
		return num;
	}

	private static int DisableScheduledTasks(IEnumerable<ScheduledTaskEntry> entries)
	{
		int num = 0;
		try
		{
			using TaskService taskService = new TaskService();
			foreach (ScheduledTaskEntry entry in entries)
			{
				if (!IsManageableTaskPath(entry.Path))
				{
					Logger.WriteLine("Skipping a protected scheduled task: " + entry.Path);
					continue;
				}
				try
				{
					Microsoft.Win32.TaskScheduler.Task task = taskService.GetTask(entry.Path);
					if (task != null && task.Enabled)
					{
						task.Enabled = false;
						num++;
					}
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Can't disable scheduled task " + entry.Path + ": " + ex.Message);
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Can't open the task scheduler: " + ex2.Message);
		}
		return num;
	}

	private static int DisableStartupFiles(IEnumerable<StartupFileEntry> entries)
	{
		int num = 0;
		string[] startupFolders = StartupFolders();
		foreach (StartupFileEntry entry in entries)
		{
			if (!IsRestorableStartupFile(entry.OriginalPath, entry.DisabledPath, startupFolders))
			{
				Logger.WriteLine("Skipping an out-of-policy startup file: " + entry.OriginalPath);
				continue;
			}
			try
			{
				if (File.Exists(entry.OriginalPath) && !File.Exists(entry.DisabledPath))
				{
					File.Move(entry.OriginalPath, entry.DisabledPath);
					num++;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Can't disable startup file " + entry.OriginalPath + ": " + ex.Message);
			}
		}
		return num;
	}

	internal static bool IsAllowedRegistryValueKind(RegistryValueKind kind)
	{
		if ((uint)(kind - 1) <= 1u)
		{
			return true;
		}
		return false;
	}

	internal static bool IsAllowedRegistryHive(string? hive)
	{
		if (hive == "HKLM" || hive == "HKCU")
		{
			return true;
		}
		return false;
	}

	internal static bool IsAllowedRunKeyPath(string? keyPath)
	{
		if (!string.IsNullOrWhiteSpace(keyPath))
		{
			return Array.Exists(RunKeyPaths, (string allowed) => string.Equals(allowed, keyPath, StringComparison.OrdinalIgnoreCase));
		}
		return false;
	}

	internal static bool IsAllowedRegistryValueName(string? name)
	{
		if (!string.IsNullOrWhiteSpace(name) && !name.Contains('\\', StringComparison.Ordinal))
		{
			return !name.Contains('/', StringComparison.Ordinal);
		}
		return false;
	}

	internal static bool IsRestorableRegistryEntry(string? hive, string? keyPath, string? name, RegistryValueKind kind)
	{
		if (IsAllowedRegistryHive(hive) && IsAllowedRunKeyPath(keyPath) && IsAllowedRegistryValueName(name))
		{
			return IsAllowedRegistryValueKind(kind);
		}
		return false;
	}

	internal static bool IsRestorableStartupFile(string? originalPath, string? disabledPath, IEnumerable<string> startupFolders)
	{
		if (string.IsNullOrWhiteSpace(originalPath) || string.IsNullOrWhiteSpace(disabledPath))
		{
			return false;
		}
		if (!string.Equals(disabledPath, originalPath + ".lmechrevo-disabled", StringComparison.Ordinal))
		{
			return false;
		}
		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(originalPath);
		}
		catch
		{
			return false;
		}
		foreach (string startupFolder in startupFolders)
		{
			if (!string.IsNullOrWhiteSpace(startupFolder))
			{
				string fullPath2;
				try
				{
					fullPath2 = Path.GetFullPath(startupFolder);
				}
				catch
				{
					continue;
				}
				string directoryName = Path.GetDirectoryName(fullPath);
				if (directoryName != null && string.Equals(directoryName.TrimEnd(Path.DirectorySeparatorChar), fullPath2.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static string[] StartupFolders()
	{
		return new string[2]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.Startup),
			Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
		};
	}

	private static RestoreOutcome RestoreSnapshot(IsolationSnapshot snapshot)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int expected = snapshot.RegistryEntries.Count + snapshot.ScheduledTasks.Count + snapshot.StartupFiles.Count;
		foreach (RegistryStartupEntry registryEntry in snapshot.RegistryEntries)
		{
			if (!IsRestorableRegistryEntry(registryEntry.Hive, registryEntry.KeyPath, registryEntry.Name, registryEntry.Kind))
			{
				Logger.WriteLine($"Refusing to restore an out-of-policy registry entry: {registryEntry.Hive}\\{registryEntry.KeyPath}\\{registryEntry.Name} ({registryEntry.Kind})");
				num2++;
				continue;
			}
			try
			{
				using RegistryKey registryKey = RegistryKey.OpenBaseKey((registryEntry.Hive == "HKLM") ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, registryEntry.View);
				using RegistryKey registryKey2 = registryKey.OpenSubKey(registryEntry.KeyPath, writable: true);
				if (registryKey2 == null)
				{
					num2++;
					continue;
				}
				if (registryKey2.GetValue(registryEntry.Name) != null)
				{
					num2++;
					continue;
				}
				registryKey2.SetValue(registryEntry.Name, registryEntry.Value, registryEntry.Kind);
				num++;
			}
			catch (Exception ex)
			{
				Logger.WriteLine($"Can't restore registry entry {registryEntry.Hive}\\{registryEntry.KeyPath}\\{registryEntry.Name}: {ex.Message}");
				num3++;
			}
		}
		try
		{
			using TaskService taskService = new TaskService();
			foreach (ScheduledTaskEntry scheduledTask in snapshot.ScheduledTasks)
			{
				if (!IsManageableTaskPath(scheduledTask.Path))
				{
					Logger.WriteLine("Refusing to restore an out-of-policy scheduled task: " + scheduledTask.Path);
					num2++;
					continue;
				}
				try
				{
					Microsoft.Win32.TaskScheduler.Task task = taskService.GetTask(scheduledTask.Path);
					if (task == null || task.Enabled == scheduledTask.WasEnabled)
					{
						num2++;
						continue;
					}
					task.Enabled = scheduledTask.WasEnabled;
					num++;
				}
				catch (Exception ex2)
				{
					Logger.WriteLine("Can't restore scheduled task " + scheduledTask.Path + ": " + ex2.Message);
					num3++;
				}
			}
		}
		catch (Exception ex3)
		{
			Logger.WriteLine("Can't open the task scheduler for restore: " + ex3.Message);
			num3 += snapshot.ScheduledTasks.Count;
		}
		string[] startupFolders = StartupFolders();
		foreach (StartupFileEntry startupFile in snapshot.StartupFiles)
		{
			if (!IsRestorableStartupFile(startupFile.OriginalPath, startupFile.DisabledPath, startupFolders))
			{
				Logger.WriteLine("Refusing to restore an out-of-policy startup file: " + startupFile.DisabledPath + " -> " + startupFile.OriginalPath);
				num2++;
				continue;
			}
			try
			{
				if (!File.Exists(startupFile.DisabledPath) || File.Exists(startupFile.OriginalPath))
				{
					num2++;
					continue;
				}
				File.Move(startupFile.DisabledPath, startupFile.OriginalPath);
				num++;
			}
			catch (Exception ex4)
			{
				Logger.WriteLine("Can't restore startup file " + startupFile.OriginalPath + ": " + ex4.Message);
				num3++;
			}
		}
		return new RestoreOutcome(num, num2, num3, expected);
	}

	private static int StopOfficialUiProcesses()
	{
		int num = 0;
		string[] uiProcessNames = UiProcessNames;
		foreach (string text in uiProcessNames)
		{
			Process[] processesByName = Process.GetProcessesByName(text);
			foreach (Process process in processesByName)
			{
				using (process)
				{
					try
					{
						if (process.Id == Environment.ProcessId || process.HasExited || process.SessionId != CurrentSessionId)
						{
							continue;
						}
						if (process.CloseMainWindow() && process.WaitForExit(800))
						{
							num++;
							continue;
						}
						process.Kill();
						if (!process.WaitForExit(1500))
						{
							Logger.WriteLine($"Official UI process did not exit in time: {text} ({process.Id})");
						}
						else
						{
							num++;
							Logger.WriteLine($"Stopped official UI process: {text} ({process.Id})");
						}
					}
					catch (Exception ex)
					{
						Logger.WriteLine($"Can't stop official UI process {text} ({process.Id}): {ex.Message}");
					}
				}
			}
		}
		if (num > 0)
		{
			RefreshTrayArea();
		}
		return num;
	}

	private static bool IsOfficialUiRunning()
	{
		int sessionId = CurrentSessionId;
		string[] uiProcessNames = UiProcessNames;
		foreach (string text in uiProcessNames)
		{
			Process[] processesByName = Process.GetProcessesByName(text);
			try
			{
				if (processesByName.Any((Process p) => !p.HasExited && p.SessionId == sessionId))
				{
					return true;
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLineThrottled("official-ui-probe", "Can't inspect official UI process " + text + ": " + ex.Message, 10000);
			}
			finally
			{
				Process[] array = processesByName;
				for (int num = 0; num < array.Length; num++)
				{
					array[num].Dispose();
				}
			}
		}
		return false;
	}

	private static bool IsOfficialUiInstalled()
	{
		long tickCount = Environment.TickCount64;
		lock (InstallationProbeLock)
		{
			if (installedCache.HasValue && tickCount < installedCacheUntil)
			{
				return installedCache.Value;
			}
		}
		bool flag = ProbeOfficialUiInstalled();
		lock (InstallationProbeLock)
		{
			installedCache = flag;
			installedCacheUntil = Environment.TickCount64 + (long)TimeSpan.FromMinutes(5L).TotalMilliseconds;
			return flag;
		}
	}

	private static bool ProbeOfficialUiInstalled()
	{
		// 官方判定：安装注册状态，而不是文件/目录残留。
		// 1) 官方 Inno Setup 卸载键（与安装器 UninstallOfficialConsole 的口径一致）
		try
		{
			string[] uninstallRoots = new string[2]
			{
				@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + OfficialAppId,
				@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + OfficialAppId
			};
			foreach (string subKey in uninstallRoots)
			{
				if (Registry.LocalMachine.OpenSubKey(subKey) != null)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		// 2) MSIX 包**注册状态**：与安装器 Install-Gcu.ps1 的 Get-AppxPackage 同口径。
		//    WindowsApps\CCU.WinUI_* 目录存在只说明有文件残留（旧版本没清干净），不代表包已注册，
		//    所以不再拿"目录存在"当判据（2026-10-06 耀世 25 5080 机主反馈）。
		bool? registered = null;
		try
		{
			registered = TryQueryRegisteredOfficialPackage();
		}
		catch
		{
		}
		if (registered == true)
		{
			return true;
		}
		if (!registered.HasValue)
		{
			// AppX 查询不可用（投影缺失/被策略拦）时才回退注册表包存储：
			// 该键记录的同样是"注册/暂存"状态而不是目录，比目录存在可靠。
			try
			{
				using (RegistryKey? appxRoot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications"))
				{
					if (appxRoot != null && appxRoot.GetSubKeyNames().Any((string name) => name.StartsWith("CCU.WinUI", StringComparison.OrdinalIgnoreCase)))
					{
						return true;
					}
				}
			}
			catch
			{
			}
		}
		// 3) 传统 OEM 路径兜底（真·桌面安装目录，不是 WindowsApps 残留）
		try
		{
			string path2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OEM");
			return Directory.Exists(path2) && Directory.EnumerateFiles(path2, "ControlCenterU.exe", SearchOption.AllDirectories).Any();
		}
		catch
		{
			return false;
		}
	}

	private static bool IsOfficialReference(string value)
	{
		return OfficialMarkers.Any((string marker) => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// 2026-10-06：查询官方控制台（CCU.WinUI*）的 AppX **包注册状态**，等价于安装器脚本
	/// Install-Gcu.ps1 里的 Get-AppxPackage。只有包真的已注册才算"仍有厂商 GCU 环境"；
	/// WindowsApps 下的目录/文件残留一律不算。返回 null 表示本机拿不到 AppX 状态（调用方回退注册表）。
	/// </summary>
	private static bool? TryQueryRegisteredOfficialPackage()
	{
		try
		{
			PackageManager packageManager = new PackageManager();
			IEnumerable<Package> packages;
			try
			{
				packages = packageManager.FindPackages();
			}
			catch (Exception)
			{
				// 非管理员时"所有用户"查询会被拒；退回当前用户（与 Get-AppxPackage 默认范围一致）。
				packages = packageManager.FindPackagesForUser(string.Empty);
			}
			bool found = packages.Any((Package package) => IsOfficialPackageName(package.Id?.Name));
			Logger.WriteLineThrottled("official-package-probe", "Official console package registration probe: registered=" + (found ? "yes" : "no"), 3600000);
			return found;
		}
		catch (Exception ex)
		{
			Logger.WriteLineThrottled("official-package-probe-failed", "AppX package query unavailable (" + ex.Message + "); falling back to the AppxAllUserStore registry store.", 3600000);
			return null;
		}
	}

	private static bool IsOfficialPackageName(string? name)
	{
		return !string.IsNullOrWhiteSpace(name) && name.StartsWith("CCU.WinUI", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 只读：WindowsApps 下是否有官方控制台的**目录残留**。只用于日志解释，不参与"是否已安装"判定。
	/// </summary>
	private static void LogWindowsAppsResidueIfAny()
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
			if (!Directory.Exists(path))
			{
				return;
			}
			int count = Directory.EnumerateDirectories(path, "CCU.WinUI_*").Count();
			if (count > 0)
			{
				Logger.WriteLineThrottled("official-console-residue", $"Official console: no registered package and no uninstall entry, but leftover WindowsApps\\CCU.WinUI_* directories exist (count={count}) -> still reported as not detected (directory residue is not an install).", 3600000);
			}
		}
		catch
		{
			// WindowsApps 的 ACL 默认不允许普通用户枚举；读不到就当没有残留。
		}
	}

	private static bool IsProcessRunning(string name)
	{
		Process[] processesByName = Process.GetProcessesByName(name);
		try
		{
			return processesByName.Length != 0;
		}
		finally
		{
			Process[] array = processesByName;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Dispose();
			}
		}
	}

	private static SnapshotReadResult ReadSnapshot()
	{
		try
		{
			if (!File.Exists(SnapshotPath))
			{
				return new SnapshotReadResult(SnapshotReadStatus.Missing, null);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't probe the official console snapshot: " + ex.Message);
			return new SnapshotReadResult(SnapshotReadStatus.Unreadable, null);
		}
		try
		{
			IsolationSnapshot isolationSnapshot = JsonSerializer.Deserialize<IsolationSnapshot>(File.ReadAllText(SnapshotPath), JsonOptions);
			if (isolationSnapshot == null)
			{
				Logger.WriteLine("Official console snapshot deserialized to null.");
				return new SnapshotReadResult(SnapshotReadStatus.Unreadable, null);
			}
			if (isolationSnapshot.Version != 1)
			{
				Logger.WriteLine($"Official console snapshot version {isolationSnapshot.Version} is not supported (expected {1}).");
				return new SnapshotReadResult(SnapshotReadStatus.Unreadable, null);
			}
			return new SnapshotReadResult(SnapshotReadStatus.Loaded, isolationSnapshot);
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Can't read official console snapshot: " + ex2.Message);
			return new SnapshotReadResult(SnapshotReadStatus.Unreadable, null);
		}
	}

	private static IsolationSnapshot? TryReadSnapshot()
	{
		return ReadSnapshot().Snapshot;
	}

	private static void SaveSnapshot(IsolationSnapshot snapshot)
	{
		EnsureSnapshotDirectory(Path.GetDirectoryName(SnapshotPath) ?? throw new InvalidOperationException("Invalid isolation snapshot path."));
		string text = $"{SnapshotPath}.{Environment.ProcessId}.tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(snapshot, JsonOptions));
		File.Move(text, SnapshotPath, overwrite: true);
	}

	private static void EnsureSnapshotDirectory(string directory)
	{
		bool flag = Directory.Exists(directory);
		Directory.CreateDirectory(directory);
		try
		{
			DirectorySecurity directorySecurity = new DirectorySecurity();
			directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
			SecurityIdentifier identity = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
			SecurityIdentifier identity2 = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
			SecurityIdentifier identity3 = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
			directorySecurity.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
			directorySecurity.AddAccessRule(new FileSystemAccessRule(identity2, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
			directorySecurity.AddAccessRule(new FileSystemAccessRule(identity3, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
			new DirectoryInfo(directory).SetAccessControl(directorySecurity);
			if (!flag)
			{
				Logger.WriteLine("Created the isolation snapshot directory with an administrators-only ACL.");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't tighten the isolation snapshot directory ACL: " + ex.Message);
		}
	}

	private static bool IsAdministrator()
	{
		using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	private static void EnsureAdministrator()
	{
		if (!IsAdministrator())
		{
			throw new UnauthorizedAccessException("Official console isolation requires administrator rights.");
		}
	}

	private static void RefreshTrayArea()
	{
		nint parent = FindWindowEx(FindWindowEx(FindWindow("Shell_TrayWnd", null), IntPtr.Zero, "TrayNotifyWnd", null), IntPtr.Zero, "SysPager", null);
		RefreshToolbar(FindWindowEx(parent, IntPtr.Zero, "ToolbarWindow32", "Notification Area"));
		RefreshToolbar(FindWindowEx(parent, IntPtr.Zero, "ToolbarWindow32", "User Promoted Notification Area"));
		RefreshToolbar(FindWindowEx(FindWindow("NotifyIconOverflowWindow", null), IntPtr.Zero, "ToolbarWindow32", "Overflow Notification Area"));
	}

	private static void RefreshToolbar(nint handle)
	{
		if (handle == IntPtr.Zero || !GetClientRect(handle, out var rect))
		{
			return;
		}
		for (int i = 0; i < rect.Right; i += 8)
		{
			for (int j = 0; j < rect.Bottom; j += 8)
			{
				PostMessage(handle, 512u, IntPtr.Zero, (j << 16) | (i & 0xFFFF));
			}
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern nint FindWindow(string className, string? windowName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern nint FindWindowEx(nint parent, nint childAfter, string className, string? windowName);

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(nint window, out RECT rect);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
