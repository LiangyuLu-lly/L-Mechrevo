using System;

namespace MechrevoLite.Gpu;

internal readonly record struct DgpuApplication(int ProcessId, string ProcessName, int SessionId, DateTime? StartedAtUtc = null, bool HasMainWindow = false);
