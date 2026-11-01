using System;
using MechrevoLite.Properties;using System.Collections.Generic;
using MechrevoLite.Properties;using System.ComponentModel;
using MechrevoLite.Properties;using System.Diagnostics;
using MechrevoLite.Properties;using System.IO;
using MechrevoLite.Properties;using System.Linq;
using MechrevoLite.Properties;using System.Text.Json;
using MechrevoLite.Properties;using System.Threading.Tasks;
using MechrevoLite.Properties;using System.Windows.Forms;
using MechrevoLite.Properties;using MechrevoLite.Helpers;
using MechrevoLite.Properties;
namespace MechrevoLite.Gpu;

internal static class ElevatedProcessKiller
{
	private static readonly TimeSpan KillWaitTimeout = TimeSpan.FromSeconds(30L);

	internal static async Task<(bool Clean, string Message)> KillWithElevationAsync(IReadOnlyList<DgpuApplication> targets)
	{
		if (targets.Count == 0)
		{
			return (Clean: true, Message: Strings.NoElevatedKillTargets);
		}
		string token = Guid.NewGuid().ToString("N");
		string requestPath = Path.Combine(Path.GetTempPath(), $"LMechrevoKill-{Guid.NewGuid():N}.json");
		string resultPath = Path.Combine(Path.GetTempPath(), $"LMechrevoKill-{Guid.NewGuid():N}.result.json");
		ElevatedKillRequest elevatedKillRequest = new ElevatedKillRequest(token, (from t in targets
			where t.StartedAtUtc.HasValue
			select new ElevatedKillTarget(t.ProcessId, t.ProcessName, t.StartedAtUtc.Value)).ToList());
		if (elevatedKillRequest.Targets.Count == 0)
		{
			return (Clean: false, Message: Strings.KillMissingStartTime);
		}
		try
		{
			await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(elevatedKillRequest)).ConfigureAwait(continueOnCapturedContext: false);
			Process helper;
			try
			{
				if (!ExecutableTrust.IsCurrentImageInProtectedLocation(out string reason))
				{
					Logger.WriteLineThrottled("kill-helper-trust", "Kill helper is being elevated from a user-writable location: " + reason, 60000);
				}
				helper = Process.Start(new ProcessStartInfo
				{
					FileName = Application.ExecutablePath,
					Arguments = $"--kill-process \"{requestPath}\" \"{resultPath}\" {token}",
					UseShellExecute = true,
					Verb = "runas",
					WindowStyle = ProcessWindowStyle.Hidden
				});
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
			{
				Logger.WriteLine("Elevated kill was cancelled by the user.");
				return (Clean: false, Message: Strings.KillElevationCancelled);
			}
			catch (Exception ex2)
			{
				Logger.WriteLine("Elevated kill helper start failed: " + ex2.Message);
				return (Clean: false, Message: Strings.KillCannotLaunchElevated);
			}
			if (helper == null)
			{
				return (Clean: false, Message: Strings.KillCannotLaunchElevated2);
			}
			try
			{
				await helper.WaitForExitAsync().WaitAsync(KillWaitTimeout).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (TimeoutException)
			{
				Logger.WriteLine("Elevated kill helper did not exit in time; continuing with its result file if any.");
			}
			helper.Dispose();
			if (!File.Exists(resultPath))
			{
				return (Clean: false, Message: Strings.KillNoResult);
			}
			ElevatedKillResult elevatedKillResult;
			try
			{
				elevatedKillResult = JsonSerializer.Deserialize<ElevatedKillResult>(await File.ReadAllTextAsync(resultPath).ConfigureAwait(continueOnCapturedContext: false));
			}
			catch (JsonException ex4)
			{
				Logger.WriteLine("Elevated kill result is malformed: " + ex4.Message);
				return (Clean: false, Message: Strings.KillInvalidResult);
			}
			if ((object)elevatedKillResult == null)
			{
				return (Clean: false, Message: Strings.KillInvalidResult);
			}
			int value = elevatedKillResult.Results.Count((ElevatedKillTargetResult r) => r.Killed);
			return (Clean: elevatedKillResult.Success, Message: elevatedKillResult.Success ? string.Format(Strings.KilledN, value) : (Strings.SomeKillFailed + string.Join("；", from r in elevatedKillResult.Results
				where !r.Killed
				select r.ProcessName + "(" + (r.Error ?? Strings.UnknownReason) + ")")));
		}
		finally
		{
			TryDelete(requestPath);
			TryDelete(resultPath);
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Elevated kill temp file cleanup failed: " + ex.Message);
		}
	}

	internal static int RunHelper(string[] args)
	{
		if (args.Length != 3)
		{
			return 2;
		}
		if (!IsValidToken(args[2]))
		{
			Logger.WriteLine("Elevated kill helper received an invalid token.");
			return 2;
		}
		ElevatedKillRequest elevatedKillRequest;
		try
		{
			elevatedKillRequest = JsonSerializer.Deserialize<ElevatedKillRequest>(File.ReadAllText(args[0]));
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Elevated kill request is unreadable: " + ex.Message);
			return 2;
		}
		if ((object)elevatedKillRequest == null || !TokenMatches(args[2], elevatedKillRequest.Token))
		{
			Logger.WriteLine("Elevated kill helper rejected a request with an invalid token.");
			return 2;
		}
		ElevatedKillResult elevatedKillResult = Execute(elevatedKillRequest);
		try
		{
			File.WriteAllText(args[1], JsonSerializer.Serialize(elevatedKillResult));
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Elevated kill result write failed: " + ex2.Message);
			return 3;
		}
		return (!elevatedKillResult.Success) ? 1 : 0;
	}

	internal static ElevatedKillResult Execute(ElevatedKillRequest request)
	{
		using Process process = Process.GetCurrentProcess();
		List<ElevatedKillTargetResult> list = new List<ElevatedKillTargetResult>();
		foreach (ElevatedKillTarget target in request.Targets)
		{
			list.Add(KillTarget(target, process.Id, process.SessionId));
		}
		return new ElevatedKillResult(list.All((ElevatedKillTargetResult r) => r.Killed), list);
	}

	private static ElevatedKillTargetResult KillTarget(ElevatedKillTarget target, int helperPid, int helperSession)
	{
		try
		{
			using Process process = Process.GetProcessById(target.ProcessId);
			string text = ValidateTarget(process, target, helperPid, helperSession);
			if (text != null)
			{
				Logger.WriteLine($"Elevated kill skipped {target.ProcessName}#{target.ProcessId}: {text}");
				return new ElevatedKillTargetResult(target.ProcessId, target.ProcessName, Killed: false, text);
			}
			process.Kill(entireProcessTree: true);
			process.WaitForExit(1500);
			Logger.WriteLine($"Elevated kill: {target.ProcessName}#{target.ProcessId} killed");
			return new ElevatedKillTargetResult(target.ProcessId, target.ProcessName, Killed: true);
		}
		catch (ArgumentException)
		{
			return new ElevatedKillTargetResult(target.ProcessId, target.ProcessName, Killed: true, Strings.ProcessExited);
		}
		catch (Exception ex2)
		{
			Logger.WriteLine($"Elevated kill failed for {target.ProcessName}#{target.ProcessId}: {ex2.Message}");
			return new ElevatedKillTargetResult(target.ProcessId, target.ProcessName, Killed: false, ex2.Message);
		}
	}

	internal static string? ValidateTarget(Process process, ElevatedKillTarget target, int helperPid, int helperSession)
	{
		try
		{
			if (!string.Equals(process.ProcessName, target.ProcessName, StringComparison.OrdinalIgnoreCase))
			{
				return Strings.KillPidReusedName;
			}
			if (process.StartTime.ToUniversalTime() != target.StartedAtUtc)
			{
				return Strings.KillPidReusedStartTime;
			}
		}
		catch (Exception ex)
		{
			return Strings.KillCannotReadIdentity + ex.Message;
		}
		if (!DgpuApplicationSafety.IsSafeCandidate(new DgpuApplication(target.ProcessId, target.ProcessName, helperSession, target.StartedAtUtc), helperPid, helperSession))
		{
			return Strings.KillTargetProtected;
		}
		return null;
	}

	internal static bool IsValidToken(string token)
	{
		if (token.Length == 32)
		{
			return token.All(char.IsAsciiHexDigit);
		}
		return false;
	}

	internal static bool TokenMatches(string? expected, string? provided)
	{
		if (expected == null || provided == null)
		{
			return false;
		}
		if (expected.Length != provided.Length)
		{
			return false;
		}
		int num = 0;
		for (int i = 0; i < expected.Length; i++)
		{
			num |= expected[i] ^ provided[i];
		}
		return num == 0;
	}
}
