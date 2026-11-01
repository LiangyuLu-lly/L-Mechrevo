using System;
using System.Threading.Tasks;

namespace MechrevoLite.Diagnostics;

internal static class DiagnosticPackRunner
{
	internal static Task<DiagnosticPackResult> ExportAsync(string zipPath, DiagnosticPackInputs inputs)
	{
		// 2026-10-22（机主：诊断包体积爆大）：单文件只取末尾 2 MiB；配合 Capture 只带 log.txt + log.1，
		// 包内日志从“最多 4×8 MiB”降到“最多 3×2 MiB”。
		return RunOffCallingThread(() => DiagnosticPackExporter.Export(zipPath, inputs, 2L * 1024L * 1024L));
	}

	internal static Task<T> RunOffCallingThread<T>(Func<T> work)
	{
		return Task.Run(work);
	}
}
