namespace MechrevoLite.Gpu.NVidia;

public sealed record GpuOverclockApplyResult(bool Success, int CoreOffset, int MemoryOffset, string? Error);
