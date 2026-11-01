using System;
using MechrevoLite.Helpers;

namespace MechrevoLite.Gpu;

/// <summary>
/// 2026-10-06 D-1「核显模式按钮收尾」——《待办-按功能整理.md》D-1 选项 2
/// （保留现在的显示方式，但加一句提示与「试过就记住」）。
///
/// 40 系没有可靠的自动判据：核显模式按钮只能按「支持切换 &amp;&amp; 支持独显直连」这条代理规则猜出来，
/// 在某些机器上按了没用。这里只负责两件事：
///   ① 记住用户对「BIOS/官方控制台里有没有集显模式这一档」的回答有多确定
///      （有/没有 = 明确表态；不确定/跳过/X、Esc = 未表态），用作「这句提示要不要显示」的依据；
///   ② 用户在不确认的机型上真点过一次「核显模式」之后，在**下次开机**回读**实际显示路由**
///      （复用 GpuRestartVerifier 既有的开机回读结果，见 GpuRestartVerifier.RunAfterBootAsync），
///      把「这台机器到底切得过去吗」写进 igpu_route_verified，此后不再靠代理规则猜。
///
/// 边界（重要）：本类**只读写 config.json**，不发任何显卡指令、不写 NVRAM、不读取任何硬件寄存器。
/// 记录的只是「刚才那次切换有没有生效」这个既有回读的结论。
/// </summary>
internal static class IgpuOnlyAttempt
{
	/// <summary>用户对「有没有集显模式这一档」的回答：yes / no / unknown（缺省视为 unknown）。</summary>
	internal const string AnswerKey = "igpu_only_answer";

	/// <summary>试过一次之后的结论：1 = 切过去了（以后一直显示），0 = 没切过去（以后隐藏）。缺省 = 没试过。</summary>
	internal const string VerifiedKey = "igpu_route_verified";

	/// <summary>「用户点了一次核显模式，等下次开机回读」的临时标记（只在一次重启的窗口内存在）。</summary>
	internal const string AttemptPendingKey = "igpu_attempt_pending";

	internal const string AnswerYes = "yes";

	internal const string AnswerNo = "no";

	internal const string AnswerUnknown = "unknown";

	/// <summary>
	/// 用户是否**没有**明确表态（答「不确定」、跳过、直接关掉引导，或根本还没被问过）。
	/// 只有这一种情况下，界面上的核显模式按钮才是「猜」出来的。
	/// </summary>
	internal static bool AnswerIsUncertain
	{
		get
		{
			string answer = AppConfig.GetString(AnswerKey) ?? string.Empty;
			if (string.Equals(answer, AnswerYes, StringComparison.OrdinalIgnoreCase) || string.Equals(answer, AnswerNo, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			return true;
		}
	}

	/// <summary>已经试过一次并且回读到了结论（不管是成功还是失败）。</summary>
	internal static bool HasVerdict
	{
		get
		{
			return AppConfig.Get(VerifiedKey, -1) >= 0;
		}
	}

	/// <summary>试过一次并且**真的切过去了**。</summary>
	internal static bool TrySucceeded
	{
		get
		{
			return AppConfig.Get(VerifiedKey, -1) == 1;
		}
	}

	/// <summary>用户在设置里拨了「显示核显模式」开关，或在引导里选了「有 / 没有」——都是明确表态。</summary>
	internal static void RememberAnswer(bool show)
	{
		AppConfig.Set(AnswerKey, show ? AnswerYes : AnswerNo);
	}

	/// <summary>引导里选了「不确定」、或直接关掉引导（X / Esc / 未选）。</summary>
	internal static void RememberUnknownAnswer()
	{
		AppConfig.Set(AnswerKey, AnswerUnknown);
	}

	/// <summary>
	/// 用户在不确认的机型上点了一次「核显模式」（切换请求已发出、等重启生效）——记一笔，
	/// 等这次重启后的开机回读来给结论。
	/// </summary>
	internal static void MarkAttempt()
	{
		AppConfig.Set(AttemptPendingKey, 1);
		AppConfig.Flush();
		Logger.WriteLine("[igpu-d1] iGPU-only attempt marked pending; the real display route will be read back after this reboot");
	}

	/// <summary>
	/// 开机回读（由 GpuRestartVerifier.RunAfterBootAsync 在算出 verdict 后调用）。
	/// 目标档位不是 0（核显模式）说明用户重启前又改了主意——这次尝试作废，不写结论。
	/// </summary>
	internal static void RecordBootVerdict(int targetMode, GpuRestartVerdict verdict)
	{
		if (AppConfig.Get(AttemptPendingKey, 0) != 1)
		{
			return;
		}
		AppConfig.Set(AttemptPendingKey, 0);
		if (targetMode != 0)
		{
			AppConfig.Flush();
			Logger.WriteLine($"[igpu-d1] iGPU-only attempt superseded (pending target={targetMode}); nothing recorded");
			return;
		}
		int verified;
		switch (verdict)
		{
		case GpuRestartVerdict.Applied:
			verified = 1;
			break;
		case GpuRestartVerdict.NotApplied:
			verified = 0;
			break;
		default:
			// Unconfirmed / NotPending：这次回读拿不到结论（路由未知等）——不写结论，保持原样
			AppConfig.Flush();
			Logger.WriteLine($"[igpu-d1] iGPU-only attempt verdict={verdict}; no conclusion, nothing recorded");
			return;
		}
		AppConfig.Set(VerifiedKey, verified);
		AppConfig.Flush();
		Logger.WriteLine($"[igpu-d1] iGPU-only attempt verdict={verdict} -> igpu_route_verified={verified} ({(verified == 1 ? "iGPU-only works on this machine; keep the button" : "iGPU-only did not take effect; stop guessing and hide the button")})");
	}
}
