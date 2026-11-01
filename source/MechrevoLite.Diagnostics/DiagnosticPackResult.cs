using System.Collections.Generic;

namespace MechrevoLite.Diagnostics;

internal sealed record DiagnosticPackResult(string ZipPath, IReadOnlyList<string> Entries, long SizeBytes);
