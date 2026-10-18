// ⭐⭐ 2026-10-19（机主：「显卡超频设置了但频率没变化」· 根因②）：
//   **"应用"是否走硬件下发的纯判据**（⛔ 不依赖硬件，便于 selftest 覆盖）✅。
//
// ❓ 根因：宝龙达**没有固件自定义档** ⇒ 编辑器里选「自定义 N」只切换"正在编辑哪份本机配置"，不切档；
//   于是自定义档**永远成不了 `ActiveModeId`**。而 `PerfModeService.UpdateCoreAsync` 原来只在
//   `ActiveModeId == mode.Id` 时才走硬件下发 ⇒ 宝龙达自定义档点【应用】只把配置存进 `mode_tune_*`，
//   **GPU 超频 / PL / 曲线等步骤一步都没执行** ❌（诊断包实证：grep `gpu-oc` 零命中）。
//
// ⭐ 规则（本文件 = 唯一判据，UI/服务共用）：
//   `ActiveModeId == mode.Id`（原口径）**或** 宝龙达 + 自定义档（本机配置 ⇒ 始终允许下发）✅
// ⚠️ 非宝龙达 ⇒ 与旧口径**逐位一致**（只有当前激活档才下发；GCU 行为不变）✅。
namespace MechrevoLite.Mode;

/// <summary>⭐ "编辑应用是否需要本地硬件下发"的纯判据。</summary>
internal static class PerfApplyGate
{
	/// <summary>
	/// ⭐ 是否应执行本机硬件下发：`isActive`（原口径）**或** 宝龙达 + 自定义档（本机配置始终可下发）✅。
	/// </summary>
	internal static bool ShouldApplyLocalCustom(bool usesBaolongda, bool isCustom, bool isActive, bool wujieApu = false)
	{
		// ⭐⭐ 2026-10-19（无界 APU）：与宝龙达同为"本机配置"⇒ 非激活档也要下发（SMU 功耗墙/曲线）✅
		return ((usesBaolongda || wujieApu) && isCustom) || isActive;
	}
}
