namespace MechrevoLite.Gpu.NVidia;

public sealed record GpuOverclockApplyRequest(int? CoreOffset, int? MemoryOffset, bool? Enabled, string? Token = null);
