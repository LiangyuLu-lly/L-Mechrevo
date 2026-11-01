// **"应用"是否走硬件下发的纯判据**（ 不依赖硬件，便于 selftest 覆盖）。
namespace MechrevoLite.Mode;

/// <summary>⭐ "编辑应用是否需要本地硬件下发"的纯判据。</summary>
internal static class PerfApplyGate
{
	/// <summary>
	/// ⭐ 是否应执行本机硬件下发：`isActive`（原口径）**或** 宝龙达 + 自定义档（本机配置始终可下发）✅。
	/// </summary>
	internal static bool ShouldApplyLocalCustom(bool usesBaolongda, bool isCustom, bool isActive, bool wujieApu = false)
	{
		// （无界 APU）：与宝龙达同为"本机配置"⇒ 非激活档也要下发（SMU 功耗墙/曲线）
		return ((usesBaolongda || wujieApu) && isCustom) || isActive;
	}
}
