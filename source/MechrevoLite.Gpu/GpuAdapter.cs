using System;

namespace MechrevoLite.Gpu;

public sealed record GpuAdapter(string Name, string? VendorId, string? DeviceId)
{
	public bool IsNvidia => string.Equals(VendorId, "10DE", StringComparison.OrdinalIgnoreCase);

	public const string NvidiaVendorId = "10DE";
}
