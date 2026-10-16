namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-15（igpu-gen50-amd）：CPU 厂商三态。<see cref="Unknown" /> 是**一等公民** —— 读不到 /
/// 认不出就是它，⛔ 绝不用"不是 Intel 就是 AMD"这种二分法（会误伤 Intel 机型）。
/// 见 <see cref="CpuVendor" />。
/// </summary>
internal enum CpuVendorKind
{
	/// <summary>读不到或认不出（注册表缺失 / 权限失败 / 未知厂商串）。下游一律按"不成立"处理。</summary>
	Unknown,
	/// <summary><c>VendorIdentifier=AuthenticAMD</c>。</summary>
	Amd,
	/// <summary><c>VendorIdentifier=GenuineIntel</c>。</summary>
	Intel
}
