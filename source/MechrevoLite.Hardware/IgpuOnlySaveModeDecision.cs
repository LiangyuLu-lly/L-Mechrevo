namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-13（用户批准「连同强制一起做」）：**核显省电**（<c>IGPU_ONLY_CONNECT_RB_*</c>）这条路的
/// 纯决策逻辑。⛔ 不含任何硬件调用、⛔ 不碰 <c>MechrevoHw</c> 实例 —— 只把
/// 「模式串 ⇄ 回读整数」与「确认判据」这两件最容易写错的事抽成**可离线断言**的纯函数，
/// 由 <see cref="MechrevoService.SetIGpuOnlySaveMode"/> 与 <c>tools\dev\igpu-selftest</c> 共用
/// （⭐ 直接 Link 同一份源码编译，⛔ 不是复制一份逻辑）。
///
/// <para>取值口径来自既有回读实现 <c>MechrevoHw.ResolveIgpuOnlySaveMode</c>（一字未改）：
/// <c>ON ⇒ 0</c>、<c>OFF ⇒ 1</c>、<c>AUTO ⇒ 3</c>、认不出 ⇒ <c>-1</c>（沿用上一次已知值）。</para>
/// </summary>
internal static class IgpuOnlySaveModeDecision
{
	/// <summary><c>IGPU_ONLY_CONNECT_RB_ON</c> 的回读值。</summary>
	internal const int ModeOn = 0;

	/// <summary><c>IGPU_ONLY_CONNECT_RB_OFF</c> 的回读值（也是 <c>MechrevoHw.IgpuOnlySaveMode</c> 的初值）。</summary>
	internal const int ModeOff = 1;

	/// <summary><c>IGPU_ONLY_CONNECT_RB_AUTO</c> 的回读值。</summary>
	internal const int ModeAuto = 3;

	/// <summary>字段缺失 / 认不出时的哨兵（<c>MechrevoHw.ResolveIgpuOnlySaveMode</c> 返回它表示「别覆盖缓存」）。</summary>
	internal const int ModeUnknown = -1;

	/// <summary>
	/// 期望值 ⇄ 命令：只认三颗按钮的三种取值（<c>OFF</c>/<c>ON</c>/<c>AUTO</c>）。
	/// ⛔ 认不出就返回 false ⇒ 调用方**不发任何命令**（与既有实现的早退一字不差）。
	/// </summary>
	internal static bool TryResolveTargetMode(string mode, out int targetMode)
	{
		switch (mode)
		{
		case "ON":
			targetMode = ModeOn;
			return true;
		case "OFF":
			targetMode = ModeOff;
			return true;
		case "AUTO":
			targetMode = ModeAuto;
			return true;
		default:
			targetMode = ModeUnknown;
			return false;
		}
	}

	/// <summary>
	/// 期望值 ⇒ 命令串（<c>IGPU_ONLY_CONNECT_RB_</c> 的后缀）。⛔ 认不出返回 <c>null</c>。
	/// </summary>
	internal static string ResolveActionName(string mode)
	{
		if (!TryResolveTargetMode(mode, out int targetMode))
		{
			return null;
		}
		return "IGPU_ONLY_CONNECT_RB_" + ActionSuffix(targetMode);
	}

	/// <summary>
	/// 期望值 ⇒ 命令后缀。⚠️ 这是**新谓词**要用的唯一映射点：
	/// <c>0 ⇒ ON</c>、<c>1 ⇒ OFF</c>、<c>3 ⇒ AUTO</c>。⛔ 认不出返回 <c>null</c>。
	/// </summary>
	internal static string ActionSuffix(int targetMode)
	{
		switch (targetMode)
		{
		case ModeOn:
			return "ON";
		case ModeOff:
			return "OFF";
		case ModeAuto:
			return "AUTO";
		default:
			return null;
		}
	}

	/// <summary>
	/// ⭐ 阶段 1 的**唯一确认判据**：<c>回读值 == 期望值</c>。
	/// <para>⚠️ 为什么不能沿用 <c>SwitchGpuMode</c> 的 <c>TargetReached</c>：那条判据看的是
	/// 显示路由（<c>CurrentGpuMode</c>/<c>GpuModeStatusVersion</c>），而**核显省电有独立字段**
	/// （<c>IGpuOnlyConnectionSwitch_Status</c> ⇒ <c>MechrevoHw.IgpuOnlySaveMode</c>）——
	/// 用显示路由根本确认不了这个动作（§9.6 的功能隔离）。</para>
	/// <para>⚠️ <c>unknown</c>（<c>-1</c>）一律判**未达成**：缓存里那个值可能只是初值 1，⛔ 不能当确认。</para>
	/// </summary>
	internal static bool IsTargetReached(int actualMode, int targetMode)
	{
		if (actualMode == ModeUnknown || targetMode == ModeUnknown)
		{
			return false;
		}
		return actualMode == targetMode;
	}

	/// <summary>
	/// 补查轮次下表：⭐ 第 1/3/5 次（0 基）补查时才重发命令（与 <c>SwitchGpuMode</c> 的
	/// <c>case 1: case 3: case 5:</c> 完全一致）。
	/// </summary>
	internal static bool ShouldResendOnAttempt(int attempt)
	{
		return attempt == 1 || attempt == 3 || attempt == 5;
	}

	/// <summary>
	/// ⭐ 阶段 3（v）：失败/超时后**是否回滚**。回滚 = 把核显省电恢复成下发前的那个已知取值。
	/// <para>允许回滚的三个条件（缺一不可）：① 这次确实下了命令（<paramref name="commandSent"/>）；
	/// ② 没在等待预算内确认（<paramref name="confirmed"/> 为假）；③ 下发前的取值是**已知**的
	/// （<paramref name="previousMode"/> ≠ <c>-1</c>）—— ⚠️ 未知时回滚等于把状态从"不知道"
	/// 改成"OFF"，那是**瞎写**，⛔ 宁可不动、只报告。</para>
	/// </summary>
	internal static bool ShouldRollback(bool commandSent, bool confirmed, int previousMode)
	{
		return commandSent && !confirmed && previousMode != ModeUnknown;
	}
}
