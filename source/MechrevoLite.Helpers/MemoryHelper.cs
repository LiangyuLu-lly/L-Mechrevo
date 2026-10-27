using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MechrevoLite.Helpers;

public static class MemoryHelper
{
	[DllImport("kernel32.dll")]
	private static extern bool SetProcessWorkingSetSize(nint hProcess, nint dwMin, nint dwMax);

	public static void TrimAfter(Task? prerequisite = null, TimeSpan? timeout = null)
	{
		Task.Run(async () =>
		{
			if (prerequisite != null)
			{
				try
				{
					await prerequisite.WaitAsync(timeout ?? TimeSpan.FromSeconds(3L));
				}
				catch
				{
				}
			}
			Trim();
		});
	}

	public static void TrimLater(TimeSpan delay, Func<bool> stillIdle)
	{
		Task.Run(async () =>
		{
			await Task.Delay(delay).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				if (stillIdle())
				{
					Trim();
				}
			}
			catch
			{
			}
		});
	}

	private static void Trim()
	{
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
		GC.WaitForPendingFinalizers();
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
		using Process process = Process.GetCurrentProcess();
		SetProcessWorkingSetSize(process.Handle, -1, -1);
	}
}
