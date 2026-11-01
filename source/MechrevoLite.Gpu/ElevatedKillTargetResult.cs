namespace MechrevoLite.Gpu;

internal sealed record ElevatedKillTargetResult(int ProcessId, string ProcessName, bool Killed, string? Error = null);
