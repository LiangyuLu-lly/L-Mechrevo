using System;
using System.Threading.Tasks;

namespace MechrevoLite.Diagnostics;

internal static class DiagnosticPackRunner
{
	internal static Task<DiagnosticPackResult> ExportAsync(string zipPath, DiagnosticPackInputs inputs)
	{
		return RunOffCallingThread(() => DiagnosticPackExporter.Export(zipPath, inputs, 8388608L));
	}

	internal static Task<T> RunOffCallingThread<T>(Func<T> work)
	{
		return Task.Run(work);
	}
}
