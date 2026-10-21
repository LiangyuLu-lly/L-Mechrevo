using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// 路由结果：命中的规则序号/说明 + 选中的后端 + 当时的机型事实。
/// <para>每次路由都会落一行可解释日志：<c>[router] backend=… | machine=… | rule=#n …</c></para>
/// </summary>
internal sealed record BackendRouteDecision(int RuleIndex, string Rule, MechrevoBackendKind Backend, MachineIdentity Identity);

/// <summary>
/// 第二层「路由」：有序声明式规则表，首条命中即选后端；未命中落到兜底规则。
/// <para>规则只读 <see cref="MachineIdentity" />，不做后端探测、不“试一个失败再换一个”。</para>
/// <para>新增机型/后端 = 加一条规则或一个事实，不改控制流。</para>
/// </summary>
internal static class BackendRouter
{
	internal static BackendRouteDecision Decide(MachineIdentity identity)
	{
		// 规则 1：板号命中宝龙达白名单 ⇒ 机型优先，哪怕同时装了 GCU 服务也交给宝龙达后端。
		if (identity.BaolongdaWhitelisted)
		{
			return new BackendRouteDecision(1, "board in Baolongda whitelist", MechrevoBackendKind.Baolongda, identity);
		}

		// 规则 2：装了 GCU 服务 ⇒ GCU（同方新机型主通路）。
		if (identity.GcuServiceInstalled)
		{
			return new BackendRouteDecision(2, "GCU service installed", MechrevoBackendKind.Gcu, identity);
		}

		// 规则 3：同方 30/40/50 系显卡 ⇒ GCU（服务未装也走 GCU，未连接时如实提示）。
		if (string.Equals(identity.GpuGeneration, "Gen30", StringComparison.Ordinal)
			|| string.Equals(identity.GpuGeneration, "Gen40", StringComparison.Ordinal)
			|| string.Equals(identity.GpuGeneration, "Gen50", StringComparison.Ordinal))
		{
			return new BackendRouteDecision(3, "GPU gen 30/40/50 -> GCU", MechrevoBackendKind.Gcu, identity);
		}

		// 规则 4：兜底 ⇒ GCU（与改动前一致：表现为「GCU 未连接」而不是选错后端）。
		return new BackendRouteDecision(4, "fallback -> GCU", MechrevoBackendKind.Gcu, identity);
	}

	internal static BackendRouteDecision DecideAndLog()
	{
		BackendRouteDecision decision = Decide(MachineIdentity.Collect());
		Logger.WriteLine("[router] backend=" + decision.Backend
			+ " | machine=" + decision.Identity.Summary
			+ " | rule=#" + decision.RuleIndex + " " + decision.Rule);
		return decision;
	}
}
