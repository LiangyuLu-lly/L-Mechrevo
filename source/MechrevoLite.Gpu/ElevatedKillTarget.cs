using System;

namespace MechrevoLite.Gpu;

internal sealed record ElevatedKillTarget(int ProcessId, string ProcessName, DateTime StartedAtUtc);
