using System.Collections.Generic;

namespace MechrevoLite.Diagnostics;

internal sealed record DiagnosticPackInputs(string Version, string ReleaseLabel, string SystemInfoText, IReadOnlyList<DiagnosticPackFile> LogFiles, IReadOnlyList<DiagnosticPackFile> ConfigFiles, string? CrashRingBufferText = null, string? LogLevel = null, string LogLevelSource = null);
