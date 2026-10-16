using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MechrevoLite.Helpers;

public static class ProcessHelper
{
	private const string ExitEventName = "Global\\LMechrevoApp-Exit";

	private static EventWaitHandle? exitEvent;

	private static long lastAdmin;

	private static bool? _isSystem;

	public static bool IsRunningAsSystem()
	{
		if (_isSystem.HasValue)
		{
			return _isSystem.Value;
		}
		using (WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent())
		{
			if (windowsIdentity == null)
			{
				_isSystem = false;
			}
			else
			{
				_isSystem = string.Equals(windowsIdentity.Name, "NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase);
			}
		}
		return _isSystem.Value;
	}

	public static bool IsLegacyHandoffAction(string? action)
	{
		if ((action == null || !action.Equals("cpu", StringComparison.OrdinalIgnoreCase)) && (action == null || !action.Equals("gpu", StringComparison.OrdinalIgnoreCase)) && (action == null || !action.Equals("uv", StringComparison.OrdinalIgnoreCase)))
		{
			return action?.Equals("services", StringComparison.OrdinalIgnoreCase) ?? false;
		}
		return true;
	}

	public static bool ShouldContinueAfterExistingInstance(bool handoff, bool ownerExited)
	{
		return handoff & ownerExited;
	}

	public static bool IsGpuOverclockHelperCommandLine(string? commandLine)
	{
		if (!string.IsNullOrWhiteSpace(commandLine))
		{
			return commandLine.Contains("--gpu-oc-helper", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	public static bool CheckAlreadyRunning(string? action = null)
	{
		EventWaitHandle eventWaitHandle = (exitEvent = CreateOrOpenExitEvent(out var created));
		if (eventWaitHandle == null)
		{
			Logger.WriteLine("Singleton event unavailable; continuing without duplicate-process enforcement.");
			return true;
		}
		if (!created)
		{
			bool flag = IsLegacyHandoffAction(action);
			List<Process> list = FindExistingUiProcesses();
			try
			{
				if (!flag)
				{
					if (list.Count == 0)
					{
						Logger.WriteLine("Singleton event has no owner in this session; continuing.");
						ReleaseExitEvent(eventWaitHandle);
						return true;
					}
					Logger.WriteLine("Another L-Mechrevo instance is already running; leaving it untouched.");
					ReleaseExitEvent(eventWaitHandle);
					return false;
				}
				if (list.Count == 0)
				{
					Logger.WriteLine("No L-Mechrevo owner exists in this session; skipping legacy handoff signal.");
					ReleaseExitEvent(eventWaitHandle);
					return true;
				}
				try
				{
					eventWaitHandle.Set();
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Broadcast exit failed: " + ex.Message);
					ReleaseExitEvent(eventWaitHandle);
					return false;
				}
				bool ownerExited = WaitForExistingOwnersExit(list, TimeSpan.FromSeconds(8L));
				if (!ShouldContinueAfterExistingInstance(flag, ownerExited))
				{
					Logger.WriteLine("Legacy elevation handoff stopped because the existing UI did not exit cleanly.");
					ReleaseExitEvent(eventWaitHandle);
					return false;
				}
				try
				{
					eventWaitHandle.Reset();
				}
				catch (Exception ex2)
				{
					Logger.WriteLine("Resetting singleton event after handoff failed: " + ex2.Message);
					ReleaseExitEvent(eventWaitHandle);
					return false;
				}
			}
			finally
			{
				foreach (Process item in list)
				{
					item.Dispose();
				}
			}
		}
		ThreadPool.RegisterWaitForSingleObject(eventWaitHandle, (object? _, bool _) =>
		{
			Application.Exit();
		}, null, -1, executeOnlyOnce: true);
		return true;
	}

	private static void ReleaseExitEvent(EventWaitHandle currentEvent)
	{
		if (exitEvent == currentEvent)
		{
			exitEvent = null;
		}
		currentEvent.Dispose();
	}

	private static EventWaitHandle? CreateOrOpenExitEvent(out bool created)
	{
		created = false;
		try
		{
			EventWaitHandleSecurity eventWaitHandleSecurity = new EventWaitHandleSecurity();
			eventWaitHandleSecurity.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, AccessControlType.Allow));
			return EventWaitHandleAcl.Create(initialState: false, EventResetMode.ManualReset, "Global\\LMechrevoApp-Exit", out created, eventWaitHandleSecurity);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Creating singleton event failed: " + ex.Message);
			try
			{
				return EventWaitHandle.OpenExisting("Global\\LMechrevoApp-Exit");
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("Opening singleton event failed: " + ex2.Message);
				return null;
			}
		}
	}

	private static List<Process> FindExistingUiProcesses()
	{
		using Process process = Process.GetCurrentProcess();
		int sessionId;
		try
		{
			sessionId = process.SessionId;
		}
		catch
		{
			return new List<Process>();
		}
		Process[] processesByName;
		try
		{
			processesByName = Process.GetProcessesByName(process.ProcessName);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Enumerating existing instances failed: " + ex.Message);
			return new List<Process>();
		}
		List<Process> list = new List<Process>();
		Process[] array = processesByName;
		foreach (Process process2 in array)
		{
			if (process2.Id == process.Id || !SameSession(process2, sessionId))
			{
				process2.Dispose();
			}
			else if (IsGpuOverclockHelper(process2))
			{
				Logger.WriteLine($"Ignoring GPU overclock helper PID {process2.Id} during singleton handoff.");
				process2.Dispose();
			}
			else
			{
				list.Add(process2);
			}
		}
		return list;
	}

	private static bool WaitForExistingOwnersExit(IReadOnlyList<Process> owners, TimeSpan timeout)
	{
		DateTime dateTime = DateTime.UtcNow + timeout;
		foreach (Process owner in owners)
		{
			while (true)
			{
				bool flag;
				try
				{
					flag = owner.HasExited;
				}
				catch (InvalidOperationException)
				{
					flag = true;
				}
				catch (Exception ex2)
				{
					Logger.WriteLine($"Reading existing instance PID {owner.Id} failed: {ex2.Message}");
					flag = false;
				}
				if (flag)
				{
					break;
				}
				TimeSpan timeSpan = dateTime - DateTime.UtcNow;
				if (timeSpan <= TimeSpan.Zero)
				{
					return false;
				}
				try
				{
					owner.WaitForExit((int)Math.Min(250.0, Math.Max(1.0, timeSpan.TotalMilliseconds)));
				}
				catch (InvalidOperationException)
				{
					break;
				}
				catch (Exception ex4)
				{
					Logger.WriteLine($"Waiting for existing instance PID {owner.Id} failed: {ex4.Message}");
					return false;
				}
			}
		}
		return true;
	}

	private static bool IsGpuOverclockHelper(Process process)
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectCollection.GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
				try
				{
					return IsGpuOverclockHelperCommandLine(managementObject["CommandLine"] as string);
				}
				finally
				{
					managementObject.Dispose();
				}
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"Reading command line for PID {process.Id} failed: {ex.Message}");
		}
		return false;
	}

	private static bool SameSession(Process process, int sessionId)
	{
		try
		{
			return process.SessionId == sessionId;
		}
		catch (Exception ex)
		{
			Logger.WriteLine($"Can't read session of PID {process.Id}: {ex.Message}");
			return false;
		}
	}

	public static bool IsUserAdministrator()
	{
		using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	public static void RunAsAdmin(string? param = null, bool force = false)
	{
		if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastAdmin) < 2000)
		{
			return;
		}
		lastAdmin = DateTimeOffset.Now.ToUnixTimeMilliseconds();
		if (!(!IsUserAdministrator() | force))
		{
			return;
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo();
		processStartInfo.UseShellExecute = true;
		processStartInfo.WorkingDirectory = Environment.CurrentDirectory;
		processStartInfo.FileName = Application.ExecutablePath;
		processStartInfo.Arguments = param;
		processStartInfo.Verb = "runas";
		try
		{
			Process.Start(processStartInfo);
			Application.Exit();
		}
		catch (Exception ex)
		{
			Logger.WriteLine(ex.Message);
		}
	}

	public static void KillByName(string name)
	{
		Process[] processesByName = Process.GetProcessesByName(name);
		try
		{
			Process[] array = processesByName;
			foreach (Process process in array)
			{
				try
				{
					process.Kill();
					Logger.WriteLine("Stopped: " + process.ProcessName);
				}
				catch (Exception ex)
				{
					Logger.WriteLine("Failed to stop: " + process.ProcessName + " " + ex.Message);
				}
			}
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

	public static void KillSmartDisplayControl()
	{
		KillByName("ASUSSmartDisplayControl");
	}

	public static void KillByProcess(Process process)
	{
		try
		{
			process.Kill();
			Logger.WriteLine("Stopped: " + process.ProcessName);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Failed to stop: " + process.ProcessName + " " + ex.Message);
		}
	}

	public static string RunCMD(string name, string args, string? directory = null, int timeoutMs = 0)
	{
		using Process process = new Process();
		process.StartInfo.UseShellExecute = false;
		process.StartInfo.CreateNoWindow = true;
		process.StartInfo.RedirectStandardOutput = true;
		process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
		process.StartInfo.FileName = name;
		process.StartInfo.Arguments = args;
		if (directory != null)
		{
			process.StartInfo.WorkingDirectory = directory;
		}
		process.Start();
		Stopwatch stopwatch = Stopwatch.StartNew();
		string text;
		if (timeoutMs > 0)
		{
			Task<string> task = process.StandardOutput.ReadToEndAsync();
			if (!task.Wait(timeoutMs))
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch
				{
				}
				stopwatch.Stop();
				Logger.WriteLine(name + " " + args);
				Logger.WriteLine($"{stopwatch.ElapsedMilliseconds} ms: TIMEOUT after {timeoutMs} ms");
				return string.Empty;
			}
			text = task.Result.Replace(Environment.NewLine, " ").Trim(' ');
		}
		else
		{
			text = process.StandardOutput.ReadToEnd().Replace(Environment.NewLine, " ").Trim(' ');
		}
		stopwatch.Stop();
		Logger.WriteLine(name + " " + args);
		Logger.WriteLine(stopwatch.ElapsedMilliseconds + " ms: " + text);
		process.WaitForExit();
		return text;
	}

	public static void SetPriority(ProcessPriorityClass priorityClass = ProcessPriorityClass.Normal)
	{
		try
		{
			using Process process = Process.GetCurrentProcess();
			process.PriorityClass = priorityClass;
		}
		catch (Exception ex)
		{
			Logger.WriteLine(ex.ToString());
		}
	}
}
