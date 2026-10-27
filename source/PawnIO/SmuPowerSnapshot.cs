// ⭐⭐ 2026-10-19（30 系 · SMU 只读快照）：`RyzenSmuService.GetPowerSnapshot()` 的返回值。
//
// 用途（两处）：
//   ① `L-Mechrevo.exe --smu-probe`（只读探针）打印"当前 limits / tctl / 两个时间窗"；
//   ② 30 系自定义模式 UI（`CustomModeForm`）判断「功耗时间窗（STAPM）」这一行**能不能显示**
//      —— 读不到当前值就**不建行**（铁律：不编数字）。
//
// ⚠️ 全部字段都是**只读采样**（SMU PM 表），不代表任何下发状态：
//    * Stapm / Slow / Fast 单位 W，TctlTemp 单位 ℃；
//    * StapmTimeSeconds / SlowTimeSeconds 单位**秒**（null = 该 PM 表版本没有这个字段，或值不像秒）；
//    * TableVersion 是 SMU 报的 PM 表版本（0x…）。
namespace PawnIO;

public sealed record SmuPowerSnapshot(
	float Stapm,
	float Fast,
	float Slow,
	float TctlTemp,
	int? StapmTimeSeconds,
	int? SlowTimeSeconds,
	uint TableVersion
);
