namespace MechrevoLite.Mode;

public enum PerfApplyResult
{
	Confirmed,
	Sent,
	Failed,
	// (b)（2026-10-09，用户原话「OK 按你说的」）：已下发、但**超过期限**（PerfLazyVerifier.DeadlineMs，默认 30s）
	// 仍是"读得到却对不上" ⇒ 记 Stale（界面文案「可能没生效」）。
	// ⚠️ Stale **不是失败**：AnyFailed 只认 Failed，红字不出现；也不谎报已生效，并给出"可以再点一次『应用』"的重试口径。
	Stale
}
